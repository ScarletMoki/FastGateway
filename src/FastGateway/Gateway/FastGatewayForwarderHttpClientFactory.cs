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
///     普通上游共用一份进程级 <see cref="SocketsHttpHandler"/>。
///     YARP 默认每个 cluster 各建一个 handler；多条路由指向同一主机时，
///     <c>MaxConnectionsPerServer</c> 会按 cluster 相乘。共享后上限才是「每上游」。
///     并发上限交给系统 nofile；handler 默认即 <see cref="int.MaxValue"/>。
/// </summary>
public sealed class StandardForwarderHttpClientFactory : IForwarderHttpClientFactory
{
    internal const int MaxConnectionsPerServer = int.MaxValue;

    private static readonly SocketsHttpHandler SharedHandler = CreateSharedHandler();

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        // disposeHandler: false —— 热重载时 YARP 会 Dispose 旧 invoker，不能带走共享 handler
        return new HttpMessageInvoker(SharedHandler, disposeHandler: false);
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
            ResponseDrainTimeout = TimeSpan.FromSeconds(5),
            // 只对真正的 HTTP/2（HTTPS ALPN）生效：单连接默认约 100 路并发流，
            // 打满后另开连接。明文 http:// 已钉 HTTP/1.1，不受此开关影响。
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = false,
            MaxConnectionsPerServer = MaxConnectionsPerServer
        };
    }
}
