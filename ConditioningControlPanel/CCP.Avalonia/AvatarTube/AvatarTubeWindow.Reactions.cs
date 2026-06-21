using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public class FlashAudioPlayingEventArgs : EventArgs
    {
        public string FilePath { get; }
        public FlashAudioPlayingEventArgs(string filePath) => FilePath = filePath;
    }

    public class ActivityChangedEventArgs : EventArgs
    {
        public object Category { get; }
        public object PreviousCategory { get; }
        public string DetectedName { get; }
        public string ServiceName { get; }
        public string PageTitle { get; }

        public ActivityChangedEventArgs(object category, object previousCategory, string detectedName,
            string serviceName = "", string pageTitle = "")
        {
            Category = category;
            PreviousCategory = previousCategory;
            DetectedName = detectedName;
            ServiceName = serviceName;
            PageTitle = pageTitle;
        }
    }

    public partial class AvatarTubeWindow
    {
        private readonly DateTime _startupTime = DateTime.Now;

        private void OnActivityChanged(object? sender, EventArgs e)
        {
            if (e is not ActivityChangedEventArgs args) return;
            if (!IsSpeechReady()) return;
            Giggle(GetPhraseForCategory(args.Category, args.DetectedName));
        }

        private void OnStillOnActivity(object? sender, EventArgs e)
        {
            if (e is not ActivityChangedEventArgs args) return;
            if (!IsSpeechReady()) return;
            Giggle(GetPhraseForCategory(args.PreviousCategory, args.DetectedName));
        }

        private void OnVideoAboutToStart(object? sender, EventArgs e)
        {
            Giggle("Ooh! Pretty spir-rals...");
        }

        private async void OnVideoEnded(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    BringAttachedPairToFront();
                });
            }

            if (_settings?.Current?.AiChatEnabled == true)
            {
                await Task.Delay(100);
                GigglePriority("That was fun~", aiGenerated: false);
            }
        }

        private void OnGameCompleted(object? sender, EventArgs e)
        {
            Giggle("Good girl! So smart!");
        }

        private void OnGameFailed(object? sender, EventArgs e)
        {
            GiggleFromCategory("GameFailed");
        }

        private void OnBubblePopped()
        {
            _bubblePopCounter++;
            if (_bubblePopCounter % 5 == 0) GiggleFromCategory("BubblePop");
        }

        private void OnBubbleMissed()
        {
            if (_random.Next(3) == 0) GiggleFromCategory("BubbleMissed");
        }

        private void OnFlashAboutToDisplay(object? sender, EventArgs e)
        {
            _flashCounter++;
            if (_settings?.Current?.FlashAudioEnabled == true) return;
            if (_flashCounter % 4 == 1) GiggleFromCategory("FlashPre");
        }

        private void OnFlashClicked(object? sender, EventArgs e)
        {
            if (_random.Next(3) == 0) GiggleFromCategory("FlashClicked");
        }

        private void OnFlashAudioPlaying(object? sender, EventArgs e)
        {
            if (e is not FlashAudioPlayingEventArgs args) return;
            var fileName = Path.GetFileNameWithoutExtension(args.FilePath) ?? "?";
            GigglePriority($"♫ {fileName}", aiGenerated: false);
        }

        private void OnSubliminalDisplayed(object? sender, EventArgs e)
        {
            _subliminalCounter++;
            if (_subliminalCounter % 10 == 0) GiggleFromCategory("SubliminalAck");
        }

        private void OnAchievementUnlocked(object? sender, Achievement achievement)
        {
            GigglePriority($"Achievement unlocked: {achievement.Name}! *giggles*", aiGenerated: false);
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            GiggleFromCategory("LevelUp");
        }

        private void OnCompanionLevelUp(object? sender, (CompanionId Companion, int NewLevel) args)
        {
            RefreshCompanionDisplay();
            var def = CompanionDefinition.GetById(args.Companion);
            if (args.NewLevel == CompanionProgress.MaxLevel)
                GigglePriority($"{def.Name} reached MAX LEVEL! *sparkles*", aiGenerated: false);
            else if (args.NewLevel % 10 == 0)
                GigglePriority($"{def.Name} is now level {args.NewLevel}! Keep going!", aiGenerated: false);
            else
                GiggleFromCategory("LevelUp");
        }

        private void OnCompanionSwitched(object? sender, CompanionId newCompanion)
        {
            RefreshCompanionDisplay();
            _speechQueue.Clear();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isGiggling = false;
            _companionGreetingDebounce?.Stop();
            _companionGreetingDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _companionGreetingDebounce.Tick += (_, _) =>
            {
                _companionGreetingDebounce.Stop();
                var name = CompanionDefinition.GetById(newCompanion).Name;
                Giggle($"Hi! {name} is here now~");
            };
            _companionGreetingDebounce.Start();
        }

        private void OnMindWipeTriggered(object? sender, EventArgs e)
        {
            _mindWipeCounter++;
            if (_mindWipeCounter % 6 == 0) GiggleFromCategory("MindWipe");
        }

        private void OnBrainDrainTriggered(object? sender, EventArgs e)
        {
            _brainDrainCounter++;
            if (_brainDrainCounter % 6 == 0) GiggleFromCategory("BrainDrain");
        }

        private void OnEngineStopped(object? sender, EventArgs e)
        {
            GiggleFromCategory("EngineStop");
        }

        public async Task<bool> PlayLockCardAiReactionAsync(object e)
        {
            if (e is not LockCardResultEventArgs args) return false;
            if (_settings?.Current?.AiChatEnabled != true) return false;

            var ai = App.Services.GetService<IAiService>();
            if (ai == null || !ai.IsAvailable) return false;

            try
            {
                var reaction = await ai.GetLockScreenReaction(args.Sentence, args.Mistakes, args.Amount);
                if (!string.IsNullOrWhiteSpace(reaction))
                {
                    GigglePriority(reaction, aiGenerated: true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Lock card AI reaction failed");
            }
            return false;
        }
    }

    public class LockCardResultEventArgs : EventArgs
    {
        public string Sentence { get; }
        public int Mistakes { get; }
        public int Amount { get; }
        public LockCardResultEventArgs(string sentence, int mistakes, int amount)
        {
            Sentence = sentence;
            Mistakes = mistakes;
            Amount = amount;
        }
    }
}
