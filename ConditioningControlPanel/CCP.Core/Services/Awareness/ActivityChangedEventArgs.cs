using System;

namespace ConditioningControlPanel.Core.Services.Awareness;

/// <summary>
/// Event args for activity change events - includes specific detected service/app.
/// Privacy-focused: only the resolved category/name/cluster is surfaced; raw window
/// titles are never stored or logged.
/// </summary>
public class ActivityChangedEventArgs : EventArgs
{
    public ActivityCategory Category { get; }
    public ActivityCategory PreviousCategory { get; }
    public string DetectedName { get; }
    public string ServiceName { get; }
    public string PageTitle { get; }
    public bool IsNewService { get; }
    public string PreviousServiceName { get; }
    public string AppCluster { get; }
    public string AppId { get; }

    public ActivityChangedEventArgs(
        ActivityCategory category,
        ActivityCategory previousCategory,
        string detectedName,
        string? serviceName = null,
        string? pageTitle = null,
        bool isNewService = false,
        string? previousServiceName = null,
        string? appCluster = null,
        string? appId = null)
    {
        Category = category;
        PreviousCategory = previousCategory;
        DetectedName = detectedName;
        ServiceName = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
        PageTitle = pageTitle ?? "";
        IsNewService = isNewService;
        PreviousServiceName = previousServiceName ?? "";
        AppCluster = appCluster ?? "";
        AppId = appId ?? "";
    }
}
