using System.Collections.Concurrent;
using Core.Entities;

namespace FastGateway.Tunnels;

/// <summary>
///     隧道节点运行时注册表（进程级）：
///     记录客户端上报的代理配置、所属网关 Server、在线控制连接。
///     路由只在控制连接在线期间注入，断线即清理，避免死路由。
/// </summary>
public static class TunnelClientProxy
{
    private static readonly ConcurrentDictionary<string, TunnelRuntimeState> States =
        new(StringComparer.OrdinalIgnoreCase);

    public static List<TunnelRuntimeState> GetAllStates()
    {
        return States.Values.ToList();
    }

    public static TunnelRuntimeState? GetState(string nodeName)
    {
        return States.GetValueOrDefault(nodeName);
    }

    public static bool IsOnline(string nodeName)
    {
        return States.TryGetValue(nodeName, out var state) && state.Client != null;
    }

    /// <summary>
    ///     客户端注册：登记上报配置（代理分配稳定 Id）、持久化上报快照，并刷新所属网关路由
    /// </summary>
    public static void Register(Tunnel tunnel, Server server)
    {
        tunnel.Proxy ??= [];
        foreach (var proxy in tunnel.Proxy)
            proxy.Id = TunnelNodeStore.ComputeProxyId(proxy.Host, proxy.Route, proxy.Domains, proxy.LocalRemote);

        var state = States.AddOrUpdate(tunnel.Name,
            _ => new TunnelRuntimeState { Tunnel = tunnel, ServerId = server.Id },
            (_, existing) =>
            {
                existing.Tunnel = tunnel;
                existing.ServerId = server.Id;
                return existing;
            });

        // 持久化上报快照与心跳，供面板展示与重启后回显
        var node = TunnelNodeStore.GetNode(tunnel.Name);
        if (node != null)
        {
            node.HeartbeatInterval = tunnel.HeartbeatInterval;
            node.ReportedProxies = tunnel.Proxy.Select(p => new TunnelNodeProxy
            {
                Id = p.Id,
                Host = p.Host,
                Route = p.Route,
                LocalRemote = p.LocalRemote,
                Description = p.Description,
                Domains = p.Domains ?? [],
                Enabled = p.Enabled
            }).ToList();
            TunnelNodeStore.UpdateNode(node);
        }

        ReloadServerRoutes(state.ServerId);
    }

    /// <summary>
    ///     控制连接建立：标记在线并注入路由
    /// </summary>
    public static void OnConnected(string nodeName, AgentClient client, Server server)
    {
        var state = States.AddOrUpdate(nodeName,
            _ => new TunnelRuntimeState { ServerId = server.Id, Client = client, ConnectedAt = DateTime.Now },
            (_, existing) =>
            {
                existing.ServerId = server.Id;
                existing.Client = client;
                existing.ConnectedAt = DateTime.Now;
                return existing;
            });

        TouchLastConnectTime(nodeName);
        ReloadServerRoutes(state.ServerId);
    }

    /// <summary>
    ///     控制连接断开：标记离线、更新最近连接时间并清理该节点的 YARP 路由
    /// </summary>
    public static void OnDisconnected(string nodeName, AgentClient client)
    {
        if (!States.TryGetValue(nodeName, out var state)) return;

        // 同名节点重连会先替换 Client，旧连接关闭时不得清掉新连接
        if (!ReferenceEquals(state.Client, client)) return;

        state.Client = null;
        state.ConnectedAt = null;

        TouchLastConnectTime(nodeName);
        ReloadServerRoutes(state.ServerId);
    }

    /// <summary>
    ///     踢下线：关闭控制连接（触发中间件收尾 → OnDisconnected 清理路由）
    /// </summary>
    public static async Task KickAsync(string nodeName)
    {
        if (States.TryGetValue(nodeName, out var state) && state.Client is { } client)
            await client.DisposeAsync();
    }

    /// <summary>
    ///     删除节点的运行时状态并清理路由
    /// </summary>
    public static async Task RemoveAsync(string nodeName)
    {
        await KickAsync(nodeName);
        if (States.TryRemove(nodeName, out var state))
            ReloadServerRoutes(state.ServerId);
    }

    /// <summary>
    ///     刷新指定 Server 的路由（用最新域名配置重建，在线隧道路由由 ReloadGateway 注入）
    /// </summary>
    public static void ReloadServerRoutes(string serverId)
    {
        var server = TunnelNodeStore.GetServer(serverId);
        if (server == null) return;

        var domainNames = TunnelNodeStore.GetDomainNamesByServerId(serverId);
        Gateway.Gateway.ReloadGateway(server, [.. domainNames]);
    }

    private static void TouchLastConnectTime(string nodeName)
    {
        var node = TunnelNodeStore.GetNode(nodeName);
        if (node == null) return;

        node.LastConnectTime = DateTime.Now;
        TunnelNodeStore.UpdateNode(node);
    }
}

/// <summary>
///     单个节点的运行时状态
/// </summary>
public sealed class TunnelRuntimeState
{
    /// <summary>
    ///     客户端最近上报的隧道配置（代理已带稳定 Id）；未注册仅连接时为 null
    /// </summary>
    public Tunnel? Tunnel { get; set; }

    /// <summary>
    ///     注册到的网关 Server Id
    /// </summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>
    ///     在线控制连接；离线为 null
    /// </summary>
    public AgentClient? Client { get; set; }

    public DateTime? ConnectedAt { get; set; }
}
