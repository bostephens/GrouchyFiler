using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GrouchyFiler.Models;

public sealed class AppConfig
{
    public bool DryRun { get; set; } = true;
    public string? LogFile { get; set; }
    public string LogLevel { get; set; } = "info";
    public long LogMaxBytes { get; set; } = 10485760;
    public int LogBackupCount { get; set; } = 3;
    public bool GrouchyMode { get; set; } = true;
    public int ScanIntervalSeconds { get; set; } = 30;
    public List<RootConfig> Roots { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static AppConfig Read(string path)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Configuration cannot be null.");
        if (config.ScanIntervalSeconds < 5 || config.ScanIntervalSeconds > 86400)
            throw new InvalidDataException("ScanIntervalSeconds must be between 5 and 86400.");
        if (config.LogMaxBytes < 4096 || config.LogMaxBytes > 1073741824 || config.LogBackupCount < 0 || config.LogBackupCount > 10)
            throw new InvalidDataException("LogMaxBytes must be 4096..1073741824 and LogBackupCount must be 0..10.");
        if (config.Roots is null) throw new InvalidDataException("Roots must be an array.");
        if (config.LogLevel is null || !new[] { "debug", "info", "warning", "error", "none" }.Contains(config.LogLevel.ToLowerInvariant()))
            throw new InvalidDataException("LogLevel must be debug, info, warning, error or none.");
        config.LogLevel = config.LogLevel.ToLowerInvariant();
        foreach (var root in config.Roots)
        {
            if (root is null || string.IsNullOrWhiteSpace(root.Path))
                throw new InvalidDataException("Every root requires an absolute folder Path.");
            root.Path = ExpandPath(root.Path);
            if (!System.IO.Path.IsPathFullyQualified(root.Path))
                throw new InvalidDataException("Every root requires an absolute folder Path after environment-variable expansion.");
            root.Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root.Path));
            if (!Directory.Exists(root.Path)) throw new InvalidDataException($"Folder does not exist: {root.Path}");
            if (string.Equals(root.Path, System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetPathRoot(root.Path)!), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a specific folder, not an entire drive.");
            if (root.Patterns is null || root.Patterns.Count == 0 || root.Exclude is null ||
                root.Exclude.Any(p => string.IsNullOrWhiteSpace(p) || p.IndexOfAny(['/', '\\']) >= 0))
                throw new InvalidDataException("Include patterns are required; exclusions must be filename wildcards without directory separators.");
            foreach (var pattern in root.Patterns)
            {
                if (pattern is null || string.IsNullOrWhiteSpace(pattern.Value) ||
                    !(string.Equals(pattern.Type, "glob", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(pattern.Type, "regex", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(pattern.Type, "literal", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Each pattern needs a glob, regex or literal Type and a nonempty Value.");
                if (string.Equals(pattern.Type, "regex", StringComparison.OrdinalIgnoreCase))
                {
                    try { _ = new Regex(pattern.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
                    catch (ArgumentException ex) { throw new InvalidDataException($"Invalid regex in {root.Path}: {pattern.Value}. {ex.Message}", ex); }
                }
                if (!string.Equals(pattern.Type, "regex", StringComparison.OrdinalIgnoreCase) && pattern.Value.IndexOfAny(['/', '\\']) >= 0)
                    throw new InvalidDataException("Glob and literal patterns must be filenames without directory separators.");
            }
            if (root.MinimumAgeSeconds < 0 || root.MinimumAgeSeconds > 315360000 || root.MinimumSizeBytes < 0 || root.MaximumSizeBytes < 0 ||
                root.MaximumSizeBytes is long max && max < root.MinimumSizeBytes)
                throw new InvalidDataException("Age and size limits are invalid.");
        }
        if (!string.IsNullOrWhiteSpace(config.LogFile))
        {
            config.LogFile = System.IO.Path.GetFullPath(ExpandPath(config.LogFile), System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            if (Enumerable.Range(0, config.LogBackupCount + 1).Any(index => string.Equals(config.LogFile + (index == 0 ? "" : "." + index), System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) ||
                config.Roots.Any(root => config.LogFile.StartsWith(root.Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(config.LogFile, root.Path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("LogFile must be outside watched folders and must not be the configuration file.");
        }
        return config;
    }

    private static string ExpandPath(string path)
    {
        foreach (Match variable in Regex.Matches(path, "%([^%]+)%"))
        {
            string name = variable.Groups[1].Value;
            if (Environment.GetEnvironmentVariable(name) is null)
                throw new InvalidDataException($"Environment variable '{name}' is not defined.");
        }
        return Environment.ExpandEnvironmentVariables(path);
    }
}

