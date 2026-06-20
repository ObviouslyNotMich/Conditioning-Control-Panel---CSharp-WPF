namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform application environment paths.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>
    /// Base directory of the application (where the entry assembly resides).
    /// </summary>
    string BaseDirectory { get; }

    /// <summary>
    /// Root path for user-specific persistent data (settings, logs, caches).
    /// </summary>
    string UserDataPath { get; }

    /// <summary>
    /// Path for user-specific roaming/config data (session exports, custom packs).
    /// On Windows this is ApplicationData; on Unix it typically equals <see cref="UserDataPath"/>.
    /// </summary>
    string ApplicationDataPath { get; }

    /// <summary>
    /// Effective assets directory. Defaults to a subfolder of <see cref="UserDataPath"/>
    /// but may be overridden by user settings.
    /// </summary>
    string EffectiveAssetsPath { get; }
}
