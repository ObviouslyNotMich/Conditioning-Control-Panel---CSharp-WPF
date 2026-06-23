using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>
/// Avalonia host for the ambient and chaos bubble engine.
/// Implements <see cref="IBubbleService"/> and the Chaos overlay bridge <see cref="IAvaloniaBubbleService"/>.
/// </summary>
public sealed class AvaloniaBubbleService : IBubbleService, IAvaloniaBubbleService, IBubbleRenderer
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IAssetLoader _assets;
    private readonly ISfxPlayer _sfx;
    private readonly IPointerState _pointerState;
    private readonly IMouseHook _mouseHook;
    private readonly ILogger<AvaloniaBubbleService>? _logger;
    private readonly ILogger<BubbleEngine>? _bubbleEngineLogger;
    private readonly BubbleEngine _ambientEngine;
    private readonly Dictionary<Guid, AvaloniaBubbleWindow> _windows = new();

    private BubbleEngine? _chaosEngine;
    private SharedHostBubbleRenderer? _sharedHostRenderer;
    private bool _sharedHost;
    private Bitmap? _bubbleBitmap;
    private int _mouseHookRefCount;

    // ---- active-toy state (Avalonia parity stubs) ----
    private bool _vibePopActive;
    private bool _vibePopHoverPops;
    private DateTime _vibePopEndUtc;
    private int _eStimCharges;
    private bool _eStimChainReaction;

    public AvaloniaBubbleService(
        ISettingsService settings,
        IScreenProvider screens,
        IAssetLoader assets,
        ISfxPlayer sfx,
        IPointerState pointerState,
        IMouseHook mouseHook,
        ILogger<AvaloniaBubbleService>? logger = null,
        ILogger<BubbleEngine>? bubbleEngineLogger = null)
    {
        _settings = settings;
        _screens = screens;
        _assets = assets;
        _sfx = sfx;
        _pointerState = pointerState;
        _mouseHook = mouseHook;
        _logger = logger;
        _bubbleEngineLogger = bubbleEngineLogger;
        _ambientEngine = new BubbleEngine(screens, settings, this, pointerState, bubbleEngineLogger);
        _ambientEngine.OnBubblePopped += OnEngineBubblePopped;
        _ambientEngine.EchoSplitRequested += OnEchoSplitRequested;
    }

    public bool IsRunning => _ambientEngine.IsRunning;
    public bool IsPaused => _ambientEngine.IsPaused;
    public int ActiveBubbles => _ambientEngine.ActiveBubbles + (_chaosEngine?.ActiveBubbles ?? 0);

    public event Action? OnBubblePopped
    {
        add => _ambientEngine.OnBubblePopped += value;
        remove => _ambientEngine.OnBubblePopped -= value;
    }

    public event Action? OnBubbleMissed
    {
        add => _ambientEngine.OnBubbleMissed += value;
        remove => _ambientEngine.OnBubbleMissed -= value;
    }

    /// <summary>Raised when an echo bubble splits and the caller should spawn child bubbles.</summary>
    public event Action<ChaosBubbleSpec, double, double>? EchoSplitRequested;

    public double ChaosRabbitTrailSecNow => _chaosEngine?.ChaosRabbitTrailSecNow ?? 0.0;

    public void SetRabbitTrailSec(double seconds) => _chaosEngine?.SetRabbitTrailSec(seconds);

    public void Start()
    {
        LoadBubbleImage();
        _ambientEngine.Start();
        InstallMouseHook();
    }

    public void Stop()
    {
        _ambientEngine.Stop();
        _chaosEngine?.Stop();
        _chaosEngine = null;
        _sharedHostRenderer = null;
        ReleaseMouseHook();
        _bubbleBitmap?.Dispose();
        _bubbleBitmap = null;
    }

    public void PauseAndClear() => _ambientEngine.PauseAndClear();
    public void Resume() => _ambientEngine.Resume();
    public void RefreshFrequency() => _ambientEngine.RefreshFrequency();
    public void SpawnOnce() => _ambientEngine.SpawnOnce();
    public void PopAllBubbles()
    {
        _ambientEngine.PopAllBubbles();
        _chaosEngine?.PopAllChaosPaid();
    }

    public int PopBubblesInRect(ConditioningControlPanel.Core.Platform.PixelRect rectDips)
    {
        int popped = 0;

        // Ambient bubbles first — they share the same window dictionary as chaos bubbles
        // but have no spec (or a non-live spec via the ambient engine).
        if (_ambientEngine != null)
        {
            List<Guid> ambientHits;
            lock (_windows)
            {
                ambientHits = _windows
                    .Where(kv => kv.Value.Scaling > 0)
                    .Where(kv =>
                    {
                        var w = kv.Value;
                        var scale = w.Scaling;
                        var r = new PixelRect(w.Position.X / scale, w.Position.Y / scale, w.Width, w.Height);
                        return rectDips.X < r.Right && rectDips.Right > r.X &&
                               rectDips.Y < r.Bottom && rectDips.Bottom > r.Y;
                    })
                    .Select(kv => kv.Key)
                    .ToList();
            }

            foreach (var id in ambientHits)
            {
                _logger?.LogDebug("PopBubblesInRect popping ambient bubble {Id}", id);
                _ambientEngine.PopBubble(id);
                popped++;
            }
        }

        if (_chaosEngine != null)
        {
            var hits = _chaosEngine.GetChaosBubblesInRect(rectDips);
            if (hits.Count > 0)
                _logger?.LogDebug("PopBubblesInRect chaos hits={Hits}", hits.Count);
            foreach (var id in hits)
            {
                var bubble = _chaosEngine.GetChaosBubble(id);
                if (bubble?.Spec is not { } spec) continue;
                if (spec.IsDarter || spec.IsFreeze || spec.IsTease || spec.IsBrittle) continue;
                _logger?.LogDebug("PopBubblesInRect popping chaos bubble {Id} live={Live}", id, spec.IsLive);
                _chaosEngine.PopBubble(id);
                popped++;
            }
        }

        return popped;
    }

    public bool AnyDarterIntersects(ConditioningControlPanel.Core.Platform.PixelRect rectDips)
    {
        if (_chaosEngine == null) return false;
        var hits = _chaosEngine.GetChaosBubblesInRect(rectDips);
        foreach (var id in hits)
        {
            var bubble = _chaosEngine.GetChaosBubble(id);
            if (bubble?.Spec is { IsDarter: true })
                return true;
        }
        return false;
    }

    // ---- Stage 2a/2b chaos mode ----

    void IBubbleService.BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null)
    {
        BeginChaosMode(onBenignPop, onDefuse, onDetonate,
            canChannel: canChannel,
            onChannelStarted: onChannelStarted,
            onChannelBroken: onChannelBroken);
    }

    public void BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Action<ChaosBubbleSpec, bool>? onDarterCaught = null,
        Action<ChaosBubbleSpec>? onFreezeCaught = null,
        Action<ChaosBubbleSpec, bool>? onChaperoneShieldBroken = null,
        Action<ChaosBubbleSpec>? onBoundEnraged = null,
        Action<ChaosBubbleSpec>? onTeaseTouched = null,
        Action<ChaosBubbleSpec>? onTeaseDenied = null,
        Action<ChaosBubbleSpec>? onBrittleShattered = null,
        Action<ChaosBubbleSpec>? onTreatExpired = null,
        Action<ChaosBubbleSpec, bool>? onDarterSpanked = null,
        double chainReachDip = 120.0,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null)
    {
        LoadBubbleImage();

        // Ensure a previous chaos engine is torn down before starting a new run.
        if (_chaosEngine != null)
        {
            _chaosEngine.EndChaosMode();
            _chaosEngine = null;
        }

        _sharedHost = _settings.Current.ChaosBubbleSharedHost;
        IBubbleRenderer chaosRenderer;
        if (_sharedHost)
        {
            ChaosBubbleHostOverlay.EnsureCreated();
            _sharedHostRenderer = new SharedHostBubbleRenderer(_bubbleBitmap);
            chaosRenderer = _sharedHostRenderer;
        }
        else
        {
            chaosRenderer = this;
        }

        _chaosEngine = new BubbleEngine(_screens, _settings, chaosRenderer, _pointerState, _bubbleEngineLogger);
        _chaosEngine.OnBubblePopped += OnEngineBubblePopped;
        _chaosEngine.EchoSplitRequested += OnEchoSplitRequested;

        Action<ChaosBubbleSpec> wrappedBenignPop = spec =>
        {
            onBenignPop(spec);
            if (spec.IsPrism)
                ShowPrismGhost(spec);
        };

        _chaosEngine.BeginChaosMode(
            wrappedBenignPop,
            onDefuse,
            onDetonate,
            onDarterCaught,
            onFreezeCaught,
            onChaperoneShieldBroken,
            onBoundEnraged,
            onTeaseTouched,
            onTeaseDenied,
            onBrittleShattered,
            onTreatExpired,
            onDarterSpanked,
            chainReachDip,
            canChannel,
            onChannelStarted,
            onChannelBroken);

        InstallMouseHook();
    }

    public void EndChaosMode()
    {
        _chaosEngine?.EndChaosMode();
        _chaosEngine = null;
        _sharedHostRenderer = null;
        ReleaseMouseHook();
    }

    public void SpawnChaosBubble(ChaosBubbleSpec spec) => _chaosEngine?.SpawnChaosBubble(spec);

    public void SpawnChaosChaperone(ChaosBubbleSpec live, ChaosBubbleSpec escort) => _chaosEngine?.SpawnChaosChaperone(live, escort);

    public void SpawnChaosBoundPair(ChaosBubbleSpec a, ChaosBubbleSpec b) => _chaosEngine?.SpawnChaosBoundPair(a, b);

    public void PopNearestBenign() => _chaosEngine?.PopNearestBenign();

    public void DefuseAllLive() => _chaosEngine?.DefuseAllLive();

    public void PopAllChaosPaid() => _chaosEngine?.PopAllChaosPaid();

    public void SetChaosFrozen(bool frozen) => _chaosEngine?.SetChaosFrozen(frozen);

    public void SetChaosTimeScale(double scale) => _chaosEngine?.SetChaosTimeScale(scale);

    public void SetChaosInputLocked(bool locked) => _chaosEngine?.SetChaosInputLocked(locked);

    public void SetVibePop(bool active, bool hoverPops = false)
    {
        _vibePopActive = active;
        _vibePopHoverPops = hoverPops;
        // Duration is driven by AvaloniaChaosService; keep sweeping until it calls SetVibePop(false).
        _vibePopEndUtc = active ? DateTime.MaxValue : DateTime.UtcNow;
    }

    public void VibrateAllForFreeze(int durationMs)
    {
        // Stage 2c: window vibration stub; harmless no-op until visual FX layer is wired.
    }

    public void ArmEStim(int charges, bool chainReaction = false)
    {
        _eStimCharges = Math.Max(0, charges);
        _eStimChainReaction = chainReaction;
    }

    public int EStimChargesLeft => _eStimCharges;

    public void TriggerPlayerRipple(Point centerPx, double radiusPx, double lifeMs) =>
        _chaosEngine?.TriggerPlayerRipple(centerPx, radiusPx, lifeMs);

    public void PlayChime(float volumeScale = 0.3f)
    {
        try { _sfx.Play("chime", volumeScale * 0.6f); }
        catch (Exception ex) { _logger?.LogWarning(ex, "PlayChime failed"); }
    }

    private void OnMouseHookLeftDown(object? sender, HookPoint e)
    {
        // The low-level hook runs off the UI thread. Marshal the decision to the UI thread so
        // we can safely inspect engine/window state and call PopBubble/BeginChaosChannel.
        Dispatcher.UIThread.Post(() => HandleMouseLeftDown(e));
    }

    private void HandleMouseLeftDown(HookPoint e)
    {
        try
        {
            // VibePopping: while armed, every left-down sweeps the immediate area.
            if (_vibePopActive && _chaosEngine != null)
            {
                if (DateTime.UtcNow >= _vibePopEndUtc) _vibePopActive = false;
                else
                {
                    const int sweep = 120;
                    PopBubblesInRect(new PixelRect(e.X - sweep, e.Y - sweep, sweep * 2, sweep * 2));
                }
            }

            // E-Stim: discharge one charge on each click while armed.
            if (_eStimCharges > 0 && _chaosEngine != null)
            {
                _eStimCharges--;
                int radius = _eStimChainReaction ? 800 : 500;
                PopBubblesInRect(new PixelRect(e.X - radius, e.Y - radius, radius * 2, radius * 2));
            }

            // Chaos bubbles (shared-host or per-window) take priority.
            if (_chaosEngine != null)
            {
                bool swallowed = _chaosEngine.OnSharedHostLeftDown(new Point(e.X, e.Y));
                if (swallowed) return;
            }

            // Ambient session bubbles: pop the window under the cursor.
            TryPopAmbientBubbleAt(e.X, e.Y);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Mouse left-down handler failed");
        }
    }

    private void OnMouseHookRightDown(object? sender, HookPoint e)
    {
        // The Ripple verb is driven by AvaloniaChaosService so it can honor recharge/cooldown.
        // The bubble engine still exposes TriggerPlayerRipple for consumers that call it directly.
    }

    private void OnMouseHookLeftUp(object? sender, HookPoint e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try { _chaosEngine?.EndActiveChaosChannel(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Mouse left-up handler failed"); }
        });
    }

    private void TryPopAmbientBubbleAt(double x, double y)
    {
        if (_ambientEngine == null || !_ambientEngine.IsRunning || _ambientEngine.IsPaused) return;

        AvaloniaBubbleWindow? hit = null;
        lock (_windows)
        {
            foreach (var kv in _windows)
            {
                var w = kv.Value;
                if (w.Scaling <= 0) continue;

                // Window.Position is in physical pixels; Width/Height are DIPs.
                var rect = new global::Avalonia.Rect(
                    w.Position.X,
                    w.Position.Y,
                    w.Width * w.Scaling,
                    w.Height * w.Scaling);

                if (rect.Contains(new global::Avalonia.Point(x, y)))
                    hit = w;
            }
        }

        if (hit?.Bubble?.StateId is Guid stateId && stateId != Guid.Empty)
        {
            try { _ambientEngine.PopBubble(stateId); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to pop ambient bubble {StateId}", stateId); }
        }
    }

    private void InstallMouseHook()
    {
        if (_mouseHookRefCount++ == 0)
        {
            _mouseHook.LeftButtonDown += OnMouseHookLeftDown;
            _mouseHook.RightButtonDown += OnMouseHookRightDown;
            _mouseHook.LeftButtonUp += OnMouseHookLeftUp;
            _mouseHook.Install();
        }
    }

    private void ReleaseMouseHook()
    {
        if (--_mouseHookRefCount <= 0)
        {
            _mouseHookRefCount = 0;
            try
            {
                _mouseHook.LeftButtonDown -= OnMouseHookLeftDown;
                _mouseHook.RightButtonDown -= OnMouseHookRightDown;
                _mouseHook.LeftButtonUp -= OnMouseHookLeftUp;
            }
            catch { }
            _mouseHook.Uninstall();
        }
    }

    private void OnEchoSplitRequested(ChaosBubbleSpec spec, double x, double y)
    {
        EchoSplitRequested?.Invoke(spec, x, y);
        SpawnEchoChildren(spec, x, y);
    }

    private void SpawnEchoChildren(ChaosBubbleSpec parent, double centerPxX, double centerPxY)
    {
        if (_chaosEngine == null) return;

        for (int i = 0; i < 2; i++)
        {
            try
            {
                var child = ChaosBubbleVariants.BuildEchoChild(
                    parent.SizePx,
                    centerPxX + Random.Shared.Next(-70, 71),
                    centerPxY + Random.Shared.Next(-50, 51),
                    parent.EffectIntensity);
                _chaosEngine.SpawnChaosBubble(child);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to spawn echo child");
            }
        }
    }

    // ---- IAvaloniaBubbleService explicit implementation ----

    int IAvaloniaBubbleService.PopBubblesInRect(global::Avalonia.Rect rectDips)
    {
        return PopBubblesInRect(new PixelRect(rectDips.X, rectDips.Y, rectDips.Width, rectDips.Height));
    }

    bool IAvaloniaBubbleService.AnyDarterIntersects(global::Avalonia.Rect rectDips) =>
        AnyDarterIntersects(new PixelRect(rectDips.X, rectDips.Y, rectDips.Width, rectDips.Height));

    // ---- IBubbleRenderer implementation ----

    void IBubbleRenderer.Create(BubbleState state)
    {
        RunOnUi(() =>
        {
            if (_windows.ContainsKey(state.Id)) return;

            var window = new AvaloniaBubbleWindow(_bubbleBitmap, state.Size);
            ApplyVisualState(window, state);
            window.Bubble.StateId = state.Id;

            // Live chaos bubbles use hold-to-defuse; everything else pops on click.
            if (state.Spec is { IsLive: true })
            {
                var bubbleId = state.Id;
                window.Bubble.Click += (_, _) => _chaosEngine?.BeginChaosChannel(bubbleId);
                window.Bubble.BubblePointerReleased += (_, _) => _chaosEngine?.EndChaosChannel(bubbleId);
            }
            else
            {
                var engine = state.Spec != null ? _chaosEngine : _ambientEngine;
                window.Click += (_, _) => engine?.PopBubble(state.Id);
            }
            _windows[state.Id] = window;

            try
            {
                window.Show();
                OverlayZ.Register(window, OverlayZ.Layer.Bubbles);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to show bubble window");
                _windows.Remove(state.Id);
            }
        });
    }

    void IBubbleRenderer.Move(BubbleState state)
    {
        RunOnUi(() =>
        {
            if (!_windows.TryGetValue(state.Id, out var window)) return;
            ApplyVisualState(window, state);
        });
    }

    void IBubbleRenderer.SetLabel(Guid id, string label)
    {
        RunOnUi(() =>
        {
            if (!_windows.TryGetValue(id, out var window)) return;
            window.Bubble.SetLabel(label);
        });
    }

    void IBubbleRenderer.SetFuse(Guid id, double fraction)
    {
        RunOnUi(() =>
        {
            if (!_windows.TryGetValue(id, out var window)) return;
            window.Bubble.SetFuse(fraction);
        });
    }

    void IBubbleRenderer.Pop(BubbleState state, Action onComplete)
    {
        RunOnUi(() =>
        {
            if (!_windows.Remove(state.Id, out var window))
            {
                onComplete();
                return;
            }

            window.Bubble.Pop(() =>
            {
                window.CloseWindow();
                onComplete();
            });
        });
    }

    void IBubbleRenderer.Destroy(Guid id)
    {
        RunOnUi(() =>
        {
            if (!_windows.Remove(id, out var window)) return;
            window.CloseWindow();
        });
    }

    private void ApplyVisualState(AvaloniaBubbleWindow window, BubbleState state)
    {
        string? label = null;
        (byte r, byte g, byte b)? tint = null;
        var fuseFraction = 1.0;
        var isBrittle = false;

        if (state.Spec is { } spec)
        {
            label = spec.Label;
            tint = (spec.TintR, spec.TintG, spec.TintB);
            fuseFraction = spec.IsLive && spec.FuseMs > 0
                ? Math.Clamp(state.FuseRemainingMs / spec.FuseMs, 0.0, 1.0)
                : 1.0;
            isBrittle = spec.IsBrittle;
        }

        window.Place(
            new global::Avalonia.PixelPoint((int)(state.X * state.Scaling), (int)(state.Y * state.Scaling)),
            state.Size,
            state.Size,
            state.Scale,
            state.Opacity,
            label,
            tint,
            fuseFraction);

        window.Bubble.SetShielded(state.IsShielded);
        window.Bubble.SetBrittle(isBrittle);
        window.Scaling = state.Scaling;
    }

    private void LoadBubbleImage()
    {
        try
        {
            if (_bubbleBitmap != null) return;
            _bubbleBitmap = AvaloniaBitmapHelper.LoadResource("bubble.png");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load bubble image");
        }
    }

    private void ShowPrismGhost(ChaosBubbleSpec spec)
    {
        RunOnUi(() =>
        {
            // Locate an existing window for this spec to inherit its position.
            if (!_windows.TryGetValue(spec.Id, out var sourceWindow))
                return;

            var size = Math.Max(20.0, spec.SizePx);
            var ghost = new AvaloniaBubbleWindow(_bubbleBitmap, size);

            string? label = spec.Label;
            (byte r, byte g, byte b)? tint = (spec.TintR, spec.TintG, spec.TintB);

            ghost.Place(
                sourceWindow.Position,
                size,
                size,
                1.0,
                1.0,
                label,
                tint,
                1.0);

            ghost.Bubble.SetShielded(false);
            ghost.Bubble.SetBrittle(false);

            try
            {
                ghost.Show();
                OverlayZ.Register(ghost, OverlayZ.Layer.Bubbles);
                ghost.Bubble.FadeOut(250.0, () => ghost.CloseWindow());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to show prism ghost bubble");
            }
        });
    }

    private void OnEngineBubblePopped()
    {
        var volume = _settings.Current.BubblesVolume / 100f * 0.6f;
        _sfx.Play("pop", volume);
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
