using NotesDeFrais.Services;

namespace NotesDeFrais.Pages;

public partial class MyExpensesPage : ContentPage
{
    private readonly AppSession session = AppServices.Session;

    public MyExpensesPage()
    {
        InitializeComponent();
        BindingContext = session;
        session.MyExpenses.CollectionChanged += (_, _) => RefreshEmptyState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunSafelyAsync(async () =>
        {
            await session.InitializeAsync();
            if (session.IsConnected)
            {
                await session.RefreshMineAsync();
            }
        });
        RefreshEmptyState();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(session.RefreshMineAsync);
        RefreshEmptyState();
    }

    private async void OnOpenReceiptClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string url && !string.IsNullOrWhiteSpace(url))
        {
            await RunSafelyAsync(() => Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred));
        }
    }

    private void RefreshEmptyState()
    {
        EmptyState.IsVisible = session.MyExpenses.Count == 0;
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
