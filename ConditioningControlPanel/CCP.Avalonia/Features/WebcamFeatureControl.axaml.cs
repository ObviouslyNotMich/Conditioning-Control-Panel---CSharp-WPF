using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class WebcamFeatureControl : UserControl
{
    public IPlatformCapabilities Capabilities { get; }

    public WebcamFeatureControl()
    {
        InitializeComponent();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
    }

    /// <summary>Host panel that receives the borrowed Lab webcam engine bar.</summary>
    public Panel WebcamSettingsHost => SettingsHost;
}
