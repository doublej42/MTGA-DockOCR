using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MTGADockOCR.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace MTGADockOCR;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs args)
    {
        var package = new DataPackage();
        package.SetText(DeckListText.Text);
        Clipboard.SetContent(package);
        StatusBar.Title = "Copied";
        StatusBar.Message = "The resolved deck list is on the clipboard.";
        StatusBar.Severity = InfoBarSeverity.Success;
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs args)
    {
        var package = new DataPackage();
        package.SetText(DiagnosticText.Text);
        Clipboard.SetContent(package);
        SetStatus("Copied", "The diagnostic log is on the clipboard.", InfoBarSeverity.Success);
    }

    internal void SetDeckList(string deckList)
    {
        DeckListText.Text = deckList;
    }

    internal void SetRecognitionResults(IReadOnlyList<RecognizedCardResult> results)
    {
        RecognitionResultsText.Text = string.Join(
            Environment.NewLine,
            results.Select(result => result.IsMatched
                ? $"[MATCHED] {result.Quantity} {result.ClaudeName} -> {result.DatabaseName} [export: {result.ExportName}]"
                : $"[UNMATCHED] {result.Quantity} {result.ClaudeName}"));
    }

    internal void ShowWindow()
    {
        AppWindow.Show();
        Activate();
    }

    internal void SetStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
    }

    internal void SetApiKeyConfigured(bool isConfigured)
    {
        ApiKeyStatusText.Text = isConfigured ? "Claude API key configured" : "Claude API key not configured";
    }

    internal async Task SetCaptureReviewAsync(CaptureReview review)
    {
        FirstScreenshotImage.Source = await DecodePngAsync(review.FirstScreenshot);
        SecondScreenshotImage.Source = await DecodePngAsync(review.SecondScreenshot);
        CombinedScreenshotImage.Source = await DecodePngAsync(review.CombinedScreenshot);
        SendToClaudeButton.IsEnabled = review.CombinedScreenshot is not null;
    }

    internal void AppendDiagnostic(string message)
    {
        DiagnosticText.Text += message + Environment.NewLine;
        DiagnosticText.Select(DiagnosticText.Text.Length, 0);
    }

    private async void SaveApiKeyButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            await ((App)Application.Current).SaveApiKeyAsync(ApiKeyInput.Password);
            ApiKeyInput.Password = string.Empty;
            SetApiKeyConfigured(true);
            SetStatus("API key saved", "The Claude API key is encrypted for this Windows user.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            SetStatus("API key not saved", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void SendToClaudeButton_Click(object sender, RoutedEventArgs args)
    {
        ((App)Application.Current).SendToClaude();
    }

    private void ClearCapturesButton_Click(object sender, RoutedEventArgs args)
    {
        ((App)Application.Current).ClearCaptures();
    }

    private static async Task<BitmapImage?> DecodePngAsync(byte[]? pngBytes)
    {
        if (pngBytes is null)
        {
            return null;
        }

        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(pngBytes);
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}