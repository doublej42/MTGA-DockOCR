using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MTGADockOCR.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace MTGADockOCR;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _claudeCountdownTimer;
    private DateTimeOffset _claudeDeadline;
    private bool _hasSendableCapture;
    private bool _isAnalyzing;

    public MainWindow()
    {
        InitializeComponent();
        _claudeCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _claudeCountdownTimer.Tick += (_, _) => UpdateClaudeCountdown();
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
        _hasSendableCapture = review.CombinedScreenshot is not null;
        SendToClaudeButton.IsEnabled = _hasSendableCapture && !_isAnalyzing;
    }

    internal void SetClaudeAnalysisState(bool isAnalyzing)
    {
        _isAnalyzing = isAnalyzing;
        ClaudeProgressPanel.Visibility = isAnalyzing ? Visibility.Visible : Visibility.Collapsed;
        ClaudeProgressRing.IsActive = isAnalyzing;
        SendToClaudeButton.IsEnabled = _hasSendableCapture && !isAnalyzing;
        ClearCapturesButton.IsEnabled = !isAnalyzing;

        if (isAnalyzing)
        {
            _claudeDeadline = DateTimeOffset.Now.AddMinutes(3);
            UpdateClaudeCountdown();
            _claudeCountdownTimer.Start();
        }
        else
        {
            _claudeCountdownTimer.Stop();
        }
    }

    internal void SetClaudeResponseReceived()
    {
        _claudeCountdownTimer.Stop();
        ClaudeProgressRing.IsActive = true;
        ClaudeProgressText.Text = "Claude responded. Matching recognized names against the local card database.";
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

    private void UpdateClaudeCountdown()
    {
        var remaining = _claudeDeadline - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        ClaudeProgressText.Text = $"Waiting for Claude. This can take up to 3 minutes ({remaining:mm\\:ss} remaining).";
    }
}