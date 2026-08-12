using MessagePack;

namespace Core.Entities;

/// <summary>
/// 黑白名单
/// </summary>
[MessagePackObject(true)]
public sealed class BlacklistAndWhitelist
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }
    
    /// <summary>
    /// IP地址列表
    /// </summary>
    public List<string> Ips { get; set; }

    /// <summary>
    /// 地区规则列表（仅黑名单生效）。格式："国家" 或 "国家|省份"，如 "美国"、"中国|广东省"。
    /// 生效时会被预编译为 IPv4 区间，请求路径零 GeoIP 查询。
    /// </summary>
    public List<string>? Regions { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enable { get; set; }
    
    /// <summary>
    /// 是否黑名单
    /// </summary>
    public bool IsBlacklist { get; set; }
}