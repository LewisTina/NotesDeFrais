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
    private FileResult? selectedReceipt;
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
        ApplyConfigurationToForm();
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

    private async void OnSaveConfigurationClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(() =>
        {
            configuration = new ExpenseAppConfiguration
            {
                ApiBaseUrl = ApiUrlEntry.Text ?? "",
                CognitoDomain = CognitoDomainEntry.Text ?? "",
                CognitoClientId = ClientIdEntry.Text ?? "",
                RedirectUri = RedirectUriEntry.Text ?? "notesdefrais://auth",
                LogoutUri = "notesdefrais://signout"
            }.Normalized();

            configuration.Save();
            RebuildServices();
            ApplyConfigurationToForm();
            return DisplayAlertAsync("Configuration", "Les parametres ont ete enregistres sur cet appareil.", "OK");
        });
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            SaveConfigurationFromForm();
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
        await RunSafelyAsync(async () =>
        {
            selectedReceipt = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choisir un justificatif",
                FileTypes = FilePickerFileType.Images
            });

            if (selectedReceipt is null)
            {
                return;
            }

            await using var stream = await selectedReceipt.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            selectedReceiptBytes = memory.ToArray();

            SelectedReceiptLabel.Text = selectedReceipt.FileName;
            ReceiptPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(selectedReceiptBytes));
            ReceiptPreviewImage.IsVisible = true;
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

            if (selectedReceipt is null || selectedReceiptBytes is null)
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

            var contentType = GetContentType(selectedReceipt);
            var uploadUrl = await expenseApiClient.CreateUploadUrlAsync(selectedReceipt.FileName, contentType);
            await expenseApiClient.UploadReceiptAsync(uploadUrl, selectedReceiptBytes, contentType);
            await expenseApiClient.CreateExpenseAsync(new CreateExpenseRequest(
                amount,
                category,
                description,
                uploadUrl.Key,
                selectedReceipt.FileName,
                contentType));

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

    private void SaveConfigurationFromForm()
    {
        configuration = new ExpenseAppConfiguration
        {
            ApiBaseUrl = ApiUrlEntry.Text ?? "",
            CognitoDomain = CognitoDomainEntry.Text ?? "",
            CognitoClientId = ClientIdEntry.Text ?? "",
            RedirectUri = RedirectUriEntry.Text ?? "notesdefrais://auth",
            LogoutUri = "notesdefrais://signout"
        }.Normalized();

        configuration.Save();
        RebuildServices();
    }

    private void RebuildServices()
    {
        authService = new CognitoAuthService(configuration);
        expenseApiClient = new ExpenseApiClient(configuration, authService.GetValidIdTokenAsync);
    }

    private void ApplyConfigurationToForm()
    {
        ApiUrlEntry.Text = configuration.ApiBaseUrl;
        CognitoDomainEntry.Text = configuration.CognitoDomain;
        ClientIdEntry.Text = configuration.CognitoClientId;
        RedirectUriEntry.Text = configuration.RedirectUri;
    }

    private void RefreshSessionUi()
    {
        var isConnected = currentUser is not null;
        SessionTitleLabel.Text = isConnected
            ? $"{currentUser!.DisplayName} - {currentUser.RoleLabel}"
            : "Configuration et connexion";
        SessionSubtitleLabel.Text = isConnected
            ? currentUser!.Email
            : "Renseigne la sortie SAM puis connecte-toi avec Cognito.";

        LoginButton.IsVisible = !isConnected;
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
        selectedReceipt = null;
        selectedReceiptBytes = null;
        SelectedReceiptLabel.Text = "Aucun fichier selectionne";
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

    private static string GetContentType(FileResult file)
    {
        if (!string.IsNullOrWhiteSpace(file.ContentType))
        {
            return file.ContentType;
        }

        return Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
