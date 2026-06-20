using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Unified login dialog that handles provider selection and new user registration.
/// </summary>
public partial class LoginDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;
    private readonly IEnumerable<IAuthProvider> _authProviders;
    private readonly IUnifiedUserService _unifiedUser;


    private static readonly HttpClient _http = new();
    private readonly string _serverUrl = "https://codebambi-proxy.vercel.app";
    private CancellationTokenSource? _checkCts;

    private string? _firstProvider;
    private string? _firstProviderToken;
    private bool _isNameAvailable;
    private bool _isAccountRegisterMode;
    private string? _pendingInviteCode;
    private string? _pendingPassword;

    private CancellationTokenSource? _deviceCts;
    private string? _deviceCode;
    private DateTimeOffset _deviceCodeExpiresAt;

    /// <summary>
    /// Result of the login process.
    /// </summary>
    public LoginResult? Result { get; private set; }

    public class LoginResult
    {
        public bool Success { get; set; }
        public bool IsLegacyUser { get; set; }
        public bool ShouldShowOgWelcome { get; set; }
        public string? UnifiedId { get; set; }
        public string? DisplayName { get; set; }
        public string? Provider { get; set; }
        public string? LinkedProvider { get; set; }
    }

    public LoginDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
        _authProviders = App.Services.GetRequiredService<IEnumerable<IAuthProvider>>();
        _unifiedUser = App.Services.GetRequiredService<IUnifiedUserService>();
Closed += (_, _) =>
        {
            _deviceCts?.Cancel();
            _deviceCts?.Dispose();
            _deviceCts = null;
        };
    }

    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void ClearSensitiveData()
    {
        _pendingInviteCode = null;
        _pendingPassword = null;
        TxtPassword.Text = "";
        TxtPasswordConfirm.Text = "";
        _checkCts?.Cancel();
        _checkCts?.Dispose();
        _checkCts = null;
    }

    private static string SanitizeError(string? error)
    {
        if (string.IsNullOrEmpty(error)) return "An error occurred";
        if (error.Contains("ECONNREFUSED") || error.Contains("Redis") || error.Contains("redis")
            || error.Contains("stack") || error.Contains("\\") || error.Contains("/api/"))
            return "Server error. Please try again later.";
        return error;
    }

    #region Provider Selection

    private async void BtnLoginDiscord_Click(object? sender, RoutedEventArgs e)
    {
        await TryLoginWithProviderAsync("discord");
    }

    private async void BtnLoginPatreon_Click(object? sender, RoutedEventArgs e)
    {
        await TryLoginWithProviderAsync("patreon");
    }

    private async void BtnLoginSubscribeStar_Click(object? sender, RoutedEventArgs e)
    {
        await TryLoginWithProviderAsync("substar");
    }

    private void BtnLoginAccount_Click(object? sender, RoutedEventArgs e)
    {
        ShowAccountPanel(isRegister: false);
    }

    private async Task TryLoginWithProviderAsync(string provider)
    {
        ShowLoading(Loc.GetF("login_connecting_to_provider", provider));

        try
        {
            var service = GetProvider(provider);

            if (service == null)
            {
                ShowError(Loc.GetF("login_{0}_service_not_available", provider));
                return;
            }

            await service.StartOAuthFlowAsync();
            var accessToken = service.GetAccessToken();

            if (string.IsNullOrEmpty(accessToken))
            {
                ShowProviderSelection();
                return;
            }

            ShowLoading(Loc.Get("login_checking_account"));

            // V2 auth service currently lives in the legacy WPF head; resolve dynamically.
            dynamic? v2Auth = CreateLegacyService("ConditioningControlPanel.Services.V2AuthService");
            if (v2Auth == null)
            {
                ShowError("Authentication service is not available in this build.");
                return;
            }

            dynamic authResponse = provider switch
            {
                "discord" => await v2Auth.AuthenticateWithDiscordAsync(accessToken),
                "substar" => await v2Auth.AuthenticateWithSubstarAsync(accessToken),
                _ => await v2Auth.AuthenticateWithPatreonAsync(accessToken),
            };

            if (!authResponse.Success)
            {
                ShowError(SanitizeError((string?)authResponse.Error));
                return;
            }

            if (authResponse.User != null && !(bool)authResponse.NeedsRegistration)
            {
                v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                _unifiedUser.UnifiedUserId = (string?)authResponse.User.UnifiedId;

                UpdateServiceProperties(provider, (string?)authResponse.User.UnifiedId, (string?)authResponse.User.DisplayName);

                Result = new LoginResult
                {
                    Success = true,
                    IsLegacyUser = (bool)authResponse.User.IsSeason0Og,
                    ShouldShowOgWelcome = (bool)authResponse.User.IsSeason0Og && _settings?.Current?.HasShownOgWelcome != true,
                    UnifiedId = (string?)authResponse.User.UnifiedId,
                    DisplayName = (string?)authResponse.User.DisplayName,
                    Provider = provider
                };

                Close(true);
                return;
            }

            _firstProvider = provider;
            _firstProviderToken = accessToken;
            ShowUsernamePanel();
        }
        catch (OperationCanceledException)
        {
            ShowProviderSelection();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Login failed for {Provider}", provider);
            ShowError(Loc.Get("login_failed_please_try_again"));
        }
    }

    #endregion

    #region Username Entry

    private void ShowUsernamePanel()
    {
        ProviderPanel.IsVisible = false;
        LoadingPanel.IsVisible = false;
        UsernamePanel.IsVisible = true;
        AccountPanel.IsVisible = false;
        DeviceCodePanel.IsVisible = false;

        TxtUsernameTitle.Text = Loc.Get("label_choose_your_display_name");
        TxtUsernameSubtitle.Text = Loc.Get("label_this_will_be_shown_on_the_leaderboard");

        BtnConfirmUsername.IsEnabled = true;
        TxtUsername.Focus();
    }

    private async void TxtUsername_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var name = (TxtUsername.Text ?? "").Trim();

        _checkCts?.Cancel();
        _checkCts = new CancellationTokenSource();
        var token = _checkCts.Token;

        if (string.IsNullOrWhiteSpace(name))
        {
            SetAvailabilityStatus(Loc.Get("login_enter_unique_name"), Brushes.Gray, false);
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

        SetAvailabilityStatus(Loc.Get("login_checking"), Brushes.Gray, false);

        try
        {
            await Task.Delay(400, token);
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
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            SetAvailabilityStatus(Loc.Get("login_error_checking_name"), Brushes.Orange, false);
            _logger?.Warning(ex, "Name availability check failed");
        }
    }

    private void SetAvailabilityStatus(string message, IBrush color, bool available)
    {
        TxtAvailability.Text = message;
        TxtAvailability.Foreground = color;
        _isNameAvailable = available;
        BtnConfirmUsername.IsEnabled = available;
    }

    private async Task<bool> CheckNameAvailabilityAsync(string name)
    {
        try
        {
            string endpoint;
            if (_firstProvider == "invite" || _firstProvider == "substar")
            {
                endpoint = $"{_serverUrl}/v2/auth/check-name?name={Uri.EscapeDataString(name)}";
            }
            else if (_firstProvider == "discord")
            {
                endpoint = $"{_serverUrl}/user/check-display-name-discord?name={Uri.EscapeDataString(name)}";
            }
            else
            {
                endpoint = $"{_serverUrl}/user/check-display-name?name={Uri.EscapeDataString(name)}";
            }

            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrEmpty(_firstProviderToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _firstProviderToken);
            }

            var response = await _http.SendAsync(request);
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
            _logger?.Warning("Name availability check failed: {Error}", ex.Message);
            return false;
        }
    }

    private async void BtnConfirmUsername_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isNameAvailable) return;
        if (_firstProvider != "invite" && string.IsNullOrEmpty(_firstProviderToken)) return;

        var displayName = (TxtUsername.Text ?? "").Trim();

        BtnConfirmUsername.IsEnabled = false;
        ShowLoading(Loc.Get("login_creating_account"));

        try
        {
            dynamic? v2Auth = CreateLegacyService("ConditioningControlPanel.Services.V2AuthService");
            if (v2Auth == null)
            {
                ShowError("Authentication service is not available in this build.");
                return;
            }

            dynamic authResponse;
            if (_firstProvider == "invite")
            {
                if (string.IsNullOrEmpty(_pendingInviteCode) || string.IsNullOrEmpty(_pendingPassword))
                {
                    ClearSensitiveData();
                    ShowError(Loc.Get("login_session_expired"));
                    return;
                }
                authResponse = await v2Auth.RegisterAsync(_pendingInviteCode, displayName, _pendingPassword);
                ClearSensitiveData();
            }
            else if (_firstProvider == "discord")
            {
                authResponse = await v2Auth.AuthenticateWithDiscordAsync(_firstProviderToken!, displayName);
            }
            else if (_firstProvider == "substar")
            {
                authResponse = await v2Auth.AuthenticateWithSubstarAsync(_firstProviderToken!, displayName);
            }
            else
            {
                authResponse = await v2Auth.AuthenticateWithPatreonAsync(_firstProviderToken!, displayName);
            }

            if (authResponse.Success && authResponse.User != null)
            {
                v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                _unifiedUser.UnifiedUserId = (string?)authResponse.User.UnifiedId;

                if (_firstProvider != "invite")
                    UpdateServiceProperties(_firstProvider!, (string?)authResponse.User.UnifiedId, (string?)authResponse.User.DisplayName);

                Result = new LoginResult
                {
                    Success = true,
                    IsLegacyUser = false,
                    ShouldShowOgWelcome = false,
                    UnifiedId = (string?)authResponse.User.UnifiedId,
                    DisplayName = (string?)authResponse.User.DisplayName,
                    Provider = _firstProvider
                };

                Close(true);
            }
            else
            {
                ClearSensitiveData();
                ShowError(SanitizeError((string?)authResponse.Error));
            }
        }
        catch (Exception ex)
        {
            ClearSensitiveData();
            _logger?.Error(ex, "Failed to create account");
            ShowError(Loc.Get("login_failed_to_create_account"));
        }
    }

    #endregion

    #region Account Login (Invite Code + Password)

    private void ShowAccountPanel(bool isRegister)
    {
        _isAccountRegisterMode = isRegister;

        ProviderPanel.IsVisible = false;
        LoadingPanel.IsVisible = false;
        UsernamePanel.IsVisible = false;
        AccountPanel.IsVisible = true;
        DeviceCodePanel.IsVisible = false;

        TxtInviteCode.Text = "";
        TxtLoginDisplayName.Text = "";
        TxtPassword.Text = "";
        TxtPasswordConfirm.Text = "";
        TxtAccountError.Text = "";
        BtnAccountSubmit.IsEnabled = true;

        if (isRegister)
        {
            TxtAccountTitle.Text = Loc.Get("label_create_account");
            BtnAccountSubmit.Content = Loc.Get("btn_next");

            LblInviteCodeHint.IsVisible = true;
            LblInviteCode.IsVisible = true;
            TxtInviteCode.IsVisible = true;
            LblDisplayName.IsVisible = false;
            TxtLoginDisplayName.IsVisible = false;
            LblPasswordConfirm.IsVisible = true;
            TxtPasswordConfirm.IsVisible = true;

            UpdateToggleText(Loc.Get("login_already_have_account"), Loc.Get("btn_login"));
            TxtInviteCode.Focus();
        }
        else
        {
            TxtAccountTitle.Text = Loc.Get("btn_login");
            BtnAccountSubmit.Content = Loc.Get("btn_login");

            LblInviteCodeHint.IsVisible = false;
            LblInviteCode.IsVisible = false;
            TxtInviteCode.IsVisible = false;
            LblDisplayName.IsVisible = true;
            TxtLoginDisplayName.IsVisible = true;
            LblPasswordConfirm.IsVisible = false;
            TxtPasswordConfirm.IsVisible = false;

            UpdateToggleText(Loc.Get("login_dont_have_account"), Loc.Get("btn_create_account"));
            TxtLoginDisplayName.Focus();
        }
    }

    private void UpdateToggleText(string prefix, string action)
    {
        TxtAccountToggle?.Inlines?.Clear();
        TxtAccountToggle?.Inlines?.Add(new Run(prefix + " ") { Foreground = new SolidColorBrush(Color.Parse("#B0B0B0")) });
        TxtAccountToggle?.Inlines?.Add(new Run(action) { Foreground = new SolidColorBrush(Color.Parse("#FF69B4")), TextDecorations = TextDecorations.Underline });
    }

    private void TxtAccountToggle_Click(object? sender, PointerPressedEventArgs e)
    {
        ShowAccountPanel(!_isAccountRegisterMode);
    }

    private void BtnAccountBack_Click(object? sender, PointerPressedEventArgs e)
    {
        ClearSensitiveData();
        ShowProviderSelection();
    }

    private async void BtnAccountSubmit_Click(object? sender, RoutedEventArgs e)
    {
        var password = TxtPassword.Text ?? string.Empty;

        if (password.Length < 8)
        {
            TxtAccountError.Text = Loc.Get("label_password_must_be_at_least_8_characters");
            return;
        }

        BtnAccountSubmit.IsEnabled = false;

        if (_isAccountRegisterMode)
        {
            var inviteCode = (TxtInviteCode.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                TxtAccountError.Text = Loc.Get("label_please_enter_your_invite_code");
                BtnAccountSubmit.IsEnabled = true;
                return;
            }

            if (TxtPasswordConfirm.Text != password)
            {
                TxtAccountError.Text = Loc.Get("label_passwords_do_not_match");
                BtnAccountSubmit.IsEnabled = true;
                return;
            }

            _pendingInviteCode = inviteCode;
            _pendingPassword = password;
            _firstProvider = "invite";
            ShowUsernamePanel();
        }
        else
        {
            var displayName = (TxtLoginDisplayName.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                TxtAccountError.Text = Loc.Get("label_please_enter_your_display_name");
                BtnAccountSubmit.IsEnabled = true;
                return;
            }

            await TryAccountLoginAsync(displayName, password);
        }
    }

    private async Task TryAccountLoginAsync(string displayName, string password)
    {
        ShowLoading(Loc.Get("login_logging_in"));

        try
        {
            dynamic? v2Auth = CreateLegacyService("ConditioningControlPanel.Services.V2AuthService");
            if (v2Auth == null)
            {
                ShowError("Authentication service is not available in this build.");
                return;
            }

            dynamic authResponse = await v2Auth.LoginAsync(displayName, password);
            ClearSensitiveData();

            if (!authResponse.Success)
            {
                ShowAccountPanel(_isAccountRegisterMode);
                TxtLoginDisplayName.Text = displayName;
                TxtAccountError.Text = SanitizeError((string?)authResponse.Error);
                return;
            }

            if (authResponse.User != null)
            {
                v2Auth.ApplyUserDataToSettings(authResponse.User, authResponse.AuthToken);
                _unifiedUser.UnifiedUserId = (string?)authResponse.User.UnifiedId;

                Result = new LoginResult
                {
                    Success = true,
                    IsLegacyUser = false,
                    ShouldShowOgWelcome = false,
                    UnifiedId = (string?)authResponse.User.UnifiedId,
                    DisplayName = (string?)authResponse.User.DisplayName,
                    Provider = "account"
                };

                Close(true);
            }
            else
            {
                ShowAccountPanel(_isAccountRegisterMode);
                TxtLoginDisplayName.Text = displayName;
                TxtAccountError.Text = Loc.Get("label_unexpected_response_from_server");
            }
        }
        catch (Exception ex)
        {
            ClearSensitiveData();
            _logger?.Error(ex, "Account login failed");
            ShowAccountPanel(_isAccountRegisterMode);
            TxtLoginDisplayName.Text = displayName;
            TxtAccountError.Text = Loc.Get("label_login_failed_please_try_again");
        }
    }

    #endregion

    #region UI Helpers

    private void ShowProviderSelection()
    {
        ProviderPanel.IsVisible = true;
        LoadingPanel.IsVisible = false;
        UsernamePanel.IsVisible = false;
        AccountPanel.IsVisible = false;
        DeviceCodePanel.IsVisible = false;
        BtnLoginDiscord.IsEnabled = true;
        BtnLoginPatreon.IsEnabled = true;
    }

    private void ShowLoading(string message)
    {
        TxtLoadingMessage.Text = message;
        ProviderPanel.IsVisible = false;
        LoadingPanel.IsVisible = true;
        UsernamePanel.IsVisible = false;
        AccountPanel.IsVisible = false;
        DeviceCodePanel.IsVisible = false;
    }

    private void ShowError(string message)
    {
        MessageBoxStub.Show(message, Loc.Get("title_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        ShowProviderSelection();
    }

    private void UpdateServiceProperties(string provider, string? unifiedId, string? displayName)
    {
        var authProvider = GetProvider(provider);
        if (authProvider == null) return;

        authProvider.UnifiedUserId = unifiedId;
        authProvider.DisplayName = displayName;
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_firstProvider))
            TryLogout(GetProvider(_firstProvider));

        _deviceCts?.Cancel();
        _deviceCode = null;
        ClearSensitiveData();
        Close(false);
    }

    private static void TryLogout(IAuthProvider? service)
    {
        if (service == null) return;
        try
        {
            service.Logout();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "Logout failed");
        }
    }

    private IAuthProvider? GetProvider(string provider)
    {
        foreach (var auth in _authProviders)
        {
            if (string.Equals(auth.ProviderName, provider, StringComparison.OrdinalIgnoreCase))
                return auth;
        }
        return null;
    }

    #endregion

    #region Device-Code Flow

    private async void BtnLoginDeviceCode_Click(object? sender, RoutedEventArgs e)
    {
        ShowLoading("Generating sign-in code...");

        try
        {
            dynamic? svc = CreateLegacyService("ConditioningControlPanel.Services.V2DeviceCodeService");
            if (svc == null)
            {
                ShowError("Device-code sign-in is not available in this build.");
                return;
            }

            dynamic resp = await svc.InitiateAsync();

            if (!resp.Success || string.IsNullOrEmpty((string?)resp.Code))
            {
                ShowError(SanitizeError((string?)resp.Error));
                return;
            }

            _deviceCode = (string?)resp.Code;
            _deviceCodeExpiresAt = (DateTimeOffset)resp.ExpiresAt;

            ShowDeviceCodePanel(_deviceCode!);
            OpenVerificationUrl();

            _deviceCts?.Cancel();
            _deviceCts?.Dispose();
            _deviceCts = new CancellationTokenSource();
            _ = PollDeviceCodeLoopAsync(_deviceCts.Token);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "[DeviceCode] Initiate exception");
            ShowError(Loc.Get("login_failed_please_try_again"));
        }
    }

    private void ShowDeviceCodePanel(string code)
    {
        ProviderPanel.IsVisible = false;
        LoadingPanel.IsVisible = false;
        UsernamePanel.IsVisible = false;
        AccountPanel.IsVisible = false;
        DeviceCodePanel.IsVisible = true;

        TxtDeviceCode.Text = code.Length == 6
            ? $"{code.Substring(0, 3)}-{code.Substring(3, 3)}"
            : code;
        TxtVerificationUrl.Text = GetVerificationUrl();
        TxtDeviceStatus.Text = "Waiting for browser confirmation...";
    }

    private static string GetVerificationUrl()
    {
        try
        {
            dynamic? svc = CreateLegacyService("ConditioningControlPanel.Services.V2DeviceCodeService");
            if (svc != null) return (string?)svc.VerificationUrl ?? "https://codebambi.app/verify";
        }
        catch { }
        return "https://codebambi.app/verify";
    }

    private static void OpenVerificationUrl()
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GetVerificationUrl(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "[DeviceCode] Failed to open browser");
        }
    }

    private async Task PollDeviceCodeLoopAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_deviceCode)) return;

        dynamic? svc = CreateLegacyService("ConditioningControlPanel.Services.V2DeviceCodeService");
        if (svc == null) return;

        int intervalMs = 3000;
        int consecutiveUnknown = 0;

        try
        {
            await Task.Delay(intervalMs, ct);

            while (!ct.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow > _deviceCodeExpiresAt)
                {
                    HandleDeviceCodeExpired();
                    return;
                }

                dynamic result = await svc.PollAsync(_deviceCode!, ct);
                if (ct.IsCancellationRequested) return;

                var status = (int)result.Status;
                switch (status)
                {
                    case 0: // Confirmed
                        HandleDeviceCodeConfirmed(result);
                        return;
                    case 1: // Pending
                        intervalMs = 3000;
                        consecutiveUnknown = 0;
                        TxtDeviceStatus.Text = "Waiting for browser confirmation...";
                        break;
                    case 2: // Expired
                        HandleDeviceCodeExpired();
                        return;
                    case 3: // NotFound
                        HandleDeviceCodeError("Sign-in code wasn't recognized. Please try again.");
                        return;
                    case 4: // RateLimited
                    case 5: // ServiceUnavailable
                        intervalMs = Math.Min(intervalMs * 2, 30000);
                        TxtDeviceStatus.Text = "Connection busy, retrying...";
                        break;
                    case 6: // BadRequest
                    case 7: // Unauthorized
                        HandleDeviceCodeError("Sign-in failed. Please try again.");
                        return;
                    default: // Unknown
                        consecutiveUnknown++;
                        if (consecutiveUnknown >= 5)
                        {
                            HandleDeviceCodeError("Lost connection to server. Please try again.");
                            return;
                        }
                        intervalMs = Math.Min(intervalMs * 2, 30000);
                        TxtDeviceStatus.Text = "Connection issue, retrying...";
                        break;
                }

                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.Error(ex, "[DeviceCode] Poll loop crashed");
            HandleDeviceCodeError("Unexpected error. Please try again.");
        }
    }

    private void HandleDeviceCodeConfirmed(dynamic result)
    {
        if (string.IsNullOrEmpty((string?)result.AuthToken) || string.IsNullOrEmpty((string?)result.UnifiedId))
        {
            HandleDeviceCodeError("Server returned an incomplete response.");
            return;
        }

        if (result.User != null)
        {
            dynamic? v2Auth = CreateLegacyService("ConditioningControlPanel.Services.V2AuthService");
            v2Auth?.ApplyUserDataToSettings(result.User, result.AuthToken);
        }
        else if (_settings?.Current != null)
        {
            _settings.Current.AuthToken = (string?)result.AuthToken;
            _settings.Current.UnifiedId = (string?)result.UnifiedId;
            (_settings as ISettingsService)?.Save();
        }
        _unifiedUser.UnifiedUserId = (string?)result.UnifiedId;

        Result = new LoginResult
        {
            Success = true,
            IsLegacyUser = false,
            ShouldShowOgWelcome = false,
            UnifiedId = (string?)result.UnifiedId,
            DisplayName = _settings?.Current?.UserDisplayName,
            Provider = "device_code"
        };

        _deviceCts?.Cancel();
        _deviceCts?.Dispose();
        _deviceCts = null;

        Close(true);
    }

    private void HandleDeviceCodeExpired()
    {
        _deviceCts?.Cancel();
        _deviceCode = null;
        MessageBoxStub.Show("Sign-in code expired. Please try again.", Loc.Get("title_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        ShowProviderSelection();
    }

    private void HandleDeviceCodeError(string message)
    {
        _deviceCts?.Cancel();
        _deviceCode = null;
        MessageBoxStub.Show(message, Loc.Get("title_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        ShowProviderSelection();
    }

    private async void BtnDeviceCodeCopy_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_deviceCode)) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                var data = new global::Avalonia.Input.DataTransfer();
                var item = new global::Avalonia.Input.DataTransferItem();
                item.SetText(_deviceCode);
                data.Add(item);
                await clipboard.SetDataAsync(data);
                TxtDeviceStatus.Text = "Code copied. Paste in your browser.";
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "[DeviceCode] Clipboard copy failed");
        }
    }

    private void BtnDeviceCodeOpenBrowser_Click(object? sender, RoutedEventArgs e)
    {
        OpenVerificationUrl();
    }

    private void BtnDeviceCodeCancel_Click(object? sender, PointerPressedEventArgs e)
    {
        _deviceCts?.Cancel();
        _deviceCode = null;
        ShowProviderSelection();
    }

    #endregion

    private static dynamic? CreateLegacyService(string typeName)
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        try
        {
            var type = Type.GetType(typeName);
            if (type == null)
            {
                // The legacy WPF head is not referenced by the Avalonia build.
                return null;
            }
            return Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "Failed to create legacy service {Type}", typeName);
            return null;
        }
    }

    private void TxtInviteCode_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var box = (TextBox?)sender;
        if (box == null) return;
        var text = box.Text ?? "";
        var upper =
text.ToUpperInvariant();
        if (text != upper)
        {
            box.Text = upper;
            box.CaretIndex = upper.Length;
        }
    }
}
