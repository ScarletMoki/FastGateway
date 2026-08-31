using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Yarp.ReverseProxy.Forwarder;

namespace FastGateway.Tunnels;

/// <summary>
///     隧道出站工厂：每个网关实例共用一份 handler（ConnectCallback 绑定本实例的 Agent）。
///     避免每条隧道路由各建一个连接池。
/// </summary>
internal sealed class TunnelClientFactory(
    AgentClientManager agentClientManager,
    AgentTunnelFactory agentTunnelFactory)
    : IForwarderHttpClientFactory, IDisposable
{
    private readonly SocketsHttpHandler _handler = CreateHandler(agentClientManager, agentTunnelFactory);

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        return new HttpMessageInvoker(_handler, disposeHandler: false);
    }

    private static SocketsHttpHandler CreateHandler(
        AgentClientManager agentClientManager,
        AgentTunnelFactory agentTunnelFactory)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            MaxConnectionsPerServer = 100,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            KeepAlivePingDelay = TimeSpan.FromMinutes(5),
            KeepAlivePingTimeout = TimeSpan.FromMinutes(5),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            ConnectTimeout = TimeSpan.FromMinutes(5),
            ResponseDrainTimeout = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = false,
            MaxAutomaticRedirections = 3
        };

        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host.ToLowerInvariant();

            if (agentClientManager.TryGetValue(host, out var agentClient))
            {
                return await agentTunnelFactory.CreateHttpTunnelAsync(agentClient.Connection, cancellationToken);
            }

            // 集群中继目的地（node_cluster-*）：主连接挂在管理应用的进程级枢纽，而非本子应用的管理器
            if (Cluster.ClusterTunnelHub.Clients.TryGetValue(host, out var clusterClient))
                return await Cluster.ClusterTunnelHub.Tunnels.CreateHttpTunnelAsync(clusterClient.Connection,
                    cancellationToken);

            return await DefaultConnectCallback(context, cancellationToken);
        };

        return handler;
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    private static async ValueTask<Stream> DefaultConnectCallback(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
