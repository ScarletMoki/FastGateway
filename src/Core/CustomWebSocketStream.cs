using System.Net.WebSockets;

namespace Core;

public class CustomWebSocketStream(WebSocket webSocket) : Stream
{
    private int _disposed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await DisposeAsync().ConfigureAwait(false);
                return 0;
            }

            return result.Count;
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1d));
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeoutTokenSource.Token)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        finally
        {
            webSocket.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1d));
                webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeoutTokenSource.Token)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
            }
        }
        finally
        {
            webSocket.Dispose();
        }
    }
}