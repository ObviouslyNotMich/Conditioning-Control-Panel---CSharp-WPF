using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>Features surfaced as quick-toggle chips on the dashboard premium rail.</summary>
    public enum PremiumFeature { Takeover, Awareness, Haptics, Lockdown, Blink, Remote }

    // Dashboard premium quick-toggle rail (left of the feature grid).
    public partial class MainWindow
    {
        private static readonly Brush PremiumDotOn = CreateFrozenBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush PremiumDotOff = CreateFrozenBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private bool _premiumRailSubscribed;
        private bool _lockdownEventsBound;
        private int _railLockdownMinutes = 15;

        private static Brush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Subscribe to patron-status changes and paint the rail for the first time.</summary>
        internal void InitPremiumRail()
        {
            if (!_premiumRailSubscribed && App.Patreon != null)
            {
                try { App.Patreon.TierChanged += (s, e) => Dispatcher.BeginInvoke(new Action(RefreshPremiumRail)); }
                catch { }
                _premiumRailSubscribed = true;
            }
            if (!_lockdownEventsBound && App.Lockdown != null)
            {
                App.Lockdown.LockdownActivated += () => Dispatcher.BeginInvoke(new Action(() => SetLockdownActiveUi(true)));
                App.Lockdown.LockdownDeactivated += () => Dispatcher.BeginInvoke(new Action(() => SetLockdownActiveUi(false)));
                App.Lockdown.CountdownTick += ts => Dispatcher.BeginInvoke(new Action(() => UpdateLockdownCountdown(ts)));
                _lockdownEventsBound = true;
            }
            RefreshPremiumRail();
        }

        // --- Lockdown chip: +/- duration, double-warning activate, live countdown ---

        internal void PremiumLockdownAdjust(int deltaMinutes)
        {
            _railLockdownMinutes = Math.Clamp(_railLockdownMinutes + deltaMinutes, 5, 180);
            if (SettingsTab?.TxtLockdownMins != null)
                SettingsTab.TxtLockdownMins.Text = _railLockdownMinutes + "m";
        }

        internal void PremiumLockdownActivate()
        {
            if (App.Lockdown == null || App.Lockdown.IsActive) return;
            var minutes = _railLockdownMinutes;
            var confirmed = WarningDialog.ShowDoubleWarning(this, "Lockdown Mode",
                "- You will be LOCKED IN for " + minutes + " minutes\n" +
                "- Strict Lock will be FORCED ON\n" +
                "- Panic Key will be DISABLED\n" +
                "- Alt+F4, Alt+Tab, Windows key, and Escape will be BLOCKED\n" +
                "- You CANNOT close or minimize the application\n" +
                "- The only escape is waiting for the timer to expire\n" +
                "  (or Ctrl+Alt+Del → Task Manager as a safety valve)");
            if (!confirmed) return;
            App.Lockdown.Activate(TimeSpan.FromMinutes(minutes));
        }

        private void SetLockdownActiveUi(bool active)
        {
            if (SettingsTab == null) return;
            if (SettingsTab.LockdownSetRow != null)
                SettingsTab.LockdownSetRow.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsTab.BtnLockdownGo != null)
                SettingsTab.BtnLockdownGo.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsTab.TxtLockdownCountdown != null)
                SettingsTab.TxtLockdownCountdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active) UpdateLockdownCountdown(App.Lockdown?.Remaining ?? TimeSpan.Zero);
        }

        private void UpdateLockdownCountdown(TimeSpan ts)
        {
            if (SettingsTab?.TxtLockdownCountdown != null)
                SettingsTab.TxtLockdownCountdown.Text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        /// <summary>
        /// Quick-toggle handler for the simple on/off premium chips. Each mirrors its
        /// tab checkbox so the existing consent / patreon-gating / service-start logic
        /// runs unchanged — we just flip it and read the result back into the dots.
        /// </summary>
        internal void PremiumChip_Click(PremiumFeature feature)
        {
            switch (feature)
            {
                case PremiumFeature.Takeover:
                    ToggleTabCheckBox(BambiTakeoverTab?.ChkAutonomyEnabled);
                    break;
                case PremiumFeature.Awareness:
                    ToggleTabCheckBox(AwarenessTab?.ChkAwarenessMaster);
                    break;
                case PremiumFeature.Haptics:
                    ToggleTabCheckBox(HapticsTab?.ChkHapticsEnabled);
                    break;
            }
            RefreshPremiumRail();
        }

        private static void ToggleTabCheckBox(System.Windows.Controls.CheckBox? cb)
        {
            if (cb == null) return;
            cb.IsChecked = !(cb.IsChecked ?? false);
        }

        /// <summary>Repaints the rail: chip state dots + the patron lock overlay (ad).</summary>
        internal void RefreshPremiumRail()
        {
            if (SettingsTab == null) return;

            var premium = App.Patreon?.HasPremiumAccess == true;
            if (SettingsTab.PremiumRailLock != null)
                SettingsTab.PremiumRailLock.Visibility = premium ? Visibility.Collapsed : Visibility.Visible;

            SetDot(SettingsTab.DotTakeover, BambiTakeoverTab?.ChkAutonomyEnabled?.IsChecked == true);
            SetDot(SettingsTab.DotAwareness, AwarenessTab?.ChkAwarenessMaster?.IsChecked == true);
            SetDot(SettingsTab.DotHaptics, HapticsTab?.ChkHapticsEnabled?.IsChecked == true);

            if (SettingsTab.TxtLockdownMins != null)
                SettingsTab.TxtLockdownMins.Text = _railLockdownMinutes + "m";
            SetLockdownActiveUi(App.Lockdown?.IsActive == true);
        }

        private static void SetDot(System.Windows.Shapes.Ellipse? dot, bool on)
        {
            if (dot != null) dot.Fill = on ? PremiumDotOn : PremiumDotOff;
        }
    }
}
