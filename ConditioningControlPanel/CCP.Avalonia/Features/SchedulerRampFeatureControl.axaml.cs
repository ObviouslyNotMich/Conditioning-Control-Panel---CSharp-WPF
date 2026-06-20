using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Combined Scheduler + Intensity Ramp panel. Composes the two existing
/// feature controls so the single bottom-bar card opens both sets of
/// controls in one popup. No extra logic here — each embedded control
/// owns its own settings load/save.
/// </summary>
public partial class SchedulerRampFeatureControl : UserControl
{
    public SchedulerRampFeatureControl()
    {
        InitializeComponent();
    }
}
