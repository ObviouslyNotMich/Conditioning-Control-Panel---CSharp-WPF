using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Affirmation + metadata modal for Preset/Session catalogue submissions.
    /// Unlike <see cref="CatalogueSubmitDialog"/> (enhancements carry their own
    /// creator/tags in the bundle), preset/session native files have neither, so
    /// this dialog collects a creator name + tags before the POST. Submit is gated
    /// on a non-empty creator AND the affirmation checkbox.
    ///
    /// Usage:
    ///   var d = new AssetSubmitDialog(assetName, defaultCreator) { Owner = this };
    ///   if (d.ShowDialog() == true) {
    ///       // d.Creator, d.Tags ready for SubmitCatalogueAssetAsync
    ///   }
    /// </summary>
    public partial class AssetSubmitDialog : Window
    {
        public bool Confirmed { get; private set; }
        public string Creator { get; private set; } = "";
        public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();

        public AssetSubmitDialog(string assetName, string? defaultCreator = null)
        {
            InitializeComponent();

            TxtSubtitle.Text = string.IsNullOrWhiteSpace(assetName)
                ? string.Empty
                : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", assetName);

            if (!string.IsNullOrWhiteSpace(defaultCreator))
                TxtCreator.Text = defaultCreator.Trim();

            ChkAffirm.Checked += (_, _) => UpdateSubmitEnabled();
            ChkAffirm.Unchecked += (_, _) => UpdateSubmitEnabled();
            TxtCreator.TextChanged += (_, _) => UpdateSubmitEnabled();
            UpdateSubmitEnabled();
        }

        private void UpdateSubmitEnabled()
        {
            BtnSubmit.IsEnabled = ChkAffirm.IsChecked == true
                && !string.IsNullOrWhiteSpace(TxtCreator.Text);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (ChkAffirm.IsChecked != true || string.IsNullOrWhiteSpace(TxtCreator.Text)) return;

            Creator = TxtCreator.Text.Trim();
            // Accept comma- or whitespace-separated tags; dedup, lowercase-trim, cap length.
            Tags = (TxtTags.Text ?? string.Empty)
                .Split(new[] { ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void LinkGuidelines_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
                e.Handled = true;
            }
            catch
            {
                // Best-effort: failure to open the browser shouldn't crash the dialog.
            }
        }
    }
}
