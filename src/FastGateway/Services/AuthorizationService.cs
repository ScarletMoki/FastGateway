using System.ComponentModel.DataAnnotations;
using FastGateway.Dto;
using FastGateway.Infrastructure;
using FastGateway.Options;

namespace FastGateway.Services;

public static class AuthorizationServiceExtensions
{
    public static IEndpointRouteBuilder MapAuthorizationService(this IEndpointRouteBuilder app)
    {
        var routeGroupBuilder = app.MapGroup("/api/v1/authorization")
            .WithTags("授权")
            .WithDescription("授权")
            .AddEndpointFilter<ResultFilter>()
            .WithDisplayName("授权");

        routeGroupBuilder.MapGet("challenge-config", (BotProtectionService botProtection) =>
            botProtection.GetAdminChallengeConfig());

        routeGroupBuilder.MapPost(string.Empty,
            async (HttpContext context, JwtHelper jwtHelper, BotProtectionService botProtection,
                AuthorizationRequest request, CancellationToken cancellationToken) =>
            {
                if (botProtection.AdminLoginEnabled)
                {
                    var verification = await botProtection.VerifyTurnstileAsync(
                        request.TurnstileToken,
                        "admin-login",
                        context.Request.Host.Host,
                        context.Connection.RemoteIpAddress?.ToString(),
                        cancellationToken);

                    if (verification.Status == BotVerificationStatus.Unavailable)
                        throw new ValidationException("人机验证服务暂时不可用，请稍后重试");

                    if (verification.Status != BotVerificationStatus.Success)
                        throw new ValidationException("请先完成人机验证");
                }

                if (request.Password == FastGatewayOptions.Password)
                    return jwtHelper.CreateToken();

                throw new ValidationException("密码错误");
            });

        return app;
    }
}