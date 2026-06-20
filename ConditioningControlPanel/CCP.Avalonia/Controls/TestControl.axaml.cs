using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Controls;

public partial class TestControl : UserControl
{
    public TestControl()
    {
        InitializeComponent();
        MyText.Text = "World";
    }
}
