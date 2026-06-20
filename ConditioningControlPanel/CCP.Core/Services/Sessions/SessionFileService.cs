using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Cross-platform session file I/O.
/// Loads built-in sessions from app assets and custom sessions from user data.
/// </summary>
public sealed class SessionFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IAppEnvironment _environment;

    public SessionFileService(IAppEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public string CustomSessionsFolder => Path.Combine(_environment.ApplicationDataPath, "CustomSessions");

    public string BuiltInSessionsFolder => Path.Combine(_environment.BaseDirectory, "assets", "sessions");

    public void EnsureCustomFolderExists()
    {
        if (!Directory.Exists(CustomSessionsFolder))
            Directory.CreateDirectory(CustomSessionsFolder);
    }

    public void ExportSession(SessionDefinition session, string filePath)
    {
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public void ExportSession(Session session, string filePath)
    {
        var definition = SessionDefinition.FromSession(session);
        ExportSession(definition, filePath);
    }

    public SessionDefinition? ImportSession(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var session = JsonSerializer.Deserialize<SessionDefinition>(json, JsonOptions);
            if (session != null)
            {
                session.Source = SessionSource.Imported;
                session.SourceFilePath = filePath;

                if (string.IsNullOrWhiteSpace(session.Name))
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (fileName.EndsWith(".session", StringComparison.OrdinalIgnoreCase))
                        fileName = fileName[..^8];

                    session.Name = fileName;
                }
            }
            return session;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool ValidateSessionFile(string filePath, out string errorMessage)
    {
        errorMessage = "";

        if (!File.Exists(filePath))
        {
            errorMessage = "File not found";
            return false;
        }

        if (!filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "File must be a .session.json file";
            return false;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var session = JsonSerializer.Deserialize<SessionDefinition>(json, JsonOptions);

            if (session == null)
            {
                errorMessage = "Failed to parse session file";
                return false;
            }

            if (string.IsNullOrWhiteSpace(session.Id))
            {
                errorMessage = "Session must have an ID";
                return false;
            }

            if (string.IsNullOrWhiteSpace(session.Name))
            {
                errorMessage = "Session must have a name";
                return false;
            }

            if (session.DurationMinutes <= 0)
            {
                errorMessage = "Session duration must be greater than 0";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"Invalid JSON: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error reading file: {ex.Message}";
            return false;
        }
    }

    public List<SessionDefinition> LoadCustomSessions()
    {
        EnsureCustomFolderExists();
        var sessions = new List<SessionDefinition>();

        foreach (var file in Directory.GetFiles(CustomSessionsFolder, "*.session.json"))
        {
            var session = ImportSession(file);
            if (session != null)
            {
                session.Source = SessionSource.Custom;
                session.SourceFilePath = file;
                sessions.Add(session);
            }
        }

        return sessions;
    }

    public List<SessionDefinition> LoadBuiltInSessions()
    {
        var sessions = new List<SessionDefinition>();

        if (!Directory.Exists(BuiltInSessionsFolder))
            return sessions;

        foreach (var file in Directory.GetFiles(BuiltInSessionsFolder, "*.session.json"))
        {
            var session = ImportSession(file);
            if (session != null)
            {
                session.Source = SessionSource.BuiltIn;
                session.SourceFilePath = file;
                sessions.Add(session);
            }
        }

        return sessions;
    }

    public string SaveCustomSession(SessionDefinition session)
    {
        EnsureCustomFolderExists();
        session.Source = SessionSource.Custom;

        string filePath;
        if (!string.IsNullOrEmpty(session.SourceFilePath) && File.Exists(session.SourceFilePath))
        {
            filePath = session.SourceFilePath;
        }
        else
        {
            var fileName = SanitizeFileName(session.Id) + ".session.json";
            filePath = Path.Combine(CustomSessionsFolder, fileName);
            session.SourceFilePath = filePath;
        }

        ExportSession(session, filePath);
        return filePath;
    }

    public string CopyToCustomSessions(string importedFilePath, SessionDefinition session)
    {
        EnsureCustomFolderExists();

        var fileName = SanitizeFileName(session.Id) + ".session.json";
        var destPath = Path.Combine(CustomSessionsFolder, fileName);

        var counter = 1;
        while (File.Exists(destPath))
        {
            fileName = $"{SanitizeFileName(session.Id)}_{counter}.session.json";
            destPath = Path.Combine(CustomSessionsFolder, fileName);
            counter++;
        }

        session.Source = SessionSource.Custom;
        session.SourceFilePath = destPath;
        ExportSession(session, destPath);

        return destPath;
    }

    public bool DeleteCustomSession(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        if (!filePath.StartsWith(CustomSessionsFolder, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            File.Delete(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetExportFileName(Session session)
    {
        return SanitizeFileName(session.Name.Replace(" ", "_").ToLowerInvariant()) + ".session.json";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
