using System.Collections.Concurrent;
using System.Diagnostics;

namespace FastGateway.Tunnels;

/// <summary>
///     Agent隧道
/// </summary>
/// <param name="logger"></param>
public partial class AgentTunnelFactory(ILogger<AgentTunnelFactory> logger)
{
    private readonly ConcurrentDictionary<Guid, PendingHttpTunnel> _httpTunnelCompletionSources = new();

    /// <summary>
    ///     创建HttpTunnel
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="proxy"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<HttpTunnel> CreateHttpTunnelAsync(AgentClientConnection connection,
        CancellationToken cancellationToken)
    {
        var tunnelId = Guid.NewGuid();
        var pendingTunnel = new PendingHttpTunnel();
        if (!_httpTunnelCompletionSources.TryAdd(tunnelId, pendingTunnel))
            throw new SystemException($"系统中已存在{tunnelId}的tunnelId");

        HttpTunnel? ownedTunnel = null;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, connection.DisposedToken);
            cancellationToken = linkedCts.Token;

            var stopwatch = Stopwatch.StartNew();
            Log.LogTunnelCreating(logger, connection.ClientId, tunnelId);
            await connection.CreateHttpTunnelAsync(tunnelId, cancellationToken);
            var httpTunnel = await pendingTunnel.Source.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!pendingTunnel.TryClaim(httpTunnel))
            {
                await httpTunnel.DisposeAsync();
                throw new OperationCanceledException("控制连接已关闭");
            }

            ownedTunnel = httpTunnel;
            cancellationToken.ThrowIfCancellationRequested();
            if (!httpTunnel.BindConnection(connection))
                throw new OperationCanceledException("控制连接已关闭");

            stopwatch.Stop();
            Log.LogTunnelCreateSuccess(logger, connection.ClientId, httpTunnel.Protocol, tunnelId,
                stopwatch.Elapsed, connection.HttpTunnelCount);
            ownedTunnel = null;
            return httpTunnel;
        }
        catch (OperationCanceledException)
        {
            Log.LogTunnelCreateFailure(logger, connection.ClientId, tunnelId, "远程端操作超时");
            throw;
        }
        catch (Exception ex)
        {
            Log.LogTunnelCreateFailure(logger, connection.ClientId, tunnelId, ex.Message);
            throw;
        }
        finally
        {
            if (ownedTunnel is not null)
            {
                try { await ownedTunnel.DisposeAsync(); }
                catch { /* ignored */ }
            }

            _httpTunnelCompletionSources.TryRemove(tunnelId, out _);
            pendingTunnel.Abandon();
        }
    }

    public bool Contains(Guid tunnelId)
    {
        return _httpTunnelCompletionSources.ContainsKey(tunnelId);
    }

    public bool SetResult(HttpTunnel httpTunnel)
    {
        if (_httpTunnelCompletionSources.TryRemove(httpTunnel.Id, out var pending) &&
            pending.TrySetResult(httpTunnel))
            return true;

        httpTunnel.Dispose();
        return false;
    }

    private sealed class PendingHttpTunnel
    {
        private readonly object _sync = new();
        private HttpTunnel? _tunnel;
        private bool _abandoned;
        private bool _claimed;
        private bool _completed;

        public TaskCompletionSource<HttpTunnel> Source { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TrySetResult(HttpTunnel tunnel)
        {
            lock (_sync)
            {
                if (_abandoned || _completed || _claimed)
                {
                    tunnel.Dispose();
                    return false;
                }

                _completed = true;
                _tunnel = tunnel;
                if (Source.TrySetResult(tunnel)) return true;

                _tunnel = null;
            }

            tunnel.Dispose();
            return false;
        }

        public bool TryClaim(HttpTunnel tunnel)
        {
            lock (_sync)
            {
                if (_abandoned || !_completed || !ReferenceEquals(_tunnel, tunnel)) return false;

                _claimed = true;
                _tunnel = null;
                return true;
            }
        }

        public void Abandon()
        {
            HttpTunnel? tunnel;
            lock (_sync)
            {
                if (_abandoned) return;

                _abandoned = true;
                tunnel = _completed && !_claimed ? _tunnel : null;
                _tunnel = null;
                if (!_completed) Source.TrySetCanceled();
            }

            tunnel?.Dispose();
        }
    }

    private static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "[{clientId}] 请求创建隧道{tunnelId}")]
        public static partial void
            LogTunnelCreating(ILogger logger, string clientId, Guid tunnelId);

        [LoggerMessage(LogLevel.Warning, "[{clientId}] 创建隧道{tunnelId}失败：{reason}")]
        public static partial void LogTunnelCreateFailure(ILogger logger, string clientId, Guid tunnelId,
            string? reason);

        [LoggerMessage(LogLevel.Information,
            "[{clientId}] 创建了{protocol}协议隧道{tunnelId}，过程耗时{elapsed}，其当前隧道总数为{tunnelCount}")]
        public static partial void LogTunnelCreateSuccess(ILogger logger, string clientId, TransportProtocol protocol,
            Guid tunnelId, TimeSpan elapsed, int tunnelCount);
    }
}