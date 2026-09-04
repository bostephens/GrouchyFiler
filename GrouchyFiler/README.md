# Grouchy Filer

Grouchy Filer watches folders and previews or permanently deletes files that match your rules. The Windows x64 executable includes the .NET runtime.

## Getting started

Keep these files together in a writable folder:

- `GrouchyFiler.exe`
- `config.json`
- `README.md` (this guide)

Run the executable. Launching another copy in the same Windows user session opens the existing window; it does not start another cleaner. The window title and tray menu **About** show version 1.0.0. If it starts in the system tray, double-click its icon to open the window. Choose **Edit Config** to change your rules, save the file, then choose **Reload Config**. Configuration always comes from beside the executable, regardless of the working directory.

The supplied configuration watches `%USERPROFILE%/Downloads` and `%TEMP%` in **dry run**. It requires files to be at least one day old and does not scan subfolders. Downloads matches `*.tmp`, excluding `keep-*`; TEMP matches `*.tmp` and `~$*`. File logging is disabled by default. If the configuration is missing, the app creates this default again.

Review dry-run results before unchecking **Dry Run Mode**. Live cleanup permanently deletes matching files; it does not use the Recycle Bin. Existing files are eligible as well as files added later. The status and activity log show which mode is active.

## Window and tray controls

| Control | What it does |
| --- | --- |
| Dry Run Mode | Checked previews matches. Unchecked enables permanent deletion. This changes the current session without rewriting the config. |
| Pause Watching | Suspends manual and scheduled scans. Resume by unchecking it or using the tray menu. |
| Grouchy Mode | Turns personality messages on or off for the current session. |
| Edit Config | Opens the active config in Notepad. |
| Reload Config | Reads saved settings and applies the saved dry-run mode. A valid reload schedules an immediate scan unless paused. |
| Scan Now | Requests a scan immediately. Shows matching previews again, progress, and a summary, including when nothing matches. |
| Save Log… | Opens a Save dialog to export the current retained history as a UTF-8 text or log file. Works even when file logging is disabled. |
| Close window | Hides the window in the tray; the app continues running. |
| Tray menu: About | Shows version and configuration location. |
| Tray menu: Exit | Stops the app. |

Pause, mode changes, reload and Exit cancel the current scan without waiting for its file or disk-log I/O. Reload still reads and validates the saved configuration. A deletion already submitted to Windows may finish; subsequent deletions are cancelled. A slow operating-system call may take time to return, and another scan waits until it does.

Scan Now also explains why matching filenames were skipped: exclusions, minimum age, size limits, or a nonempty file under an empty-only rule. Scheduled scans omit these routine skip messages to reduce noise.

Scheduled scans suppress repeated preview messages for unchanged files. Manual scans show them again. The window refreshes its bounded activity log in batches every 100 ms. Overlapping scans do not run concurrently.

## Log retention and export

The app retains the most recent **5,000 log entries or 1,000,000 characters**, whichever limit is reached first. Older entries are removed automatically from memory. Individual entries longer than 8,192 characters are truncated. This keeps log-history memory bounded even when the app runs for months; it does not accumulate an unlimited queue while the window is hidden or busy.

The textbox shows up to the latest 100,000 characters for responsiveness. **Save Log…** exports the retained history, including messages that have not yet appeared in the textbox. The export is a snapshot taken when you choose the destination, with timestamps and an omission notice if older entries are unavailable. It does not clear the log, include already-discarded history, or restore logs from a previous app session.

Optional `logFile` output is disabled by default. When enabled, the current file rotates at `logMaxBytes` (default 10 MiB), retaining `logBackupCount` backups (default 3): `grouchy.log.1` is newest, followed by `.2` and `.3`. The oldest backup is replaced, giving a default steady-state limit of 40 MiB. A single oversized entry is truncated to fit. Existing oversized logs or backups from an older, larger retention setting may remain until replaced; reducing the backup count does not delete surplus old backups.

Disk writes use a bounded background queue of 1,024 entries, each capped at 8,192 characters. If disk writes cannot keep up, the oldest pending disk entries are dropped. Pending entries may be lost at application exit; this is an activity log, not a guaranteed audit trail. Save Log exports retained in-app history independently of disk logging.

## JSON format

Use ordinary JSON: double-quoted property names and strings, lowercase `true`, `false`, and `null`, and commas between entries. Do not add comments or trailing commas. Copy examples from this guide into the appropriate array; examples here are not automatically active rules.

Use forward slashes in Windows paths, or double each backslash in JSON:

```json
{ "path": "C:/Data/Downloads" }
```

```json
{ "path": "C:\\Data\\Downloads" }
```

Setting names are case-insensitive. Unknown settings are rejected so spelling mistakes are reported. A configuration error enables dry run and pauses cleanup. Correct the file, reload, then uncheck Pause Watching.

## App settings

| Setting | Meaning and default |
| --- | --- |
| `dryRun` | `true` previews; `false` permanently deletes matching files. Defaults to `true` if omitted. Startup and reload honor this saved value, including `false`. The desktop checkbox does not save changes to it. |
| `logFile` | Optional filename or path for an appended activity log. `null`, an empty string, or omitting the setting disables file logging. Default: `null`. Relative paths resolve beside the config; environment variables are supported. The file must be outside watched roots and must not be the config itself. |
| `logLevel` | Minimum level written to `logFile`: `debug`, `info`, `warning`, `error`, or `none`. Default: `info`. It does not filter the window's activity log. |
| `logMaxBytes` | Maximum bytes per disk-log file before rotation. Default: `10485760` (10 MiB). Allowed: `4096` through `1073741824` (1 GiB). |
| `logBackupCount` | Number of rotated backups, from `0` through `10`. Default: `3`. With `0`, the current log is reset when full. Backup filenames are reserved for the logger and must not be the configuration file. |
| `grouchyMode` | `true` adds personality messages; `false` suppresses them. Default: `true`. |
| `scanIntervalSeconds` | Seconds between automatic scans, from `5` through `86400` (24 hours). Default: `30`. Loading config also starts a scan. |
| `roots` | Array of independent folder rules. Every scan visits each root. An empty array means no watched folders. Each root can use different patterns and filters. |

Log levels include messages at that level and above:

| Level | File-log contents |
| --- | --- |
| `debug` | All messages, including personality messages. |
| `info` | Configuration/mode messages, scan progress, previews, deletions, warnings and errors. |
| `warning` | Scan/file-access warnings and errors. |
| `error` | Errors, such as an invalid configuration. |
| `none` | No file-log output. |

To write rotating logs outside the app folder, use:

```json
{ "logFile": "%LOCALAPPDATA%/GrouchyFiler/grouchy.log", "logLevel": "info" }
```

## Folder settings

Every object in `roots` supports these settings:

| Setting | Meaning and default |
| --- | --- |
| `path` | Required existing absolute folder, or an environment-variable path such as `%TEMP%`. Whole drive roots are rejected. |
| `includeSubdirectories` | `false` checks only this folder; `true` also checks its ordinary subfolders. Default: `false`. Symbolic links and junctions are skipped. |
| `patterns` | Required nonempty array of include patterns. A file needs to match at least one. Patterns match filenames, not full paths. |
| `exclude` | Array of filename glob strings to preserve even if an include pattern matches. Default: `[]`. Exclusions always win. |
| `minimumAgeSeconds` | Minimum seconds since the file's last write, inclusive. Allowed: `0` through `315360000`. Default when omitted: `60`; shipped rules use `86400` (one day). A two-second settling minimum applies even if this is `0`. Later scans reconsider files as they age. |
| `minimumSizeBytes` | Inclusive minimum size in bytes. Must be nonnegative. Default: `0`, allowing empty files. |
| `maximumSizeBytes` | Inclusive maximum size in bytes. `null` means unlimited and is the default. A number must be at least `minimumSizeBytes`. For example, `1048576` is 1 MiB. |
| `emptyOnly` | `true` restricts matches to zero-byte files. Default: `false`. Other conditions still apply, so use `minimumSizeBytes: 0` with this option. |

A file must match an include pattern, avoid all exclusions, and satisfy **every** age and size condition. Live cleanup skips busy/inaccessible files and retries them on later scans. Inaccessible folders are reported individually; scanning continues with accessible sibling folders and the other roots. Before live deletion, the app pins ordinary ancestor directories, opens the file exclusively, and rechecks age and size through that handle. It deletes that same file through the handle; busy, linked, or changed files are skipped.

## Environment variables

`path` and `logFile` expand Windows environment variables when configuration loads:

- `%TEMP%` — the current user's temporary folder.
- `%USERPROFILE%/Downloads` — Downloads under the user's profile.
- `%LOCALAPPDATA%/GrouchyFiler/grouchy.log` — a log under local application data.

Names are case-insensitive. Undefined variables cause a configuration error. Restart the app after changing Windows environment variables so it receives the new environment. Variables are not expanded inside pattern values or exclusions.

## Pattern types and examples

Each pattern has a `type` (`glob`, `literal`, or `regex`) and a nonempty `value`. Type names and filename matching are case-insensitive. Any one include pattern can match; exclusions still win.

### Glob

Glob patterns match the whole filename. `*` matches any sequence of characters and `?` matches exactly one character. Other punctuation is literal.

```json
[
  { "type": "glob", "value": "*.tmp" },
  { "type": "glob", "value": "*.bak" },
  { "type": "glob", "value": "~$*" },
  { "type": "glob", "value": "report-?.txt" }
]
```

| Value | Examples |
| --- | --- |
| `*.tmp` | Matches `scratch.tmp` and `SCRATCH.TMP`, but not `scratch.tmp.bak`. |
| `*.bak` | Matches `settings.bak`. |
| `~$*` | Matches names beginning with `~$`, such as `~$report.docx`. The tilde needs no backslash. |
| `report-?.txt` | Matches `report-1.txt`, but not `report-12.txt`. |

Existing string includes such as `"patterns": ["*.tmp"]` also work as glob shorthand.

### Literal

Literal patterns match an exact filename. Wildcards and regex punctuation have no special meaning.

```json
[
  { "type": "literal", "value": "Thumbs.db" },
  { "type": "literal", "value": "debug.log" }
]
```

These match `Thumbs.db` and `debug.log`, including differences in capitalization. They do not match `debug.session.log`.

### Regular expression

Regex patterns use .NET regular expressions. They may match part of a filename; use `^` and `$` to anchor the whole name. Double regex backslashes inside JSON strings.

```json
[
  { "type": "regex", "value": "^debug\\..*\\.log$" },
  { "type": "regex", "value": "^backup-\\d+\\.bak$" }
]
```

The first matches `debug.session.log`. The second matches `backup-123.bak`, but not `backup-old.bak`. In the JSON text, `\\.` represents a regex literal dot and `\\d` represents a digit. Invalid expressions are rejected when the configuration loads, with the folder and expression included in the error. Matches exceeding 100 ms return no match.

### Exclusions

Exclusions are glob strings, not typed pattern objects:

```json
{ "exclude": ["keep-*", "important.tmp"] }
```

This preserves `keep-old.tmp` and `important.tmp` even if the include patterns match them. Use `[]` for no exclusions.

## Example with multiple roots

This complete config previews two independent folders. It does not enable deletion or disk logging.

```json
{
  "dryRun": true,
  "logFile": null,
  "logLevel": "info",
  "logMaxBytes": 10485760,
  "logBackupCount": 3,
  "grouchyMode": true,
  "scanIntervalSeconds": 30,
  "roots": [
    {
      "path": "%USERPROFILE%/Downloads",
      "includeSubdirectories": false,
      "patterns": [{ "type": "glob", "value": "*.tmp" }],
      "exclude": ["keep-*"],
      "minimumAgeSeconds": 86400,
      "minimumSizeBytes": 0,
      "maximumSizeBytes": null,
      "emptyOnly": false
    },
    {
      "path": "%TEMP%",
      "includeSubdirectories": false,
      "patterns": [
        { "type": "glob", "value": "*.tmp" },
        { "type": "glob", "value": "~$*" }
      ],
      "exclude": [],
      "minimumAgeSeconds": 86400,
      "minimumSizeBytes": 0,
      "maximumSizeBytes": null,
      "emptyOnly": false
    }
  ]
}
```

Edit or add objects in `roots` to watch more folders. Use folders that exist on your machine. For ordinary cleanup, avoid folders whose structure is being changed by untrusted programs; linked paths are deliberately skipped.


