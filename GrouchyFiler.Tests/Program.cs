using System.Collections.Concurrent;
using System.Text.Json;
using GrouchyFiler.Models;
using GrouchyFiler.Services;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--instance-probe")
        {
            using var probe = new SingleInstance(args[1]);
            Environment.ExitCode = probe.IsPrimary ? 1 : 0;
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data"));
        var sandbox = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        CheckEnvironmentPaths(sandbox);
        CheckLogRetention(sandbox);
        CheckMultipleRoots(sandbox);
        CheckScanFeedback(sandbox);
        CheckLiveLogDisplay(sandbox);
        CheckDesktopLiveControls(sandbox);
        Run(sandbox);
        CheckCleanup(sandbox);
        ReviewChecks.Run(sandbox, Check);
        Console.WriteLine($"Remaining test fixtures preserved at {sandbox}");
        Console.WriteLine($"{checks} checks passed.");
    }

    private static void CheckLogRetention(string sandbox)
    {
        var history = new RecentLog();
        history.Add("oldest-marker");
        for (int i = 0; i < RecentLog.MaxEntries; i++) history.Add($"entry-{i}");
        var snapshot = history.Read()!;
        int entryCount = snapshot.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Count(line => !line.StartsWith("[Log]"));
        Check(entryCount == RecentLog.MaxEntries && !snapshot.Text.Contains("oldest-marker") && snapshot.Text.Contains("entry-4999"), "in-app history evicts oldest entries at the entry limit");
        Check(history.Read(snapshot.Version) is null, "unchanged history needs no UI refresh");
        for (int i = 0; i < 2000; i++) history.Add($"large-{i}: " + new string('x', 2000));
        snapshot = history.Read()!;
        Check(snapshot.Text.Length <= RecentLog.MaxCharacters && !snapshot.Text.Contains("entry-4999") && snapshot.Text.Contains("large-1999"), "character limit bounds history independently of entry count");
        var display = history.Read(characterLimit: GrouchyFiler.MainForm.DisplayLogCharacters)!;
        Check(display.Text.Length <= GrouchyFiler.MainForm.DisplayLogCharacters && display.Text.Contains("large-1999"), "textbox snapshot stays within its smaller display limit");
        history.Add(new string('z', 2_000_000));
        snapshot = history.Read()!;
        Check(snapshot.Text.Contains("[truncated]") && snapshot.Text.Length <= RecentLog.MaxCharacters, "oversized messages are truncated before retention");
        Parallel.For(0, 20000, i => history.Add($"parallel-{i}"));
        history.Add("concurrent-final-marker");
        snapshot = history.Read()!;
        Check(snapshot.Text.Length <= RecentLog.MaxCharacters && snapshot.Text.Contains("concurrent-final-marker"), "concurrent logging preserves retention limits and newest entries");

        using var form = new GrouchyFiler.MainForm(Path.Combine(sandbox, "export-config.json"));
        // Do not show the form or run its timer: export must include unpainted messages.
        form.Log("export-old-marker");
        for (int i = 0; i < 6000; i++) form.Log($"export-entry-{i}");
        form.Log("latest Unicode message: café — 完了");
        string export = Path.Combine(sandbox, "exported-current-log.txt");
        form.SaveCurrentLog(export);
        string saved = File.ReadAllText(export);
        Check(saved.Contains("latest Unicode message: café — 完了") && saved.Contains("export-entry-5999") && !saved.Contains("export-old-marker"), "export includes unpainted recent messages and respects eviction");
        Check(saved.Length > GrouchyFiler.MainForm.DisplayLogCharacters && saved.Length <= RecentLog.MaxCharacters, "export contains full retained history beyond textbox limit");
        form.SaveCurrentLog(Path.Combine(sandbox, "exported-again.txt"));
        Check(saved == File.ReadAllText(Path.Combine(sandbox, "exported-again.txt")), "saving a log does not clear or mutate retained history");
        Check(form.Controls.OfType<Button>().Any(b => b.Name == "btnSaveLog" && b.Enabled), "desktop exposes Save Log button");
    }

    private static void CheckMultipleRoots(string sandbox)
    {
        string first = Directory.CreateDirectory(Path.Combine(sandbox, "multi-first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(sandbox, "multi-second")).FullName;
        string Make(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, "test fixture");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
            return path;
        }
        string a = Make(first, "old.tmp"), b = Make(second, "debug.log");
        string keep = Make(first, "keep-old.tmp"), wrong = Make(second, "old.tmp");
        var config = new AppConfig
        {
            Roots = [
                new() { Path = first, Patterns = [new() { Value = "*.tmp" }], Exclude = ["keep-*"] },
                new() { Path = second, Patterns = [new() { Type = "literal", Value = "debug.log" }] }
            ]
        };
        string path = Path.Combine(sandbox, "multiple-roots.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        var messages = new List<string>();
        using var service = new WatcherService(messages.Add, _ => { }, path => new GuardedTestCleanup(sandbox).ValidateTarget(path));
        Check(service.LoadConfig(path) && service.Config.Roots.Count == 2, "multiple root rules load together");
        service.ScanNow();
        Check(messages.Any(m => m.Contains("[DryRun]") && m.Contains(a)) && messages.Any(m => m.Contains("[DryRun]") && m.Contains(b)), "one scan previews matches in both roots");
        Check(!messages.Any(m => m.Contains("[DryRun]") && (m.Contains(keep) || m.Contains(wrong))), "each root applies its own patterns and exclusions");
        service.DryRun = false;
        service.ScanNow();
        Check(!File.Exists(a) && !File.Exists(b) && File.Exists(keep) && File.Exists(wrong), "guarded live cleanup processes both roots independently");
        b = Make(second, "debug.log");
        service.Config.Roots[0].Path = Path.Combine(first, "unavailable");
        service.ScanNow();
        Check(!File.Exists(b) && messages.Any(m => m.Contains("Cannot scan")), "unavailable first root does not prevent scanning the second");
    }

    private static void CheckScanFeedback(string sandbox)
    {
        var messages = new ConcurrentQueue<string>();
        var statuses = new ConcurrentQueue<string>();
        using var enteredFolder = new ManualResetEventSlim();
        using var releaseFolder = new ManualResetEventSlim();
        bool block = false;
        using var service = new WatcherService(message =>
        {
            messages.Enqueue(message);
            if (block && message.StartsWith("Scanning folder:"))
            {
                enteredFolder.Set();
                if (!releaseFolder.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Scan test was not released.");
            }
        }, statuses.Enqueue, _ => throw new Exception("Feedback tests must never delete files."));
        service.ScanNow();
        Check(messages.Any(m => m.Contains("no folders configured")), "manual scan explains missing configuration");
        service.IsPaused = true;
        service.ScanNow();
        Check(messages.Any(m => m.Contains("watching is paused")), "manual scan explains pause state");
        service.IsPaused = false;
        string watched = Directory.CreateDirectory(Path.Combine(sandbox, "scan-feedback")).FullName;
        string matching = Path.Combine(watched, "old.tmp");
        File.WriteAllText(matching, "test");
        File.SetLastWriteTimeUtc(matching, DateTime.UtcNow.AddHours(-2));
        File.WriteAllText(Path.Combine(watched, "notes.txt"), "keep");
        string configPath = Path.Combine(sandbox, "scan-feedback.json");
        var config = new AppConfig { Roots = [new() { Path = watched, Patterns = [new() { Value = "*.tmp" }] }] };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        Check(service.LoadConfig(configPath), "scan feedback configuration loads");
        messages.Clear();
        service.ScanNow();
        service.ScanNow();
        Check(messages.Count(m => m.Contains("[DryRun]") && m.Contains(matching)) == 2, "repeated manual scans replay matching previews");
        Check(messages.Count(m => m.StartsWith("Scan complete: 2 files checked, 1 matched, 1 previews, 0 errors")) == 2, "manual summaries include all checked files and matches");
        service.ScanScheduled();
        Check(messages.Count(m => m.Contains("[DryRun]") && m.Contains(matching)) == 2 && messages.Any(m => m.StartsWith("Scan complete: 2 files checked, 1 matched, 0 previews")), "scheduled scan deduplicates previews but still reports matches");
        Check(statuses.Any(s => s.StartsWith("Scanning")) && statuses.Last().Contains("Watching"), "scan status transitions back to watching");

        block = true;
        var scan = Task.Run(service.ScanNow);
        try
        {
            Check(enteredFolder.Wait(TimeSpan.FromSeconds(5)), "folder progress is reported before scan finishes");
            service.ScanNow();
            Check(!scan.IsCompleted && messages.Any(m => m.Contains("another scan is already running")), "overlapping manual scan reports the active scan");
        }
        finally { releaseFolder.Set(); scan.GetAwaiter().GetResult(); block = false; }

        service.Config.Roots[0].Patterns = [new() { Value = "*.unmatched" }];
        service.ScanNow();
        Check(messages.Last().StartsWith("Scan complete: 2 files checked, 0 matched, 0 previews"), "no-match scan visibly completes");
        service.Config.Roots[0].Path = Path.Combine(watched, "missing");
        service.ScanNow();
        Check(messages.Last().Contains("1 errors") && messages.Any(m => m.StartsWith("Cannot scan")), "unavailable folder appears in scan error summary");
    }

    private static void CheckLiveLogDisplay(string sandbox)
    {
        string watched = Directory.CreateDirectory(Path.Combine(sandbox, "ui-scan")).FullName;
        File.WriteAllText(Path.Combine(watched, "notes.txt"), "test");
        string configPath = Path.Combine(sandbox, "ui-config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new AppConfig
        {
            ScanIntervalSeconds = 86400,
            Roots = [new() { Path = watched, Patterns = [new() { Value = "*.tmp" }] }]
        }, AppConfig.JsonOptions));
        // Exercise the real WinForms message loop using only this run's config and fixtures.
        using var form = new GrouchyFiler.MainForm(configPath) { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        var log = form.Controls.OfType<TextBox>().Single();
        bool PumpUntil(Func<bool> condition) => SpinWait.SpinUntil(() => { Application.DoEvents(); return condition(); }, 5000);
        Check(PumpUntil(() => log.Text.Contains("Scan complete:")), "textbox receives background scan completion through UI timer");
        Task.Run(() => form.Log("worker-thread log probe")).GetAwaiter().GetResult();
        Check(PumpUntil(() => log.Text.Contains("worker-thread log probe")), "worker-thread log appears without waiting for another user action");
        form.Show(); // Reopen after startup's asynchronous OnShown has hidden it to the tray.
        Application.DoEvents();
        log.Clear();
        var scan = form.Controls.OfType<Button>().Single(b => b.Name == "btnScanNow");
        scan.PerformClick();
        Check(PumpUntil(() => scan.Enabled && log.Text.Contains("Manual scan started") && log.Text.Contains("Scan complete: 1 files checked, 0 matched")), "actual Scan Now button displays a no-match scan result");
        Check(log.Text.Contains("Scan Now requested.") && scan.Text == "Scan Now", "manual scan gives immediate feedback and restores button");
        using var windowPreview = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(windowPreview, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        windowPreview.Save(Path.Combine(AppContext.BaseDirectory, "live-window-preview.png"));
    }

    private static void CheckDesktopLiveControls(string sandbox)
    {
        string configPath = Path.Combine(sandbox, "first-run", "config.json");
        var guard = new GuardedTestCleanup(sandbox);
        using var form = new GrouchyFiler.MainForm(configPath, path => guard.ValidateTarget(path)) { Opacity = 0, ShowInTaskbar = false };
        form.Show();
        var log = form.Controls.OfType<TextBox>().Single();
        bool PumpUntil(Func<bool> condition) => SpinWait.SpinUntil(() => { Application.DoEvents(); return condition(); }, 5000);
        Check(PumpUntil(() => File.Exists(configPath) && log.Text.Contains("Configuration:")), "first-run window creates its isolated configuration");
        // Inspect the defaults without scanning the real folders; the test instance has no timer.
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), AppConfig.JsonOptions)!;
        using var strictConfig = JsonDocument.Parse(File.ReadAllText(configPath));
        Check(strictConfig.RootElement.GetProperty("dryRun").GetBoolean(), "first-run template is strict JSON without comments or trailing commas");
        var dryRun = form.Controls.OfType<CheckBox>().Single(c => c.Name == "chkDryRun");
        Check(config.DryRun && config.Roots.Count == 2 && dryRun.Checked, "newly created config has two roots and defaults to dryRun true");
        Check(config.Roots[0].Path == "%USERPROFILE%/Downloads" && config.Roots[1].Path == "%TEMP%", "default roots include Downloads and TEMP");
        Check(config.LogFile is null && !File.Exists(Path.Combine(Path.GetDirectoryName(configPath)!, "grouchy.log")), "first-run config disables extra log files");
        string watched = Directory.CreateDirectory(Path.Combine(sandbox, "desktop-live-files")).FullName;
        string Make(string name)
        {
            string path = Path.Combine(watched, name);
            File.WriteAllText(path, "generated fixture");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            return path;
        }
        string target = Make("delete-me.tmp");
        config.Roots = [new() { Path = watched, Patterns = [new() { Value = "*.tmp" }] }];
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        var reload = form.Controls.OfType<Button>().Single(b => b.Name == "btnReloadConfig");
        var scan = form.Controls.OfType<Button>().Single(b => b.Name == "btnScanNow");
        reload.PerformClick();
        form.Controls.OfType<CheckBox>().Single(c => c.Name == "chkPause").Checked = false;
        dryRun.Checked = false;
        Check(AppConfig.Read(configPath).DryRun, "desktop toggle does not overwrite saved config");
        scan.PerformClick();
        Check(PumpUntil(() => scan.Enabled && !File.Exists(target) && log.Text.Contains("Deleted:")), "desktop checkbox and Scan Now perform guarded real deletion");
        Check(form.Controls.OfType<Label>().Any(l => l.Text.Contains("LIVE")), "desktop status clearly identifies live cleanup");
        target = Make("preserve-me.tmp");
        dryRun.Checked = true;
        log.Clear();
        scan.PerformClick();
        Check(PumpUntil(() => scan.Enabled && log.Text.Contains("[DryRun]")) && File.Exists(target), "desktop checkbox immediately restores preview-only scanning");
        config.DryRun = false;
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        reload.PerformClick();
        Check(!dryRun.Checked, "desktop reload honors dryRun false from config");
        File.WriteAllText(configPath, "{ invalid");
        reload.PerformClick();
        Check(dryRun.Checked && form.Controls.OfType<CheckBox>().Single(c => c.Name == "chkPause").Checked && File.Exists(target), "invalid desktop reload turns dry run on and pauses cleanup");
    }

    private static void CheckCleanup(string sandbox)
    {
        var cleanup = new GuardedTestCleanup(sandbox);
        bool Denied(Action action)
        {
            try { action(); return false; }
            catch (UnauthorizedAccessException) { return true; }
        }
        Check(Denied(() => new GuardedTestCleanup(AppContext.BaseDirectory)), "cleanup boundary cannot be above test-data");
        Check(Denied(() => new GuardedTestCleanup(Path.GetDirectoryName(sandbox)!)), "cleanup boundary cannot be test-data itself");
        Check(Denied(() => cleanup.ValidateTarget(Path.Combine(AppContext.BaseDirectory, "config.sample.json"))), "paths above test-data rejected before deletion");
        Check(Denied(() => cleanup.Delete(sandbox)), "test run directory itself cannot be deleted");
        Check(Denied(() => cleanup.Delete("relative.tmp")), "relative deletion targets rejected");
        var neighbor = Directory.CreateDirectory(sandbox + "-neighbor").FullName;
        var sentinel = Path.Combine(neighbor, "sentinel.tmp");
        File.WriteAllText(sentinel, "preserve this sibling fixture");
        File.SetLastWriteTimeUtc(sentinel, DateTime.UtcNow.AddHours(-2));
        Check(Denied(() => cleanup.Delete(sentinel)) && File.Exists(sentinel), "similarly named sibling folder is outside cleanup boundary");
        Check(Denied(() => cleanup.Delete(Path.Combine(sandbox, "..", Path.GetFileName(neighbor), "sentinel.tmp"))), "parent traversal cannot escape cleanup boundary");

        string watched = Directory.CreateDirectory(Path.Combine(sandbox, "cleanup-files")).FullName;
        string Make(string name, string contents = "test")
        {
            string path = Path.GetFullPath(Path.Combine(watched, name));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            return path;
        }
        string glob = Make("old.tmp");
        string literal = Make("Thumbs.db");
        string regex = Make("debug.session.log");
        string excluded = Make("keep-old.tmp");
        string other = Make("notes.txt");
        string nested = Make("nested/old.tmp");
        string fresh = Make("fresh.tmp");
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
        Check(Denied(() => cleanup.Delete(Path.GetDirectoryName(nested)!)), "directories inside test run cannot be deleted");
        var rule = new RootConfig
        {
            Path = watched, MinimumAgeSeconds = 3600, Exclude = ["keep-*"],
            Patterns = [new() { Value = "*.tmp" }, new() { Type = "literal", Value = "Thumbs.db" }, new() { Type = "regex", Value = @"^debug\..*\.log$" }]
        };
        string configPath = Path.Combine(sandbox, "cleanup-config.json");
        var config = new AppConfig { DryRun = true, Roots = [rule] };
        void Save() => File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        Save();
        var messages = new List<string>();
        using var service = new WatcherService(messages.Add, _ => { }, path => cleanup.ValidateTarget(path));
        Check(service.LoadConfig(configPath) && service.DryRun, "cleanup instance honors true dry-run config");
        service.ScanNow();
        Check(File.Exists(glob) && messages.Any(m => m.Contains("[DryRun]")), "cleanup test dry run previews and preserves files");
        service.DryRun = false;
        Check(!service.DryRun, "test harness can explicitly enable cleanup");
        service.IsPaused = true;
        service.ScanNow();
        Check(File.Exists(glob) && File.Exists(literal), "pause prevents actual test deletion");
        service.IsPaused = false;
        using (var held = new FileStream(glob, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service.ScanNow();
            Check(File.Exists(glob), "cleanup skips locked fixture");
        }
        Check(!File.Exists(literal) && !File.Exists(regex), "literal and regex fixtures are actually deleted");
        service.ScanNow();
        Check(!File.Exists(glob), "glob fixture deleted after lock released");
        Check(File.Exists(excluded) && File.Exists(other) && File.Exists(nested) && File.Exists(fresh), "live cleanup preserves excluded, nonmatching, nested and fresh files");
        string toggled = Make("toggled.tmp");
        service.DryRun = true;
        service.ScanNow();
        Check(File.Exists(toggled), "switching back to dry run stops deletion");
        rule.IncludeSubdirectories = true; Save();
        service.DryRun = false;
        Check(service.LoadConfig(configPath) && service.DryRun, "successful reload resets test cleanup to dry run");
        service.DryRun = false;
        service.ScanNow();
        Check(!File.Exists(nested) && !File.Exists(toggled), "recursive cleanup deletes eligible nested fixtures");
        File.WriteAllText(configPath, "{ broken");
        Check(!service.LoadConfig(configPath) && service.DryRun && service.IsPaused, "invalid reload disables test cleanup and pauses");
        rule.Path = neighbor; config.DryRun = false; Save();
        Check(service.LoadConfig(configPath) && !service.DryRun, "false config enables live mode with test boundary guard");
        service.DryRun = false;
        service.IsPaused = false;
        service.ScanNow();
        Check(File.ReadAllText(sentinel) == "preserve this sibling fixture" && messages.Any(m => m.Contains("Cleanup cannot leave")), "service cannot delete outside its allowed test run even with incorrect rules");
        using var desktop = new WatcherService(_ => { }, _ => { });
        desktop.DryRun = false;
        Check(!desktop.DryRun, "desktop service supports the live-mode toggle");
        desktop.DryRun = true;
        Check(desktop.DryRun, "desktop service can return to dry run");
    }

    private static void CheckEnvironmentPaths(string sandbox)
    {
        string variable = "GROUCHY_TEST_" + Guid.NewGuid().ToString("N");
        string configPath = Path.Combine(sandbox, "environment-config.json");
        string folder = Directory.CreateDirectory(Path.Combine(sandbox, "env-watched")).FullName;
        Environment.SetEnvironmentVariable(variable, sandbox);
        try
        {
            var config = new AppConfig
            {
                Roots = [new() { Path = $"%{variable.ToLowerInvariant()}%/env-watched", Patterns = [new() { Value = "%TEMP%*.tmp" }] }],
                LogFile = $"%{variable}%/env.log"
            };
            void Save() => File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
            Save();
            var loaded = AppConfig.Read(configPath);
            Check(loaded.Roots[0].Path == folder, "environment folder expands case-insensitively with a suffix");
            Check(loaded.LogFile == Path.Combine(sandbox, "env.log"), "environment log path expands");
            Check(loaded.Roots[0].Patterns[0].Value == "%TEMP%*.tmp", "environment expansion leaves patterns untouched");
            config.Roots[0].Path = $"%{variable}%";
            config.LogFile = null;
            Save();
            Check(AppConfig.Read(configPath).Roots[0].Path == sandbox, "standalone environment variable resolves as folder");
            config.Roots[0].Path = $"%{variable}%/env-watched";
            config.LogFile = $"%{variable}%/env-watched/bad.log";
            Save();
            bool Rejected()
            {
                try { AppConfig.Read(configPath); return false; }
                catch (InvalidDataException) { return true; }
            }
            Check(Rejected(), "expanded log path still cannot target a watched folder");
            config.Roots[0].Path = folder;
            config.LogFile = $"%{variable}_UNDEFINED%/test.log";
            Save();
            Check(Rejected(), "undefined log environment variable rejected");
            config.LogFile = null;
            config.Roots[0].Path = $"%{variable}_UNDEFINED%";
            Save();
            Check(Rejected(), "undefined folder environment variable rejected");
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    private static void Run(string sandbox)
    {
        bool Match(string type, string value, string name) => PatternMatcher.Matches(
            new RootConfig { Patterns = [new() { Type = type, Value = value }] }, Path.Combine(sandbox, name));
        Check(Match("glob", "report-?.[txt]", "REPORT-1.[TXT]"), "glob supports question mark and escapes regex punctuation");
        Check(!Match("glob", "*.tmp", "a.tmp.bak"), "glob matches the whole filename");
        Check(!Match("glob", "file?.txt", "file12.txt"), "glob question mark matches exactly one character");
        Check(Match("literal", "File[1].TMP", "file[1].tmp"), "literal matching is case-insensitive");
        Check(!Match("literal", "*.tmp", "file.tmp"), "literal wildcard characters are not expanded");
        Check(Match("regex", @"^report-\d+\.txt$", "REPORT-123.TXT"), "regex supports escapes and anchors");
        Check(Match("REGEX", "port", "report.txt"), "regex permits substring matches and case-insensitive types");
        Check(!Match("regex", "[", "report.txt"), "invalid regex returns false");
        Check(!Match("regex", "^(a+)+$", new string('a', 10000) + "!"), "pathological regex times out safely");
        Check(!Match("unknown", "*", "report.txt"), "unknown pattern type does not match");
        Check(!PatternMatcher.Matches(new RootConfig(), Path.Combine(sandbox, "report.txt")), "empty patterns do not match");
        Check(PatternMatcher.Matches(new RootConfig { Patterns = [new() { Type = "regex", Value = "[" }, new() { Type = "literal", Value = "report.txt" }] }, Path.Combine(sandbox, "report.txt")), "invalid pattern does not prevent a later match");
        var legacy = JsonSerializer.Deserialize<RootConfig>("{\"Patterns\":[\"*.tmp\"]}", AppConfig.JsonOptions)!;
        Check(PatternMatcher.Matches(legacy, Path.Combine(sandbox, "old.tmp")), "legacy JSON string patterns remain supported");
        var rootPath = Directory.CreateDirectory(Path.Combine(sandbox, "watched")).FullName;
        var sample = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config.sample.json")), AppConfig.JsonOptions)!;
        Check(sample.Roots[0].Path == @"C:\Users\Example\Downloads", "sample JSON decodes Windows path correctly");
        Check(PatternMatcher.Matches(sample.Roots[0], Path.Combine(rootPath, "~$document.docx")), "sample temporary-document glob matches");
        Check(PatternMatcher.Matches(sample.Roots[0], Path.Combine(rootPath, "debug.session.log")), "sample debug regex matches");
        Check(!PatternMatcher.Matches(sample.Roots[0], Path.Combine(rootPath, "debugXsession.log")), "sample regex requires a literal dot");
        string Make(string name, string contents = "test")
        {
            var path = Path.GetFullPath(Path.Combine(rootPath, name));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            return path;
        }
        var eligible = Make("old.TMP");
        var excluded = Make("keep-old.tmp");
        var other = Make("notes.txt");
        var nested = Make("child/nested.tmp");
        var fresh = Make("fresh.tmp");
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
        var rule = new RootConfig { Path = rootPath, Patterns = [new() { Type = "glob", Value = "*.tmp" }], Exclude = ["keep-*"], MinimumAgeSeconds = 3600 };
        var config = new AppConfig { DryRun = true, LogFile = "grouchy.log", LogLevel = "info", Roots = [rule], ScanIntervalSeconds = 86400 };
        var configPath = Path.Combine(sandbox, "config.json");
        void Save() => File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        Save();
        var messages = new ConcurrentQueue<string>();
        using var service = new WatcherService(messages.Enqueue, _ => { });
        Check(service.LoadConfig(configPath), "valid configuration loads");
        Check(service.DryRun && service.Config.DryRun, "true configuration enables dry run");
        Check(typeof(WatcherService).GetProperty(nameof(WatcherService.DryRun))!.CanWrite, "service exposes a dry-run toggle");
        Check(SpinWait.SpinUntil(() => messages.Any(m => m.Contains(eligible)) && messages.Any(m => m.StartsWith("Scan complete:")), 5000), "existing matching file is previewed");
        Check(File.Exists(eligible), "dry run preserves matching files");
        service.ScanScheduled();
        Check(SpinWait.SpinUntil(() => File.Exists(Path.Combine(sandbox, "grouchy.log")) && File.ReadAllText(Path.Combine(sandbox, "grouchy.log")).Contains("[DryRun]"), 5000), "relative log file records previews beside configuration");
        Check(messages.Count(m => m.Contains(eligible)) == 1, "dry-run duplicate events suppressed");
        Check(!messages.Any(m => m.Contains(excluded) || m.Contains(other) || m.Contains(nested) || m.Contains(fresh)), "exclusion, extension, recursion and age filters respected");
        service.IsPaused = true;
        service.ScanNow();
        var pausedFile = Make("paused.tmp");
        service.ScanNow();
        Check(!messages.Any(m => m.Contains(pausedFile)), "pause prevents previews");
        service.IsPaused = false;
        using (var held = new FileStream(eligible, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            service.ScanNow();
            Check(File.Exists(eligible), "locked file is preserved");
        }
        service.ScanNow();
        Check(File.Exists(eligible) && File.ReadAllText(eligible) == "test", "repeated scans preserve matching file and contents");
        Check(File.Exists(excluded) && File.Exists(other) && File.Exists(nested) && File.Exists(fresh), "all nonmatching files remain after scans");
        rule.IncludeSubdirectories = true;
        Save();
        Check(service.LoadConfig(configPath) && service.DryRun, "reload applies configured dry run");
        Check(SpinWait.SpinUntil(() => { service.ScanNow(); return messages.Any(m => m.Contains(nested)); }, 5000), "recursive rule previews nested files");
        File.WriteAllText(configPath, "{ invalid json");
        Check(!service.LoadConfig(configPath) && service.DryRun && service.IsPaused, "invalid reload pauses previews and remains dry run");
        Save();
        Check(service.LoadConfig(configPath), "configuration recovers after correction");
        File.WriteAllText(configPath, "{\"Roots\":[],\"Typo\":true}");
        Check(!service.LoadConfig(configPath), "unknown configuration fields are rejected");
        rule.Patterns.Clear(); Save();
        Check(!service.LoadConfig(configPath), "empty include patterns are rejected");
        rule.Patterns = [new() { Value = "*" }];
        rule.EmptyOnly = true;
        var empty = Make("empty.tmp", "");
        Check(WatcherService.Matches(rule, new FileInfo(empty), DateTime.UtcNow), "empty-only rule accepts zero bytes");
        Check(!WatcherService.Matches(rule, new FileInfo(other), DateTime.UtcNow), "empty-only rule rejects nonempty files");
        rule.EmptyOnly = false; rule.MinimumSizeBytes = 5;
        Check(!WatcherService.Matches(rule, new FileInfo(other), DateTime.UtcNow), "minimum size enforced");
        rule.MinimumSizeBytes = 0; rule.MaximumSizeBytes = 3;
        Check(!WatcherService.Matches(rule, new FileInfo(other), DateTime.UtcNow), "maximum size enforced");
        var filteredLog = Path.Combine(sandbox, "filtered.log");
        var logger = new FileLogger(filteredLog, "warning");
        logger.Write("hidden info");
        logger.Write("visible warning", "warning");
        Check(!File.ReadAllText(filteredLog).Contains("hidden info") && File.ReadAllText(filteredLog).Contains("visible warning"), "log threshold filters file output");
        config.LogLevel = "invalid"; Save();
        Check(!service.LoadConfig(configPath), "invalid log level rejected");
        config.LogLevel = "info"; config.LogFile = Path.Combine(rootPath, "bad.log"); Save();
        Check(!service.LoadConfig(configPath) && !File.Exists(config.LogFile), "log inside watched folder rejected without writing");
        using var form = new GrouchyFiler.MainForm();
        Check(form.Controls.Count > 0, "main window and resources construct successfully");
        var dryRunControl = form.Controls.OfType<CheckBox>().Single(c => c.Name == "chkDryRun");
        Check(dryRunControl.Checked && dryRunControl.Enabled, "UI defaults dry run on and allows toggling");
        form.CreateControl();
        using var preview = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(preview, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        preview.Save(Path.Combine(AppContext.BaseDirectory, "window-preview.png"));
    }
}


