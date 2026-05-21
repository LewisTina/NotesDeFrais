using System.Collections.ObjectModel;
using System.Globalization;
using NotesDeFrais.Models;
using NotesDeFrais.Services;

namespace NotesDeFrais;

public partial class MainPage : ContentPage
{
    private ExpenseAppConfiguration configuration = ExpenseAppConfiguration.Load();
    private CognitoAuthService authService;
    private ExpenseApiClient expenseApiClient;
    private AppUserProfile? currentUser;
    private string? selectedReceiptFileName;
    private string? selectedReceiptContentType;
    private byte[]? selectedReceiptBytes;
    private bool initialized;

    public ObservableCollection<ExpenseRequest> MyExpenses { get; } = [];

    public ObservableCollection<ExpenseRequest> AdminExpenses { get; } = [];

    public MainPage()
    {
        InitializeComponent();
        authService = new CognitoAuthService(configuration);
        expenseApiClient = new ExpenseApiClient(configuration, authService.GetValidIdTokenAsync);
        BindingContext = this;

        CategoryPicker.SelectedIndex = 0;
        AdminStatusPicker.SelectedIndex = 0;
        RefreshSessionUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (initialized)
        {
            return;
        }

        initialized = true;
        await RunSafelyAsync(async () =>
        {
            currentUser = await authService.RestoreUserAsync();
            RefreshSessionUi();

            if (currentUser is not null)
            {
                await RefreshAllAsync();
            }
        });
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            if (!configuration.IsComplete)
            {
                await DisplayAlertAsync("Connexion indisponible", "L'application n'est pas encore disponible. Contacte l'administrateur de l'application.", "OK");
                return;
            }

            currentUser = await authService.SignInAsync();
            RefreshSessionUi();
            await RefreshAllAsync();
        });
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            await authService.SignOutAsync();
            currentUser = null;
            MyExpenses.Clear();
            AdminExpenses.Clear();
            RefreshSessionUi();
        });
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(RefreshAllAsync);
    }

    private async void OnRefreshMineClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(RefreshMineAsync);
    }

    private async void OnRefreshAdminClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(RefreshAdminAsync);
    }

    private async void OnAdminStatusChanged(object? sender, EventArgs e)
    {
        if (currentUser?.IsAdmin == true)
        {
            await RunSafelyAsync(RefreshAdminAsync);
        }
    }

    private async void OnPickReceiptClicked(object? sender, EventArgs e)
    {
        try
        {
            SetBusy(false);
            var pickedReceipt = await FilePicker.Default.PickAsync(CreateReceiptPickOptions());

            if (pickedReceipt is null)
            {
                return;
            }

            if (!IsSupportedReceipt(pickedReceipt))
            {
                await DisplayAlertAsync("Format invalide", "Choisis une image au format JPG, PNG, WEBP ou GIF.", "OK");
                return;
            }

            SetBusy(true);
            var bytes = await ReadReceiptBytesAsync(pickedReceipt);
            SetSelectedReceipt(pickedReceipt.FileName, GetContentType(pickedReceipt.FileName, pickedReceipt.ContentType), bytes);
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("Justificatif", error.Message, "OK");
        }
        finally
        {
            SetBusy(false);
            RefreshSessionUi();
        }
    }

    private async void OnLoadReceiptPathClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var path = (ReceiptPathEntry.Text ?? "").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                await DisplayAlertAsync("Justificatif", "Renseigne le chemin complet du fichier.", "OK");
                return;
            }

            if (!File.Exists(path))
            {
                await DisplayAlertAsync("Justificatif", $"Fichier introuvable : {path}", "OK");
                return;
            }

            var fileName = Path.GetFileName(path);
            if (!IsSupportedReceipt(fileName, null))
            {
                await DisplayAlertAsync("Format invalide", "Choisis une image au format JPG, PNG, WEBP ou GIF.", "OK");
                return;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            SetSelectedReceipt(fileName, GetContentType(fileName, null), bytes);
        });
    }

    private async void OnCreateExpenseClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
                && !decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            {
                await DisplayAlertAsync("Montant invalide", "Renseigne un montant numerique.", "OK");
                return;
            }

            if (amount <= 0)
            {
                await DisplayAlertAsync("Montant invalide", "Le montant doit etre superieur a zero.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedReceiptFileName)
                || string.IsNullOrWhiteSpace(selectedReceiptContentType)
                || selectedReceiptBytes is null
                || selectedReceiptBytes.Length == 0)
            {
                await DisplayAlertAsync("Justificatif requis", "Ajoute une image du justificatif avant d'envoyer la demande.", "OK");
                return;
            }

            var category = CategoryPicker.SelectedItem?.ToString() ?? "Autre";
            var description = DescriptionEditor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlertAsync("Description requise", "Ajoute une courte description de la depense.", "OK");
                return;
            }

            var uploadUrl = await expenseApiClient.CreateUploadUrlAsync(selectedReceiptFileName, selectedReceiptContentType);
            await expenseApiClient.UploadReceiptAsync(uploadUrl, selectedReceiptBytes, selectedReceiptContentType);
            await expenseApiClient.CreateExpenseAsync(new CreateExpenseRequest(
                amount,
                category,
                description,
                uploadUrl.Key,
                selectedReceiptFileName,
                selectedReceiptContentType));

            ResetExpenseForm();
            await RefreshMineAsync();
            await DisplayAlertAsync("Demande envoyee", "La note de frais est maintenant en attente de validation.", "OK");
        });
    }

    private async void OnOpenReceiptClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await RunSafelyAsync(() => Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred));
    }

    private async void OnApproveExpenseClicked(object? sender, EventArgs e)
    {
        await DecideFromButtonAsync(sender, "APPROVED", "Accepter la demande");
    }

    private async void OnRejectExpenseClicked(object? sender, EventArgs e)
    {
        await DecideFromButtonAsync(sender, "REJECTED", "Refuser la demande");
    }

    private async Task DecideFromButtonAsync(object? sender, string status, string title)
    {
        if (sender is not Button button || button.CommandParameter is not string requestId)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var comment = await DisplayPromptAsync(title, "Commentaire optionnel", "Valider", "Annuler");
            if (comment is null)
            {
                return;
            }

            await expenseApiClient.DecideAsync(requestId, status, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
            await RefreshAdminAsync();
            await RefreshMineAsync();
        });
    }

    private async Task RefreshAllAsync()
    {
        await RefreshMineAsync();

        if (currentUser?.IsAdmin == true)
        {
            await RefreshAdminAsync();
        }
    }

    private async Task RefreshMineAsync()
    {
        var expenses = await expenseApiClient.ListMyExpensesAsync();
        ReplaceItems(MyExpenses, expenses);
        MyExpensesEmptyLabel.IsVisible = MyExpenses.Count == 0;
    }

    private async Task RefreshAdminAsync()
    {
        if (currentUser?.IsAdmin != true)
        {
            return;
        }

        var status = AdminStatusPicker.SelectedItem?.ToString() ?? "PENDING";
        var expenses = await expenseApiClient.ListAdminExpensesAsync(status);
        ReplaceItems(AdminExpenses, expenses);
        AdminExpensesEmptyLabel.IsVisible = AdminExpenses.Count == 0;
    }

    private void RefreshSessionUi()
    {
        var isConnected = currentUser is not null;
        SessionTitleLabel.Text = isConnected
            ? $"{currentUser!.DisplayName} - {currentUser.RoleLabel}"
            : "Connexion";
        SessionSubtitleLabel.Text = isConnected
            ? currentUser!.Email
            : configuration.IsComplete
                ? "Connecte-toi avec Cognito pour utiliser l'application."
                : "L'application n'est pas encore disponible. Contacte l'administrateur de l'application.";

        LoginButton.IsVisible = !isConnected;
        LoginButton.IsEnabled = configuration.IsComplete;
        LogoutButton.IsVisible = isConnected;
        RefreshButton.IsVisible = isConnected;
        CreateExpenseSection.IsVisible = isConnected;
        MyExpensesSection.IsVisible = isConnected;
        AdminSection.IsVisible = currentUser?.IsAdmin == true;
        MyExpensesEmptyLabel.IsVisible = isConnected && MyExpenses.Count == 0;
        AdminExpensesEmptyLabel.IsVisible = currentUser?.IsAdmin == true && AdminExpenses.Count == 0;
    }

    private void ResetExpenseForm()
    {
        AmountEntry.Text = "";
        DescriptionEditor.Text = "";
        selectedReceiptFileName = null;
        selectedReceiptContentType = null;
        selectedReceiptBytes = null;
        SelectedReceiptLabel.Text = "Aucun fichier selectionne";
        ReceiptPathEntry.Text = "";
        ReceiptPreviewImage.Source = null;
        ReceiptPreviewImage.IsVisible = false;
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            SetBusy(true);
            await action();
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("Erreur", error.Message, "OK");
        }
        finally
        {
            SetBusy(false);
            RefreshSessionUi();
        }
    }

    private void SetBusy(bool isBusy)
    {
        BusyIndicator.IsRunning = isBusy;
        BusyIndicator.IsVisible = isBusy;
        LoginButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        LogoutButton.IsEnabled = !isBusy;
    }

    private static void ReplaceItems(ObservableCollection<ExpenseRequest> target, IEnumerable<ExpenseRequest> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void SetSelectedReceipt(string fileName, string contentType, byte[] bytes)
    {
        selectedReceiptFileName = fileName;
        selectedReceiptContentType = contentType;
        selectedReceiptBytes = bytes;

        SelectedReceiptLabel.Text = $"{fileName} ({bytes.Length / 1024m:N1} Ko)";
        ReceiptPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        ReceiptPreviewImage.IsVisible = true;
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

    private static PickOptions CreateReceiptPickOptions()
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

    private static bool IsSupportedReceipt(FileResult file)
    {
        return IsSupportedReceipt(file.FileName, file.ContentType);
    }

    private static bool IsSupportedReceipt(string fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
    }

    private static async Task<byte[]> ReadReceiptBytesAsync(FileResult file)
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
}
