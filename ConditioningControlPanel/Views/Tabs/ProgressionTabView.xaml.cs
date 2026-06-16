using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class ProgressionTabView : UserControl
    {
        public ProgressionTabView()
        {
            InitializeComponent();
        }

        private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCheckUpdates_Click(sender, e);
        }
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscord_Click(sender, e);
        }
        private void BtnEditBouncingText_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEditBouncingText_Click(sender, e);
        }
        private void BtnLockCardSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLockCardSettings_Click(sender, e);
        }
        private void BtnManageLockCardPhrases_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnManageLockCardPhrases_Click(sender, e);
        }
        private void BtnReportBug_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnReportBug_Click(sender, e);
        }
        private void BtnSelectSpiral_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSelectSpiral_Click(sender, e);
        }
        private void BtnTestBubbleCount_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestBubbleCount_Click(sender, e);
        }
        private void BtnTestLockCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestLockCard_Click(sender, e);
        }
        private void BtnTestMindWipe_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestMindWipe_Click(sender, e);
        }
        private void ChkBouncingTextAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBouncingTextAlwaysOnTop_Changed(sender, e);
        }
        private void ChkBouncingTextEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBouncingTextEnabled_Changed(sender, e);
        }
        private void ChkBubbleCountEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBubbleCountEnabled_Changed(sender, e);
        }
        private void ChkBubbleCountStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBubbleCountStrict_Changed(sender, e);
        }
        private void ChkBubblesEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkBubblesEnabled_Changed(sender, e);
        }
        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDiscordRichPresence_Changed(sender, e);
        }
        private void ChkLockCardEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkLockCardEnabled_Changed(sender, e);
        }
        private void ChkLockCardStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkLockCardStrict_Changed(sender, e);
        }
        private void ChkMindWipeEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkMindWipeEnabled_Changed(sender, e);
        }
        private void ChkMindWipeLoop_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkMindWipeLoop_Changed(sender, e);
        }
        private void ChkPinkFilterEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkPinkFilterEnabled_Changed(sender, e);
        }
        private void ChkSpiralEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSpiralEnabled_Changed(sender, e);
        }
        private void CmbBubbleCountDifficulty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbBubbleCountDifficulty_Changed(sender, e);
        }
        private void SliderBouncingTextSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBouncingTextSize_Changed(sender, e);
        }
        private void SliderBouncingTextSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBouncingTextSpeed_Changed(sender, e);
        }
        private void SliderBubbleCountFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBubbleCountFreq_Changed(sender, e);
        }
        private void SliderBubbleFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBubbleFreq_Changed(sender, e);
        }
        private void SliderBubbleVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderBubbleVolume_Changed(sender, e);
        }
        private void SliderLockCardFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderLockCardFreq_Changed(sender, e);
        }
        private void SliderLockCardRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderLockCardRepeats_Changed(sender, e);
        }
        private void SliderMindWipeFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderMindWipeFreq_Changed(sender, e);
        }
        private void SliderMindWipeVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderMindWipeVolume_Changed(sender, e);
        }
        private void SliderMultiplier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderMultiplier_Changed(sender, e);
        }
        private void SliderPinkOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderPinkOpacity_Changed(sender, e);
        }
        private void SliderRampDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderRampDuration_Changed(sender, e);
        }
        private void SliderSpiralOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderSpiralOpacity_Changed(sender, e);
        }
    }
}
