using System.IO.Enumeration;
using System.Diagnostics;
using System.Threading.Channels;
using GrouchyFiler.Models;

namespace GrouchyFiler.Services;

public sealed class WatcherService : IDisposable
{
    private readonly Action<string> logCallback;
    private readonly Action<string>? cleanupForTesting;
    private FileLogger? fileLogger;
    private readonly Action<string> updateStatus;
    private readonly object gate = new();
    private readonly Dictionary<string, (long Length, DateTime Modified)> previews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<(FileLogger Logger, string Message, string Level)> diskLogs = Channel.CreateBounded<(FileLogger, string, string)>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private System.Threading.Timer? timer;
    private CancellationTokenSource? activeScan;
    private bool disposed, paused;
    private bool grouchy = true, dryRun = true;
    private int scanning;
    private long settingsVersion, previewVersion = -1;
    public AppConfig Config { get; private set; } = new();
    public bool DryRun
    {
        get { lock (gate) return dryRun; }
        set
        {
            lock (gate) { dryRun = value; Config.DryRun = value; settingsVersion++; activeScan?.Cancel(); }
            Log(value ? "Dry run enabled. Files will only be previewed." : "Live cleanup enabled. Matching files will be permanently deleted.");
            ReportStatus();
        }
    }
    public bool IsPaused
    {
        get { lock (gate) return paused; }
        set { lock (gate) { paused = value; if (value) activeScan?.Cancel(); } ReportStatus(); }
    }
    public bool GrouchyMode { get { lock (gate) return grouchy; } set { lock (gate) grouchy = value; } }

    public WatcherService(Action<string> logCallback, Action<string> statusCallback)
    {
        this.logCallback = logCallback;
        updateStatus = statusCallback;
        _ = Task.Run(async () =>
        {
            await foreach (var entry in diskLogs.Reader.ReadAllAsync())
            {
                try { entry.Logger.Write(entry.Message, entry.Level); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { logCallback($"Cannot write log file: {ex.Message}"); }
            }
        });
    }

    // Test callbacks validate the test-data boundary BEFORE production deletion.
    // Test instances scan explicitly; they never schedule background cleanup.
    internal WatcherService(Action<string> logCallback, Action<string> statusCallback, Action<string> cleanupForTesting)
        : this(logCallback, statusCallback)
    { this.cleanupForTesting = cleanupForTesting ?? throw new ArgumentNullException(nameof(cleanupForTesting)); }

    private void Log(string message, string level = "info")
    {
        logCallback(message);
        var logger = Volatile.Read(ref fileLogger);
        if (logger is not null) diskLogs.Writer.TryWrite((logger, message.Length > 8192 ? message[..8192] : message, level));
    }

    public bool LoadConfig(string path)
    {
        try
        {
            var candidate = AppConfig.Read(path);
            lock (gate)
            {
                if (disposed) return false;
                activeScan?.Cancel();
                Config = candidate;
                settingsVersion++;
                dryRun = candidate.DryRun;
                fileLogger = new FileLogger(candidate.LogFile, candidate.LogLevel, candidate.LogMaxBytes, candidate.LogBackupCount);
                grouchy = candidate.GrouchyMode;
                if (cleanupForTesting is null)
                {
                    timer ??= new System.Threading.Timer(_ => ScanScheduled(), null, Timeout.Infinite, Timeout.Infinite);
                    timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(candidate.ScanIntervalSeconds));
                }
            }
            Log($"Loaded {candidate.Roots.Count} folder rule(s). {(candidate.DryRun ? "Dry run enabled." : "Live cleanup enabled — matching files will be permanently deleted.")}");
            ReportStatus();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            lock (gate) { activeScan?.Cancel(); dryRun = true; Config.DryRun = true; paused = true; }
            ReportStatus();
            Log($"Configuration error; cleanup paused and dry run enabled: {ex.Message}", "error");
            return false;
        }
    }

    private void ReportStatus()
    {
        string status;
        lock (gate)
        {
            if (disposed) return;
            status = $"{(paused ? "Paused" : Config.Roots.Count == 0 ? "No folders configured" : "Watching")} — {(dryRun ? "dry run" : "LIVE — permanent deletion")}";
        }
        updateStatus(status);
    }
    public void ScanNow() => Scan(true);
    internal void ScanScheduled() => Scan(false);

    private void Scan(bool manual)
    {
        if (Interlocked.CompareExchange(ref scanning, 1, 0) != 0)
        { if (manual) Log("Scan not started: another scan is already running."); return; }
        var elapsed = Stopwatch.StartNew();
        var progress = Stopwatch.StartNew();
        int examined = 0, matched = 0, previewed = 0, deleted = 0, errors = 0;
        bool started = false;
        string result = "Scan complete";
        using var cancellation = new CancellationTokenSource();
        try
        {
            AppConfig snapshot;
            bool preview;
            long version;
            string? unavailable;
            lock (gate)
            {
                snapshot = Config;
                preview = dryRun;
                version = settingsVersion;
                unavailable = disposed ? "service is stopped." : paused ? "watching is paused. Uncheck Pause Watching to scan." : snapshot.Roots.Count == 0 ? "no folders configured. Use Edit Config, then Reload Config." : null;
                if (unavailable is null) activeScan = cancellation;
            }
            if (unavailable is not null) { if (manual) Log("Scan not started: " + unavailable); return; }
            started = true;
            // Only the scanning thread owns this cache. Reloads/mode changes cancel this scan.
            if (manual || previewVersion != version || !preview) previews.Clear();
            previewVersion = version;
            Log($"{(manual ? "Manual" : "Scheduled")} scan started: {snapshot.Roots.Count} folder rule(s), {(preview ? "dry run" : "live cleanup")}.");
            updateStatus("Scanning… 0 files checked");
            foreach (var root in snapshot.Roots)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Log($"Scanning folder: {root.Path} (subfolders: {(root.IncludeSubdirectories ? "yes" : "no")}).");
                foreach (var file in EnumerateFiles(root, cancellation.Token, message => { errors++; Log(message, "warning"); }))
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    examined++;
                    var outcome = ProcessFile(root, file, manual, preview, cancellation.Token);
                    if (outcome is FileOutcome.Previewed or FileOutcome.AlreadyPreviewed or FileOutcome.Deleted) matched++;
                    if (outcome == FileOutcome.Previewed) previewed++;
                    if (outcome == FileOutcome.Deleted) deleted++;
                    if (outcome == FileOutcome.Error) errors++;
                    if (progress.ElapsedMilliseconds >= 250)
                    {
                        Log($"Scan progress: {examined} files checked, {matched} matched.");
                        if (!cancellation.IsCancellationRequested) updateStatus($"Scanning… {examined} checked, {matched} matched");
                        progress.Restart();
                    }
                }
            }
        }
        catch (OperationCanceledException) { result = "Scan stopped: mode, pause, configuration or shutdown changed"; }
        finally
        {
            lock (gate) { if (ReferenceEquals(activeScan, cancellation)) activeScan = null; }
            try
            {
                if (started)
                {
                    Log($"{result}: {examined} files checked, {matched} matched, {previewed} previews, {errors} errors{(deleted > 0 ? $", {deleted} files deleted" : "")} ({elapsed.Elapsed.TotalSeconds:F1}s).");
                    ReportStatus();
                }
            }
            finally { Volatile.Write(ref scanning, 0); }
        }
    }


    internal static IEnumerable<string> EnumerateFiles(RootConfig root, CancellationToken cancellation, Action<string> report)
    {
        var pending = new Stack<string>();
        pending.Push(root.Path);
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        while (pending.TryPop(out string? folder))
        {
            cancellation.ThrowIfCancellationRequested();
            IEnumerator<string>? iterator = null;
            try
            {
                if (HasReparseAncestor(folder)) { report($"Skipped linked folder: {folder}"); continue; }
                iterator = Directory.EnumerateFileSystemEntries(folder, "*", options).GetEnumerator();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { report($"Cannot scan {folder}: {ex.Message}"); }
            if (iterator is null) continue;
            using (iterator)
            {
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string entry;
                    bool directory;
                    try
                    {
                        if (!iterator.MoveNext()) break;
                        entry = iterator.Current;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { report($"Cannot scan {folder}: {ex.Message}"); break; }
                    try { directory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { report($"Cannot inspect {entry}: {ex.Message}"); continue; }
                    if (directory) { if (root.IncludeSubdirectories) pending.Push(entry); }
                    else yield return entry;
                }
            }
        }
    }

    public static bool Matches(RootConfig root, FileInfo file, DateTime utcNow) => FilterReason(root, file.FullName, file.Length, file.LastWriteTimeUtc, utcNow) is null;
    internal static string? FilterReason(RootConfig root, string path, long length, DateTime modified, DateTime utcNow)
    {
        if (!PatternMatcher.Matches(root, path)) return "filename does not match";
        if (root.Exclude.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true))) return "excluded by a preservation rule";
        if (utcNow - modified < TimeSpan.FromSeconds(Math.Max(2, root.MinimumAgeSeconds))) return $"too young (minimum age {Math.Max(2, root.MinimumAgeSeconds)} seconds)";
        if (length < root.MinimumSizeBytes || root.MaximumSizeBytes is long max && length > max) return "size outside configured limits";
        if (root.EmptyOnly && length != 0) return "file is not empty";
        return null;
    }
    private enum FileOutcome { Ignored, Previewed, AlreadyPreviewed, Deleted, Error }
    private FileOutcome ProcessFile(RootConfig root, string path, bool manual, bool preview, CancellationToken cancellation)
    {
        try
        {
            if (!PatternMatcher.Matches(root, path)) return FileOutcome.Ignored;
            if (HasReparseAncestor(path)) { Log($"Skipped {path}: linked path", "warning"); return FileOutcome.Ignored; }
            var file = new FileInfo(path);
            if (!file.Exists) return FileOutcome.Ignored;
            string? reason = FilterReason(root, path, file.Length, file.LastWriteTimeUtc, DateTime.UtcNow);
            if (reason is not null) { if (manual) Log($"Skipped {path}: {reason}"); return FileOutcome.Ignored; }
            cancellation.ThrowIfCancellationRequested();
            if (!preview)
            {
                cleanupForTesting?.Invoke(path);
                reason = FileCleanup.Delete(path, root, cancellation);
                if (reason is not null) { Log($"Skipped {path}: {reason}"); return FileOutcome.Ignored; }
                Log($"Deleted: {path}");
                if (GrouchyMode) Log("Another bit of clutter evicted. You're welcome.", "debug");
                return FileOutcome.Deleted;
            }
            var stamp = (file.Length, file.LastWriteTimeUtc);
            if (!manual && previews.TryGetValue(path, out var previous) && previous == stamp) return FileOutcome.AlreadyPreviewed;
            if (previews.Count >= 10000) previews.Clear();
            previews[path] = stamp;
            Log($"[DryRun] Would delete (preview only): {path}");
            if (GrouchyMode) Log("I've got my eye on that clutter. Leaving it right where it is.", "debug");
            return FileOutcome.Previewed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log($"Skipped {path}: {ex.Message}", "warning"); return FileOutcome.Error; }
    }
    private static bool HasReparseAncestor(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; activeScan?.Cancel(); timer?.Dispose(); timer = null; }
        diskLogs.Writer.TryComplete();
    }
}

