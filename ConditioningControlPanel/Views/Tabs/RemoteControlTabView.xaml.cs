using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class RemoteControlTabView : UserControl
    {
        public RemoteControlTabView()
        {
            InitializeComponent();
        }

        private void BtnCopyRemoteCode_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCopyRemoteCode_Click(sender, e);
        }
        private void BtnCopyRemoteLink_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCopyRemoteLink_Click(sender, e);
        }
        private void BtnEditEmoteCancel_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEditEmoteCancel_Click(sender, e);
        }
        private void BtnEditEmoteSave_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEditEmoteSave_Click(sender, e);
        }
        private void BtnEmoteCustomSend_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEmoteCustomSend_Click(sender, e);
        }
        private void BtnEmoteEdit_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEmoteEdit_Click(sender, e);
        }
        private void BtnEmotePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnEmotePreset_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnStopRemote_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnStopRemote_Click(sender, e);
        }
        private void ChkOptInTag_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkOptInTag_Click(sender, e);
        }
        private void ChkOptIntoDirectory_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkOptIntoDirectory_Changed(sender, e);
        }
        private void ChkRemoteControlEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkRemoteControlEnabled_Changed(sender, e);
        }
        private void ChkRemoteShareAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkRemoteShareAvatar_Changed(sender, e);
        }
        private void ChkStopEffectsOnRemoteDisconnect_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkStopEffectsOnRemoteDisconnect_Changed(sender, e);
        }
        private void CmbRemoteTier_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbRemoteTier_SelectionChanged(sender, e);
        }
        private void TierCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TierCard_Click(sender, e);
        }
        private void TxtEditEmoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtEditEmoteText_TextChanged(sender, e);
        }
        private void TxtEmoteCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtEmoteCustom_KeyDown(sender, e);
        }
        private void TxtOptInStatus_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtOptInStatus_TextChanged(sender, e);
        }
    }
}
