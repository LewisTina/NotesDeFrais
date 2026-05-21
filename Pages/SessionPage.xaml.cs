using NotesDeFrais.Services;

namespace NotesDeFrais.Pages;

public partial class SessionPage : ContentPage
{
    private readonly AppSession session = AppServices.Session;

    public SessionPage()
    {
        InitializeComponent();
        BindingContext = session;
        ApplyConfiguration();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyConfiguration();
        await RunSafelyAsync(session.InitializeAsync);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            session.SaveConfiguration(ReadConfiguration());
            ApplyConfiguration();
            await DisplayAlertAsync("Configuration", "Parametres enregistres.", "OK");
        });
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(() => session.SignInAsync(ReadConfiguration()));
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(session.SignOutAsync);
    }

    private ExpenseAppConfiguration ReadConfiguration()
    {
        return new ExpenseAppConfiguration
        {
            ApiBaseUrl = ApiUrlEntry.Text ?? "",
            CognitoDomain = CognitoDomainEntry.Text ?? "",
            CognitoClientId = ClientIdEntry.Text ?? "",
            RedirectUri = RedirectUriEntry.Text ?? "notesdefrais://auth",
            LogoutUri = "notesdefrais://signout"
        }.Normalized();
    }

    private void ApplyConfiguration()
    {
        var configuration = session.Configuration;
        ApiUrlEntry.Text = configuration.ApiBaseUrl;
        CognitoDomainEntry.Text = configuration.CognitoDomain;
        ClientIdEntry.Text = configuration.CognitoClientId;
        RedirectUriEntry.Text = configuration.RedirectUri;
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
