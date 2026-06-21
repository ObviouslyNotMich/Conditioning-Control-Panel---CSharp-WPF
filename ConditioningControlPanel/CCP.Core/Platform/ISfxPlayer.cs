namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform one-shot sound effect player.
/// </summary>
public interface ISfxPlayer
{
    /// <summary>
    /// Plays the named effect if it can be resolved.
    /// </summary>
    /// <param name="name">Effect base name (extension resolved by implementation).</param>
    /// <param name="volume">Playback volume scale, 0.0 to 1.0.</param>
    void Play(string name, float volume = 0.6f);
}
