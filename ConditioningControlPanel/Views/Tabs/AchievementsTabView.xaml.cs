using System;
using System.Diagnostics;
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
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.patreon.com/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Patreon link");
            }
        }
    }
}
