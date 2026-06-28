using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Speech;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// "She's Listening" — the voice-control surface (offline mic features: spoken mantras + the
/// "Hey Bambi" voice commands). Binds the same voice AppSettings the Takeover tab uses and re-arms
/// the wake loop via <see cref="IAutonomyService.RefreshVoiceInputModes"/> whenever a toggle flips.
/// Port of the WPF SheListeningTabView (whose code-behind delegated to MainWindow handlers).
/// </summary>
public partial class SheListeningTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settings;
    private readonly ISpeechRecognitionService? _speech;
    private readonly IAutonomyService? _autonomy;
    private readonly ILogger<SheListeningTabViewModel>? _logger;

    private bool _syncing;

    [ObservableProperty] private bool _micConsentGiven;
    [ObservableProperty] private bool _wakeWordEnabled;
    [ObservableProperty] private string _wakeWords = "hey bambi";
    [ObservableProperty] private bool _pushToTalkEnabled;
    [ObservableProperty] private SpeechInputDevice? _selectedDevice;
    [ObservableProperty] private bool _engineAvailable;
    [ObservableProperty] private string _statusText = "";

    public ObservableCollection<SpeechInputDevice> Devices { get; } = new();

    /// <summary>A short, on-screen command cheat-sheet (a slice of the full grammar).</summary>
    public IReadOnlyList<string> CommandHints { get; } = new[]
    {
        "“bubbles” / “stop bubbles”", "“show me a video”", "“flash me”", "“the spiral”",
        "“make it pink”", "“deeper”", "“quiz me”", "“lock me”", "“mute” / “louder”",
        "“take over”", "“red” (stops everything)", "“stop listening”",
    };

    public SheListeningTabViewModel() : base("shelistening", "She's Listening", "🎤")
    {
    }

    public SheListeningTabViewModel(
        ISettingsService settings,
        ISpeechRecognitionService speech,
        IAutonomyService autonomy,
        ILogger<SheListeningTabViewModel> logger) : base("shelistening", "She's Listening", "🎤")
    {
        _settings = settings;
        _speech = speech;
        _autonomy = autonomy;
        _logger = logger;
        SyncFromSettings();
        RefreshDevices();
    }

    public override void OnSelected()
    {
        base.OnSelected();
        SyncFromSettings();
        RefreshDevices();
    }

    private void SyncFromSettings()
    {
        var s = _settings?.Current;
        if (s == null) return;
        _syncing = true;
        try
        {
            MicConsentGiven = s.MicConsentGiven;
            WakeWordEnabled = s.SpeechWakeWordEnabled;
            WakeWords = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
            PushToTalkEnabled = s.SpeechPushToTalkEnabled;
            EngineAvailable = _speech?.IsAvailable ?? false;
            StatusText = EngineAvailable
                ? "Offline voice engine ready."
                : "Voice engine unavailable — drop a Vosk model into Resources/Models/vosk.";
        }
        finally { _syncing = false; }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Devices.Clear();
        var list = _speech?.EnumerateInputDevices() ?? new List<SpeechInputDevice>();
        foreach (var d in list) Devices.Add(d);

        var idx = _settings?.Current?.SpeechInputDeviceIndex ?? -1;
        SelectedDevice = Devices.FirstOrDefault(d => d.Index == idx);
        if (SelectedDevice is null && Devices.Count > 0) SelectedDevice = Devices[0];
    }

    [RelayCommand]
    private void StopMic() => _autonomy?.StopVoiceInput();

    // ── settings write-back + re-arm on each toggle ──

    partial void OnMicConsentGivenChanged(bool value) => Apply(s => s.MicConsentGiven = value);
    partial void OnWakeWordEnabledChanged(bool value) => Apply(s => s.SpeechWakeWordEnabled = value);
    partial void OnPushToTalkEnabledChanged(bool value) => Apply(s => s.SpeechPushToTalkEnabled = value);
    partial void OnWakeWordsChanged(string value) => Apply(s => s.SpeechWakeWords = value?.Trim() ?? "");
    partial void OnSelectedDeviceChanged(SpeechInputDevice? value)
    {
        if (value is { } d) Apply(s => s.SpeechInputDeviceIndex = d.Index);
    }

    private void Apply(System.Action<ConditioningControlPanel.Models.AppSettings>? write)
    {
        if (_syncing) return;
        var s = _settings?.Current;
        if (s == null || write == null) return;
        try
        {
            write(s);
            _settings!.Save();
            _autonomy?.RefreshVoiceInputModes();
        }
        catch (System.Exception ex) { _logger?.LogDebug(ex, "SheListeningTab: failed to apply a voice setting"); }
    }
}
