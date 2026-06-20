using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private readonly DateTime _startupTime = DateTime.Now;

        private void OnActivityChanged(object? sender, EventArgs e)
        {
            // TODO: window-awareness reaction hook.
        }

        private void OnStillOnActivity(object? sender, EventArgs e)
        {
            // TODO: still-on-activity reaction hook.
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
            // TODO: show flash audio filename in the bubble.
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
            await Task.Yield();
            return false;
        }
    }
}
