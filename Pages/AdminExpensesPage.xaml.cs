using NotesDeFrais.Services;

namespace NotesDeFrais.Pages;

public partial class AdminExpensesPage : ContentPage
{
    private readonly AppSession session = AppServices.Session;

    public AdminExpensesPage()
    {
        InitializeComponent();
        BindingContext = session;
        StatusPicker.SelectedIndex = 0;
        session.AdminExpenses.CollectionChanged += (_, _) => RefreshState();
        session.PropertyChanged += (_, _) => RefreshState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunSafelyAsync(async () =>
        {
            await session.InitializeAsync();
            if (session.IsAdmin)
            {
                await session.RefreshAdminAsync(CurrentStatus);
            }
        });
        RefreshState();
    }

    private string CurrentStatus => StatusPicker.SelectedItem?.ToString() ?? "PENDING";

    private async void OnStatusChanged(object? sender, EventArgs e)
    {
        if (session.IsAdmin)
        {
            await RunSafelyAsync(() => session.RefreshAdminAsync(CurrentStatus));
        }

        RefreshState();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(() => session.RefreshAdminAsync(CurrentStatus));
        RefreshState();
    }

    private async void OnOpenReceiptClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string url && !string.IsNullOrWhiteSpace(url))
        {
            await RunSafelyAsync(() => Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred));
        }
    }

    private async void OnApproveClicked(object? sender, EventArgs e)
    {
        await DecideAsync(sender, "APPROVED", "Accepter la demande");
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        await DecideAsync(sender, "REJECTED", "Refuser la demande");
    }

    private async Task DecideAsync(object? sender, string status, string title)
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

            await session.DecideAsync(requestId, status, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), CurrentStatus);
        });
        RefreshState();
    }

    private void RefreshState()
    {
        AccessState.IsVisible = !session.IsAdmin;
        EmptyState.IsVisible = session.IsAdmin && session.AdminExpenses.Count == 0;
        ListPanel.IsVisible = session.IsAdmin && session.AdminExpenses.Count > 0;
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            await DisplayAlertAsync("Erreur", error.Message, "OK");
        }
    }
}
