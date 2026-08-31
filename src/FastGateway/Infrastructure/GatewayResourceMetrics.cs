namespace FastGateway.Infrastructure;

internal static class GatewayResourceMetrics
{
    private static long _activeHttpRequests;
    private static long _activeControlConnections;
    private static long _activeHttpTunnels;
    private static long _activeWebSockets;
    private static long _activeTcpConnections;
    private static long _activeUdpSessions;
    private static long _tcpRejected;
    private static long _udpRejected;
    private static long _failoverRequests;
    private static long _failoverAttempts;
    private static long _failoverRetries;
    private static long _failoverExhausted;

    public static long ActiveHttpRequests => Volatile.Read(ref _activeHttpRequests);
    public static long ActiveControlConnections => Volatile.Read(ref _activeControlConnections);
    public static long ActiveHttpTunnels => Volatile.Read(ref _activeHttpTunnels);
    public static long ActiveWebSockets => Volatile.Read(ref _activeWebSockets);
    public static long ActiveTcpConnections => Volatile.Read(ref _activeTcpConnections);
    public static long ActiveUdpSessions => Volatile.Read(ref _activeUdpSessions);
    public static long TcpRejected => Volatile.Read(ref _tcpRejected);
    public static long UdpRejected => Volatile.Read(ref _udpRejected);
    public static long FailoverRequests => Volatile.Read(ref _failoverRequests);
    public static long FailoverAttempts => Volatile.Read(ref _failoverAttempts);
    public static long FailoverRetries => Volatile.Read(ref _failoverRetries);
    public static long FailoverExhausted => Volatile.Read(ref _failoverExhausted);

    public static void HttpRequestOpened() => Interlocked.Increment(ref _activeHttpRequests);

    public static void HttpRequestClosed() => Decrement(ref _activeHttpRequests);

    public static void ControlConnectionOpened() => Interlocked.Increment(ref _activeControlConnections);

    public static void ControlConnectionClosed() => Decrement(ref _activeControlConnections);

    public static void HttpTunnelOpened() => Interlocked.Increment(ref _activeHttpTunnels);

    public static void HttpTunnelClosed() => Decrement(ref _activeHttpTunnels);

    public static void WebSocketOpened() => Interlocked.Increment(ref _activeWebSockets);

    public static void WebSocketClosed() => Decrement(ref _activeWebSockets);

    public static void TcpConnectionOpened() => Interlocked.Increment(ref _activeTcpConnections);

    public static void TcpConnectionClosed() => Decrement(ref _activeTcpConnections);

    public static void UdpSessionOpened() => Interlocked.Increment(ref _activeUdpSessions);

    public static void UdpSessionClosed() => Decrement(ref _activeUdpSessions);

    public static void RecordTcpRejected() => Interlocked.Increment(ref _tcpRejected);

    public static void RecordUdpRejected() => Interlocked.Increment(ref _udpRejected);

    public static void FailoverRequest() => Interlocked.Increment(ref _failoverRequests);

    public static void FailoverAttempt(bool retry)
    {
        Interlocked.Increment(ref _failoverAttempts);
        if (retry) Interlocked.Increment(ref _failoverRetries);
    }

    public static void FailoverBudgetExhausted() => Interlocked.Increment(ref _failoverExhausted);

    private static void Decrement(ref long value)
    {
        while (true)
        {
            var current = Volatile.Read(ref value);
            if (current <= 0) return;
            if (Interlocked.CompareExchange(ref value, current - 1, current) == current) return;
        }
    }
}
