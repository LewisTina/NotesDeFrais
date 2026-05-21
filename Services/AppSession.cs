using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NotesDeFrais.Models;

namespace NotesDeFrais.Services;

public sealed class AppSession : INotifyPropertyChanged
{
    private ExpenseAppConfiguration configuration = ExpenseAppConfiguration.Load();
    private CognitoAuthService authService;
    private ExpenseApiClient expenseApiClient;
    private AppUserProfile? currentUser;
    private bool isBusy;
    private bool initialized;

    public AppSession()
    {
        authService = new CognitoAuthService(configuration);
        expenseApiClient = new ExpenseApiClient(configuration, authService.GetValidIdTokenAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ExpenseRequest> MyExpenses { get; } = [];

    public ObservableCollection<ExpenseRequest> AdminExpenses { get; } = [];

    public ExpenseAppConfiguration Configuration => configuration;

    public AppUserProfile? CurrentUser
    {
        get => currentUser;
        private set
        {
            if (currentUser == value)
            {
                return;
            }

            currentUser = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(RoleLabel));
            OnPropertyChanged(nameof(SessionTitle));
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value)
            {
                return;
            }

            isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected => CurrentUser is not null;

    public bool IsAdmin => CurrentUser?.IsAdmin == true;

    public string DisplayName => CurrentUser?.DisplayName ?? "Non connecte";

    public string Email => CurrentUser?.Email ?? "Connecte-toi pour utiliser l'application";

    public string RoleLabel => CurrentUser?.RoleLabel ?? "Session inactive";

    public string SessionTitle => IsConnected ? $"{DisplayName} - {RoleLabel}" : "Configuration et connexion";

    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await RunAsync(async () =>
        {
            CurrentUser = await authService.RestoreUserAsync();
            if (IsConnected)
            {
                await RefreshAllAsync();
            }
        });
    }

    public void SaveConfiguration(ExpenseAppConfiguration nextConfiguration)
    {
        configuration = nextConfiguration.Normalized();
        configuration.Save();
        RebuildClients();
        OnPropertyChanged(nameof(Configuration));
    }

    public async Task SignInAsync(ExpenseAppConfiguration nextConfiguration)
    {
        await RunAsync(async () =>
        {
            SaveConfiguration(nextConfiguration);
            CurrentUser = await authService.SignInAsync();
            await RefreshAllAsync();
        });
    }

    public async Task SignOutAsync()
    {
        await RunAsync(async () =>
        {
            await authService.SignOutAsync();
            CurrentUser = null;
            MyExpenses.Clear();
            AdminExpenses.Clear();
        });
    }

    public async Task CreateExpenseAsync(decimal amount, string category, string description, ReceiptDraft receipt)
    {
        await RunAsync(async () =>
        {
            var uploadUrl = await expenseApiClient.CreateUploadUrlAsync(receipt.FileName, receipt.ContentType);
            await expenseApiClient.UploadReceiptAsync(uploadUrl, receipt.Bytes, receipt.ContentType);
            await expenseApiClient.CreateExpenseAsync(new CreateExpenseRequest(
                amount,
                category,
                description,
                uploadUrl.Key,
                receipt.FileName,
                receipt.ContentType));

            await RefreshMineAsync();
        });
    }

    public async Task RefreshAllAsync()
    {
        await RefreshMineAsync();
        if (IsAdmin)
        {
            await RefreshAdminAsync("PENDING");
        }
    }

    public async Task RefreshMineAsync()
    {
        await RunAsync(async () =>
        {
            var expenses = await expenseApiClient.ListMyExpensesAsync();
            ReplaceItems(MyExpenses, expenses);
        });
    }

    public async Task RefreshAdminAsync(string status)
    {
        if (!IsAdmin)
        {
            AdminExpenses.Clear();
            return;
        }

        await RunAsync(async () =>
        {
            var expenses = await expenseApiClient.ListAdminExpensesAsync(status);
            ReplaceItems(AdminExpenses, expenses);
        });
    }

    public async Task DecideAsync(string requestId, string status, string? comment, string currentFilter)
    {
        await RunAsync(async () =>
        {
            await expenseApiClient.DecideAsync(requestId, status, comment);
            await RefreshAdminAsync(currentFilter);
            await RefreshMineAsync();
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildClients()
    {
        authService = new CognitoAuthService(configuration);
        expenseApiClient = new ExpenseApiClient(configuration, authService.GetValidIdTokenAsync);
    }

    private static void ReplaceItems(ObservableCollection<ExpenseRequest> target, IEnumerable<ExpenseRequest> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ReceiptDraft(string FileName, string ContentType, byte[] Bytes);
