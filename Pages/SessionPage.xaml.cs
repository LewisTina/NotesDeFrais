using NotesDeFrais.Services;

namespace NotesDeFrais.Pages;

public partial class SessionPage : ContentPage
{
    private readonly AppSession session = AppServices.Session;

    public SessionPage()
    {
        InitializeComponent();
        BindingContext = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunSafelyAsync(session.InitializeAsync);
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(session.SignInAsync);
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(session.SignOutAsync);
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
