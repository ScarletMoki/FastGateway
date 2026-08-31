using FastGateway.Options;
using System.Diagnostics;
using System.Net;
using System.Text;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Forwarder;

namespace FastGateway.Middleware;

public sealed class FailoverHttpClientPool : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<int, HttpMessageInvoker> _clients = new();
    private bool _disposed;

    public HttpMessageInvoker GetOrCreate(int connectTimeoutMs)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_clients.TryGetValue(connectTimeoutMs, out var client))
                return client;

            client = new HttpMessageInvoker(CreateHandler(connectTimeoutMs), disposeHandler: true);
            _clients.Add(connectTimeoutMs, client);
            return client;
        }
    }

    private static SocketsHttpHandler CreateHandler(int connectTimeoutMs)
    {
        return new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            ConnectTimeout = TimeSpan.FromMilliseconds(connectTimeoutMs),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ResponseDrainTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = FastGatewayOptions.MaxConnectionsPerUpstream
        };
    }

    public void Dispose()
    {
        HttpMessageInvoker[] clients;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            clients = _clients.Values.ToArray();
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            try { client.Dispose(); }
            catch { /* ignore */ }
        }
    }
}
