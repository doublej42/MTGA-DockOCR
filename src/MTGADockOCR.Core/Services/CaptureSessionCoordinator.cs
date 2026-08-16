namespace MTGADockOCR.Core.Services;

public sealed class CaptureSessionCoordinator
{
    private readonly TimeSpan _pairingWindow;
    private DateTimeOffset? _firstCaptureAt;

    public CaptureSessionCoordinator(TimeSpan? pairingWindow = null)
    {
        _pairingWindow = pairingWindow ?? TimeSpan.FromSeconds(5);
    }

    public CaptureAction RegisterCapture(DateTimeOffset capturedAt)
    {
        if (_firstCaptureAt is null)
        {
            _firstCaptureAt = capturedAt;
            return CaptureAction.WaitForPair;
        }

        if (capturedAt - _firstCaptureAt < _pairingWindow)
        {
            _firstCaptureAt = null;
            return CaptureAction.AnalyzePair;
        }

        _firstCaptureAt = capturedAt;
        return CaptureAction.AnalyzeSingleAndWaitForPair;
    }

    public bool TryExpire(DateTimeOffset now)
    {
        if (_firstCaptureAt is null || now - _firstCaptureAt < _pairingWindow)
        {
            return false;
        }

        _firstCaptureAt = null;
        return true;
    }
}

public enum CaptureAction
{
    WaitForPair,
    AnalyzePair,
    AnalyzeSingleAndWaitForPair,
}