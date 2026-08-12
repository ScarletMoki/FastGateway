using System.Text.Json.Serialization;

namespace FastGateway.Cluster;

public enum ClusterRole
{
    /// <summary>独立网关（默认）</summary>
    Standalone = 0,

    /// <summary>主网关：持有配置真相，向从节点推送</summary>
    Master = 1,

    /// <summary>从节点：配置由主网关下发，本地只读缓存</summary>
    Worker = 2
}

/// <summary>
///     集群状态（data/cluster.json）。主从两种角色共用一个结构：
///     Master 使用 Nodes/Invites；Worker 使用 MasterEndpoint/NodeId/NodeToken。
/// </summary>
public class ClusterState
{
    public ClusterRole Role { get; set; } = ClusterRole.Standalone;

    // ===== Worker 侧 =====
    public string? NodeId { get; set; }

    public string? NodeName { get; set; }

    public string? MasterEndpoint { get; set; }

    public string? NodeToken { get; set; }

    /// <summary>最近一次成功应用的主网关配置版本</summary>
    public long SyncedVersion { get; set; }

    public DateTime? LastSyncTime { get; set; }

    // ===== Master 侧 =====
    public List<ClusterNode> Nodes { get; set; } = new();

    public List<ClusterInvite> Invites { get; set; } = new();
}

/// <summary>主网关登记的从节点</summary>
public class ClusterNode
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>节点专属令牌，从节点建立同步通道时校验</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; }
}

/// <summary>接入邀请：生成的接入码内含 Endpoint + Token</summary>
public class ClusterInvite
{
    public string Token { get; set; } = string.Empty;

    /// <summary>主网关对外可达的管理地址（从节点回连用）</summary>
    public string Endpoint { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}

/// <summary>
///     集群状态专用源生成上下文（AOT），独立于 AppJsonContext 以开启缩进、保持文件可读。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ClusterState))]
public partial class ClusterJsonContext : JsonSerializerContext
{
}
