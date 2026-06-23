namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform seam for periodic screen OCR used by the Awareness Engine.
/// Implementations are expected to be Windows-only; other platforms degrade
/// to a no-op.
/// </summary>
public interface IScreenOcrService
{
    bool IsRunning { get; }
    void Start();
    void Stop();
    void UpdateInterval(int intervalMs);
}
