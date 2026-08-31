namespace FastGateway.Dto;

/// <summary>
///     系统版本信息（/api/v1/system/version）
/// </summary>
public sealed class SystemVersionDto
{
    public string Version { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public string Os { get; set; } = string.Empty;
}

/// <summary>
///     网关运行与版本信息（/api/v1/system/info）
/// </summary>
/// <summary>
///     网关资源运行快照（/api/v1/system/resources）
/// </summary>
public sealed class SystemResourceDto
{
    public long ActiveHttpRequests { get; set; }

    public long ActiveControlConnections { get; set; }

    public long ActiveHttpTunnels { get; set; }

    public long ActiveWebSockets { get; set; }

    public long ActiveTcpConnections { get; set; }

    public long ActiveUdpSessions { get; set; }

    public long TcpRejected { get; set; }

    public long UdpRejected { get; set; }

    public long FailoverRequests { get; set; }

    public long FailoverAttempts { get; set; }

    public long FailoverRetries { get; set; }

    public long FailoverExhausted { get; set; }

    public DateTime CheckedAtUtc { get; set; }
}

public sealed class SystemInfoDto
{
    public string? Name { get; set; }

    public string Version { get; set; } = string.Empty;

    public string? InformationalVersion { get; set; }

    public string? FileVersion { get; set; }

    public string? Product { get; set; }

    public string? Description { get; set; }

    public string? Company { get; set; }

    public string? YarpVersion { get; set; }

    public string Framework { get; set; } = string.Empty;

    public string Os { get; set; } = string.Empty;

    public string OsArchitecture { get; set; } = string.Empty;

    public string ProcessArchitecture { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public string? EnvironmentName { get; set; }

    public DateTime ServerTime { get; set; }

    public int? ProcessId { get; set; }

    public DateTime? ProcessStartTime { get; set; }

    public long? UptimeSeconds { get; set; }
}
