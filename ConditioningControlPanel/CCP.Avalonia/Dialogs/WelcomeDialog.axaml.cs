using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Welcome dialog shown on first launch.
/// </summary>
public partial class WelcomeDialog : Window
{
    public WelcomeDialog()
    {
        InitializeComponent();

// TODO: App.Mods?.GetAffirmation() is a WPF-only ModService method not yet on IModService.
        // Using a placeholder until the core mod service exposes affirmations.
        const string affirmation = "Subject";
        TxtWelcomeHeading.Text = Loc.GetF("label_welcome", affirmation);
    }

    private void BtnBegin_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    /// <summary>
    /// Show welcome dialog if user hasn't been welcomed yet.
    /// </summary>
    /// <returns>True if welcome was shown (first launch), false otherwise.</returns>
    public static async Task<bool> ShowIfNeeded(Window owner)
    {
        var settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
        if (settings?.Current is { Welcomed: false })
        {
            var dialog = new WelcomeDialog();
            await dialog.ShowDialog<bool?>(owner);

            settings.Current.Welcomed = true;
            settings.Save();
            return true;
        }
        return false;
}
}
