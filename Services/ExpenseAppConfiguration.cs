namespace NotesDeFrais.Services;

public sealed record ExpenseAppConfiguration
{
    public string ApiBaseUrl { get; init; } = "";

    public string CognitoDomain { get; init; } = "";

    public string CognitoClientId { get; init; } = "";

    public string RedirectUri { get; init; } = "notesdefrais://auth";

    public string LogoutUri { get; init; } = "notesdefrais://signout";

    public string Scopes { get; init; } = "openid email profile";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ApiBaseUrl)
        && !string.IsNullOrWhiteSpace(CognitoDomain)
        && !string.IsNullOrWhiteSpace(CognitoClientId)
        && Uri.TryCreate(RedirectUri, UriKind.Absolute, out _);

    public static ExpenseAppConfiguration Load()
    {
        Console.WriteLine("Loading configuration...");
        return new ExpenseAppConfiguration
        {
            ApiBaseUrl = GetValue("EXPENSE_API_URL"),
            CognitoDomain = GetValue("COGNITO_DOMAIN"),
            CognitoClientId = GetValue("COGNITO_CLIENT_ID"),
            RedirectUri = GetValue("COGNITO_REDIRECT_URI", "notesdefrais://auth"),
            LogoutUri = GetValue("COGNITO_LOGOUT_URI", "notesdefrais://signout")
        }.Normalized();
    }

    public ExpenseAppConfiguration Normalized()
    {
        return this with
        {
            ApiBaseUrl = TrimTrailingSlash(ApiBaseUrl),
            CognitoDomain = NormalizeDomain(CognitoDomain),
            CognitoClientId = CognitoClientId.Trim(),
            RedirectUri = RedirectUri.Trim(),
            LogoutUri = LogoutUri.Trim()
        };
    }

    private static string GetValue(string environmentName, string fallback = "")
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return fallback;
    }

    private static string NormalizeDomain(string value)
    {
        var domain = TrimTrailingSlash(value);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return "";
        }

        return domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? domain
            : $"https://{domain}";
    }

    private static string TrimTrailingSlash(string value) => value.Trim().TrimEnd('/');
}
