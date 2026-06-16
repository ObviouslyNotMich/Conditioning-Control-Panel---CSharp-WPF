using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class LockdownTabView : UserControl
    {
        public LockdownTabView()
        {
            InitializeComponent();
        }

        private void BtnActivateLockdown_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnActivateLockdown_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void TxtLockdownExit_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtLockdownExit_KeyDown(sender, e);
        }
        private void TxtLockdownTimer_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtLockdownTimer_Click(sender, e);
        }
    }
}
