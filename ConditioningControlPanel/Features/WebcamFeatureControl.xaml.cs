using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Popup host for the Lab webcam tracking controls. The actual controls
    /// (LabWebcamEngineBar) are borrowed from the Lab tab by MainWindow and
    /// parented into <see cref="SettingsHost"/> while the popup is open, then
    /// returned to the Lab on close — so there is a single source of truth and
    /// every existing handler keeps working.
    /// </summary>
    public partial class WebcamFeatureControl : UserControl
    {
        public WebcamFeatureControl()
        {
            InitializeComponent();
        }

        /// <summary>Host panel that receives the borrowed Lab webcam engine bar.</summary>
        public Panel WebcamSettingsHost => SettingsHost;
    }
}
