using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class EnhancementsTabView : UserControl
    {
        public EnhancementsTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Redirects vertical mouse wheel scrolling to horizontal scrolling for the skill tree
        /// </summary>
        private void SkillTreeScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // Scroll horizontally instead of vertically
                double offset = scrollViewer.HorizontalOffset - (e.Delta * 0.5);
                scrollViewer.ScrollToHorizontalOffset(offset);
                e.Handled = true;
            }
        }
    }
}
