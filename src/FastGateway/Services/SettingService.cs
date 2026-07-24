using System.ComponentModel.DataAnnotations;
using FastGateway.Dto;
using FastGateway.Infrastructure;
using FastGateway.Services.Statistics;

namespace FastGateway.Services;

public static class SettingService
{
    public static IEndpointRouteBuilder MapSetting(this IEndpointRouteBuilder app)
    {
        var setting = app.MapGroup("/api/v1/setting")
            .WithTags("设置")
            .RequireAuthorization()
            .WithDescription("设置管理")
            .AddEndpointFilter<ResultFilter>()
            .WithDisplayName("设置");

        setting.MapGet(string.Empty,
                async (SettingProvide settingProvide) => await settingProvide.GetListAsync())
            .WithDescription("获取设置列表").WithDisplayName("获取设置列表").WithTags("设置");

        setting.MapGet("{key}",
                async (SettingProvide settingProvide, string key) => await settingProvide.GetStringAsync(key))
            .WithDescription("获取设置").WithDisplayName("获取设置")
            .WithTags("设置");

        setting.MapPost("{key}",
                async (SettingProvide settingProvide, ConfigurationService configService, SettingInput input) =>
                {
                    if (input.Key == LogRetention.SettingKey &&
                        (!int.TryParse(input.Value, out var days) || !LogRetention.IsAllowed(days)))
                        throw new ValidationException("日志保留天数仅支持 1、7、15、30 天");

                    await settingProvide.SetAsync(input.Key, input.Value);

                    if (input.Key == LogRetention.SettingKey) LogRetention.Refresh(configService);
                })
            .WithDescription("设置设置").WithDisplayName("设置设置")
            .WithTags("设置");

        return app;
    }
}