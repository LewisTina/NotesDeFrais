namespace NotesDeFrais.Services;

public sealed record ExpenseAppConfiguration
{
    private const string ApiUrlKey = "expense_api_url";
    private const string CognitoDomainKey = "cognito_domain";
    private const string CognitoClientIdKey = "cognito_client_id";
    private const string RedirectUriKey = "cognito_redirect_uri";
    private const string LogoutUriKey = "cognito_logout_uri";

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
            ApiBaseUrl = GetValue("EXPENSE_API_URL", ApiUrlKey),
            CognitoDomain = GetValue("COGNITO_DOMAIN", CognitoDomainKey),
            CognitoClientId = GetValue("COGNITO_CLIENT_ID", CognitoClientIdKey),
            RedirectUri = GetValue("COGNITO_REDIRECT_URI", RedirectUriKey, "notesdefrais://auth"),
            LogoutUri = GetValue("COGNITO_LOGOUT_URI", LogoutUriKey, "notesdefrais://signout")
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

    public void Save()
    {
        var normalized = Normalized();
        Preferences.Set(ApiUrlKey, normalized.ApiBaseUrl);
        Preferences.Set(CognitoDomainKey, normalized.CognitoDomain);
        Preferences.Set(CognitoClientIdKey, normalized.CognitoClientId);
        Preferences.Set(RedirectUriKey, normalized.RedirectUri);
        Preferences.Set(LogoutUriKey, normalized.LogoutUri);
    }

    private static string GetValue(string environmentName, string preferenceName, string fallback = "")
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return Preferences.Get(preferenceName, fallback);
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
