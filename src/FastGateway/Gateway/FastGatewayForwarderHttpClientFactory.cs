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
///     默认客户端一律 HTTP/1.1（版本由 ForwarderRequestConfig 钉死，HTTP/2 仅隧道路径使用），
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
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ResponseDrainTimeout = TimeSpan.FromSeconds(5)
        };
    }
}
