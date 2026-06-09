using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The kinds of one-shot effect a Chaos bubble can carry. Each maps to a
/// concrete <see cref="EffectPayload"/> that wraps an existing App.* trigger.
/// </summary>
public enum EffectBubblePayloadKind
{
    Flash,
    Subliminal,
    Overlay,
    Video,
    HtLink,
    Audio,
    BambiFreeze
}

/// <summary>
/// A Chaos bubble's payload — the effect that fires when the bubble is popped
/// (benign "treat") or when a live bubble's fuse expires undefused
/// ("detonation"). Concrete payloads are thin wrappers over the app's existing
/// one-shot triggers (FlashService, SubliminalService, …). <see cref="Strength"/>
/// (0..100, derived from bubble size at spawn) scales the effect: bigger bubble
/// ⇒ stronger payload.
///
/// All <see cref="Fire"/> implementations are invoked on the UI thread (from a
/// DispatcherTimer tick or a click handler) and must be self-contained — the
/// underlying App.* services own their own dispatching/threading.
/// </summary>
public abstract class EffectPayload
{
    /// <summary>Short human label for the bubble tag, payload feed and bark context.</summary>
    public abstract string DisplayName { get; }

    public abstract EffectBubblePayloadKind Kind { get; }

    /// <summary>0..100, scaled from bubble size at spawn. Drives effect intensity.</summary>
    public int Strength { get; set; }

    /// <summary>Run the wrapped one-shot effect, scaled by <see cref="Strength"/>.</summary>
    public abstract void Fire();

    /// <summary>Lerp helper: map Strength 0..100 onto [min,max].</summary>
    protected int Scale(int min, int max) =>
        min + (int)Math.Round((max - min) * Math.Clamp(Strength, 0, 100) / 100.0);

    protected double ScaleD(double min, double max) =>
        min + (max - min) * Math.Clamp(Strength, 0, 100) / 100.0;
}

/// <summary>Fires a flash burst — more/bigger/faster images at higher Strength.</summary>
public sealed class FlashPayload : EffectPayload
{
    public override string DisplayName => "flash";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Flash;

    public override void Fire()
    {
        try
        {
            int amount = Scale(1, 3);
            int duration = Scale(250, 700);
            int size = Scale(45, 95);
            App.Flash?.TriggerFlashOnce(amount, duration, size, suppressHaptic: false);
        }
        catch (Exception ex) { App.Logger?.Debug("FlashPayload: {E}", ex.Message); }
    }
}

/// <summary>Flashes a subliminal from the active pool.</summary>
public sealed class SubliminalPayload : EffectPayload
{
    public override string DisplayName => "subliminal";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Subliminal;

    public override void Fire()
    {
        try { App.Subliminal?.FlashSubliminal(); }
        catch (Exception ex) { App.Logger?.Debug("SubliminalPayload: {E}", ex.Message); }
    }
}

/// <summary>Snaps an overlay (pink_filter / spiral / braindrain) on for a few seconds.</summary>
public sealed class OverlayPayload : EffectPayload
{
    private readonly string _kind;
    public OverlayPayload(string overlayKind) => _kind = overlayKind;

    public override string DisplayName => _kind;
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Overlay;

    public override void Fire()
    {
        try
        {
            int duration = Scale(1500, 4500);
            // braindrain interprets opacity*100 as an intensity 1..200 (blur = intensity*0.4);
            // pink_filter / spiral take a plain 0..1 opacity.
            double opacity = _kind == "braindrain" ? ScaleD(0.30, 1.40) : ScaleD(0.25, 0.70);
            App.Overlay?.ShowOverlayTimed(_kind, duration, opacity);
        }
        catch (Exception ex) { App.Logger?.Debug("OverlayPayload: {E}", ex.Message); }
    }
}

/// <summary>Triggers a mandatory video (silent no-op if the video pool is empty).</summary>
public sealed class VideoPayload : EffectPayload
{
    public override string DisplayName => "video";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Video;

    public override void Fire()
    {
        try { App.Video?.TriggerVideo(silentIfEmpty: true); }
        catch (Exception ex) { App.Logger?.Debug("VideoPayload: {E}", ex.Message); }
    }
}

/// <summary>Opens a HypnoTube link from the pool in the embedded browser, auto-fullscreen.</summary>
public sealed class HtLinkPayload : EffectPayload
{
    public override string DisplayName => "HT link";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.HtLink;

    public override void Fire()
    {
        try
        {
            var url = HtLinkPool.PickRandom();
            if (string.IsNullOrWhiteSpace(url)) return;
            if (Application.Current?.MainWindow is MainWindow mw)
                mw.NavigateToUrlInBrowser(url!, autoPlayFullscreen: true);
        }
        catch (Exception ex) { App.Logger?.Debug("HtLinkPayload: {E}", ex.Message); }
    }
}

/// <summary>Plays a one-shot subliminal whisper (reuses the subliminal audio path).</summary>
public sealed class AudioPayload : EffectPayload
{
    public override string DisplayName => "whisper";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Audio;

    public override void Fire()
    {
        // No dedicated SFX pool yet — reuse the subliminal whisper for an audio sting.
        try { App.Subliminal?.FlashSubliminal(); }
        catch (Exception ex) { App.Logger?.Debug("AudioPayload: {E}", ex.Message); }
    }
}

/// <summary>Triggers a Bambi Freeze (special subliminal + audio).</summary>
public sealed class BambiFreezePayload : EffectPayload
{
    public override string DisplayName => "freeze";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BambiFreeze;

    public override void Fire()
    {
        try { App.Subliminal?.TriggerBambiFreeze(); }
        catch (Exception ex) { App.Logger?.Debug("BambiFreezePayload: {E}", ex.Message); }
    }
}
