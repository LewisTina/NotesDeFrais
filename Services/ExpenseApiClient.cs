using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NotesDeFrais.Models;

namespace NotesDeFrais.Services;

public sealed class ExpenseApiClient
{
    private readonly HttpClient httpClient = new();
    private readonly ExpenseAppConfiguration configuration;
    private readonly Func<Task<string?>> getIdTokenAsync;

    public ExpenseApiClient(ExpenseAppConfiguration configuration, Func<Task<string?>> getIdTokenAsync)
    {
        this.configuration = configuration.Normalized();
        this.getIdTokenAsync = getIdTokenAsync;
    }

    public async Task<UploadUrlResponse> CreateUploadUrlAsync(string fileName, string contentType)
    {
        using var response = await SendApiAsync(HttpMethod.Post, "/expenses/upload-url", new UploadUrlRequest(fileName, contentType));
        return await ReadApiAsync<UploadUrlResponse>(response);
    }

    public async Task UploadReceiptAsync(UploadUrlResponse uploadUrl, byte[] fileBytes, string contentType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl.UploadUrl)
        {
            Content = new ByteArrayContent(fileBytes)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        foreach (var header in uploadUrl.Headers.Where(header => !header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Le chargement du justificatif a echoue ({(int)response.StatusCode}) : {body}");
        }
    }

    public async Task<ExpenseRequest> CreateExpenseAsync(CreateExpenseRequest expense)
    {
        using var response = await SendApiAsync(HttpMethod.Post, "/expenses", expense);
        return await ReadApiAsync<ExpenseRequest>(response);
    }

    public async Task<IReadOnlyList<ExpenseRequest>> ListMyExpensesAsync()
    {
        using var response = await SendApiAsync(HttpMethod.Get, "/expenses");
        return await ReadApiAsync<IReadOnlyList<ExpenseRequest>>(response);
    }

    public async Task<IReadOnlyList<ExpenseRequest>> ListAdminExpensesAsync(string status)
    {
        var path = $"/expenses?admin=true&status={Uri.EscapeDataString(status)}";
        using var response = await SendApiAsync(HttpMethod.Get, path);
        return await ReadApiAsync<IReadOnlyList<ExpenseRequest>>(response);
    }

    public async Task<ExpenseRequest> DecideAsync(string requestId, string status, string? comment)
    {
        using var response = await SendApiAsync(
            HttpMethod.Patch,
            $"/admin/expenses/{Uri.EscapeDataString(requestId)}",
            new DecisionRequest(status, comment));

        return await ReadApiAsync<ExpenseRequest>(response);
    }

    private async Task<HttpResponseMessage> SendApiAsync(HttpMethod method, string path, object? body = null)
    {
        if (string.IsNullOrWhiteSpace(configuration.ApiBaseUrl))
        {
            throw new InvalidOperationException("L'URL de l'API n'est pas configuree.");
        }

        var token = await getIdTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Connecte-toi avant d'appeler l'API.");
        }

        using var request = new HttpRequestMessage(method, $"{configuration.ApiBaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions.Default);
        }

        return await httpClient.SendAsync(request);
    }

    private static async Task<T> ReadApiAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body);
            throw new InvalidOperationException(error ?? $"Erreur API ({(int)response.StatusCode}) : {body}");
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions.Default)
            ?? throw new InvalidOperationException("La reponse API est vide.");
    }

    private static string? TryReadError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiError>(body, JsonOptions.Default)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
