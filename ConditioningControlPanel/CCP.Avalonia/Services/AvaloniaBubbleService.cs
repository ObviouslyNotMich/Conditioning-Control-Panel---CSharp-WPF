using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Platform;
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
    private readonly IScheduler _scheduler;
    private readonly IUiDispatcher _dispatcher;
    private readonly IPointerState _pointerState;
    private readonly IMouseHook _mouseHook;
    private readonly IAppLogger? _logger;
    private readonly BubbleEngine _ambientEngine;
    private readonly Dictionary<Guid, AvaloniaBubbleWindow> _windows = new();

    private BubbleEngine? _chaosEngine;
    private SharedHostBubbleRenderer? _sharedHostRenderer;
    private bool _sharedHost;
    private Bitmap? _bubbleBitmap;

    public AvaloniaBubbleService(
        ISettingsService settings,
        IScreenProvider screens,
        IAssetLoader assets,
        ISfxPlayer sfx,
        IScheduler scheduler,
        IUiDispatcher dispatcher,
        IPointerState pointerState,
        IMouseHook mouseHook,
        IAppLogger? logger = null)
    {
        _settings = settings;
        _screens = screens;
        _assets = assets;
        _sfx = sfx;
        _scheduler = scheduler;
        _dispatcher = dispatcher;
        _pointerState = pointerState;
        _mouseHook = mouseHook;
        _logger = logger;
        _ambientEngine = new BubbleEngine(screens, settings, this, scheduler, pointerState, logger);
        _ambientEngine.OnBubblePopped += OnEngineBubblePopped;
        _ambientEngine.EchoSplitRequested += (spec, px, py) => EchoSplitRequested?.Invoke(spec, px, py);
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
    }

    public void Stop()
    {
        _ambientEngine.Stop();
        _chaosEngine?.Stop();
        _chaosEngine = null;
        _sharedHostRenderer = null;
        UninstallMouseHook();
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

    public void PopBubblesInRect(ConditioningControlPanel.Core.Platform.PixelRect rectDips)
    {
        if (_chaosEngine == null) return;
        var hits = _chaosEngine.GetChaosBubblesInRect(rectDips);
        foreach (var id in hits)
        {
            var bubble = _chaosEngine.GetChaosBubble(id);
            if (bubble?.Spec is not { } spec) continue;
            if (spec.IsDarter || spec.IsFreeze || spec.IsTease || spec.IsBrittle) continue;
            _chaosEngine.PopBubble(id);
        }
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

    void IBubbleService.BeginChaosMode(Action<ChaosBubbleSpec> onBenignPop, Action<ChaosBubbleSpec, double, bool> onDefuse, Action<ChaosBubbleSpec> onDetonate)
    {
        BeginChaosMode(onBenignPop, onDefuse, onDetonate);
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
        double chainReachDip = 120.0)
    {
        LoadBubbleImage();

        // Ensure a previous chaos engine is torn down before starting a new run.
        if (_chaosEngine != null)
        {
            _chaosEngine.EndChaosMode();
            _chaosEngine = null;
        }
        UninstallMouseHook();

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

        _chaosEngine = new BubbleEngine(_screens, _settings, chaosRenderer, _scheduler, _pointerState, _logger);
        _chaosEngine.OnBubblePopped += OnEngineBubblePopped;
        _chaosEngine.EchoSplitRequested += (spec, px, py) => EchoSplitRequested?.Invoke(spec, px, py);

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
            chainReachDip);

        if (_sharedHost)
        {
            _mouseHook.LeftButtonDown += OnMouseHookLeftDown;
            _mouseHook.RightButtonDown += OnMouseHookRightDown;
            _mouseHook.Install();
        }
    }

    public void EndChaosMode()
    {
        _chaosEngine?.EndChaosMode();
        _chaosEngine = null;
        _sharedHostRenderer = null;
        UninstallMouseHook();
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

    private void OnMouseHookLeftDown(object? sender, HookPoint e)
    {
        var pt = new Point(e.X, e.Y);
        var swallow = _chaosEngine?.OnSharedHostLeftDown(pt) ?? false;
        // Swallow is not directly supported by EventHandler<HookPoint>; the hook always calls
        // CallNextHookEx. The WPF version returns a bool via its own hook contract. For Stage 2c
        // we record the swallow decision on the last click for future use.
        _lastHookSwallow = swallow;
    }

    private void OnMouseHookRightDown(object? sender, HookPoint e)
    {
        _chaosEngine?.TriggerPlayerRipple(new Point(e.X, e.Y));
    }

    private bool _lastHookSwallow;

    private void UninstallMouseHook()
    {
        try
        {
            _mouseHook.LeftButtonDown -= OnMouseHookLeftDown;
            _mouseHook.RightButtonDown -= OnMouseHookRightDown;
        }
        catch { }
        _mouseHook.Uninstall();
    }

    // ---- IAvaloniaBubbleService explicit implementation ----

    void IAvaloniaBubbleService.PopBubblesInRect(global::Avalonia.Rect rectDips)
    {
        PopBubblesInRect(new PixelRect(rectDips.X, rectDips.Y, rectDips.Width, rectDips.Height));
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

            window.Click += (_, _) => _ambientEngine.PopBubble(state.Id);
            _windows[state.Id] = window;

            try
            {
                window.Show();
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to show bubble window");
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
    }

    private void LoadBubbleImage()
    {
        try
        {
            if (_bubbleBitmap != null) return;

            var uri = new Uri("avares://CCP.Avalonia/Assets/bubble.png");
            if (_assets.Exists(uri))
            {
                using var stream = _assets.Open(uri);
                _bubbleBitmap = new Bitmap(stream);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to load avares bubble image");
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "Resources", "bubble.png");
        if (File.Exists(fallback))
        {
            try
            {
                using var stream = File.OpenRead(fallback);
                _bubbleBitmap = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to load fallback bubble image");
            }
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
                ghost.Bubble.FadeOut(250.0, () => ghost.CloseWindow());
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to show prism ghost bubble");
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
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Post(action);
    }
}
