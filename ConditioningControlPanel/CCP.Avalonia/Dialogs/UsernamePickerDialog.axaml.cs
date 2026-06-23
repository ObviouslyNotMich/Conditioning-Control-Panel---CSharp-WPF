using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;

using Newtonsoft.Json.Linq;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for choosing a display name on first login.
/// </summary>
public partial class UsernamePickerDialog : Window
{
    private readonly ILogger<UsernamePickerDialog> _logger;


    private static readonly HttpClient _http = new();
    private readonly string _serverUrl = "https://codebambi-proxy.vercel.app";
    private CancellationTokenSource? _checkCts;
    private bool _isAvailable;

    /// <summary>
    /// The chosen display name (null if cancelled).
    /// </summary>
    public string? ChosenDisplayName { get; private set; }

    public UsernamePickerDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<UsernamePickerDialog>>();
Closed += (_, _) =>
        {
            _checkCts?.Cancel();
            _checkCts?.Dispose();
            _checkCts = null;
        };
    }

    /// <summary>
    /// Show the dialog configured for a new user.
    /// </summary>
    public void ConfigureForNewUser()
    {
        OgWelcomePanel.IsVisible = false;
        SuggestionPanel.IsVisible = false;
        TxtSubtitle.Text = Loc.Get("label_this_name_will_be_shown_on_the_leaderboard_an");
    }

    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void TxtUsername_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var name = (TxtUsername.Text ?? "").Trim();

        _checkCts?.Cancel();
        _checkCts = new CancellationTokenSource();
        var token = _checkCts.Token;

        if (string.IsNullOrWhiteSpace(name))
        {
            SetAvailabilityStatus(Loc.Get("login_enter_unique_display_name"), Brushes.Gray, false);
            return;
        }

        if (name.Length < 3)
        {
            SetAvailabilityStatus(Loc.Get("login_name_min_3_chars"), Brushes.Orange, false);
            return;
        }

        if (name.Length > 30)
        {
            SetAvailabilityStatus(Loc.Get("login_name_max_30_chars"), Brushes.Orange, false);
            return;
        }

        SetAvailabilityStatus(Loc.Get("login_checking_availability"), Brushes.Gray, false);

        try
        {
            await Task.Delay(500, token);
            if (token.IsCancellationRequested) return;

            var available = await CheckNameAvailabilityAsync(name);

            if (token.IsCancellationRequested) return;

            if (available)
            {
                SetAvailabilityStatus(Loc.GetF("login_name_available", name), Brushes.LightGreen, true);
            }
            else
            {
                SetAvailabilityStatus(Loc.GetF("login_name_already_taken", name), Brushes.Orange, false);
            }
        }
        catch (TaskCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            SetAvailabilityStatus(Loc.GetF("login_could_not_check", ex.Message), Brushes.Orange, false);
        }
    }

    private void SetAvailabilityStatus(string message, IBrush color, bool available)
    {
        TxtAvailability.Text = message;
        TxtAvailability.Foreground = color;
        _isAvailable = available;
        BtnConfirm.IsEnabled = available;
    }

    private async Task<bool> CheckNameAvailabilityAsync(string name)
    {
        try
        {
            var response = await _http.GetAsync($"{_serverUrl}/user/check-display-name?display_name={Uri.EscapeDataString(name)}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(json);
                return (bool?)result["available"] ?? false;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Name availability check failed: {Error}", ex.Message);
            return false;
}
    }

    private void BtnUseSuggestion_Click(object? sender, RoutedEventArgs e)
    {
        // Legacy suggestion panel - kept for XAML compatibility.
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        ChosenDisplayName = null;
        Close(false);
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_isAvailable)
        {
            ChosenDisplayName = (TxtUsername.Text ?? "").Trim();
            Close(true);
        }
    }
}
