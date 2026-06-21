using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Services;
using ConditioningControlPanel.Avalonia.Services.AttentionCheck;
using ConditioningControlPanel.Avalonia.Services.BouncingText;
using ConditioningControlPanel.Avalonia.Services.BubbleCount;
using ConditioningControlPanel.Avalonia.Services.Companion;
using ConditioningControlPanel.Avalonia.Services.Flash;
using ConditioningControlPanel.Avalonia.Services.Haptics;
using ConditioningControlPanel.Avalonia.Services.InteractionQueue;
using ConditioningControlPanel.Avalonia.Services.KeywordTriggers;
using ConditioningControlPanel.Avalonia.Services.LockCard;
using ConditioningControlPanel.Avalonia.Services.Lockdown;
using ConditioningControlPanel.Avalonia.Services.Logging;
using ConditioningControlPanel.Avalonia.Services.MindWipe;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Avalonia.Services.Subliminal;
using ConditioningControlPanel.Avalonia.Services.Webcam;
using ConditioningControlPanel.Avalonia.Services.Sessions;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Avalonia.Services.Moderation;
using ConditioningControlPanel.Avalonia.Services.RemoteControl;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;
using ConditioningControlPanel.Core.Services.AvailableSubjects;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.AIService.Enrichment;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Quests;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Update;
using ConditioningControlPanel.Core.Services.Chaos;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ConditioningControlPanel.Avalonia;

/// <summary>
/// Dependency-injection registration helpers for the Avalonia head.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Avalonia platform seam implementations and portable Core services
    /// used by the Conditioning Control Panel cross-platform shell.
    /// </summary>
    public static IServiceCollection ConfigureCoreServices(this IServiceCollection services)
    {
        var isMobile = OperatingSystem.IsAndroid();

        // Platform seams - singletons unless they own per-control or per-window state.
        services.AddSingleton<IAppEnvironment, AvaloniaAppEnvironment>();
        services.AddSingleton<LibVLC>(_ =>
        {
            LibVLCSharp.Shared.Core.Initialize();
            return new LibVLC();
        });
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IScheduler, AvaloniaScheduler>();
        services.AddSingleton<IScreenProvider, AvaloniaScreenProvider>();
        services.AddSingleton<IAssetLoader, AvaloniaAssetLoader>();
        services.AddSingleton<IPointerState, AvaloniaPointerState>();
        services.AddSingleton<ISfxPlayer, AvaloniaSfxPlayer>();
        services.AddSingleton<IMouseHook, AvaloniaMouseHook>();
        services.AddSingleton<IBubbleService, AvaloniaBubbleService>();
        services.AddSingleton<IBrowserHost, AvaloniaBrowserHost>();
        services.AddSingleton<ISecretStore, AvaloniaSecretStore>();
        services.AddSingleton<ISingleInstanceService, AvaloniaSingleInstanceService>();
        services.AddSingleton<IUpdateInstaller, AvaloniaUpdateInstaller>();
        services.AddSingleton<IWallpaperProvider, AvaloniaWallpaperProvider>();
        services.AddSingleton<IFrameSource, AvaloniaFrameSource>();
        services.AddSingleton<ISystemAudioDucker, AvaloniaSystemAudioDucker>();
        services.AddSingleton<IAudioDeviceService, AvaloniaAudioDeviceService>();
        services.AddSingleton<IAudioPlayer, AvaloniaAudioPlayer>();
        services.AddSingleton<IHapticsService, AvaloniaHapticsService>();

        if (isMobile)
        {
            // Android/iOS cannot use the desktop tray icon, window chrome, or global input hooks.
            services.AddSingleton<IInputHook, MobileInputHook>();
            services.AddSingleton<IHotkeyProvider, MobileHotkeyProvider>();
            services.AddSingleton<IWindowChrome, MobileWindowChrome>();
            services.AddSingleton<ITrayIcon, MobileTrayIcon>();
            services.AddSingleton<IBrowserHost, MobileBrowserHost>();
            services.AddSingleton<IFilePickerService, MobileFilePicker>();
            services.AddSingleton<IPlatformCapabilities, MobilePlatformCapabilities>();
        }
        else
        {
            services.AddSingleton<IInputHook, AvaloniaInputHook>();
            services.AddSingleton<IHotkeyProvider, AvaloniaHotkeyProvider>();
            services.AddSingleton<IWindowChrome, AvaloniaWindowChrome>();
            services.AddSingleton<ITrayIcon, AvaloniaTrayIcon>();
            services.AddSingleton<IFilePickerService, DesktopFilePickerService>();
            services.AddSingleton<IPlatformCapabilities, AvaloniaPlatformCapabilities>();
        }

        // Dialog service needs a way to reach the current TopLevel at call time.
        services.AddSingleton<IDialogService>(_ => new AvaloniaDialogService(() => GetCurrentTopLevel()));

        // Overlay surface is a Window, so a new instance per consumer is safer than a singleton.
        services.AddTransient<IOverlaySurface, AvaloniaOverlaySurface>();

        // IVideoSurface requires a VideoView instance at construction time and is therefore
        // not registered globally. Consumers should create AvaloniaVideoSurface directly:
        //   var surface = new AvaloniaVideoSurface(videoView);

        // Core services that are safe to register as singletons today.
        services.AddSingleton<IPromptService, PromptService>();
        services.AddSingleton<IPromptValidator, PromptValidator>();
        services.AddSingleton<IModerationGuard, ModerationGuard>();
        services.AddSingleton<ILogger>(_ => Log.Logger);
        services.AddSingleton<IAppLogger, SerilogAppLogger>();
        services.AddSingleton<AvaloniaDualMonitorVideoService>();

        // Settings, session and achievement services (extracted to Core).
        services.AddSingleton<ISettingsBackupProvider, AvaloniaSettingsBackupProvider>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISkillTreeService, AvaloniaSkillTreeService>();
        services.AddSingleton<IProgressionService, AvaloniaProgressionService>();
        services.AddSingleton<IModService, AvaloniaModService>();
        services.AddSingleton<IInteractionQueueService, AvaloniaInteractionQueueService>();
        services.AddSingleton<IBubbleCountService, AvaloniaBubbleCountService>();
        services.AddSingleton<IFlashService, AvaloniaFlashService>();
        services.AddSingleton<ILockCardService, AvaloniaLockCardService>();
        services.AddSingleton<ISubliminalService, AvaloniaSubliminalService>();
        services.AddSingleton<IVideoService, AvaloniaVideoService>();
        services.AddSingleton<IMindWipeService, AvaloniaMindWipeService>();
        services.AddSingleton<IBouncingTextService, AvaloniaBouncingTextService>();
        services.AddSingleton<IOverlayService, AvaloniaOverlayService>();
        services.AddSingleton<IWebcamService, AvaloniaWebcamService>();
        services.AddSingleton<IAttentionCheckService, AvaloniaAttentionCheckService>();
        services.AddSingleton<IModerationCounter, AvaloniaModerationCounter>();
        services.AddSingleton<IModerationLog, AvaloniaModerationLog>();
        services.AddSingleton<ISessionPlatformBridge, AvaloniaSessionPlatformBridge>();
        services.AddSingleton<SessionFileService>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IRoadmapService, AvaloniaRoadmapService>();
        services.AddSingleton<IQuestService, QuestService>();
        services.AddSingleton<IAchievementService, AchievementService>();

        // Auth, Chaos, avatar, bark, video and session-log stubs for the Avalonia head.
        services.AddSingleton<IUnifiedUserService, AvaloniaUnifiedUserService>();
        services.AddSingleton<IAuthProvider, AvaloniaDiscordProvider>();
        services.AddSingleton<IAuthProvider, AvaloniaPatreonProvider>();
        services.AddSingleton<IAuthProvider, AvaloniaSubscribeStarProvider>();
        services.AddSingleton<IChaosService, AvaloniaChaosService>();
        services.AddSingleton<IAvatarWindowService, AvaloniaAvatarWindowService>();
        services.AddSingleton<IBarkService, AvaloniaBarkService>();
        services.AddSingleton<IVideoInfo, AvaloniaVideoInfo>();
        services.AddSingleton<IMainWindowService, AvaloniaMainWindowService>();
        services.AddSingleton<ISessionLogService, AvaloniaSessionLogService>();

        services.AddSingleton<IKeywordTriggerPresetService, AvaloniaKeywordTriggerPresetService>();
        services.AddSingleton<IKeywordTriggerService, AvaloniaKeywordTriggerService>();
        services.AddSingleton<ICompanionPhraseService, AvaloniaCompanionPhraseService>();
        services.AddSingleton<IAvailableSubjectsService, AvailableSubjectsService>();
        services.AddSingleton<IRemoteControlService, AvaloniaRemoteControlService>();
        services.AddSingleton<ILockdownService, AvaloniaLockdownService>();
        services.AddSingleton<ISessionEffectOrchestrator, AvaloniaSessionEffectOrchestrator>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IStartupRegistration, AvaloniaStartupRegistration>();
        services.AddSingleton<ChaosCrashSentinel>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<AppInfoTabViewModel>();
        services.AddTransient<SettingsTabViewModel>();
        services.AddTransient<PresetsTabViewModel>();
        services.AddTransient<PresetIOTabViewModel>();
        services.AddTransient<QuestsTabViewModel>();
        services.AddTransient<LevelFeaturesTabViewModel>();
        services.AddTransient<PatreonTabViewModel>();
        services.AddTransient<DeeperTabViewModel>();
        services.AddTransient<DeeperHubTabViewModel>();
        services.AddTransient<EnhancementsTabViewModel>();
        services.AddTransient<DeeperSubmissionsTabViewModel>();
        services.AddTransient<CompanionHubTabViewModel>();
        services.AddTransient<CompanionTabViewModel>();
        services.AddTransient<BambiTakeoverTabViewModel>();
        services.AddTransient<HapticsTabViewModel>();
        services.AddTransient<AwarenessTabViewModel>();
        services.AddSingleton<LabTabViewModel>();
        services.AddTransient<BlinkTrainerTabViewModel>();
        services.AddTransient<RemoteControlTabViewModel>();
        services.AddTransient<AvailableSubjectsTabViewModel>();
        services.AddTransient<ProfileTabViewModel>();
        services.AddTransient<LockdownTabViewModel>();
        services.AddTransient<AssetsTabViewModel>();
        services.AddTransient<CatalogueSubmissionsTabViewModel>();
        services.AddTransient<AchievementsTabViewModel>();
        services.AddTransient<LeaderboardTabViewModel>();
        services.AddTransient<MarqueeTabViewModel>();
        services.AddTransient<AnimationsTabViewModel>();

        return services;
    }

    private static TopLevel? GetCurrentTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single
            && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view);
        }

        return null;
    }
}
