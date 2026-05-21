using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotesDeFrais.Models;

namespace NotesDeFrais.Services;

public sealed class CognitoAuthService
{
    private const string IdTokenKey = "auth_id_token";
    private const string AccessTokenKey = "auth_access_token";
    private const string RefreshTokenKey = "auth_refresh_token";
    private const string FallbackPrefix = "fallback_";

    private readonly HttpClient httpClient = new();
    private readonly ExpenseAppConfiguration configuration;

    public CognitoAuthService(ExpenseAppConfiguration configuration)
    {
        this.configuration = configuration.Normalized();
    }

    public async Task<AppUserProfile?> RestoreUserAsync()
    {
        var token = await GetValidIdTokenAsync();
        return string.IsNullOrWhiteSpace(token) ? null : JwtReader.ReadUser(token);
    }

    public async Task<AppUserProfile> SignInAsync()
    {
        EnsureConfigured();

        var codeVerifier = CreateCodeVerifier();
        var state = CreateCodeVerifier();
        var authorizeUrl = BuildAuthorizeUrl(codeVerifier, state);

        var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = new Uri(authorizeUrl),
            CallbackUrl = new Uri(configuration.RedirectUri),
            PrefersEphemeralWebBrowserSession = true
        });

        if (!result.Properties.TryGetValue("state", out var returnedState)
            || !string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("La reponse Cognito ne correspond pas a la session de connexion.");
        }

        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Cognito n'a pas retourne de code d'autorisation.");
        }

        var tokenResponse = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = configuration.CognitoClientId,
            ["code"] = code,
            ["redirect_uri"] = configuration.RedirectUri,
            ["code_verifier"] = codeVerifier
        });

        await SaveTokensAsync(tokenResponse);

        return JwtReader.ReadUser(tokenResponse.IdToken);
    }

    public async Task<string?> GetValidIdTokenAsync()
    {
        var idToken = await GetTokenAsync(IdTokenKey);
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        if (!JwtReader.ExpiresSoon(idToken))
        {
            return idToken;
        }

        var refreshToken = await GetTokenAsync(RefreshTokenKey);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await SignOutAsync(openHostedLogout: false);
            return null;
        }

        var refreshed = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = configuration.CognitoClientId,
            ["refresh_token"] = refreshToken
        });

        await SaveTokensAsync(refreshed, refreshToken);
        return refreshed.IdToken;
    }

    public async Task SignOutAsync(bool openHostedLogout = true)
    {
        RemoveToken(IdTokenKey);
        RemoveToken(AccessTokenKey);
        RemoveToken(RefreshTokenKey);

        if (!openHostedLogout || string.IsNullOrWhiteSpace(configuration.CognitoDomain))
        {
            return;
        }

        var logoutUri = string.IsNullOrWhiteSpace(configuration.LogoutUri)
            ? configuration.RedirectUri
            : configuration.LogoutUri;

        var url = $"{configuration.CognitoDomain}/logout?client_id={Uri.EscapeDataString(configuration.CognitoClientId)}"
            + $"&logout_uri={Uri.EscapeDataString(logoutUri)}";

        await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
    }

    private string BuildAuthorizeUrl(string codeVerifier, string state)
    {
        var challenge = CreateCodeChallenge(codeVerifier);
        var values = new Dictionary<string, string>
        {
            ["client_id"] = configuration.CognitoClientId,
            ["response_type"] = "code",
            ["scope"] = configuration.Scopes,
            ["redirect_uri"] = configuration.RedirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state
        };

        return $"{configuration.CognitoDomain}/oauth2/authorize?{ToQuery(values)}";
    }

    private async Task<CognitoTokenResponse> RequestTokenAsync(Dictionary<string, string> values)
    {
        EnsureConfigured();

        using var content = new FormUrlEncodedContent(values);
        using var response = await httpClient.PostAsync($"{configuration.CognitoDomain}/oauth2/token", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Cognito a refuse la requete token ({(int)response.StatusCode}) : {body}");
        }

        return JsonSerializer.Deserialize<CognitoTokenResponse>(body, JsonOptions.Default)
            ?? throw new InvalidOperationException("La reponse token Cognito est vide.");
    }

    private async Task SaveTokensAsync(CognitoTokenResponse tokenResponse, string? existingRefreshToken = null)
    {
        await SetTokenAsync(IdTokenKey, tokenResponse.IdToken);
        await SetTokenAsync(AccessTokenKey, tokenResponse.AccessToken);

        var refreshToken = string.IsNullOrWhiteSpace(tokenResponse.RefreshToken)
            ? existingRefreshToken
            : tokenResponse.RefreshToken;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await SetTokenAsync(RefreshTokenKey, refreshToken);
        }
    }

    private static async Task<string?> GetTokenAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key) ?? Preferences.Get($"{FallbackPrefix}{key}", null);
        }
        catch
        {
            return Preferences.Get($"{FallbackPrefix}{key}", null);
        }
    }

    private static async Task SetTokenAsync(string key, string value)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
            Preferences.Remove($"{FallbackPrefix}{key}");
        }
        catch
        {
            Preferences.Set($"{FallbackPrefix}{key}", value);
        }
    }

    private static void RemoveToken(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch
        {
            // SecureStorage can be unavailable on unsigned local MacCatalyst builds.
        }

        Preferences.Remove($"{FallbackPrefix}{key}");
    }

    private void EnsureConfigured()
    {
        if (!configuration.IsComplete)
        {
            throw new InvalidOperationException("Renseigne l'URL API, le domaine Cognito, le client id et l'URI de retour avant de te connecter.");
        }
    }

    private static string ToQuery(Dictionary<string, string> values)
    {
        return string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static string CreateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed class CognitoTokenResponse
{
    [JsonPropertyName("id_token")]
    public string IdToken { get; init; } = "";

    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = "";

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "";
}
