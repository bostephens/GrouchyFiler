using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GrouchyFiler.Models;
using GrouchyFiler.Services;

internal static class ReviewChecks
{
    internal static void Run(string sandbox, Action<bool, string> check)
    {
        string folder = Directory.CreateDirectory(Path.Combine(sandbox, "review")).FullName;
        var guard = new GuardedTestCleanup(sandbox);
        string target = Path.Combine(folder, "race.tmp");
        var rule = new RootConfig { Path = folder, Patterns = [new() { Value = "*.tmp" }], MinimumAgeSeconds = 60, MaximumSizeBytes = 4 };
        var config = new AppConfig { DryRun = false, Roots = [rule] };
        string configPath = Path.Combine(sandbox, "review-config.json");
        void Save() => File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        void OldFile() { File.WriteAllText(target, "old"); File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddHours(-1)); }
        Save();
        OldFile();
        var logs = new List<string>();
        using (var service = new WatcherService(logs.Add, _ => { }, path => { guard.ValidateTarget(path); File.WriteAllText(path, "new"); }))
        {
            service.LoadConfig(configPath);
            service.ScanNow();
            check(File.Exists(target) && logs.Any(x => x.Contains("too young")), "metadata is rechecked after a candidate changes before deletion");
        }
        OldFile();
        using (var service = new WatcherService(_ => { }, _ => { }, path => { guard.ValidateTarget(path); File.WriteAllText(path, "now too large"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1)); }))
        {
            service.LoadConfig(configPath); service.ScanNow();
            check(File.Exists(target), "handle metadata enforces size after a candidate changes");
        }
        OldFile();
        bool writerDenied = false, renameDenied = false;
        guard.ValidateTarget(target);
        FileCleanup.Delete(target, rule, beforeDeleteForTesting: () =>
        {
            try { File.WriteAllText(target, "replacement"); } catch (IOException) { writerDenied = true; }
            try { File.Move(target, Path.Combine(folder, "renamed.tmp")); } catch (IOException) { renameDenied = true; }
        });
        check(writerDenied && renameDenied && !File.Exists(target), "validated file remains exclusive through deletion of the same handle");
        OldFile();
        using (var cancellation = new CancellationTokenSource())
        {
            guard.ValidateTarget(target);
            try { FileCleanup.Delete(target, rule, cancellation.Token, cancellation.Cancel); }
            catch (OperationCanceledException) { }
            check(File.Exists(target), "cancellation immediately before disposition preserves the file");
        }
        foreach (string action in new[] { "pause", "dry run", "reload", "dispose" })
        {
            OldFile();
            using var reached = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var service = new WatcherService(_ => { }, _ => { }, path =>
            {
                guard.ValidateTarget(path); reached.Set();
                if (!release.Wait(10000)) throw new TimeoutException("Test did not release blocked scan.");
            });
            service.LoadConfig(configPath);
            var scan = Task.Run(service.ScanNow);
            try
            {
                check(reached.Wait(5000), action + " test reaches in-flight scan");
                var control = Task.Run(() =>
                {
                    if (action == "pause") service.IsPaused = true;
                    else if (action == "dry run") service.DryRun = true;
                    else if (action == "reload") service.LoadConfig(configPath);
                    else service.Dispose();
                });
                check(control.Wait(2000), action + " returns while file processing is blocked");
            }
            finally { release.Set(); scan.GetAwaiter().GetResult(); }
            check(File.Exists(target), action + " cancels deletion after blocked processing resumes");
        }
        OldFile();
        using (var writer = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            guard.ValidateTarget(target);
            bool busy = false;
            try { FileCleanup.Delete(target, rule); } catch (IOException) { busy = true; }
            check(busy && File.Exists(target), "writers allowing delete sharing still prevent cleanup");
        }
        config.DryRun = true;
        rule.Exclude = ["race.tmp"];
        Save(); logs.Clear();
        using (var service = new WatcherService(logs.Add, _ => { }, path => guard.ValidateTarget(path)))
        {
            service.LoadConfig(configPath); service.ScanNow();
            check(logs.Any(x => x.Contains("excluded by")), "manual scan explains preservation exclusions");
            rule.Exclude.Clear(); rule.MinimumSizeBytes = 4; Save(); service.LoadConfig(configPath); service.ScanNow();
            check(logs.Any(x => x.Contains("size outside")), "manual scan explains size limits");
            rule.MinimumSizeBytes = 0; rule.EmptyOnly = true; Save(); service.LoadConfig(configPath); service.ScanNow();
            check(logs.Any(x => x.Contains("file is not empty")), "manual scan explains empty-only filter");
        }
        rule.EmptyOnly = false;
        rule.Patterns = [new() { Type = "regex", Value = "[" }]; Save();
        using (var service = new WatcherService(logs.Add, _ => { }, path => guard.ValidateTarget(path)))
        {
            check(!service.LoadConfig(configPath) && service.IsPaused && service.DryRun && logs.Any(x => x.Contains("Invalid regex")), "invalid regex identifies configuration error and fails safely");
        }
        rule.Patterns = [new() { Value = "*.tmp" }];
        config.LogMaxBytes = 0; Save();
        bool invalid = false;
        try { AppConfig.Read(configPath); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "invalid disk retention settings rejected");
        config.LogMaxBytes = 4096;
        config.LogFile = Path.Combine(sandbox, "reserved-log");
        string backupConfig = config.LogFile + ".1";
        File.WriteAllText(backupConfig, JsonSerializer.Serialize(config, AppConfig.JsonOptions));
        invalid = false;
        try { AppConfig.Read(backupConfig); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "log backups cannot overwrite the active configuration");
        string diskLog = Path.Combine(sandbox, "rotation.log");
        var logger = new FileLogger(diskLog, "info", 4096, 2);
        for (int i = 0; i < 30; i++) logger.Write($"entry-{i} " + new string('x', 1000));
        check(Enumerable.Range(0, 3).All(i => File.Exists(diskLog + (i == 0 ? "" : "." + i)) && new FileInfo(diskLog + (i == 0 ? "" : "." + i)).Length <= 4096) && !File.Exists(diskLog + ".3"), "disk rotation bounds active file and backup count");
        check(File.ReadAllText(diskLog).Contains("entry-29") && !File.ReadAllText(diskLog + ".2").Contains("entry-0 "), "rotation preserves newest messages and replaces oldest backup");
        logger.Write(new string('\u263a', 10000));
        check(new FileInfo(diskLog).Length <= 4096 && !File.ReadAllText(diskLog).Contains('\ufffd'), "oversized disk entries are capped without splitting UTF-8 characters");
        string noBackupLog = Path.Combine(sandbox, "no-backups.log");
        var noBackups = new FileLogger(noBackupLog, "info", 4096, 0);
        for (int i = 0; i < 10; i++) noBackups.Write(new string('a', 2000));
        check(new FileInfo(noBackupLog).Length <= 4096 && !File.Exists(noBackupLog + ".1"), "zero backups caps the current log without archives");

        var blocked = Directory.CreateDirectory(Path.Combine(folder, "blocked"));
        string sibling = Directory.CreateDirectory(Path.Combine(folder, "accessible")).FullName;
        string siblingFile = Path.Combine(sibling, "sibling.tmp");
        File.WriteAllText(siblingFile, "test");
        var originalAcl = blocked.GetAccessControl();
        var acl = blocked.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            blocked.SetAccessControl(acl);
            var errors = new List<string>();
            rule.IncludeSubdirectories = true;
            var files = WatcherService.EnumerateFiles(rule, CancellationToken.None, errors.Add).ToArray();
            check(files.Contains(siblingFile) && errors.Any(x => x.Contains(blocked.FullName)), "inaccessible child is reported while accessible siblings are still scanned");
        }
        finally { blocked.SetAccessControl(originalAcl); }

        string name = @"Local\GrouchyFiler-Test-" + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstance(name))
        {
            check(primary.IsPrimary, "first instance acquires ownership");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(name);
            using var secondary = Process.Start(start)!;
            check(secondary.WaitForExit(5000) && secondary.ExitCode == 0 && primary.TakeActivation(), "second process signals the original instance and exits");
        }
        using (var restarted = new SingleInstance(name)) check(restarted.IsPrimary, "instance ownership is released on exit");
        using var form = new GrouchyFiler.MainForm(configPath, path => guard.ValidateTarget(path));
        check(form.Text.Contains(typeof(GrouchyFiler.MainForm).Assembly.GetName().Version!.ToString(3)), "desktop title displays release version");
    }
}


