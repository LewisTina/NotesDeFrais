using System.Text;
using System.Text.Json;
using NotesDeFrais.Models;

namespace NotesDeFrais.Services;

public static class JwtReader
{
    public static AppUserProfile ReadUser(string jwt)
    {
        using var document = ReadPayload(jwt);
        var root = document.RootElement;
        var email = GetString(root, "email") ?? "unknown@example.com";
        var name = GetString(root, "name")
            ?? GetString(root, "given_name")
            ?? email;
        var groups = GetGroups(root);

        return new AppUserProfile(
            GetString(root, "sub") ?? "",
            email,
            name,
            groups);
    }

    public static bool ExpiresSoon(string jwt)
    {
        using var document = ReadPayload(jwt);
        if (!document.RootElement.TryGetProperty("exp", out var expElement)
            || !expElement.TryGetInt64(out var exp))
        {
            return true;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
        return expiresAt <= DateTimeOffset.UtcNow.AddMinutes(2);
    }

    private static JsonDocument ReadPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Le jeton Cognito est invalide.");
        }

        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonDocument.Parse(json);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlySet<string> GetGroups(JsonElement root)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("cognito:groups", out var element))
        {
            return groups;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in element.EnumerateArray())
            {
                if (group.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(group.GetString()))
                {
                    groups.Add(group.GetString()!);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            foreach (var group in element.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                groups.Add(group);
            }
        }

        return groups;
    }
}
