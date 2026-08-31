namespace FastGateway.Options;

public sealed class BotProtectionOptions
{
    public const string Name = "BotProtection";

    public bool EnabledForAdminLogin { get; set; }

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string CookieSigningKey { get; set; } = string.Empty;

    public int ClearanceLifetimeMinutes { get; set; } = 30;

    public int ChallengeLifetimeMinutes { get; set; } = 5;

    public string VerifyEndpoint { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public string[] AllowedHostnames { get; set; } = [];

    public bool BindClearanceToIp { get; set; }

    public int VerifyRequestsPerMinute { get; set; } = 20;
}
