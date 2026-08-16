using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MTGADockOCR.Core.Services;
using MTGADockOCR.Services;

namespace MTGADockOCR;

public partial class App : Application
{
    private Window? _window;
    private DpapiSecretStore? _secretStore;
    private GlobalHotkeyService? _hotkeyService;
    private CaptureWorkflow? _captureWorkflow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        var applicationDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MTGADockOCR");
        _secretStore = new DpapiSecretStore(Path.Combine(applicationDataPath, "claude-api-key.bin"));
        var databasePath = Path.Combine(AppContext.BaseDirectory, "Data", "AllPrintings.sqlite");
        _captureWorkflow = new CaptureWorkflow(
            new ForegroundWindowCaptureService(),
            new CardRepository(databasePath),
            _secretStore,
            new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
        _captureWorkflow.StatusChanged += OnWorkflowStatusChanged;
        _captureWorkflow.DeckReady += OnDeckReady;
        _captureWorkflow.RecognitionResultsReady += OnRecognitionResultsReady;
        _captureWorkflow.ReviewChanged += OnReviewChanged;
        _captureWorkflow.DiagnosticLogged += OnDiagnosticLogged;
        _ = ShowApiKeyConfigurationAsync();

        try
        {
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.Pressed += OnHotkeyPressed;
            ((MainWindow)_window).AppendDiagnostic("Global Ctrl+Alt+D hotkey registered.");
        }
        catch (InvalidOperationException exception)
        {
            ((MainWindow)_window).SetStatus("Hotkey unavailable", exception.Message, InfoBarSeverity.Warning);
            ((MainWindow)_window).AppendDiagnostic($"Global hotkey registration failed: {exception}");
        }

        _window.Activate();
    }

    internal async Task SaveApiKeyAsync(string apiKey)
    {
        if (_secretStore is null)
        {
            throw new InvalidOperationException("Settings are not initialized.");
        }

        await _secretStore.SetAsync(apiKey, CancellationToken.None);
    }

    internal void SendToClaude()
    {
        _captureWorkflow?.SendToClaude();
    }

    internal void ClearCaptures()
    {
        _captureWorkflow?.ClearCaptures();
    }

    private async Task ShowApiKeyConfigurationAsync()
    {
        try
        {
            var apiKey = await _secretStore!.GetAsync(CancellationToken.None);
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                var mainWindow = (MainWindow)_window;
                mainWindow.SetApiKeyConfigured(!string.IsNullOrWhiteSpace(apiKey));
                mainWindow.AppendDiagnostic(string.IsNullOrWhiteSpace(apiKey)
                    ? "No Claude API key is configured."
                    : "A Claude API key is configured for this Windows user.");
            });
        }
        catch (Exception exception)
        {
            _window?.DispatcherQueue.TryEnqueue(() => ((MainWindow)_window).AppendDiagnostic($"Could not read the saved Claude API key: {exception.Message}"));
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs args)
    {
        _window?.DispatcherQueue.TryEnqueue(() => ((MainWindow)_window).AppendDiagnostic("Global Ctrl+Alt+D hotkey received."));
        _captureWorkflow?.RequestCapture();
    }

    private void OnWorkflowStatusChanged(object? sender, string message)
    {
        _window?.DispatcherQueue.TryEnqueue(() => ((MainWindow)_window).SetStatus("MTGA Dock OCR", message, InfoBarSeverity.Informational));
    }

    private void OnDeckReady(object? sender, string deckList)
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            var mainWindow = (MainWindow)_window;
            mainWindow.SetDeckList(deckList);
            mainWindow.ShowWindow();
        });
    }

    private void OnRecognitionResultsReady(object? sender, IReadOnlyList<RecognizedCardResult> results)
    {
        _window?.DispatcherQueue.TryEnqueue(() => ((MainWindow)_window).SetRecognitionResults(results));
    }

    private void OnReviewChanged(object? sender, CaptureReview review)
    {
        _window?.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await ((MainWindow)_window).SetCaptureReviewAsync(review);
            }
            catch (Exception exception)
            {
                ((MainWindow)_window).AppendDiagnostic($"Could not display capture preview: {exception}");
            }
        });
    }

    private void OnDiagnosticLogged(object? sender, string message)
    {
        _window?.DispatcherQueue.TryEnqueue(() => ((MainWindow)_window).AppendDiagnostic(message));
    }
}