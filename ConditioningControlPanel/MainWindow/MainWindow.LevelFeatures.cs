using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Level-gated mini-features: Bubble Count, Bouncing Text, Mind Wipe, Brain Drain.
    public partial class MainWindow
    {
        #region Bubble Count (Level 50)

        internal void ChkBubbleCountEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ProgressionTab.ChkBubbleCountEnabled.IsChecked ?? false;
            App.Settings.Current.BubbleCountEnabled = isEnabled;
            
            // Immediately update service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.BubbleCount.Start();
                }
                else
                {
                    App.BubbleCount.Stop();
                }
                App.Logger?.Information("Bubble Count toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        internal void SliderBubbleCountFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtBubbleCountFreq == null) return;
            ProgressionTab.TxtBubbleCountFreq.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BubbleCountFrequency = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.BubbleCount.RefreshSchedule();
            }
            
            App.Settings.Save();
        }

        internal void CmbBubbleCountDifficulty_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || ProgressionTab.CmbBubbleCountDifficulty.SelectedItem == null) return;
            
            var item = ProgressionTab.CmbBubbleCountDifficulty.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item?.Tag != null && int.TryParse(item.Tag.ToString(), out int difficulty))
            {
                App.Settings.Current.BubbleCountDifficulty = difficulty;
                App.Settings.Save();
            }
        }

        internal void ChkBubbleCountStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ProgressionTab.ChkBubbleCountStrict.IsChecked ?? false;

            // Show warning when enabling strict mode
            if (isEnabled)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(this,
                    "Strict Bubble Count",
                    "• You will NOT be able to skip the bubble count challenge\n" +
                    "• You MUST answer correctly to dismiss\n" +
                    "• Wrong answers force you to REWATCH the video\n" +
                    "• Mercy system grants escape after 3 retries (if enabled)\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    // Detach handlers before reverting to avoid re-entrancy
                    // (_isLoading can be clobbered by other methods during the dialog's message pump)
                    ProgressionTab.ChkBubbleCountStrict.Checked -= ChkBubbleCountStrict_Changed;
                    ProgressionTab.ChkBubbleCountStrict.Unchecked -= ChkBubbleCountStrict_Changed;
                    ProgressionTab.ChkBubbleCountStrict.IsChecked = false;
                    ProgressionTab.ChkBubbleCountStrict.Checked += ChkBubbleCountStrict_Changed;
                    ProgressionTab.ChkBubbleCountStrict.Unchecked += ChkBubbleCountStrict_Changed;
                    return;
                }
            }

            App.Settings.Current.BubbleCountStrictLock = isEnabled;
            App.Settings.Save();
        }

        internal void BtnTestBubbleCount_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("test_bubblecount"); } catch { }
            App.BubbleCount.TriggerGame(forceTest: true);
        }

        #endregion

        #region Bouncing Text (Level 60)

        internal void ChkBouncingTextEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ProgressionTab.ChkBouncingTextEnabled.IsChecked ?? false;
            App.Settings.Current.BouncingTextEnabled = isEnabled;
            
            // Immediately update service if engine is running
            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.BouncingText.Start();
                }
                else
                {
                    App.BouncingText.Stop();
                }
                App.Logger?.Information("Bouncing Text toggled: {Enabled}", isEnabled);
            }
            
            App.Settings.Save();
        }

        internal void SliderBouncingTextSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtBouncingTextSpeed == null) return;
            ProgressionTab.TxtBouncingTextSpeed.Text = ((int)e.NewValue).ToString();
            App.Settings.Current.BouncingTextSpeed = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.BouncingText.Refresh();
            }
            App.Settings.Save();
        }

        internal void SliderBouncingTextSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtBouncingTextSize == null) return;
            ProgressionTab.TxtBouncingTextSize.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.BouncingTextSize = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.BouncingText.Refresh();
            }
            App.Settings.Save();
        }

        internal void BtnEditBouncingText_Click(object sender, RoutedEventArgs e)
        {
            var editor = new TextEditorDialog("Bouncing Text Phrases", App.Settings.Current.BouncingTextPool);
            editor.Owner = this;

            if (editor.ShowDialog() == true && editor.ResultData != null)
            {
                App.Settings.Current.BouncingTextPool = editor.ResultData;
                App.Logger?.Information("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
                App.Settings.Save();
            }
        }

        internal void ChkBouncingTextAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.BouncingTextAlwaysOnTop = ProgressionTab.ChkBouncingTextAlwaysOnTop.IsChecked ?? false;
            App.Settings.Save();
        }

        #endregion

        #region Mind Wipe (Lvl 75)

        internal void ChkMindWipeEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isEnabled = ProgressionTab.ChkMindWipeEnabled.IsChecked ?? false;
            App.Settings.Current.MindWipeEnabled = isEnabled;
            
            // Immediately update service if engine is running (non-session mode)
            if (_isRunning && _sessionEngine?.CurrentSession == null)
            {
                if (isEnabled)
                {
                    App.MindWipe.Start(App.Settings.Current.MindWipeFrequency, App.Settings.Current.MindWipeVolume / 100.0);
                }
                else
                {
                    App.MindWipe.Stop();
                }
                App.Logger?.Information("Mind Wipe toggled: {Enabled}", isEnabled);
            }
            App.Settings.Save();
        }

        internal void SliderMindWipeFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtMindWipeFreq == null) return;
            ProgressionTab.TxtMindWipeFreq.Text = $"{(int)e.NewValue}/h";
            App.Settings.Current.MindWipeFrequency = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.MindWipe.UpdateSettings(App.Settings.Current.MindWipeFrequency, App.Settings.Current.MindWipeVolume / 100.0);
            }
            App.Settings.Save();
        }

        internal void SliderMindWipeVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtMindWipeVolume == null) return;
            ProgressionTab.TxtMindWipeVolume.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.MindWipeVolume = (int)e.NewValue;
            
            if (_isRunning)
            {
                App.MindWipe.UpdateSettings(App.Settings.Current.MindWipeFrequency, App.Settings.Current.MindWipeVolume / 100.0);
            }
            App.Settings.Save();
        }

        internal void ChkMindWipeLoop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            var isLooping = ProgressionTab.ChkMindWipeLoop.IsChecked ?? false;
            App.Settings.Current.MindWipeLoop = isLooping;
            
            // Start/stop loop immediately
            if (isLooping)
            {
                App.MindWipe.StartLoop(App.Settings.Current.MindWipeVolume / 100.0);
            }
            else
            {
                App.MindWipe.StopLoop();
            }
            
            App.Settings.Save();
            App.Logger?.Information("Mind Wipe loop toggled: {Looping}", isLooping);
        }

        internal void BtnTestMindWipe_Click(object sender, RoutedEventArgs e)
        {
            App.MindWipe.TriggerOnce();
        }

        #endregion

        #region Brain Drain (Lvl 70)

        private void ChkBrainDrainEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ProgressionTab.ChkBrainDrainEnabled.IsChecked ?? false;
            App.Settings.Current.BrainDrainEnabled = isEnabled;

            if (_isRunning)
            {
                if (isEnabled)
                {
                    App.BrainDrain.Start();
                }
                else
                {
                    App.BrainDrain.Stop();
                }
                App.Logger?.Information("Brain Drain toggled: {Enabled}", isEnabled);
            }

            App.Settings.Save();
        }

        private void SliderBrainDrainIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || ProgressionTab.TxtBrainDrainIntensity == null) return;
            ProgressionTab.TxtBrainDrainIntensity.Text = $"{(int)e.NewValue}%";
            App.Settings.Current.BrainDrainIntensity = (int)e.NewValue;

            if (_isRunning)
            {
                App.BrainDrain.UpdateSettings();
            }
            App.Settings.Save();
        }

        private void ChkBrainDrainHighRefresh_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isHighRefresh = ProgressionTab.ChkBrainDrainHighRefresh.IsChecked ?? false;
            App.Settings.Current.BrainDrainHighRefresh = isHighRefresh;

            // If brain drain is running, restart it to apply new interval
            if (_isRunning && App.BrainDrain.IsRunning)
            {
                App.BrainDrain.Stop();
                App.BrainDrain.Start();
            }

            App.Logger?.Information("Brain Drain High Refresh toggled: {Enabled}", isHighRefresh);
            App.Settings.Save();
        }

        #endregion
    }
}
