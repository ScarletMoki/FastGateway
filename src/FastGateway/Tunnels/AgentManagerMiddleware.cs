using Core.Entities;

namespace FastGateway.Tunnels;

/// <summary>
///     节点服务中间件
///     用于与节点服务建立主连接、创建隧道和管理隧道连接、保持ping服务。
/// </summary>
internal partial class AgentManagerMiddleware(
    AgentTunnelFactory agentTunnelFactory,
    ILogger<AgentManagerMiddleware> logger,
    Server server,
    AgentClientManager agentClientManager) : IMiddleware
{
    /// <summary>
    ///     HTTP请求中间件的入口方法
    /// </summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // 创建自定义特性并判断协议是否允许
        var feature = new FastFeature(context);
        if (IsAllowProtocol(feature.Protocol))
        {
            // 设置特性
            context.Features.Set<IFastFeature>(feature);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        // 从请求中获取节点名（query 兼容旧客户端，header 供 WebSocket 使用）
        var nodeName = context.Request.Query["nodeName"].ToString();
        if (string.IsNullOrEmpty(nodeName))
            nodeName = context.Request.Headers["nodeName"].ToString();

        // 如果节点名为空，调用下一个中间件（数据隧道回连走 tunnelId）
        if (string.IsNullOrEmpty(nodeName))
        {
            await next(context);
            return;
        }

        // 节点级密钥认证（优先 Authorization 头，query 仅兼容保留）
        var credential = TunnelNodeStore.ExtractCredential(context);
        var auth = TunnelNodeStore.Authenticate(nodeName, credential);
        if (!auth.Success)
        {
            // 延迟响应降低暴力枚举效率
            await Task.Delay(3000);
            context.Response.StatusCode = auth.StatusCode;
            return;
        }

        // 心跳间隔采用客户端上报值（注册时持久化），钳制在合理范围
        var heartbeatSeconds = TunnelNodeStore.ClampHeartbeatSeconds(auth.Node!.HeartbeatInterval);
        var connectionConfig = new ConnectionConfig
        {
            KeepAliveInterval = TimeSpan.FromSeconds(heartbeatSeconds)
        };

        // 构建应用程序主机名
        var host = "node_" + nodeName;

        AgentClient? client = null;
        var added = false;
        try
        {
            // 创建连接
            var stream = await feature.AcceptAsSafeWriteStreamAsync();

            // 创建客户端连接
            var connection = new AgentClientConnection(host, stream, connectionConfig, logger);

            // 使用连接创建客户端对象，并添加到客户端管理器
            client = new AgentClient(connection, agentTunnelFactory, context);
            await using var _ = client;
            if (await agentClientManager.AddAsync(client, default))
            {
                added = true;
                Log.LogNodeConnected(logger, nodeName, heartbeatSeconds);
                TunnelClientProxy.OnConnected(nodeName, client, server);

                // 等待连接关闭
                await connection.WaitForCloseAsync();
            }
        }
        catch (Exception e)
        {
            // 记录错误日志
            logger.LogError(e, "Failed to create client connection.");
        }
        finally
        {
            if (client != null)
            {
                if (added)
                {
                    try { await agentClientManager.RemoveAsync(client, CancellationToken.None); }
                    catch { /* ignored */ }
                }

                // 更新离线状态与最近连接时间，并清理该节点的 YARP 路由
                TunnelClientProxy.OnDisconnected(nodeName, client);
                Log.LogNodeDisconnected(logger, nodeName);
            }
        }
    }

    /// <summary>
    ///     校验协议是否为允许的类型
    /// </summary>
    private static bool IsAllowProtocol(TransportProtocol protocol)
    {
        return protocol is TransportProtocol.Http11 or TransportProtocol.Http2 or TransportProtocol.WebSocketWithHttp11
            or TransportProtocol.WebSocketWithHttp2;
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "节点 [{nodeName}] 控制连接已建立，心跳间隔 {heartbeatSeconds}s")]
        public static partial void LogNodeConnected(ILogger logger, string nodeName, int heartbeatSeconds);

        [LoggerMessage(LogLevel.Information, "节点 [{nodeName}] 控制连接已断开，路由已清理")]
        public static partial void LogNodeDisconnected(ILogger logger, string nodeName);
    }
}
