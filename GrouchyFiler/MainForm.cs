using System.Diagnostics;
using System.Text;
using GrouchyFiler.Services;

namespace GrouchyFiler;

public partial class MainForm : Form
{
    private readonly NotifyIcon trayIcon;
    private readonly Icon applicationIcon;
    private readonly WatcherService watcherService;
    private readonly string configPath;
    private bool exiting, syncing;
    private bool firstRun;
    private readonly RecentLog recentLog = new();
    private long displayedLogVersion = -1;
    internal const int DisplayLogCharacters = 100000;

    public MainForm() : this(Path.Combine(AppContext.BaseDirectory, "config.json")) { }

    internal MainForm(string configPath, Action<string>? cleanupForTesting = null)
    {
        this.configPath = configPath;
        InitializeComponent();
        string version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        Text = $"Grouchy Filer {version}";
        // Runtime-added controls must use the same scaling as the designer controls.
        float scaleX = ClientSize.Width / 778f, scaleY = ClientSize.Height / 444f;
        Point UiPoint(int x, int y) => new((int)Math.Round(x * scaleX), (int)Math.Round(y * scaleY));
        Size UiSize(int width, int height) => new((int)Math.Round(width * scaleX), (int)Math.Round(height * scaleY));
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("GrouchyFiler.AppIcon.ico")
            ?? throw new InvalidOperationException("Application icon resource is missing."))
        using (var resourceIcon = new Icon(iconStream))
        {
            applicationIcon = (Icon)resourceIcon.Clone();
        }
        Icon = applicationIcon;
        ShowIcon = true;
        textBox1.ReadOnly = true;
        components ??= new System.ComponentModel.Container();
        var logTimer = new System.Windows.Forms.Timer(components) { Interval = 100 };
        logTimer.Tick += (_, _) => FlushLog();
        logTimer.Start();
        trayIcon = new NotifyIcon(components)
        {
            Icon = applicationIcon,
            Text = "Grouchy Filer", Visible = true
        };
        var menu = new ContextMenuStrip(components);
        menu.Items.Add("Open Grouchy Filer", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Pause / Resume", null, (_, _) => chkPause.Checked = !chkPause.Checked);
        menu.Items.Add("Edit configuration", null, (_, _) => EditConfiguration());
        menu.Items.Add("About", null, (_, _) => MessageBox.Show(this, $"Grouchy Filer {version}\nWindows x64 · Self-contained desktop app\nConfiguration: {this.configPath}", "About Grouchy Filer", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("Exit", null, (_, _) => { exiting = true; Close(); });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        Action<string> statusCallback = status => OnUi(() => lblStatus.Text = "Status: " + status);
        watcherService = cleanupForTesting is null
            ? new WatcherService(Log, statusCallback)
            : new WatcherService(Log, statusCallback, cleanupForTesting);
        chkDryRun.Checked = true;
        chkDryRun.Enabled = true;
        chkDryRun.Text = "Dry Run Mode";
        chkDryRun.CheckedChanged += (_, _) => { if (!syncing) watcherService.DryRun = chkDryRun.Checked; };
        chkGrouchy.Checked = true;
        chkPause.CheckedChanged += (_, _) => { if (!syncing) watcherService.IsPaused = chkPause.Checked; };
        chkGrouchy.CheckedChanged += (_, _) => { if (!syncing) watcherService.GrouchyMode = chkGrouchy.Checked; };
        var edit = new Button { Text = "Edit Config", Location = UiPoint(162, 62), Size = UiSize(150, 34) };
        edit.Click += (_, _) => EditConfiguration();
        Controls.Add(edit);
        var scan = new Button { Name = "btnScanNow", Text = "Scan Now", Location = UiPoint(322, 62), Size = UiSize(150, 34) };
        scan.Click += async (_, _) =>
        {
            scan.Enabled = false;
            scan.Text = "Scanning…";
            Log("Scan Now requested.");
            try { await Task.Run(watcherService.ScanNow); }
            catch (Exception ex) { Log($"Scan failed: {ex.Message}"); }
            finally
            {
                if (!IsDisposed)
                {
                    FlushLog();
                    scan.Text = "Scan Now";
                    scan.Enabled = true;
                }
            }
        };
        Controls.Add(scan);
        var saveLog = new Button { Name = "btnSaveLog", Text = "Save Log…", Location = UiPoint(482, 62), Size = UiSize(150, 34) };
        saveLog.Click += (_, _) => SaveLog();
        Controls.Add(saveLog);
        Controls.Add(new Label { Text = "Recent log: up to 5,000 entries / 1M characters.", AutoSize = true, Location = UiPoint(162, 110) });
        Controls.Add(new Label { Text = "Match files by folder, filename, age and size.\nDry run off: matching files are permanently deleted.", AutoSize = true, Location = UiPoint(162, 12) });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            firstRun = !File.Exists(configPath);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (firstRun)
            {
                using var template = typeof(MainForm).Assembly.GetManifestResourceStream("GrouchyFiler.DefaultConfig.json")
                    ?? throw new InvalidOperationException("Default configuration resource is missing.");
                using var reader = new StreamReader(template);
                File.WriteAllText(configPath, reader.ReadToEnd());
            }
            var loaded = ReloadConfiguration();
            Log($"Configuration: {configPath}");
            if (firstRun) Log("Default rules cover Downloads and TEMP. Review them using Edit Config, then Reload Config. Dry run is enabled for the first run.");
            else if (loaded && watcherService.Config.Roots.Count > 0) HideToTray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log($"Cannot initialize configuration: {ex.Message}"); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); }
        else { watcherService.Dispose(); trayIcon.Visible = false; }
        base.OnFormClosing(e);
    }

    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    internal void ShowMainWindow() { Show(); WindowState = FormWindowState.Normal; ShowInTaskbar = true; Activate(); }
    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke((MethodInvoker)(() => { if (!IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { }
        }
        else action();
    }
    public void Log(string message) => recentLog.Add(message);

    private void FlushLog()
    {
        var snapshot = recentLog.Read(displayedLogVersion, DisplayLogCharacters);
        if (snapshot is null) return;
        displayedLogVersion = snapshot.Version;
        textBox1.Text = snapshot.Text;
        textBox1.SelectionStart = textBox1.TextLength;
        textBox1.ScrollToCaret();
    }

    internal void SaveCurrentLog(string path)
    {
        // Capture directly from history, including entries not yet painted by the UI timer.
        string snapshot = recentLog.Read()!.Text;
        File.WriteAllText(path, snapshot, new UTF8Encoding(false));
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save current log",
            FileName = $"GrouchyFiler-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log",
            DefaultExt = "txt", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveCurrentLog(dialog.FileName);
            Log($"Saved current log to: {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not save log: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Could not save log", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void EditConfiguration()
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { configPath }, UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { Log($"Cannot open config: {ex.Message}"); }
    }
    private bool ReloadConfiguration()
    {
        var loaded = watcherService.LoadConfig(configPath);
        syncing = true;
        try
        {
            chkDryRun.Checked = watcherService.DryRun;
            chkPause.Checked = watcherService.IsPaused;
            chkGrouchy.Checked = watcherService.GrouchyMode;
        }
        finally { syncing = false; }
        return loaded;
    }
    private void btnReloadConfig_Click(object sender, EventArgs e) => ReloadConfiguration();
}

