using System.Globalization;

namespace NotesDeFrais.Models;

public sealed record UploadUrlRequest(string FileName, string ContentType);

public sealed record UploadUrlResponse(
    string Mode,
    string Key,
    string UploadUrl,
    string Method,
    Dictionary<string, string> Headers);

public sealed record CreateExpenseRequest(
    decimal Amount,
    string Category,
    string Description,
    string ReceiptS3Key,
    string ReceiptFileName,
    string ReceiptContentType);

public sealed record DecisionRequest(string Status, string? AdminComment);

public sealed record ApiError(string Error);

public sealed class ExpenseRequest
{
    public string RequestId { get; init; } = "";

    public string EmployeeId { get; init; } = "";

    public string EmployeeEmail { get; init; } = "";

    public string EmployeeName { get; init; } = "";

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "EUR";

    public string Category { get; init; } = "";

    public string Description { get; init; } = "";

    public string ReceiptS3Key { get; init; } = "";

    public string ReceiptFileName { get; init; } = "";

    public string ReceiptContentType { get; init; } = "";

    public string? ReceiptPreviewDataUrl { get; init; }

    public string? ReceiptUrl { get; init; }

    public string Status { get; init; } = "PENDING";

    public string? AdminComment { get; init; }

    public string? ReviewedBy { get; init; }

    public string CreatedAt { get; init; } = "";

    public string UpdatedAt { get; init; } = "";

    public string DisplayAmount => $"{Amount.ToString("N2", CultureInfo.CurrentCulture)} {Currency}";

    public string DisplayStatus => Status switch
    {
        "APPROVED" => "Acceptee",
        "REJECTED" => "Refusee",
        "PENDING" => "En attente",
        _ => Status
    };

    public string DisplayCreatedAt
    {
        get
        {
            if (DateTimeOffset.TryParse(CreatedAt, out var date))
            {
                return date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            }

            return CreatedAt;
        }
    }

    public bool HasReceipt => !string.IsNullOrWhiteSpace(ReceiptUrl);

    public bool HasAdminComment => !string.IsNullOrWhiteSpace(AdminComment);
}
