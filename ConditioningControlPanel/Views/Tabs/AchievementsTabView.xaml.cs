using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AchievementsTabView : UserControl
    {
        public AchievementsTabView()
        {
            InitializeComponent();
        }

        private void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnVisitPatreon_Click(sender, e);
        }
    }
}
