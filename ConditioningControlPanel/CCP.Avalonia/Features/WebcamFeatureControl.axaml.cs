using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class WebcamFeatureControl : UserControl
{
    public IPlatformCapabilities Capabilities { get; }

    /// <summary>Shared Lab tab view model that backs the webcam engine bar.</summary>
    public LabTabViewModel WebcamViewModel { get; }

    public WebcamFeatureControl()
    {
        InitializeComponent();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
        WebcamViewModel = App.Services.GetRequiredService<LabTabViewModel>();
    }
}
