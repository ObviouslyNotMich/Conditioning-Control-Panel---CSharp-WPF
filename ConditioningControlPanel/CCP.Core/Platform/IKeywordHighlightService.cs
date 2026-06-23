using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform seam for the keyword/OCR highlight overlay that briefly
/// glows around matched words on screen.
/// </summary>
public interface IKeywordHighlightService
{
    void ShowHighlight(List<OcrWordHit> words);
    void RefreshCaptureVisibility();
}
