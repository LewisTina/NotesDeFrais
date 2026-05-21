namespace NotesDeFrais.Services;

public static class ReceiptFileService
{
    public static async Task<ReceiptDraft?> PickAsync()
    {
        var result = await FilePicker.Default.PickAsync(CreatePickOptions());
        if (result is null)
        {
            return null;
        }

        if (!IsSupported(result.FileName, result.ContentType))
        {
            throw new InvalidOperationException("Choisis une image au format JPG, PNG, WEBP ou GIF.");
        }

        var bytes = await ReadBytesAsync(result);
        return new ReceiptDraft(result.FileName, GetContentType(result.FileName, result.ContentType), bytes);
    }

    public static async Task<ReceiptDraft> LoadPathAsync(string path)
    {
        var normalizedPath = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new InvalidOperationException("Renseigne le chemin complet du fichier.");
        }

        if (!File.Exists(normalizedPath))
        {
            throw new InvalidOperationException($"Fichier introuvable : {normalizedPath}");
        }

        var fileName = Path.GetFileName(normalizedPath);
        if (!IsSupported(fileName, null))
        {
            throw new InvalidOperationException("Choisis une image au format JPG, PNG, WEBP ou GIF.");
        }

        var bytes = await File.ReadAllBytesAsync(normalizedPath);
        return new ReceiptDraft(fileName, GetContentType(fileName, null), bytes);
    }

    private static PickOptions CreatePickOptions()
    {
        if (DeviceInfo.Platform == DevicePlatform.MacCatalyst)
        {
            return new PickOptions
            {
                PickerTitle = "Choisir un justificatif"
            };
        }

        return new PickOptions
        {
            PickerTitle = "Choisir un justificatif",
            FileTypes = FilePickerFileType.Images
        };
    }

    private static async Task<byte[]> ReadBytesAsync(FileResult file)
    {
        if (DeviceInfo.Platform == DevicePlatform.MacCatalyst
            && !string.IsNullOrWhiteSpace(file.FullPath)
            && File.Exists(file.FullPath))
        {
            return await File.ReadAllBytesAsync(file.FullPath);
        }

        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static bool IsSupported(string fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
    }

    private static string GetContentType(string fileName, string? reportedContentType)
    {
        if (!string.IsNullOrWhiteSpace(reportedContentType)
            && reportedContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return reportedContentType;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
