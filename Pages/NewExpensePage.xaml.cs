using System.Globalization;
using NotesDeFrais.Services;

namespace NotesDeFrais.Pages;

public partial class NewExpensePage : ContentPage
{
    private readonly AppSession session = AppServices.Session;
    private ReceiptDraft? selectedReceipt;

    public NewExpensePage()
    {
        InitializeComponent();
        BindingContext = session;
        CategoryPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunSafelyAsync(session.InitializeAsync);
    }

    private async void OnPickReceiptClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            var receipt = await ReceiptFileService.PickAsync();
            if (receipt is not null)
            {
                SetReceipt(receipt);
            }
        });
    }

    private async void OnLoadReceiptPathClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            SetReceipt(await ReceiptFileService.LoadPathAsync(ReceiptPathEntry.Text ?? ""));
        });
    }

    private async void OnCreateExpenseClicked(object? sender, EventArgs e)
    {
        await RunSafelyAsync(async () =>
        {
            if (!session.IsConnected)
            {
                await DisplayAlertAsync("Connexion requise", "Connecte-toi avant de creer une demande.", "OK");
                return;
            }

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

            var description = DescriptionEditor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlertAsync("Description requise", "Ajoute une courte description de la depense.", "OK");
                return;
            }

            if (selectedReceipt is null)
            {
                await DisplayAlertAsync("Justificatif requis", "Ajoute une image du justificatif avant d'envoyer la demande.", "OK");
                return;
            }

            await session.CreateExpenseAsync(
                amount,
                CategoryPicker.SelectedItem?.ToString() ?? "Autre",
                description,
                selectedReceipt);

            ResetForm();
            await DisplayAlertAsync("Demande envoyee", "La note de frais est maintenant en attente de validation.", "OK");
        });
    }

    private void SetReceipt(ReceiptDraft receipt)
    {
        selectedReceipt = receipt;
        ReceiptEmptyState.IsVisible = false;
        ReceiptPreviewPanel.IsVisible = true;
        ReceiptNameLabel.Text = receipt.FileName;
        ReceiptSizeLabel.Text = $"{receipt.Bytes.Length / 1024m:N1} Ko";
        ReceiptPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(receipt.Bytes));
    }

    private void ResetForm()
    {
        AmountEntry.Text = "";
        DescriptionEditor.Text = "";
        ReceiptPathEntry.Text = "";
        selectedReceipt = null;
        ReceiptPreviewImage.Source = null;
        ReceiptPreviewPanel.IsVisible = false;
        ReceiptEmptyState.IsVisible = true;
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
