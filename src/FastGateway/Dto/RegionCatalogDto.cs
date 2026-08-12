namespace FastGateway.Dto;

/// <summary>
///     可选地区目录（来自 ip2region.xdb 扫描去重），供前端地区黑名单选择器使用。
/// </summary>
public sealed class RegionCatalogDto
{
    /// <summary>
    /// 全部国家/地区名称（已归一化为中文展示名）。
    /// </summary>
    public List<string> Countries { get; set; } = [];

    /// <summary>
    /// 中国省级行政区名称（规则格式为 "中国|省份"）。
    /// </summary>
    public List<string> ChinaProvinces { get; set; } = [];
}
