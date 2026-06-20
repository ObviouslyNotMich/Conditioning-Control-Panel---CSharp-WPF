using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Cross-platform achievement tracker. Mirrors the legacy WPF <see cref="ConditioningControlPanel.Services.AchievementService"/>
/// but uses the Core platform seams for timers, paths, dispatching and logging.
/// </summary>
public sealed class AchievementService : IAchievementService, IDisposable
{
    private readonly IAppEnvironment _environment;
    private readonly IAppLogger _logger;
    private readonly IScheduler _scheduler;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IEnumerable<IAuthProvider> _authProviders;
    private readonly string _progressPath;
    private readonly IDisposable _saveTimer;
    private readonly IDisposable _trackingTimer;

    private AchievementProgress _progress;
    private bool _isDirty;
    private DateTime _lastPinkFilterCheck = DateTime.Now;
    private DateTime _lastSpiralCheck = DateTime.Now;
    private DateTime _lastBrainDrainCheck = DateTime.Now;
    private DateTime _lastMindWipeCheck = DateTime.Now;
    private DateTime _lastDeeperCheck = DateTime.Now;
    private bool _isDisposed;

    public event EventHandler<Achievement>? AchievementUnlocked;

    /// <inheritdoc />
    public bool SuppressPopups { get; set; }

    /// <inheritdoc />
    public AchievementProgress Progress => _progress;

    /// <inheritdoc />
    public bool CanUnlockExclusive => _authProviders.Any(p =>
        string.Equals(p.ProviderName, "patreon", StringComparison.OrdinalIgnoreCase) && p.HasPremiumAccess);

    public AchievementService(IAppEnvironment environment, IAppLogger logger, IScheduler scheduler, IUiDispatcher uiDispatcher, IEnumerable<IAuthProvider>? authProviders = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _authProviders = authProviders ?? Enumerable.Empty<IAuthProvider>();

        _progressPath = Path.Combine(environment.UserDataPath, "achievements.json");
        _progress = LoadProgress();

        // Reset continuous/session-based counters on startup (these shouldn't persist)
        _progress.ContinuousSpiralMinutes = 0;
        _progress.ContinuousMindWipeSeconds = 0;
        _progress.AltTabPressedThisSession = false;
        _progress.AvatarClickCount = 0;
        _progress.AvatarClickStartTime = null;

        // Check daily streak on startup
        _progress.UpdateDailyStreak();
        _progress.SyncCurrentStreak();
        _isDirty = true;

        // Auto-save every 30 seconds if dirty (off UI thread)
        _saveTimer = scheduler.StartPeriodicTimer(TimeSpan.FromSeconds(30), OnAutoSaveTick);

        // Track time-based achievements every second
        _trackingTimer = scheduler.StartPeriodicTimer(TimeSpan.FromSeconds(1), TrackTimeBasedProgress);

        _logger.Information("AchievementService initialized. {Count} achievements unlocked.",
            _progress.UnlockedAchievements.Count);
    }

    private AchievementProgress LoadProgress()
    {
        try
        {
            if (File.Exists(_progressPath))
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<AchievementProgress>(json) ?? new AchievementProgress();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load achievement progress");
        }

        return new AchievementProgress();
    }

    /// <inheritdoc />
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_progressPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save achievement progress");
        }
    }

    private void OnAutoSaveTick()
    {
        if (!_isDirty) return;

        _isDirty = false;
        var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
        var path = _progressPath;
        _ = Task.Run(() =>
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save achievement progress");
            }
        });
    }

    /// <inheritdoc />
    public void TrackTimeProgress() => TrackTimeBasedProgress();

    /// <summary>
    /// Track time-based progress (called every second).
    /// </summary>
    private void TrackTimeBasedProgress()
    {
        if (_isDisposed) return;

        var settings = App.Settings?.Current;
        if (settings == null) return;

        var now = DateTime.Now;
        var overlayRunning = IsOverlayRunning();

        // Track total conditioning time for skill tree (when overlay is running = session active)
        if (overlayRunning)
        {
            App.SkillTree?.AddConditioningTime(1.0 / 60.0);
        }

        // Track Pink Filter time - only when overlay is actually running
        var isPinkFilterActive = settings.PinkFilterEnabled && overlayRunning;
        if (isPinkFilterActive)
        {
            var elapsed = (now - _lastPinkFilterCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1) // Sanity check - max 6 seconds between ticks
            {
                _progress.TotalPinkFilterMinutes += elapsed;
                _isDirty = true;

                if (_progress.TotalPinkFilterMinutes >= 600)
                {
                    TryUnlock("rose_tinted_reality");
                }

                TryQuestTrack("TrackPinkFilterMinutes", elapsed);
            }
            _lastPinkFilterCheck = now;
        }
        else
        {
            _lastPinkFilterCheck = now;
        }

        // Track Spiral time - only when overlay is actually running
        var isSpiralActive = settings.SpiralEnabled && overlayRunning;
        if (isSpiralActive)
        {
            var elapsed = (now - _lastSpiralCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1)
            {
                _progress.TotalSpiralMinutes += elapsed;
                _progress.ContinuousSpiralMinutes += elapsed;
                _isDirty = true;

                if (_progress.ContinuousSpiralMinutes >= 20)
                {
                    TryUnlock("spiral_eyes");
                }

                TryQuestTrack("TrackSpiralMinutes", elapsed);
            }
            _lastSpiralCheck = now;
        }
        else
        {
            _progress.ContinuousSpiralMinutes = 0;
            _lastSpiralCheck = now;
        }

        // Track BrainDrain time - only when overlay is actually running
        var isBrainDrainActive = settings.BrainDrainEnabled && overlayRunning;
        if (isBrainDrainActive)
        {
            var elapsed = (now - _lastBrainDrainCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1)
            {
                TryQuestTrack("TrackBrainDrainMinutes", elapsed);
            }
            _lastBrainDrainCheck = now;
        }
        else
        {
            _lastBrainDrainCheck = now;
        }

        // Track Deeper player time — only while an enhancement is actively playing.
        if (IsDeeperActivelyPlaying())
        {
            var elapsed = (now - _lastDeeperCheck).TotalMinutes;
            if (elapsed > 0 && elapsed < 0.1)
            {
                _progress.DeeperMinutes += elapsed;
                _isDirty = true;
                if (_progress.DeeperMinutes >= 600)
                {
                    TryUnlock("permanent_resident");
                }
            }
            _lastDeeperCheck = now;
        }
        else
        {
            _lastDeeperCheck = now;
        }

        // Check System Overload (Bubbles + Bouncing Text + Spiral all active)
        if (settings.BubblesEnabled && settings.BouncingTextEnabled && settings.SpiralEnabled)
        {
            if (!_progress.HasSystemOverload)
            {
                _progress.HasSystemOverload = true;
                _isDirty = true;
                TryUnlock("system_overload");
            }
        }

        // Check Total Lockdown (Strict Lock + No Panic + Pink Filter)
        if (settings.StrictLockEnabled && !settings.PanicKeyEnabled && settings.PinkFilterEnabled)
        {
            if (!_progress.HasTotalLockdown)
            {
                _progress.HasTotalLockdown = true;
                _isDirty = true;
                TryUnlock("total_lockdown");
            }
        }
    }

    /// <inheritdoc />
    public void CheckLevelAchievements(int level)
    {
        if (level >= 10) TryUnlock("plastic_initiation");
        if (level >= 20) TryUnlock("dumb_bimbo");
        if (level >= 50) TryUnlock("fully_synthetic");
        if (level >= 75) TryUnlock("docile_cow");
        if (level >= 100) TryUnlock("perfect_plastic_puppet");
        if (level >= 125) TryUnlock("brainwashed_slavedoll");
        if (level >= 150) TryUnlock("platinum_puppet");
    }

    /// <inheritdoc />
    public void CheckDailyMaintenance()
    {
        if (_progress.ConsecutiveDays >= 7)
        {
            TryUnlock("daily_maintenance");
        }
    }

    /// <inheritdoc />
    public void TrackFlashImage()
    {
        _progress.TotalFlashImages++;
        _isDirty = true;

        if (_progress.TotalFlashImages >= 5000)
        {
            TryUnlock("retinal_burn");
        }

        TryQuestTrack("TrackFlashImage");
    }

    /// <inheritdoc />
    public void TrackBubblePopped()
    {
        _progress.TotalBubblesPopped++;
        _isDirty = true;

        if (_progress.TotalBubblesPopped >= 1000)
        {
            TryUnlock("pop_the_thought");
        }

        // Award 1 sparkle point every 100 bubbles
        if (_progress.TotalBubblesPopped % 100 == 0)
        {
            var settings = App.Settings?.Current;
            if (settings != null)
            {
                settings.SkillPoints += 1;
                App.Settings?.Save();
                _logger.Information("Bubble milestone! {Total} bubbles popped — awarded 1 sparkle point (total: {Points})",
                    _progress.TotalBubblesPopped, settings.SkillPoints);
                ShowBubbleMilestoneNotification(_progress.TotalBubblesPopped);
            }
        }

        TryQuestTrack("TrackBubblePopped");
    }

    private void ShowBubbleMilestoneNotification(int totalBubbles)
    {
        try
        {
            var fakeAchievement = new Achievement
            {
                Id = "bubble_milestone",
                Name = Core.Localization.Loc.GetF("achievement_bubble_milestone_name", totalBubbles),
                FlavorText = Core.Localization.Loc.Get("achievement_bubble_milestone_flavor"),
                ImageName = "bubble_pop.png",
                Category = AchievementCategory.Minigames
            };

            _uiDispatcher.Post(() =>
            {
                try
                {
                    _logger.Debug("Firing bubble milestone achievement event for: {Total}", totalBubbles);
                    AchievementUnlocked?.Invoke(this, fakeAchievement);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to show bubble milestone popup");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to show bubble milestone notification");
        }
    }

    /// <inheritdoc />
    public void TrackBubbleCountResult(bool correct)
    {
        if (correct)
        {
            _progress.BubbleCountCorrectStreak++;
            if (_progress.BubbleCountCorrectStreak > _progress.BubbleCountBestStreak)
            {
                _progress.BubbleCountBestStreak = _progress.BubbleCountCorrectStreak;
            }

            if (_progress.BubbleCountCorrectStreak >= 5)
            {
                TryUnlock("mathematicians_nightmare");
            }
        }
        else
        {
            _progress.BubbleCountCorrectStreak = 0;
        }

        TrackBubbleCountGameResult(correct);
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackLockCardCompletion(double seconds, int totalChars, int errors, int phrases)
    {
        _progress.TotalLockCardsCompleted++;
        _isDirty = true;
        _logger.Information("Lock card tracked! Total lock cards completed: {Count}", _progress.TotalLockCardsCompleted);
        Save();

        TryQuestTrack("TrackLockCardCompleted");

        if (errors == 0)
        {
            _progress.HasPerfectLockCard = true;
            TryUnlock("typing_tutor");
        }

        if (phrases >= 3 && seconds < 15)
        {
            if (seconds < _progress.FastestLockCardSeconds)
            {
                _progress.FastestLockCardSeconds = seconds;
            }
            TryUnlock("obedience_reflex");
        }
    }

    /// <inheritdoc />
    public void TrackVideoWatched(double durationSeconds)
    {
        if (durationSeconds <= 0) return;

        var minutes = durationSeconds / 60.0;
        _progress.TotalVideoMinutes += minutes;
        _isDirty = true;
        _logger.Information("Video watched: {Duration}s ({Minutes:F2} min). Total: {Total:F1} minutes",
            durationSeconds, minutes, _progress.TotalVideoMinutes);
        Save();

        TryQuestTrack("TrackVideoMinutes", minutes);
    }

    /// <inheritdoc />
    public void TrackAttentionCheckFailed()
    {
        _progress.AttentionCheckFailures++;
        _isDirty = true;

        if (_progress.AttentionCheckFailures >= 3)
        {
            TryUnlock("mercy_beggar");
        }
    }

    /// <inheritdoc />
    public void TrackMindWipeDuration(double seconds)
    {
        _progress.ContinuousMindWipeSeconds = seconds;
        _isDirty = true;

        if (seconds >= 60)
        {
            TryUnlock("clean_slate");
        }
    }

    /// <inheritdoc />
    public void TrackCornerHit()
    {
        if (!_progress.HasHitCorner)
        {
            _progress.HasHitCorner = true;
            _isDirty = true;
            TryUnlock("corner_hit");
        }
    }

    /// <inheritdoc />
    public void TrackAvatarClick()
    {
        var clickCount = _progress.AvatarClickCount + 1;
        _logger.Debug("TrackAvatarClick called. Current count will be: {Count}", clickCount);

        if (_progress.TrackAvatarClick())
        {
            _logger.Information("20 clicks reached! Unlocking Neon Obsession...");
            TryHapticAvatarEasterEggPattern();
            TryUnlock("neon_obsession");
        }
        if (_progress.TrackNeedyDollClick())
        {
            _logger.Information("150 clicks in 60s! Unlocking Needy Doll...");
            TryUnlock("needy_doll");
        }
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackAltTab()
    {
        _progress.AltTabPressedThisSession = true;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackPanicPressed()
    {
        _progress.LastPanicPressTime = DateTime.Now;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSessionStart()
    {
        _progress.ResetSessionTracking();
        TrackSessionStarted();
        CheckRelapse();
        _isDirty = true;
    }

    /// <inheritdoc />
    public void CheckRelapse()
    {
        if (_progress.LastPanicPressTime.HasValue)
        {
            var elapsed = (DateTime.Now - _progress.LastPanicPressTime.Value).TotalSeconds;
            if (elapsed <= 10)
            {
                TryUnlock("relapse");
            }
        }
    }

    /// <inheritdoc />
    public void TrackSessionComplete(string sessionName, double durationMinutes, bool noPanicEnabled, bool strictLockEnabled)
    {
        _logger.Information("TrackSessionComplete called: Session={Name}, Duration={Duration:F1}min, NoPanic={NoPanic}, StrictLock={Strict}",
            sessionName, durationMinutes, noPanicEnabled, strictLockEnabled);

        _progress.CompletedSessions.Add(sessionName);

        if (durationMinutes > _progress.LongestSessionMinutes)
        {
            _progress.LongestSessionMinutes = durationMinutes;
        }

        if (durationMinutes >= 180)
        {
            _logger.Information("Deep Sleep check: Session duration {Duration:F1}min >= 180min, unlocking!", durationMinutes);
            TryUnlock("deep_sleep");
        }
        else if (durationMinutes >= 60)
        {
            _logger.Debug("Session {Duration:F1}min completed - need 180min for Deep Sleep achievement", durationMinutes);
        }

        if (noPanicEnabled)
        {
            _logger.Information("No panic was enabled - unlocking 'what_panic_button'");
            _progress.CompletedSessionWithNoPanic = true;
            TryUnlock("what_panic_button");
        }

        var sessionLower = sessionName.ToLowerInvariant();

        if (sessionLower.Contains("distant doll"))
        {
            TryUnlock("sofa_decor");
        }

        if (sessionLower.Contains("good girls") && strictLockEnabled)
        {
            _progress.CompletedGoodGirlsWithStrictLock = true;
            TryUnlock("look_but_dont_touch");
        }

        if (sessionLower.Contains("morning drift"))
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 6 && hour < 9)
            {
                _progress.CompletedMorningDriftInMorning = true;
                TryUnlock("morning_glory");
            }
        }

        if (sessionLower.Contains("gamer girl") && !_progress.AltTabPressedThisSession)
        {
            _progress.CompletedGamerGirlNoAltTab = true;
            TryUnlock("player_2_disconnected");
        }

        TryQuestTrack("TrackSessionCompleted");
        _isDirty = true;
    }

    /// <inheritdoc />
    public bool TryUnlock(string achievementId)
    {
        _logger.Debug("TryUnlock called for: {Id}", achievementId);

        if (_progress.IsUnlocked(achievementId))
        {
            _logger.Debug("Achievement {Id} already unlocked", achievementId);
            return false;
        }

        if (!Achievement.All.TryGetValue(achievementId, out var achievement))
        {
            _logger.Warning("Unknown achievement ID: {Id}", achievementId);
            return false;
        }

        _progress.Unlock(achievementId);
        _isDirty = true;
        Save();

        _logger.Information("Achievement unlocked: {Name} (ID: {Id}){Suppressed}", achievement.Name, achievementId,
            SuppressPopups ? " (popup suppressed)" : "");

        if (SuppressPopups) return true;

        try
        {
            _uiDispatcher.Post(() =>
            {
                try
                {
                    _logger.Debug("Firing AchievementUnlocked event for: {Name}", achievement.Name);
                    AchievementUnlocked?.Invoke(this, achievement);
                    TryHapticAchievementPattern();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to fire achievement event");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to post achievement event");
        }

        return true;
    }

    /// <inheritdoc />
    public void TrackAttentionCheckPassed(bool isVideo = false)
    {
        _progress.TotalAttentionChecksPassed++;
        if (isVideo)
        {
            _progress.VideoAttentionChecksPassed++;
        }
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackVideoAttentionCheckFailed()
    {
        _progress.VideoAttentionChecksFailed++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackBubbleCountGameStarted()
    {
        _progress.TotalBubbleCountGames++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackBubbleCountGameResult(bool success)
    {
        if (success)
        {
            _progress.TotalBubbleCountCorrect++;
        }
        else
        {
            _progress.TotalBubbleCountFailed++;
        }
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSessionStarted()
    {
        _progress.TotalSessionsStarted++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSessionAbandoned()
    {
        _progress.TotalSessionsAbandoned++;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackXPEarned(double amount)
    {
        _progress.TotalXPEarned += amount;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void TrackSkillPointsEarned(int amount)
    {
        _progress.TotalSkillPointsEarned += amount;
        _isDirty = true;
    }

    /// <inheritdoc />
    public void MarkDirty() => _isDirty = true;

    /// <inheritdoc />
    public void ResetProgress()
    {
        _progress = new AchievementProgress();
        _isDirty = false;
        Save();
        _logger.Information("AchievementService progress reset");
    }

    /// <inheritdoc />
    public int GetUnlockedCount() => _progress.UnlockedAchievements.Count;

    /// <inheritdoc />
    public int GetTotalCount()
    {
        var count = 0;
        foreach (var a in Achievement.All.Values)
            if (!a.IsHidden) count++;
        return count;
    }

    /// <inheritdoc />
    public int GetUnlockedCount(bool exclusive)
    {
        var count = 0;
        foreach (var id in _progress.UnlockedAchievements)
        {
            if (Achievement.All.TryGetValue(id, out var a) && a.IsExclusive == exclusive)
                count++;
        }
        return count;
    }

    /// <inheritdoc />
    public int GetTotalCount(bool exclusive)
    {
        var count = 0;
        foreach (var a in Achievement.All.Values)
        {
            if (a.IsHidden) continue;
            if (a.IsExclusive == exclusive) count++;
        }
        return count;
    }

    /// <inheritdoc />
    public bool TryUnlockExclusive(string achievementId)
    {
        if (_progress.IsUnlocked(achievementId)) return false;
        if (!CanUnlockExclusive)
        {
            _logger.Debug("Exclusive achievement {Id} withheld — user not entitled", achievementId);
            return false;
        }
        return TryUnlock(achievementId);
    }

    /// <inheritdoc />
    public void TrackFeatureUsed(string featureId, double amount = 1)
    {
        switch (featureId.ToLowerInvariant())
        {
            case "flashimage":
            case "flash_image":
                for (int i = 0; i < (int)amount; i++) TrackFlashImage();
                break;

            case "bubblepopped":
            case "bubble_popped":
                for (int i = 0; i < (int)amount; i++) TrackBubblePopped();
                break;

            case "avatarclick":
            case "avatar_click":
                TrackAvatarClick();
                break;

            case "cornerhit":
            case "corner_hit":
                TrackCornerHit();
                break;

            case "videowatched":
            case "video_watched":
                TrackVideoWatched(amount);
                break;

            default:
                _logger.Debug("TrackFeatureUsed: unmapped feature '{FeatureId}' amount {Amount}", featureId, amount);
                break;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _saveTimer.Dispose();
        _trackingTimer.Dispose();
        Save();
    }

    private static bool IsOverlayRunning()
    {
        try
        {
            var overlay = App.Overlay;
            if (overlay == null) return false;

            // Prefer a strongly typed interface if the head wires one up.
            if (overlay is IOverlaySurface surface) return surface.IsVisible;

            // Fall back to dynamic for legacy WPF OverlayService.
            dynamic d = overlay;
            return d.IsRunning == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDeeperActivelyPlaying()
    {
        try
        {
            var deeper = App.DeeperHost;
            if (deeper == null) return false;

            dynamic d = deeper;
            return d.IsActivelyPlaying == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool GetDynamicBoolean(object? target, string propertyName)
    {
        try
        {
            if (target == null) return false;

            var property = target.GetType().GetProperty(propertyName);
            if (property != null && property.PropertyType == typeof(bool))
            {
                return (bool)property.GetValue(target)!;
            }

            dynamic d = target;
            return d[propertyName] == true || d.GetType().GetProperty(propertyName)?.GetValue(d) == true;
        }
        catch
        {
            return false;
        }
    }

    private void TryQuestTrack(string methodName, params object[] args)
    {
        try
        {
            var quests = App.Quests;
            if (quests == null) return;

            var method = quests.GetType().GetMethod(methodName);
            method?.Invoke(quests, args);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Quest track call {MethodName} failed", methodName);
        }
    }

    private void TryHapticAchievementPattern()
    {
        try
        {
            var haptics = App.Haptics;
            if (haptics == null) return;

            var method = haptics.GetType().GetMethod("AchievementPatternAsync");
            if (method == null) return;

            _ = method.Invoke(haptics, null);
            _ = method.Invoke(haptics, null);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Achievement haptic pattern failed");
        }
    }

    private void TryHapticAvatarEasterEggPattern()
    {
        try
        {
            var haptics = App.Haptics;
            if (haptics == null) return;

            var method = haptics.GetType().GetMethod("AvatarEasterEggPatternAsync");
            if (method == null) return;

            _ = method.Invoke(haptics, null);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Avatar easter-egg haptic pattern failed");
        }
    }
}
