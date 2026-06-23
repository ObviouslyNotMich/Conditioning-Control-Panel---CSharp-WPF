namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Trend information for quiz scores in a category.
/// </summary>
public class QuizScoreTrend
{
    public int LatestPercent { get; set; }
    public int PreviousPercent { get; set; }
    public int AveragePercent { get; set; }
    public int QuizCount { get; set; }
    public TrendDirection Direction { get; set; }
    public int DeltaPercent { get; set; }
}
