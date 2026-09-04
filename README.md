# Grouchy Filer

A Windows tray app that previews or deletes files matching explicit folder rules. The published Windows x64 executable includes the .NET runtime; the .NET 10 SDK is required to build from source.

Run `dotnet run --project GrouchyFiler` or build with `dotnet build GrouchyFiler.slnx -c Release` and launch `GrouchyFiler/bin/Release/net10.0-windows/GrouchyFiler.exe`.

Publish the standalone distribution with `dotnet publish GrouchyFiler/GrouchyFiler.csproj -c Release -p:PublishProfile=SingleFile` (or select the SingleFile profile in Visual Studio). Distribute `GrouchyFiler.exe`, `config.json`, and `README.md` from `artifacts/GrouchyFiler`. Managed dependencies, native runtime libraries and icons are bundled in the executable. Release builds omit debug symbols; source paths are mapped to a neutral build path. Native runtime components extract to .NET's temporary bundle cache at startup. Publishing supplies a safe default config only when the destination config does not already exist.

The app reads `config.json` beside the executable, independent of the working directory. If missing, it creates the same safe default shipped by the publish profile. With no folders configured, the window opens for setup. Choose **Edit Config**, review the default rules, change their paths and filters, save, then choose **Reload Config**. Keep the app in a writable folder. The older `%LOCALAPPDATA%/GrouchyFiler/config.json` is no longer used automatically; copy your desired settings into the config beside the executable.

Folder Path and LogFile support Windows environment variables, for example `"path": "%TEMP%"`, `"path": "%USERPROFILE%/Downloads"`, or `"logFile": "%LOCALAPPDATA%/GrouchyFiler/grouchy.log"`. Variables expand from the running app's environment on each config load, before folder and log-path validation. Names are case-insensitive on Windows. Undefined variables produce a configuration error and pause previews. Restart the app after changing Windows environment variables so it receives the updated environment. Pattern values and exclusions are not expanded.

Newly created configs set **DryRun to true** and include separate Downloads and TEMP folder rules, with a one-day minimum age and subfolder scanning disabled. Check **Dry Run Mode** to preview matches; uncheck it to enable permanent deletion (not the Recycle Bin). Live cleanup applies to existing and newly eligible matching files on manual and scheduled scans. The status and log identify the active mode. Checkbox changes apply to the current session without rewriting the config. Startup and Reload Config honor the saved DryRun value, including false; omitting it defaults to true. Pause is also a session control. Configuration errors enable dry run and pause cleanup; fix and reload, then uncheck Pause Watching.

Close the window to keep the app in the tray. Double-click its tray icon to reopen it, or use the tray menu's **Exit** to quit. Launches with configured folders start in the tray.

Example configuration:

The repository's `config.json` includes the Downloads example. It is a sample, not automatically activated; use Edit Config to update the active configuration. Windows paths require doubled backslashes in JSON (or use forward slashes). The glob `~$*` needs no backslash; the regex `^debug\\..*\\.log$` matches names such as `debug.session.log`.

```json
{
  "DryRun": true,
  "LogFile": "grouchy.log",
  "LogLevel": "info",
  "GrouchyMode": true,
  "ScanIntervalSeconds": 30,
  "Roots": [
    {
      "Path": "C:/Users/YourName/Downloads",
      "IncludeSubdirectories": false,
      "Patterns": [
        { "Type": "glob", "Value": "*.tmp" },
        { "Type": "regex", "Value": "^backup-\\d+\\.bak$" },
        { "Type": "literal", "Value": "debug.log" }
      ],
      "Exclude": ["keep-*"],
      "MinimumAgeSeconds": 86400,
      "MinimumSizeBytes": 0,
      "MaximumSizeBytes": null,
      "EmptyOnly": false
    }
  ]
}
```

Patterns match filenames case-insensitively. `glob` matches the entire filename, with `*` matching any sequence and `?` one character. `literal` matches an exact filename. `regex` uses .NET regular expressions; add anchors when you want a whole-filename match. Invalid regexes are rejected on configuration load; matches exceeding 100 ms return false. JSON regex backslashes must be doubled, as shown above. Existing string patterns such as `"*.tmp"` remain supported as glob shorthand. Exclude remain glob strings. At least one include pattern is required; any include can match and exclusions win. All age and size conditions must match. Age uses the last-write time, with a minimum two-second settling period. Size is in bytes; null means no maximum. EmptyOnly restricts matches to zero-byte files. Subfolder scanning is opt-in. Drive roots are rejected, and reparse points (including junctions and symbolic links) are skipped. Use this for ordinary local folders, not directories whose structure is being changed by untrusted processes.

Scans include existing files and run every 5–86400 seconds. Dry-run scanning reads file metadata only. Live cleanup pins ancestor directories and validates file metadata while holding the same exclusive handle used for deletion; busy files are skipped and retried on a later scan. Scan Now reports startup, folders, progress during longer scans, and a summary of checked files, matches, previews, deletions and errors. A manual scan shows matching files again; scheduled scans suppress duplicate previews until reload or a mode change. Paused, unconfigured and already-running scans explain why a manual scan cannot start. The window updates its bounded activity log in batches every 100 ms and scrolls to the newest messages. Grouchy Mode adds personality messages to previews and deletions.

LogFile optionally appends service messages to disk. Relative paths resolve beside the active config. The default `logFile: null` avoids creating additional log files; the window still displays activity. To enable disk logging without adding files beside the app, use a path such as `%LOCALAPPDATA%/GrouchyFiler/grouchy.log`. The log must be outside watched folders and separate from the config. LogLevel filters file output: debug (including personality), info (including previews), warning, error, or none. UI messages remain visible at every level. Omit LogFile or set it to null to disable file logging. Disk logs rotate at LogMaxBytes (default 10 MiB) with LogBackupCount backups (default 3). Disk writes use a bounded background queue to keep controls responsive; pending entries can be dropped on overload or exit. See the shipped guide for limits and configuration examples.

Run the regression checks with `dotnet run --project GrouchyFiler.Tests -c Release`. Live tests wrap the production deletion operation with a guard that verifies each absolute, normalized target is strictly below that run's unique `test-data` subfolder. It rejects paths outside that folder, directories, symbolic links and junctions. Tests delete selected generated fixtures only; remaining fixtures and logs are preserved. Live test instances use explicit scans and isolated configs, including tests of the actual desktop checkbox and first-run defaults. Your active desktop config is not used by tests.

Multiple folders are supported through the roots array. Each root has independent patterns, exclusions, age/size filters and subfolder settings. The default configuration includes %USERPROFILE%/Downloads and %TEMP%; each scan processes both. Add more root objects to cover additional folders.

The shipped user guide is maintained in GrouchyFiler/README.md and copied to the publish folder automatically. It documents every setting and provides strict JSON examples for glob, literal, and regex patterns. The shipped config and embedded first-run template contain no comments or trailing commas.

Version 1.0.0 adds per-user-session single-instance activation, responsive scan cancellation, per-folder enumeration errors, manual skip explanations, configuration-time regex validation, and bounded disk-log rotation. Pause, mode changes, reload and shutdown cancel in-flight scans before further deletion; an OS deletion already submitted can finish. The tray About menu and window title display the version. Live regression checks cover cancellation and file-change races, exclusively open handle deletion, inaccessible siblings, and cross-process activation. No code signing is configured.


## License and disclaimer

Copyright (c) 2026 bostephens. Licensed under the MIT License. Redistribution must retain the copyright and license notice in all copies or substantial portions of the software. Third-party components remain subject to their respective licenses.

**Use at your own risk. This application can permanently delete files.** To the maximum extent permitted by applicable law, the authors and copyright holders disclaim all warranties and liability arising from use of this software, including liability for data loss, accidental deletion, data corruption, software defects, system damage, business interruption, lost profits, or other damages. You are responsible for reviewing your configuration and maintaining backups. This notice summarizes the MIT License disclaimer; it does not add restrictions to the MIT License or limit rights that cannot legally be excluded.

See [LICENSE](LICENSE) for the complete terms.
