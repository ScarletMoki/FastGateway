namespace FastGateway.Services.Statistics;

/// <summary>
///     请求日志（request_log 明细与分钟桶）保留期配置。
///     由系统设置控制，仅允许 1/7/15/30 天，默认 7 天；
///     后台清理与查询侧共用此快照，保证清理边界与查询降级一致。
/// </summary>
public static class LogRetention
{
    public const string SettingKey = "log_retention_days";
    public const int DefaultDays = 7;

    private static readonly int[] AllowedDays = [1, 7, 15, 30];

    private static volatile int _rawDays = DefaultDays;

    /// <summary>当前生效的明细保留天数</summary>
    public static int RawDays => _rawDays;

    public static bool IsAllowed(int days)
    {
        return Array.IndexOf(AllowedDays, days) >= 0;
    }

    /// <summary>从配置重新读取保留天数（非法值回退默认），并更新快照</summary>
    public static int Refresh(ConfigurationService configService)
    {
        var days = DefaultDays;
        var setting = configService.GetSetting(SettingKey);
        if (setting?.Value != null && int.TryParse(setting.Value, out var parsed) && IsAllowed(parsed))
            days = parsed;

        _rawDays = days;
        return days;
    }
}
