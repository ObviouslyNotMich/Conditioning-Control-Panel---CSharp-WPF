using CommunityToolkit.Mvvm.ComponentModel;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Base view-model for a shell tab entry. Subclass per tab so the
/// <see cref="Avalonia.Controls.TabControl"/> can pick a content template
/// by concrete type while sharing header bindings.
/// </summary>
public partial class TabItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _header = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private TabCapabilityRequirements _requiredCapabilities = TabCapabilityRequirements.None;

    public TabItemViewModel()
    {
    }

    public TabItemViewModel(string key, string header, string icon)
    {
        _key = key;
        _header = header;
        _icon = icon;
    }

    public TabItemViewModel(string key, string header, string icon, TabCapabilityRequirements requiredCapabilities)
    {
        _key = key;
        _header = header;
        _icon = icon;
        _requiredCapabilities = requiredCapabilities;
    }

    /// <summary>
    /// Called when this tab becomes the selected tab. Subclasses can start
    /// timers, polling, or other active work here.
    /// </summary>
    public virtual void OnSelected() { }

    /// <summary>
    /// Called when the user navigates away from this tab. Subclasses should
    /// stop timers, polling, or other active work here.
    /// </summary>
    public virtual void OnDeselected() { }
}
