using System;

namespace ConditioningControlPanel.Avalonia.Services.Moderation
{
    /// <summary>
    /// CCBill AI Content Merchant Addendum prohibited categories.
    /// Mirrored from the WPF head so Avalonia can compile without taking a dependency
    /// on the legacy project. This will be unified once the §19.4 project-reference
    /// collapse is unblocked.
    /// </summary>
    public enum ProhibitedCategory
    {
        Illegal,
        Minor,
        NonConsensual,
        Incest,
        Bestiality,
        Watersports,
        SnuffViolence,
        HypnosisSexual,
        Prostitution,
        Polygamy,
        HateSpeech,
        Deepfake,
        ProfessionalAdvice,
        PromptExtraction,
        SystemPromptLeak
    }

    public record ModerationCounterState(
        int HitsInLastTenMinutes,
        bool WarningTriggered,
        bool CooldownActive,
        DateTime? CooldownEndsAt);

    public interface IModerationCounter
    {
        void RecordHit(ProhibitedCategory category, string source);
        ModerationCounterState GetState();
        void LoadFromDisk();

        event Action<ModerationCounterState>? WarningTriggered;
        event Action<DateTime>? CooldownStarted;
        event Action? CooldownEnded;
    }

    /// <summary>
    /// Avalonia stub for the moderation counter. Always reports no active cooldown.
    /// The real sliding-window counter lives in the WPF head; this stub lets the
    /// AvatarTube chat path compile and run until the service is ported to Core.
    /// </summary>
    public sealed class AvaloniaModerationCounter : IModerationCounter
    {
        public event Action<ModerationCounterState>? WarningTriggered;
        public event Action<DateTime>? CooldownStarted;
        public event Action? CooldownEnded;

        public void RecordHit(ProhibitedCategory category, string source)
        {
            // No-op in the Avalonia stub.
        }

        public ModerationCounterState GetState()
            => new(0, false, false, null);

        public void LoadFromDisk()
        {
            // No-op.
        }
    }
}
