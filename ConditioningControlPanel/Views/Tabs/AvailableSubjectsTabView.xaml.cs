using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AvailableSubjectsTabView : UserControl
    {
        public AvailableSubjectsTabView()
        {
            InitializeComponent();
        }

        private void AvailableSubjectsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.AvailableSubjectsScroller_PreviewMouseWheel(sender, e);
        }
        private void BtnBecomeASubject_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBecomeASubject_Click(sender, e);
        }
        private void BtnConnectSubject_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnConnectSubject_Click(sender, e);
        }
    }
}
