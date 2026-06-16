using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class PatreonTabView : UserControl
    {
        public PatreonTabView()
        {
            InitializeComponent();
        }

        private void BtnAddKeywordTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAddKeywordTrigger_Click(sender, e);
        }
        private void BtnBackupSettingsNow_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBackupSettingsNow_Click(sender, e);
        }
        private void BtnDiscordLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscordLogin_Click(sender, e);
        }
        private void BtnExportData_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnExportData_Click(sender, e);
        }
        private void BtnImportFromCustomTriggers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnImportFromCustomTriggers_Click(sender, e);
        }
        private void BtnKeywordTriggersStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnKeywordTriggersStartStop_Click(sender, e);
        }
        private void BtnLinkDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLinkDiscord_Click(sender, e);
        }
        private void BtnLinkPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLinkPatreon_Click(sender, e);
        }
        private void BtnPatreonLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPatreonLogin_Click(sender, e);
        }
        private void BtnPrivacyPolicy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPrivacyPolicy_Click(sender, e);
        }
        private void BtnRestoreSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRestoreSettings_Click(sender, e);
        }
        private void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnVisitPatreon_Click(sender, e);
        }
        private void ChkHighlightVisibleInCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkHighlightVisibleInCapture_Changed(sender, e);
        }
        private void ChkKeywordHighlightEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkKeywordHighlightEnabled_Changed(sender, e);
        }
        private void ChkScreenOcrEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkScreenOcrEnabled_Changed(sender, e);
        }
        private void CmbOcrConfirmation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbOcrConfirmation_SelectionChanged(sender, e);
        }
        private void CmbOcrHighlightMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbOcrHighlightMode_SelectionChanged(sender, e);
        }
        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.InnerScrollViewer_PreviewMouseWheel(sender, e);
        }
        private void SliderKeywordBufferTimeout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordBufferTimeout_ValueChanged(sender, e);
        }
        private void SliderKeywordGlobalCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordGlobalCooldown_ValueChanged(sender, e);
        }
        private void SliderKeywordHighlightDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordHighlightDuration_ValueChanged(sender, e);
        }
        private void SliderKeywordSessionMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderKeywordSessionMultiplier_ValueChanged(sender, e);
        }
        private void SliderScreenOcrInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderScreenOcrInterval_ValueChanged(sender, e);
        }
    }
}
