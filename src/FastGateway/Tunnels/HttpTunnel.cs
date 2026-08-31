using Core;
using FastGateway.Infrastructure;

namespace FastGateway.Tunnels;

/// <summary>
///     http隧道
/// </summary>
public sealed partial class HttpTunnel(Stream inner, Guid tunnelId, TransportProtocol protocol, ILogger logger)
    : DelegatingStream(inner)
{
    private readonly TaskCompletionSource _closeTaskCompletionSource = new();
    private readonly long _tickCout = Environment.TickCount64;
    private readonly object _lifecycleSync = new();
    private AgentClientConnection? _connection;
    private bool _counted;
    private bool _closed;
    private int _disposed;

    /// <summary>
    ///     等待HttpClient对其关闭
    /// </summary>
    public Task Closed => _closeTaskCompletionSource.Task;

    /// <summary>
    ///     隧道标识
    /// </summary>
    public Guid Id { get; } = tunnelId;

    /// <summary>
    ///     传输协议
    /// </summary>
    public TransportProtocol Protocol { get; } = protocol;

    public bool BindConnection(AgentClientConnection connection)
    {
        lock (_lifecycleSync)
        {
            if (_closed || _counted || !connection.TryBindHttpTunnel(this)) return false;

            _connection = connection;
            _counted = true;
            GatewayResourceMetrics.HttpTunnelOpened();
            return true;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (!SetClosedResult()) return;
        await Inner.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (!SetClosedResult()) return;
        Inner.Dispose();
    }

    private bool SetClosedResult()
    {
        int? httpTunnelCount;
        AgentClientConnection? connection;
        lock (_lifecycleSync)
        {
            if (_disposed != 0) return false;

            _disposed = 1;
            _closed = true;
            connection = _connection;
            httpTunnelCount = _counted && connection is not null
                ? connection.DecrementHttpTunnelCount(this)
                : null;
            if (_counted) GatewayResourceMetrics.HttpTunnelClosed();
            _counted = false;
        }

        _closeTaskCompletionSource.TrySetResult();
        var lifeTime = TimeSpan.FromMilliseconds(Environment.TickCount64 - _tickCout);
        Log.LogTunnelClosed(logger, connection?.ClientId, Protocol, Id, lifeTime, httpTunnelCount);
        return true;
    }

    public override string ToString()
    {
        return Id.ToString();
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information,
            "[{clientId}] 关闭了{protocol}协议隧道{tunnelId}，生命周期为{lifeTime}，其当前隧道总数为{tunnelCount}")]
        public static partial void LogTunnelClosed(ILogger logger, string? clientId, TransportProtocol protocol,
            Guid tunnelId, TimeSpan lifeTime, int? tunnelCount);
    }
}