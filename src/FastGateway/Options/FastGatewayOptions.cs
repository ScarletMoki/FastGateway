namespace FastGateway.Options;

public class FastGatewayOptions
{
    public static string TunnelToken { get; set; } = "Aa123456.";

    public static string Password { get; set; } = "Aa123456";

    /// <summary>
    ///     每个上游 origin（scheme+host+port）的最大出站连接数。
    ///     HTTP/1.1 上游无多路复用，若不设上限，高并发时每个在途请求各占一条 TCP 连接，
    ///     文件描述符会被打爆（ENFILE：Too many open files in system）；
    ///     达到上限后多余请求在连接池内排队等待空闲连接，而非无限建连。
    /// </summary>
    public static int MaxConnectionsPerUpstream { get; set; } = 1024;

    public static void Initialize(IConfiguration configuration)
    {
        var tunnelToken = configuration["TunnelToken"];
        if (!string.IsNullOrEmpty(tunnelToken))
            TunnelToken = tunnelToken;
        else
            throw new ArgumentNullException(nameof(TunnelToken), "TunnelToken cannot be null or empty.");

        var password = configuration["Password"];

        if (string.IsNullOrEmpty(password)) password = configuration["PASSWORD"] ?? "Aa123456";

        if (!string.IsNullOrEmpty(password))
            Password = password;
        else
            throw new ArgumentNullException(nameof(Password), "Password cannot be null or empty.");

        var maxConnections = configuration["MaxConnectionsPerUpstream"]
                             ?? configuration["MAX_CONNECTIONS_PER_UPSTREAM"];
        if (int.TryParse(maxConnections, out var parsed) && parsed > 0)
            MaxConnectionsPerUpstream = parsed;
    }
}