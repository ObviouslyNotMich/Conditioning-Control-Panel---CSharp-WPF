using System;

namespace ConditioningControlPanel.Avalonia.Services.Tutorial;

/// <summary>
/// Static hand-off bus for the two-part Deeper interactive tutorials.
/// NewEnhancementDialog sets <see cref="PendingPart2Tutorial"/> after the user
/// clicks Create with a valid source; DeeperEditorWindow reads and clears it in
/// its Opened handler to start Part 2.
/// </summary>
public static class TutorialEventBus
{
    public static TutorialType? PendingPart2Tutorial { get; set; }

    public static string? LastSavedEnhancementPath { get; set; }

    public static event EventHandler<string>? Event;

    public static void Emit(string name)
    {
        Event?.Invoke(null, name);
    }
}
