using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using global::Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>View-model backing the Avalonia Chaos HUD bindings.</summary>
public sealed class AvaloniaChaosHudViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _clockText = "0:00";
    public string ClockText { get => _clockText; set { _clockText = value; OnPropertyChanged(); } }

    private string _scoreText = "0";
    public string ScoreText { get => _scoreText; set { _scoreText = value; OnPropertyChanged(); } }

    private string _totalMultText = "x1.0";
    public string TotalMultText { get => _totalMultText; set { _totalMultText = value; OnPropertyChanged(); } }

    private double _focus;
    public double Focus { get => _focus; set { _focus = value; OnPropertyChanged(); } }

    private double _focusMax = 100;
    public double FocusMax { get => _focusMax; set { _focusMax = value; OnPropertyChanged(); } }

    private string _focusText = "50 / 100";
    public string FocusText { get => _focusText; set { _focusText = value; OnPropertyChanged(); } }

    private string _rippleText = "READY";
    public string RippleText { get => _rippleText; set { _rippleText = value; OnPropertyChanged(); } }

    private bool _rippleReady;
    public bool RippleReady
    {
        get => _rippleReady;
        set
        {
            _rippleReady = value;
            RippleForeground = value ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xAA, 0xB8, 0xB8, 0xD0));
            OnPropertyChanged();
        }
    }

    private IBrush _rippleForeground = Brushes.White;
    public IBrush RippleForeground { get => _rippleForeground; set { _rippleForeground = value; OnPropertyChanged(); } }

    public ObservableCollection<ChaosSidebarBoon> ActiveSidebarToys { get; } = new();
    public ObservableCollection<ChaosSidebarBoon> ActiveSidebarAccessories { get; } = new();
    public ObservableCollection<ChaosSidebarBoon> RunPickTiles { get; } = new();
    public ObservableCollection<ChaosSidebarBoon> RunModifiers { get; } = new();
    public ObservableCollection<ChaosToyState> ActiveToys { get; } = new();
    public ObservableCollection<string> RecentEvents { get; } = new();

    private string _shieldText = "0 ♥";
    public string ShieldText { get => _shieldText; set { _shieldText = value; OnPropertyChanged(); } }

    private int _combo;
    public int Combo { get => _combo; set { _combo = value; OnPropertyChanged(); } }

    private double _comboMult = 1.0;
    public double ComboMult { get => _comboMult; set { _comboMult = value; OnPropertyChanged(); } }

    private double _difficultyMult = 1.0;
    public double DifficultyMult { get => _difficultyMult; set { _difficultyMult = value; OnPropertyChanged(); } }

    private double _heatMult = 1.0;
    public double HeatMult { get => _heatMult; set { _heatMult = value; OnPropertyChanged(); } }

    private double _boonMult = 1.0;
    public double BoonMult { get => _boonMult; set { _boonMult = value; OnPropertyChanged(); } }

    private double _heat;
    public double Heat { get => _heat; set { _heat = value; OnPropertyChanged(); } }

    private double _runProgress;
    public double RunProgress { get => _runProgress; set { _runProgress = value; OnPropertyChanged(); } }

    private string _runTimeText = "0:00";
    public string RunTimeText { get => _runTimeText; set { _runTimeText = value; OnPropertyChanged(); } }

    private string _actWaveText = "I · 1";
    public string ActWaveText { get => _actWaveText; set { _actWaveText = value; OnPropertyChanged(); } }

    public void Mirror(ChaosRunState state)
    {
        ClockText = state.ClockText;
        ScoreText = state.ScoreText;
        TotalMultText = state.TotalMultText;
        Focus = state.Focus;
        FocusMax = state.FocusMax;
        FocusText = state.FocusText;
        RippleText = state.RippleText;
        RippleReady = state.RippleReady;
        ShieldText = state.ShieldText;
        Combo = state.Combo;
        ComboMult = state.ComboMult;
        DifficultyMult = state.DifficultyMult;
        HeatMult = state.HeatMult;
        BoonMult = state.BoonMult;
        Heat = state.Heat;
        RunProgress = state.RunProgress;
        RunTimeText = state.RunTimeText;
        ActWaveText = state.ActWaveText;
        Sync(ActiveSidebarToys, state.ActiveSidebarToys);
        Sync(ActiveSidebarAccessories, state.ActiveSidebarAccessories);
        Sync(RunPickTiles, state.RunPickTiles);
        Sync(RunModifiers, state.RunModifiers);
        Sync(ActiveToys, state.ActiveToys);
        Sync(RecentEvents, state.RecentEvents);
    }

    private static void Sync<T>(ObservableCollection<T> dest, IEnumerable<T> src)
    {
        dest.Clear();
        foreach (var item in src) dest.Add(item);
    }
}
