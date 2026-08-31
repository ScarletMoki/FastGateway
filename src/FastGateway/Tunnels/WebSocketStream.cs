using System.Net.WebSockets;
using System.Threading.Tasks.Sources;
using FastGateway.Infrastructure;

namespace FastGateway.Tunnels;

internal sealed class WebSocketStream : Stream, IValueTaskSource<object?>, ICloseable
{
    private readonly WebSocket _ws;
    private readonly object _sync = new();
    private ManualResetValueTaskSourceCore<object?> _tcs = new() { RunContinuationsAsynchronously = true };
    private int _disposed;

    public WebSocketStream(WebSocket ws)
    {
        _ws = ws;
        GatewayResourceMetrics.WebSocketOpened();
    }

    internal ValueTask<object?> StreamCompleteTask => new(this, _tcs.Version);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public bool IsClosed
    {
        get
        {
            try
            {
                return _ws.State != WebSocketState.Open;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }

    public void Abort()
    {
        CompleteAndDispose();
    }

    public object? GetResult(short token)
    {
        return _tcs.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return _tcs.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        _tcs.OnCompleted(continuation, state, token, flags);
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await _ws.SendAsync(buffer, WebSocketMessageType.Binary, false, cancellationToken);
        }
        catch
        {
            CompleteAndDispose();
            throw;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ValueWebSocketReceiveResult result;
        try
        {
            result = await _ws.ReceiveAsync(buffer, cancellationToken);
        }
        catch
        {
            CompleteAndDispose();
            throw;
        }

        if (result.MessageType == WebSocketMessageType.Close)
        {
            CompleteAndDispose();
            return 0;
        }

        return result.Count;
    }

    protected override void Dispose(bool disposing)
    {
        CompleteAndDispose();
    }

    private void CompleteAndDispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try { _ws.Abort(); }
            catch { /* ignore */ }
            try { _ws.Dispose(); }
            catch { /* ignore */ }
            GatewayResourceMetrics.WebSocketClosed();
        }

        lock (_sync)
        {
            if (_tcs.GetStatus(_tcs.Version) == ValueTaskSourceStatus.Pending)
                _tcs.SetResult(null);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            if (_disposed != 0) return;
            _tcs.Reset();
        }
    }
}