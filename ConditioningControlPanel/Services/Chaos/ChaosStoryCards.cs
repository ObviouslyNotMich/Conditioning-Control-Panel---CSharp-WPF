using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Opens a <see cref="ChaosConversation"/> as a Hades-style character card, reusing the existing
/// <see cref="ChaosOverlayWindow"/> card host (no new window, no new pause mechanism):
///   - in a live descent → <see cref="ChaosModeService.PlayStoryCard"/> claims the lesson-card pause
///     (clock + spawns + bubble motion frozen) and shows the card in the run overlay, over the live
///     zone backdrop plate, resuming the field when the card closes.
///   - at the hub (no run) → a standalone overlay-window instance shows the card over the hub backdrop
///     and closes itself when done.
/// Routed to here by <see cref="ChaosNarrativeDirector"/> on an eligible STORY trigger.
/// </summary>
internal static class ChaosStoryCards
{
    public static void Play(ChaosConversation convo)
    {
        if (convo == null) return;
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (App.Chaos?.IsRunning == true) App.Chaos.PlayStoryCard(convo);
                else PlayStandalone(convo);
            }
            catch (Exception ex) { App.Logger?.Warning("ChaosStoryCards.Play failed: {E}", ex.Message); }
        }));
    }

    // Hub (or anywhere without a live run): a transient full-screen overlay instance that hosts the
    // card and disposes itself on completion.
    private static void PlayStandalone(ChaosConversation convo)
    {
        ImageSource? bg = ChaosArt.Resolve("hub", "backdrop") ?? ChaosArt.Resolve("backdrops", "depth1");
        // The constructor already sizes/places the window on the PRIMARY screen at (0,0) — same as the
        // run overlay. Do NOT reposition to VirtualScreenLeft/Top, which spans a multi-monitor desktop.
        var win = new ChaosOverlayWindow();
        if (App.MainWindowRef != null) { try { win.Owner = App.MainWindowRef; } catch { } }
        win.Show();
        win.ShowConversation(convo, bg, onComplete: () => { try { win.Close(); } catch { } });
    }
}
