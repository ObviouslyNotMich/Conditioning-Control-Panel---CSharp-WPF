using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Core.Models;

/// <summary>
/// One catalogue entry as returned by /api/enhancements/by-ht-url.
/// This mirrors the legacy WPF <c>ConditioningControlPanel.Services.CatalogueEntry</c>
/// so the Avalonia picker dialog can be typed without referencing the WPF head.
/// </summary>
public sealed record CatalogueEntry(
    string Id,
    string Title,
    string Description,
    string CreatorName,
    string? RemixerName,
    List<string> Tags,
    string? License,
    int ViewCount,
    string HtUrl,
    string? ThumbnailPath,
    string FileUrl)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title;

    public string BylineText => string.IsNullOrEmpty(RemixerName)
        ? Loc.GetF("dialog_catalogue_picker_by_fmt", CreatorName)
        : Loc.GetF("dialog_catalogue_picker_remix_by_fmt", RemixerName);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasTags => Tags is { Count: > 0 };

    public string ViewCountText => $"👁 {ViewCount}";

    public string LicenseText => string.IsNullOrWhiteSpace(License)
        ? Loc.Get("dialog_catalogue_picker_no_license")
        : License;
}
