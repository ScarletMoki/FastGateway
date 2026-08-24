using FastGateway.Tunnels;
using System.Diagnostics;
using System.Net;
using System.Text;
using Yarp.ReverseProxy.Forwarder;

namespace FastGateway.Gateway;

internal sealed class FastGatewayForwarderHttpClientFactory(
    TunnelClientFactory tunnelClientFactory,
    StandardForwarderHttpClientFactory standardForwarderHttpClientFactory)
    : IForwarderHttpClientFactory
{
    private const string ClientModeMetadataKey = "FastGateway.ClientMode";
    private const string TunnelClientMode = "Tunnel";

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        if (context.NewMetadata is not null &&
            context.NewMetadata.TryGetValue(ClientModeMetadataKey, out var clientMode) &&
            string.Equals(clientMode, TunnelClientMode, StringComparison.OrdinalIgnoreCase))
        {
            return tunnelClientFactory.CreateClient(context);
        }

        return standardForwarderHttpClientFactory.CreateClient(context);
    }
}

/// <summary>
///     普通上游共用一份进程级 <see cref="SocketsHttpHandler"/>，避免 YARP 默认
///     每个 cluster 各建一个 handler、连接池按 cluster 数相乘。
///     协议与超时参数对齐 v2.14.0：明文上游 HTTP/1.1、HTTPS 上游 ALPN 协商 HTTP/2，
///     且不设 MaxConnectionsPerServer 上限：高峰期上限会把多余请求压进池内排队，
///     排队请求持续占用入站连接，反而加速 fd 堆积；出站规模交由系统 nofile 约束。
/// </summary>
public sealed class StandardForwarderHttpClientFactory : IForwarderHttpClientFactory
{
    private static readonly Lazy<SocketsHttpHandler> SharedHandler =
        new(CreateSharedHandler, LazyThreadSafetyMode.ExecutionAndPublication);

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        // disposeHandler: false —— 热重载时 YARP 会 Dispose 旧 invoker，不能带走共享 handler
        return new HttpMessageInvoker(SharedHandler.Value, disposeHandler: false);
    }

    private static SocketsHttpHandler CreateSharedHandler()
    {
        return new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            // 建连快速失败（v2.14.0 同值）：上游过载时半开连接最多占用 fd 1 秒，
            // 避免高峰期 pending connect 大量堆积触发 ENFILE
            ConnectTimeout = TimeSpan.FromSeconds(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ResponseDrainTimeout = TimeSpan.FromSeconds(5),
            // 仅对 HTTPS ALPN 协商出的 HTTP/2 生效：单连接约 100 路并发流，打满后另开连接
            EnableMultipleHttp2Connections = true
        };
    }
}
