using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Avatar
{
    /// <summary>
    /// Resolves whether the active mod ships an emotive-portrait avatar manifest and loads it.
    /// </summary>
    public interface IAvatarPortraitService
    {
        bool HasManifestForActiveMod();
        IAvatarPortraitSet? Load();
    }

    /// <summary>
    /// A loaded emotive-portrait avatar set. UI-framework-free: consumers ask for absolute file paths
    /// and create their own bitmaps.
    /// </summary>
    public interface IAvatarPortraitSet
    {
        int SkinCount { get; }
        string IdleEmotion { get; }
        string DefaultEmotion { get; }
        AvatarDirector Director { get; }
        IReadOnlyList<AvatarSkin> Skins { get; }

        int ClampSkin(int skinIndex);
        string? EmotionForLine(string? lineId);
        string EmotionForMood(string? mood);
        IReadOnlyList<string> FxForEmotion(string emotion);
        IReadOnlyList<string> GetBucketPaths(int skinIndex, string emotion);
    }
}
