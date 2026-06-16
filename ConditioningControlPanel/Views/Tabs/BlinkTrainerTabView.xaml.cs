using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class BlinkTrainerTabView : UserControl
    {
        public BlinkTrainerTabView()
        {
            InitializeComponent();
        }

        private void BlinkTrainerMixOptionMix_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerMixOptionMix_Click(sender, e);
        }
        private void BlinkTrainerMixOptionSame_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerMixOptionSame_Click(sender, e);
        }
        private void BlinkTrainerSlider_DragEnd(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerSlider_DragEnd(sender, e);
        }
        private void BlinkTrainerSlider_DragStart(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerSlider_DragStart(sender, e);
        }
        private void BlinkTrainerSlider_LostCapture(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerSlider_LostCapture(sender, e);
        }
        private void BlinkTrainerStageMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BlinkTrainerStageMedia_MediaEnded(sender, e);
        }
        private void BtnBlinkTrainerAddFolderCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerAddFolderCard_Click(sender, e);
        }
        private void BtnBlinkTrainerCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerCalibrate_Click(sender, e);
        }
        private void BtnBlinkTrainerGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerGateUnlock_Click(sender, e);
        }
        private void BtnBlinkTrainerManageConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerManageConsent_Click(sender, e);
        }
        private void BtnBlinkTrainerQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerQuickRecal_Click(sender, e);
        }
        private void BtnBlinkTrainerRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerRevokeConsent_Click(sender, e);
        }
        private void BtnBlinkTrainerStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerStartSession_Click(sender, e);
        }
        private void BtnBlinkTrainerStartStopTracker_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerStartStopTracker_Click(sender, e);
        }
        private void BtnBlinkTrainerWebcamRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBlinkTrainerWebcamRefresh_Click(sender, e);
        }
        private void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBlinkRecalShortcut_Changed(sender, e);
        }
        private void ChkBlinkTrainerRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBlinkTrainerRestrictGazeToCalScreen_Changed(sender, e);
        }
        private void CmbBlinkTrainerWebcamDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbBlinkTrainerWebcamDevice_SelectionChanged(sender, e);
        }
        private void CmbBlinkTrainerWebcamMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbBlinkTrainerWebcamMonitor_SelectionChanged(sender, e);
        }
        private void SliderBlinkTrainerDurationNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBlinkTrainerDurationNew_Changed(sender, e);
        }
        private void SliderBlinkTrainerOpacityNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBlinkTrainerOpacityNew_Changed(sender, e);
        }
        private void SliderBlinkTrainerOpacityNew_Loaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBlinkTrainerOpacityNew_Loaded(sender, e);
        }
        private void ToggleBlinkTrainerIncludeVideos_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ToggleBlinkTrainerIncludeVideos_Changed(sender, e);
        }
    }
}
