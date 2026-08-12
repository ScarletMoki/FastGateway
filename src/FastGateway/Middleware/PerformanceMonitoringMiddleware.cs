using FastGateway.Services;
using System.Diagnostics;

namespace FastGateway.Middleware;

public class PerformanceMonitoringMiddleware
{
    private readonly RequestDelegate _next;

    public PerformanceMonitoringMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // /api/v1/qps 内部含 1 秒阻塞的网络采样，且被仪表盘每 3 秒轮询，
        // 计入统计会把 P95/P99 与成功率数据污染成“监控自己”，这里直接跳过。
        if (context.Request.Path.StartsWithSegments("/api/v1/qps", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // GetTimestamp 免去每请求一个 Stopwatch 对象分配
        var start = Stopwatch.GetTimestamp();

        try
        {
            // 记录请求开始
            QpsService.IncrementServiceRequests();

            await _next(context);

            QpsService.RecordSuccessRequest((long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception)
        {
            QpsService.RecordFailedRequest((long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }
}

public static class PerformanceMonitoringMiddlewareExtensions
{
    public static IApplicationBuilder UsePerformanceMonitoring(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PerformanceMonitoringMiddleware>();
    }
}