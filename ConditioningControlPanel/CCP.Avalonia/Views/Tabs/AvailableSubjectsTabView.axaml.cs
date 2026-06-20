using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AvailableSubjectsTabView : UserControl
    {
        public AvailableSubjectsTabView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is AvailableSubjectsTabViewModel vm)
            {
                vm.RequestSelectTab += OnRequestSelectTab;
            }
        }

        private void OnRequestSelectTab(string key)
        {
            if (TopLevel.GetTopLevel(this) is Window window
                && window.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.SelectTabCommand.Execute(key);
            }
        }

        private void AvailableSubjectsScroller_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (AvailableSubjectsScroller is null || e.Delta.Y == 0) return;

            var offset = AvailableSubjectsScroller.Offset;
            offset = offset.WithX(offset.X - e.Delta.Y * 40);
            AvailableSubjectsScroller.Offset = offset;
            e.Handled = true;
        }

        private void BtnBecomeASubject_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is AvailableSubjectsTabViewModel vm)
            {
                vm.BecomeSubjectCommand.Execute(null);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (DataContext is AvailableSubjectsTabViewModel vm)
            {
                vm.RequestSelectTab -= OnRequestSelectTab;
            }
        }
    }
}
