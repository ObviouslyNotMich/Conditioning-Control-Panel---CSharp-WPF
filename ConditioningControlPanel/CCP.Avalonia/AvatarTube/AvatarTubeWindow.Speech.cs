using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

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

        private enum ActivityCategory
        {
            Unknown,
            Gaming,
            Browsing,
            Social,
            Shopping,
            Working,
            Media,
            Learning,
            Idle
        }

        private void ShowGreeting()
        {
            var phrase = PickPhrase("StartupGreeting") ?? GetRandomBambiPhrase();
            Giggle(phrase);
        }

        private void GiggleFromCategory(string category)
        {
            var phrase = PickPhrase(category);
            if (string.IsNullOrWhiteSpace(phrase)) return;

            string? audioPath = ResolvePhraseAudio(category, phrase);
            Giggle(phrase, audioPath);
        }

        private string GetPhraseForCategory(object category, string detectedName = "")
        {
            var ac = category is ActivityCategory cat ? cat : ActivityCategory.Unknown;
            return GetPhraseForCategory(ac, detectedName);
        }

        private string GetPhraseForCategory(ActivityCategory category, string detectedName = "")
        {
            var lowerName = detectedName?.ToLowerInvariant() ?? "";

            string categoryName;
            if (lowerName.Contains("discord"))
                categoryName = "Discord";
            else if (lowerName.Contains("bambicloud") || lowerName.Contains("hypnotube"))
                categoryName = "TrainingSite";
            else if (lowerName.Contains("bambi") || lowerName.Contains("sissy") || lowerName.Contains("hypno"))
                categoryName = "HypnoContent";
            else
            {
                categoryName = category switch
                {
                    ActivityCategory.Gaming => "Gaming",
                    ActivityCategory.Browsing => "Browsing",
                    ActivityCategory.Shopping => "Shopping",
                    ActivityCategory.Social => "Social",
                    ActivityCategory.Working => "Working",
                    ActivityCategory.Media => "Media",
                    ActivityCategory.Learning => "Learning",
                    ActivityCategory.Idle => "WindowAwarenessIdle",
                    _ => "RandomFloating"
                };
            }

            var phrase = PickPhrase(categoryName);
            if (string.IsNullOrWhiteSpace(phrase)) phrase = "*giggles*";

            if (phrase.Contains("{0}"))
            {
                if (!string.IsNullOrEmpty(detectedName))
                    phrase = string.Format(phrase, detectedName);
                else
                    phrase = phrase.Replace("{0} ", " ").Replace("{0}", "").Replace("  ", " ").Trim();
            }

            return phrase;
        }

        private string GetRandomBambiPhrase()
        {
            var pool = GetEnabledPhrases("Generic")
                .Concat(GetEnabledPhrases("RandomFloating"))
                .ToArray();

            if (pool.Length == 0)
            {
                pool = GetRawPhrases("Generic")
                    .Concat(GetRawPhrases("RandomFloating"))
                    .ToArray();
            }

            return pool.Length == 0 ? "*giggles*" : pool[_random.Next(pool.Length)];
        }

        /// <summary>
        /// Returns enabled phrases for a category, merging mod phrases and custom phrases.
        /// </summary>
        private string[] GetEnabledPhrases(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return Array.Empty<string>();

            var mod = GetRawPhrases(category);
            var custom = GetCustomPhrasesForCategory(category);
            var combined = mod.Concat(custom.Select(c => c.Text)).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

            if (combined.Count == 0 && category != "Generic" && category != "RandomFloating")
            {
                // Fall back to generic pool so the avatar never goes completely silent.
                combined = GetRawPhrases("Generic").Concat(GetRawPhrases("RandomFloating")).ToList();
            }

            return MakeModAware(combined);
        }

        private string? PickPhrase(string category)
        {
            var pool = GetEnabledPhrases(category);
            return pool.Length == 0 ? null : pool[_random.Next(pool.Length)];
        }

        private string[] GetRawPhrases(string category)
        {
            var svc = App.Services.GetService<global::ConditioningControlPanel.IModService>();
            try { return svc?.GetPhrases(category) ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        private string[] MakeModAware(IEnumerable<string> phrases)
        {
            var svc = App.Services.GetService<global::ConditioningControlPanel.IModService>();
            return phrases.Select(p => svc?.MakeModAware(p) ?? p).ToArray();
        }

        private IEnumerable<CustomCompanionPhrase> GetCustomPhrasesForCategory(string category)
        {
            var settings = _settings?.Current;
            if (settings?.CustomCompanionPhrases == null) return Enumerable.Empty<CustomCompanionPhrase>();
            return settings.CustomCompanionPhrases
                .Where(c => !string.IsNullOrEmpty(c.Text)
                         && string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)
                         && c.Enabled);
        }

        private string? ResolvePhraseAudio(string category, string text)
        {
            var custom = GetCustomPhrasesForCategory(category).FirstOrDefault(c => c.Text == text);
            if (custom == null || string.IsNullOrEmpty(custom.AudioFileName)) return null;

            var path = Path.Combine(CompanionPhrase.DefaultAudioFolder, custom.AudioFileName);
            return File.Exists(path) ? path : null;
        }

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
