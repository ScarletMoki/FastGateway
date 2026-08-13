using System.Runtime.InteropServices;
using System.Text;
using FastGateway.BackgroundTask;
using FastGateway.Cluster;
using FastGateway.Infrastructure;
using FastGateway.Middleware;
using FastGateway.Options;
using FastGateway.Services;
using FastGateway.Services.Statistics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FastGateway;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        // 明文上游（http://meteor-api:8080）默认不能走 HTTP/2。打开后 YARP 才能对 h2c 服务
        // 发 prior-knowledge 前言，否则会静默降到 HTTP/1.1，每个并发请求占一条 TCP。
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = args
        });

        FastGatewayOptions.Initialize(builder.Configuration);

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Name));

        builder.Services.AddHttpClient();

        // 判断是否window，
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            builder.Services.AddWindowsService(option => { option.ServiceName = "FastGateway"; });

        var jwtOptions = builder.Configuration.GetSection(JwtOptions.Name).Get<JwtOptions>();

        builder.Services
            .AddAuthorization()
            .AddAuthentication(options => { options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme; })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromDays(jwtOptions.ExpireDay),
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret))
                };
            });

        builder.Services.AddTransient<JwtHelper>();
        builder.Services.AddScoped<SettingProvide>();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            options.SerializerOptions.Converters.Add(new DateTimeJsonConverter());
        });
        builder.Services.AddSystemUsage();
        builder.Services.AddResponseCompression();

        // 宿主级总保险：后台服务（统计、证书续期等）抛出未捕获异常时，默认行为 StopHost 会
        // 连带停掉整个网关，导致所有客户端连接被断开。改为 Ignore，任何后台任务崩溃都不再拖垮
        // 转发主流程；后台服务内部各自记录日志并自愈。
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

        builder.Services.AddHostedService<RenewSslBackgroundService>();
        builder.Services.AddHostedService<StatisticsBackgroundService>();
        builder.Services.AddSingleton<ConfigurationService>();
        builder.Services.AddSingleton<ClusterStateService>();
        builder.Services.AddHostedService<ClusterNodeAgentService>();
        builder.Services.AddHostedService<ClusterRelayAgentService>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<ConfigurationService>();

            // 集群中枢：订阅配置变更，主网关角色下自动向从节点推送
            ClusterHub.Initialize(configService, scope.ServiceProvider.GetRequiredService<ClusterStateService>());

            var certs = configService.GetActiveCerts();
            CertService.InitCert(certs);

            var blacklistAndWhitelists = configService.GetBlacklistAndWhitelists();
            var rateLimits = configService.GetRateLimits();

            BlacklistAndWhitelistService.RefreshCache(blacklistAndWhitelists);

            foreach (var item in configService.GetServers())
                await Task.Factory.StartNew(async () =>
                    await Gateway.Gateway.BuilderGateway(item, configService.GetDomainNamesByServerId(item.Id),
                        blacklistAndWhitelists, rateLimits));

            // 启动 L4 端口转发（TCP/UDP）
            foreach (var item in configService.GetStreamForwards())
                _ = Gateway.StreamProxyManager.StartAsync(item);
        }

        app.Use(async (context, next) =>
        {
            await next();

            if (context.Response.StatusCode == 404)
            {
                context.Request.Path = "/index.html";
                await next();
            }
        });

        app.UseResponseCompression();
        
        app.UsePerformanceMonitoring();

        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        // 集群同步通道使用 WebSocket
        app.UseWebSockets();

        // 集群数据面隧道：从节点出站注册，供跨节点请求中继（NodeToken 鉴权）
        app.Map(ClusterTunnelHub.EndpointPath, tunnel => tunnel.Run(ClusterTunnelHub.HandleAsync));

        StatisticsDb.Initialize(app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("StatisticsDb"));

        app.MapDomain()
            .MapBlacklistAndWhitelist()
            .MapAbnormalIp()
            .MapCert()
            .MapSetting()
            .MapApiQpsService()
            .MapFileStorage()
            .MapRateLimit()
            .MapAuthorizationService()
            .MapServer()
            .MapStreamForward()
            .MapTunnel()
            .MapSystem()
            .MapCluster()
            .MapStatistics();

        await app.RunAsync();
    }
}
