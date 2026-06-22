using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Captures Avalonia log events of interest during a smoke test while still
/// mirroring them to the console.
/// </summary>
internal sealed class SmokeTestLogSink : ILogSink
{
    private readonly LogEventLevel _minimumLevel;
    private readonly List<SmokeTestLogEntry> _entries = new();

    public SmokeTestLogSink(LogEventLevel minimumLevel = LogEventLevel.Warning)
    {
        _minimumLevel = minimumLevel;
    }

    public IReadOnlyList<SmokeTestLogEntry> Entries => _entries;

    public bool IsEnabled(LogEventLevel level, string area)
        => level >= _minimumLevel;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        if (!IsEnabled(level, area)) return;
        var rendered = messageTemplate;
        Console.WriteLine($"[{level}] [{area}] {rendered}");
        if (IsInteresting(area, level))
            _entries.Add(new SmokeTestLogEntry(level.ToString(), area, rendered));
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        // Avalonia uses named placeholders (e.g. {Property}, {Expression}) in its message templates.
        // string.Format expects numeric placeholders, so format them manually to avoid first-chance
        // FormatExceptions while still producing a readable merged message.
        var rendered = RenderMessageTemplate(messageTemplate, propertyValues);

        Console.WriteLine($"[{level}] [{area}] {rendered}");
        if (IsInteresting(area, level))
            _entries.Add(new SmokeTestLogEntry(level.ToString(), area, rendered));
    }

    private static string RenderMessageTemplate(string messageTemplate, object?[]? propertyValues)
    {
        if (propertyValues is null || propertyValues.Length == 0)
            return messageTemplate;

        // Avalonia structured templates list placeholders in declaration order and pass
        // matching property values in the same order. Replace {Name} placeholders with
        // the corresponding positional value.
        var values = propertyValues;
        var index = 0;
        var result = System.Text.RegularExpressions.Regex.Replace(
            messageTemplate,
            @"\{([A-Za-z_][A-Za-z0-9_]*)\}",
            m =>
            {
                if (index < values.Length)
                {
                    var value = values[index++];
                    return value?.ToString() ?? "(null)";
                }
                return m.Value;
            });

        // If there were leftover values (unmatched placeholders), append them explicitly.
        if (index < values.Length)
        {
            result += " | values: " + string.Join(", ", values.Skip(index).Select(v => v?.ToString() ?? "(null)"));
        }

        return result;
    }

    private static bool IsInteresting(string area, LogEventLevel level)
    {
        // Capture warnings/errors from binding, resources, and layout — these
        // usually indicate parity gaps (missing key, missing asset, broken path).
        if (level >= LogEventLevel.Error)
            return true;

        var lower = area.ToLowerInvariant();
        return lower.Contains("binding") || lower.Contains("resource") || lower.Contains("layout");
    }
}

internal sealed record SmokeTestLogEntry(string Level, string Area, string Message);
