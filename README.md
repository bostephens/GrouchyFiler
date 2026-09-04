# Grouchy Filer

**Keep temporary files under control—with a preview before cleanup.**

Grouchy Filer is a Windows tray app that finds files using folder, filename, age, and size rules. Preview what would be removed, then enable live cleanup when you are ready.

[Download the latest release](https://github.com/bostephens/GrouchyFiler/releases/latest) · [User guide](GrouchyFiler/README.md) · [Configuration example](#configuration) · [MIT license](LICENSE)

## At a glance

| Feature | What you can do |
| --- | --- |
| Multiple folders | Give each folder its own patterns, exclusions, and filters. |
| Flexible matching | Use glob patterns, exact filenames, or regular expressions. |
| Controlled cleanup | Set minimum age, size limits, and optional subfolder scanning. |
| Dry-run previews | See eligible files before enabling permanent deletion. |
| Scheduled or manual scans | Scan automatically or choose **Scan Now** for immediate feedback. |
| Bounded logs | View recent activity, export it, and optionally keep rotating disk logs. |
| Tray operation | Keep the app running in the background; a second launch opens the existing instance. |

**Platform:** Windows x64. Release downloads include the .NET runtime; no separate runtime installation is required.

## Quick start

1. Download and extract the ZIP from the [latest release](https://github.com/bostephens/GrouchyFiler/releases/latest).
2. Keep `GrouchyFiler.exe`, `config.json`, and `README.md` together in a writable folder.
3. Run `GrouchyFiler.exe`. If it starts in the tray, double-click its icon to open the window.
4. Choose **Edit Config**, review your folders and rules, save, then choose **Reload Config**.
5. Leave **Dry Run Mode** checked and choose **Scan Now** to inspect the results.
6. When satisfied, uncheck **Dry Run Mode** to enable live cleanup.

> [!WARNING]
> Live cleanup permanently deletes matching files. It does not use the Recycle Bin. Review dry-run results and keep backups before enabling it.

### Safe starting defaults

The supplied configuration enables dry run and checks these folders every **30 seconds**:

| Folder | Included filenames | Preserved filenames |
| --- | --- | --- |
| `%USERPROFILE%/Downloads` | `*.tmp` | `keep-*` |
| `%TEMP%` | `*.tmp`, `~$*` | No additional exclusions |

Both rules require files to be **at least one day old**. Subfolder scanning and disk logging are disabled by default. If `config.json` is missing, the app recreates these defaults.

## Everyday controls

| Control | Behavior |
| --- | --- |
| **Dry Run Mode** | Preview when checked; permanently delete eligible files when unchecked. |
| **Scan Now** | Show progress, matching files, skip explanations, and a completion summary. |
| **Pause Watching** | Pause scanning and request cancellation of the current scan. |
| **Edit Config / Reload Config** | Edit the saved rules, then apply them. |
| **Save Log…** | Export the currently retained activity history. |
| **Grouchy Mode** | Toggle the app's personality messages. |
| **Close window** | Hide the app in the tray while it continues running. |
| **Tray → Exit** | Stop the app. |

Checkbox changes apply to the current session. They do **not** rewrite `config.json`. Startup and reload honor the saved `dryRun` value, including `false`.

## Configuration

The app always reads `config.json` **beside the executable**, regardless of the working directory. Use ordinary JSON without comments or trailing commas.

Each object in `roots` is an independent folder rule. This example previews temporary files in `%TEMP%`, preserving names beginning with `keep-`:

```json
{
  "dryRun": true,
  "logFile": null,
  "scanIntervalSeconds": 30,
  "roots": [
    {
      "path": "%TEMP%",
      "includeSubdirectories": false,
      "patterns": [
        { "type": "glob", "value": "*.tmp" }
      ],
      "exclude": ["keep-*"],
      "minimumAgeSeconds": 86400,
      "minimumSizeBytes": 0,
      "maximumSizeBytes": null,
      "emptyOnly": false
    }
  ]
}
```

### Filename patterns

Matching is case-insensitive and applies to the **filename**, not the full path. A file must match at least one include pattern; exclusions always win.

| Type | Example value in JSON | Matches |
| --- | --- | --- |
| `glob` | `"*.tmp"` | Any filename ending in `.tmp`. |
| `glob` | `"report-?.txt"` | `report-1.txt`, but not `report-12.txt`. |
| `literal` | `"Thumbs.db"` | That exact filename, ignoring case. |
| `regex` | `"^backup-\\d+\\.bak$"` | Names such as `backup-123.bak`. |

`*` matches any sequence of characters; `?` matches one character. Regex backslashes must be doubled inside JSON strings. Invalid regex syntax is reported when configuration loads.

### Paths and filters

- **Environment variables:** `path` and `logFile` accept values such as `%TEMP%` and `%LOCALAPPDATA%/GrouchyFiler/grouchy.log`. Undefined variables produce a configuration error.
- **Windows paths:** use forward slashes (`C:/Data/Downloads`) or double each backslash in JSON.
- **Age:** measured from the last write. A two-second settling minimum applies even if `minimumAgeSeconds` is `0`.
- **Size:** limits are inclusive and measured in bytes. `maximumSizeBytes: null` means no maximum; `emptyOnly: true` requires a zero-byte file.
- **Subfolders:** scanned only when `includeSubdirectories` is `true`. Symbolic links and junctions are skipped.

See the [complete user guide](GrouchyFiler/README.md) for every setting, defaults, multiple-root examples, and additional pattern examples. The repository's root `config.json` is a sample; it is not your active desktop configuration.

## Scanning and safety

Scans include existing files and reconsider files as they become old enough to qualify. Automatic intervals can be configured from **5 seconds to 24 hours**.

- Dry runs inspect metadata and leave matching files in place.
- Live cleanup rechecks eligibility while holding the same exclusive file handle used for deletion.
- Busy files are skipped and retried later. Inaccessible subfolders are reported while accessible siblings continue to be scanned.
- Pause, mode changes, reload, and shutdown request scan cancellation. A deletion already submitted to Windows may still finish.
- Configuration errors enable dry run and pause cleanup. Correct the file, reload, then uncheck **Pause Watching**.

> [!TIP]
> If a new file is not removed, check its minimum age first. **Scan Now** explains routine skips, including age, exclusions, and size limits. Scheduled scans suppress repeated previews for unchanged files.

## Logs and retention

| Destination | Default retention |
| --- | --- |
| In-app history | Latest **5,000 entries or 1,000,000 characters**, whichever limit is reached first. |
| Visible textbox | Up to the latest **100,000 characters** for responsiveness. |
| Optional disk log | **10 MiB per file**, with **3 rotated backups**. Disabled by default. |

**Save Log…** exports retained in-app history, including entries beyond the visible textbox. It works with disk logging disabled.

To enable disk logging, set `logFile` to a path outside watched folders, such as `%LOCALAPPDATA%/GrouchyFiler/grouchy.log`. `logLevel` filters disk output; `logMaxBytes` and `logBackupCount` control rotation. The bounded background queue may drop pending disk entries on overload or exit, so the log is not a guaranteed audit trail.

## Build and test

Development requires Windows and the **.NET 10 SDK**.

The local `graphics/` folder is excluded from version control. To use a custom icon, place `default.ico` in that folder before building. Builds without it use the standard application icon; no separate icon file is needed at runtime.

### Run from source

```powershell
dotnet run --project GrouchyFiler
```

### Run regression checks

```powershell
dotnet run --project GrouchyFiler.Tests -c Release
```

Live deletion tests use generated files within a unique `test-data` subfolder and enforce that boundary before deletion. They do not use your active desktop config. Remaining fixtures and test logs are preserved.

### Publish a standalone release

```powershell
dotnet publish GrouchyFiler/GrouchyFiler.csproj -c Release -p:PublishProfile=SingleFile
```

Distribute the three files from `artifacts/GrouchyFiler`:

```text
GrouchyFiler.exe
config.json
README.md
```

The executable bundles managed dependencies, native runtime libraries, and icons. Release builds omit debug symbols and map source paths to a neutral location. Native runtime components extract to .NET's temporary bundle cache at startup.

Publishing preserves an existing destination config and supplies the safe default only when it is absent. The shipped guide is maintained in [`GrouchyFiler/README.md`](GrouchyFiler/README.md) and copied during publishing. Releases are unsigned.

## License and disclaimer

Copyright (c) 2026 **bostephens**. Licensed under the [MIT License](LICENSE). Redistribution must retain the copyright and license notice in all copies or substantial portions of the software. Third-party components remain subject to their respective licenses.

**Use at your own risk. This application can permanently delete files.** To the maximum extent permitted by applicable law, the authors and copyright holders disclaim all warranties and liability arising from use of this software, including liability for data loss, accidental deletion, data corruption, software defects, system damage, business interruption, lost profits, or other damages.

You are responsible for reviewing your configuration and maintaining backups. This notice summarizes the MIT License disclaimer; it does not add restrictions to the MIT License or limit rights that cannot legally be excluded. See [LICENSE](LICENSE) for the complete terms.
