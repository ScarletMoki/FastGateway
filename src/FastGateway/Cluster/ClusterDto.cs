using FastGateway.Services;
using MessagePack;

namespace FastGateway.Cluster;

/// <summary>生成接入码入参</summary>
public class GenerateInviteInput
{
    /// <summary>本网关（主）对外可达的管理地址，例如 https://gw-a.example.com:8080</summary>
    public string Endpoint { get; set; } = string.Empty;
}

public class GenerateInviteResult
{
    /// <summary>接入码：Base64Url(JSON{endpoint,token})，粘贴到从网关即可加入</summary>
    public string Code { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}

/// <summary>接入码内嵌的数据</summary>
public class ClusterInviteCode
{
    public string Endpoint { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}

/// <summary>从网关"一键加入"入参</summary>
public class JoinClusterInput
{
    public string Code { get; set; } = string.Empty;

    public string NodeName { get; set; } = string.Empty;
}

/// <summary>从节点向主网关注册（匿名接口，凭邀请令牌）</summary>
public class RegisterNodeInput
{
    public string NodeName { get; set; } = string.Empty;
}

public class RegisterNodeResult
{
    public string NodeId { get; set; } = string.Empty;

    public string NodeToken { get; set; } = string.Empty;
}

/// <summary>集群状态展示 DTO（角色相关字段按需填充）</summary>
public class ClusterStateDto
{
    public ClusterRole Role { get; set; }

    public string? NodeName { get; set; }

    public string? MasterEndpoint { get; set; }

    public long SyncedVersion { get; set; }

    public DateTime? LastSyncTime { get; set; }

    /// <summary>Worker：与主网关的同步通道是否在线</summary>
    public bool Connected { get; set; }

    public List<ClusterNodeDto> Nodes { get; set; } = new();
}

public class ClusterNodeDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Online { get; set; }

    public DateTime? LastSeen { get; set; }

    public long SyncedVersion { get; set; }

    public DateTime RegisteredAt { get; set; }
}

/// <summary>同步通道消息（主从双向共用一个信封，MessagePack 二进制编码）</summary>
[MessagePackObject(true)]
public class ClusterMessage
{
    public const string TypeConfig = "config";
    public const string TypePing = "ping";
    public const string TypePong = "pong";
    public const string TypeAck = "ack";
    public const string TypeRemoved = "removed";

    public string Type { get; set; } = string.Empty;

    public long Version { get; set; }

    public ClusterConfigPayload? Config { get; set; }
}

/// <summary>全量配置快照：网关配置 + 证书文件内容</summary>
[MessagePackObject(true)]
public class ClusterConfigPayload
{
    public GatewayConfig Config { get; set; } = new();

    public List<ClusterCertFile> CertFiles { get; set; } = new();
}

[MessagePackObject(true)]
public class ClusterCertFile
{
    public string CertId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>PFX 文件内容（MessagePack 原生 bin 编码，无 Base64 膨胀）</summary>
    public byte[] Data { get; set; } = [];
}
