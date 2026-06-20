using System;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        public class ChatMessage
        {
            public string Text { get; set; } = string.Empty;
            public bool IsUser { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string TimeLabel => Timestamp.ToString("HH:mm");
        }

        private enum SpeechSource
        {
            Preset,
            Trigger,
            AI
        }

        private void ShowGreeting()
        {
            Giggle(GetRandomBambiPhrase());
        }

        private void GiggleFromCategory(string category)
        {
            Giggle($"[{category}] {GetRandomBambiPhrase()}");
        }

        private string GetPhraseForCategory(object category, string name) => $"Ooh, {name}?~";

        private void StartTypewriter(string text, bool slow)
        {
            _typewriterTimer?.Stop();
            _typewriterFullText = text;
            _typewriterIndex = 0;
            _typewriterTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(slow ? 60 : 20)
            };
            _typewriterTimer.Tick += (_, _) =>
            {
                if (_typewriterIndex >= _typewriterFullText.Length)
                {
                    _typewriterTimer?.Stop();
                    PopulateSpeechBubble(_typewriterFullText);
                    return;
                }
                _typewriterIndex++;
                PopulateSpeechBubble(_typewriterFullText[.._typewriterIndex]);
            };
            _typewriterTimer.Start();
        }

        private void StopTypewriter()
        {
            _typewriterTimer?.Stop();
        }

        private double EstimateTypewriterDurationMs(int length, bool slow) => length * (slow ? 60 : 20);
    }
}
