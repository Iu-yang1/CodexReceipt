using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ReceiptTray;

static class Program
{
    public static bool DemoOnStart;
    public static bool DemoWarningsOnStart;
    public static bool CheckMarker;
    public static bool ShowcaseOnStart;
    public static bool Diagnostics;
    public static bool SmokeTest;
    public static string DataDirectory => AppPaths.DataDirectory;
    [STAThread]
    static void Main(string[] args)
    {
        DemoOnStart = args.Contains("--demo-batch");
        DemoWarningsOnStart = args.Contains("--demo-warnings");
        CheckMarker = args.Contains("--verify-marker");
        ShowcaseOnStart = args.Contains("--showcase");
        SmokeTest = args.Contains("--smoke-test");
        Diagnostics = args.Contains("--diagnostics") || CheckMarker || SmokeTest;
        AppPaths.Initialize(args);
#if !PORTABLE
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
#endif
        if (args.Contains("--connection-check")) { CheckConnection().GetAwaiter().GetResult(); return; }
        using var mutex = new Mutex(true, SmokeTest ? "Local\\CodexReceiptSmokeTest" : "Local\\CodexReceiptDesktopTrial", out var first);
        if (!first) { MessageBox.Show("小票已在后台运行，请在任务栏通知区域找到小票图标。", "Codex 小票"); return; }
        ApplicationConfiguration.Initialize();
        try { AppPaths.ExtractAssets(); Application.Run(new ReceiptWindow()); }
        catch (Exception ex)
        {
            if (Diagnostics) File.WriteAllText(Path.Combine(DataDirectory, "startup-error.txt"), ex.ToString());
            MessageBox.Show("桌面小票启动失败，请检查程序是否完整，以及用户缓存目录是否可写。", "Codex 小票");
        }
    }
    static async Task CheckConnection()
    {
        bool connected = false; int windows = 0;
        using var client = new AccountClient(q => { windows = q.windows.Length; connected = true; });
        try { await client.Start(); }
        catch { }
        File.WriteAllText(Path.Combine(DataDirectory, "connection-check.json"), JsonSerializer.Serialize(new { connected, windows, codexLocated = CodexLocator.Find() != null }));
    }
}

sealed partial class ReceiptWindow : Form
{
    readonly WebView2 view = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
    readonly ContextMenuStrip paperMenu = new();
    readonly System.Windows.Forms.Timer pointerTimer = new() { Interval = 80 };
    readonly System.Windows.Forms.Timer poll = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer accountTimer = new() { Interval = 60000 };
    readonly UsageMonitor monitor;
    AccountClient? account;
    Quota? latest;
    readonly Dictionary<int, (long reset, int level)> warned = new();
    readonly QuotaResetTracker resetTracker = new();
    readonly Queue<Bill> waiting = new();
    bool ready, polling, refreshing, initialPrinted;
    int serial, printed, demoPrinted;
    readonly List<string> demoWarningIds = new();
    readonly List<JsonElement> showcasePrinted = new();
    bool soundEnabled;
    string audioState = "idle";
    string accountState = "connecting";
    string runtime = "";
    double viewportWidth = 320, viewportHeight = 760;
    int paperRegionCount;
    JsonElement? typography;
    JsonElement? stackLayout;
    JsonElement? markerCheck;
    bool matchedDisplayScale;
    protected override bool ShowWithoutActivation => true;
    public ReceiptWindow()
    {
        Text = "Codex 桌面小票"; FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false;
        BackColor = Color.FromArgb(255,253,246);
        var screen = Screen.PrimaryScreen!.WorkingArea;
        ClientSize = new Size(290, Math.Min(760, screen.Height - 28));
        StartPosition = FormStartPosition.Manual; Location = new Point(screen.Right - Width - 14, screen.Bottom - Height - 14);
        Controls.Add(view);
        Region = new Region(new Rectangle(0, Height - 1, 1, 1));
        paperMenu.Closed += (_, _) => Send(new { type = "menuClosed" });
        pointerTimer.Tick += (_, _) =>
        {
            var p = PointToClient(Cursor.Position);
            Send(new { type = "pointer", x = p.X * viewportWidth / ClientSize.Width, y = p.Y * viewportHeight / ClientSize.Height });
        };
        monitor = new UsageMonitor(Program.SmokeTest ? Path.Combine(Program.DataDirectory, "empty-sessions") : Path.Combine(AppPaths.CodexHome, "sessions"), ReceiveQuota, ReceiveBill);
        InitializeTray();
        poll.Tick += async (_, _) => await Poll();
        accountTimer.Tick += async (_, _) => await RefreshAccount();
        Shown += async (_, _) => await Initialize();
        FormClosed += (_, _) => { poll.Stop(); accountTimer.Stop(); pointerTimer.Stop(); smokeTimer?.Stop(); account?.Dispose(); WriteStatus("stopped"); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); trayIcon?.Dispose(); paperMenu.Dispose(); };
    }
    async Task Initialize()
    {
        try
        {
            runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDirectory, ".webview"), new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.IsMuted = false;
            view.CoreWebView2.SetVirtualHostNameToFolderMapping("receipt.local", AppPaths.AssetsDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://receipt.local/", StringComparison.Ordinal)) e.Cancel = true; };
            view.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            view.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                switch (doc.RootElement.Field("type").Text())
                {
                    case "ready": ready = true; WriteStatus(); break;
                    case "soundState": soundEnabled = doc.RootElement.Field("enabled").ValueKind == JsonValueKind.True; WriteStatus(); break;
                    case "printed":
                        printed++;
                        soundEnabled = doc.RootElement.Field("soundEnabled").ValueKind == JsonValueKind.True;
                        audioState = doc.RootElement.Field("audioState").Text();
                        if (doc.RootElement.Field("showcase").ValueKind == JsonValueKind.True) showcasePrinted.Add(doc.RootElement.Clone());
                        if (doc.RootElement.Field("demo").ValueKind == JsonValueKind.True)
                        {
                            demoPrinted++;
                            if (doc.RootElement.Field("receipt").Text() == "warning") demoWarningIds.Add(doc.RootElement.Field("id").Text());
                        }
                        typography = doc.RootElement.Field("typography").Clone(); WriteStatus(); _ = SavePreview();
                        if (Program.CheckMarker && markerCheck == null) _ = VerifyMarker();
                        break;
                    case "paperRegion": ApplyPaperRegion(doc.RootElement); break;
                    case "contextMenu": ShowPaperMenu(doc.RootElement); break;
                    case "stackSettled": stackLayout = doc.RootElement.Clone(); WriteStatus(); _ = SavePreview(); break;
                    case "curlPreview": _ = SavePreview("curl-preview.png"); break;
                    case "hide": Hide(); break;
                    case "quit": Close(); break;
                }
            };
            view.CoreWebView2.Navigate("https://receipt.local/index.html");
            await Poll();
            if (!Program.SmokeTest)
            {
                account = new AccountClient(q => OnUi(() => ReceiveQuota(q)));
                try { await account.Start(); accountState = "connected"; connectionDetail = "已连接本机 Codex"; }
                catch (Exception ex) { ConnectionFailed(ex); }
            }
            else { accountState = "smoke-test"; connectionDetail = "隔离启动检查"; }
            for (int i = 0; i < 50 && !ready; i++) await Task.Delay(100);
            if (ready)
            {
                if (Program.ShowcaseOnStart) { serial = 6; Send(new { type = "showcase", window = latest?.windows.FirstOrDefault() }); }
                else if (latest != null || Program.SmokeTest) Print(null);
                initialPrinted = true;
                while (waiting.TryDequeue(out var bill)) Print(bill);
            }
            if (ready && Program.DemoOnStart) Send(new { type = "demoBatch", count = 10 });
            if (ready && Program.DemoWarningsOnStart) Send(new { type = "demoWarnings" });
            poll.Start(); if (!Program.SmokeTest) accountTimer.Start(); pointerTimer.Start(); SendStatus(); WriteStatus();
            if (Program.SmokeTest) { smokeTimer = new() { Interval = 9000 }; smokeTimer.Tick += (_, _) => Close(); smokeTimer.Start(); }
            else if (accountState != "connected") tray.ShowBalloonTip(6000, "Codex 小票已在后台运行", connectionDetail, ToolTipIcon.Info);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            accountState = "webview-missing"; connectionDetail = "需要安装 Microsoft Edge WebView2 Runtime"; UpdateTrayStatus(); Hide();
            if (Program.SmokeTest) { WriteStatus("failed"); Close(); return; }
            tray.ShowBalloonTip(8000, "需要 WebView2", "右键小票托盘图标，打开官方 WebView2 安装页面。安装后重新启动小票。", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (Program.Diagnostics) File.WriteAllText(Path.Combine(Program.DataDirectory, "startup-error.txt"), ex.ToString());
            accountState = "initialization-failed"; connectionDetail = "界面初始化失败，请重新启动小票。"; UpdateTrayStatus();
            if (Program.SmokeTest) Close(); else { Hide(); tray.ShowBalloonTip(6000, "Codex 小票", connectionDetail, ToolTipIcon.Warning); }
        }
    }
    async Task SavePreview(string filename = "last-receipt.png")
    {
        if (!Program.Diagnostics) return;
        try
        {
            await using var output = File.Create(Path.Combine(Program.DataDirectory, filename));
            await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    async Task VerifyMarker()
    {
        Program.CheckMarker = false;
        try
        {
            var json = await view.CoreWebView2.ExecuteScriptAsync("window.receiptMarkerSelfTest()");
            using var doc = JsonDocument.Parse(json); markerCheck = doc.RootElement.Clone(); WriteStatus();
            await view.CoreWebView2.ExecuteScriptAsync("window.receiptMarkerPreview(true)");
            try { await Task.Delay(160); await SavePreview("marker-preview.png"); }
            finally { await view.CoreWebView2.ExecuteScriptAsync("window.receiptMarkerPreview(false)"); }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
    }
    void ApplyPaperRegion(JsonElement message)
    {
        if (!matchedDisplayScale)
        {
            matchedDisplayScale = true;
            double ratio = message.Field("pixelRatio").Real();
            if (ratio <= 0) ratio = DeviceDpi / 96.0;
            var screen = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size((int)Math.Ceiling(296 * ratio), Math.Min((int)Math.Ceiling(760 * ratio), screen.Height - 28));
            Location = new Point(screen.Right - Width - 14, screen.Bottom - Height - 14);
            return; // The next frame supplies rectangles in the resized viewport.
        }
        viewportWidth = Math.Max(1, message.Field("viewportWidth").Real());
        viewportHeight = Math.Max(1, message.Field("viewportHeight").Real());
        var next = new Region(); next.MakeEmpty();
        paperRegionCount = 0;
        var rects = message.Field("rects");
        if (rects.ValueKind == JsonValueKind.Array)
        {
            double sx = ClientSize.Width / viewportWidth, sy = ClientSize.Height / viewportHeight;
            foreach (var r in rects.EnumerateArray())
            {
                var rect = Rectangle.Intersect(ClientRectangle, new Rectangle(
                    (int)Math.Floor(r.Field("x").Real() * sx), (int)Math.Floor(r.Field("y").Real() * sy),
                    (int)Math.Ceiling(r.Field("width").Real() * sx), (int)Math.Ceiling(r.Field("height").Real() * sy)));
                if (rect.Width > 0 && rect.Height > 0)
                {
                    using var edge = r.Field("folded").ValueKind == JsonValueKind.True ? FoldedOutline(rect) : PaperOutline(rect, (float)(2 * sy), r.Field("topEdge").ValueKind == JsonValueKind.True, r.Field("bottomEdge").ValueKind == JsonValueKind.True);
                    next.Union(edge); paperRegionCount++;
                }
            }
        }
        var old = Region; Region = next; old?.Dispose();
    }
    internal static System.Drawing.Drawing2D.GraphicsPath PaperOutline(Rectangle rect, float depth, bool top, bool bottom)
    {
        var points = new List<PointF>();
        depth = Math.Min(depth, rect.Height / 3f);
        for (int i = 0; i <= 100; i++) points.Add(new(rect.Left + rect.Width * i / 100f, rect.Top + (top && i % 2 == 0 ? depth : 0)));
        for (int i = 100; i >= 0; i--) points.Add(new(rect.Left + rect.Width * i / 100f, rect.Bottom - (bottom && i % 2 == 0 ? depth : 0)));
        var path = new System.Drawing.Drawing2D.GraphicsPath(); path.AddPolygon(points.ToArray()); return path;
    }
    static System.Drawing.Drawing2D.GraphicsPath FoldedOutline(Rectangle r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath(); float corner = Math.Min(5, r.Height / 3f);
        path.AddBezier(r.Left+corner,r.Top+1,r.Left+r.Width*.3f,r.Top-1,r.Left+r.Width*.65f,r.Top+2,r.Right-corner,r.Top);
        path.AddBezier(r.Right-corner,r.Top,r.Right+1,r.Top,r.Right,r.Bottom,r.Right-corner,r.Bottom-1);
        path.AddBezier(r.Right-corner,r.Bottom-1,r.Left+r.Width*.7f,r.Bottom-2,r.Left+r.Width*.3f,r.Bottom+1,r.Left+corner,r.Bottom);
        path.AddBezier(r.Left+corner,r.Bottom,r.Left-1,r.Bottom,r.Left-1,r.Top+1,r.Left+corner,r.Top+1);
        path.CloseFigure(); return path;
    }
    void ShowPaperMenu(JsonElement message)
    {
        paperMenu.Items.Clear();
        bool busy = message.Field("busy").ValueKind == JsonValueKind.True;
        var soundItem = new ToolStripMenuItem("打印音效") { CheckOnClick = true, Checked = message.Field("sound").ValueKind == JsonValueKind.True };
        soundItem.Click += (_, _) => Send(new { type = "action", action = "sound", enabled = soundItem.Checked });
        paperMenu.Items.Add(soundItem);
        paperMenu.Items.Add("全部折叠", null, (_, _) => Send(new { type = "action", action = "fold" })).Enabled = !busy;
        paperMenu.Items.Add("撕下全部小票", null, (_, _) => Send(new { type = "action", action = "tear" })).Enabled = !busy;
        paperMenu.Items.Add(new ToolStripSeparator());
        paperMenu.Items.Add("打印当前额度", null, async (_, _) => { await RefreshAccount(); if (ready) Print(null); });
        paperMenu.Items.Add("连续打印 10 张演示票", null, (_, _) => Send(new { type = "demoBatch", count = 10 }));
        paperMenu.Items.Add("打印黄色小票", null, (_, _) => Send(new { type = "printSpecial", tone = "yellow" }));
        paperMenu.Items.Add("打印红色小票", null, (_, _) => Send(new { type = "printSpecial", tone = "red" }));
        paperMenu.Items.Add("打印青色小票", null, (_, _) => Send(new { type = "printSpecial", tone = "cyan" }));
        paperMenu.Items.Add("退出", null, (_, _) => Close());
        var p = new Point((int)(message.Field("x").Real() * ClientSize.Width / viewportWidth), (int)(message.Field("y").Real() * ClientSize.Height / viewportHeight));
        paperMenu.Show(this, p);
    }
    void OnUi(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    void ReceiveQuota(Quota q)
    {
        if (InvokeRequired) { OnUi(() => ReceiveQuota(q)); return; }
        if (latest != null && q.at < latest.at) return;
        var resets = resetTracker.Observe(q, ready && initialPrinted);
        latest = q; SendStatus(); WriteStatus();
        foreach (var reset in resets)
        {
            warned.Remove(reset.window.minutes);
            var record = new { id = (++serial).ToString("D6"), resetOnly = true, reset,
                printedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), warnings = Array.Empty<WindowQuota>() };
            Send(new { type = "receipt", record });
            if (!Visible) Show();
        }
    }
    void ReceiveBill(Bill b)
    {
        OnUi(() => { if (ready && initialPrinted) Print(b); else waiting.Enqueue(b); });
    }
    async Task Poll()
    {
        if (polling || IsDisposed) return;
        polling = true;
        try { await Task.Run(monitor.Poll); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { polling = false; WriteStatus(); }
    }
    async Task RefreshAccount()
    {
        if (refreshing || IsDisposed || Program.SmokeTest) return;
        refreshing = true;
        try
        {
            if (accountState != "connected")
            {
                account?.Dispose(); account = new AccountClient(q => OnUi(() => ReceiveQuota(q))); await account.Start();
            }
            else await account!.Refresh();
            accountState = "connected";
            connectionDetail = "已连接本机 Codex";
        }
        catch (Exception ex) { ConnectionFailed(ex); }
        finally { refreshing = false; SendStatus(); WriteStatus(); }
    }
    internal static int WarningLevel(double remaining) => remaining <= 0 ? 2 : remaining <= 10 ? 1 : 0;
    internal static bool ShouldPrintWarning(WindowQuota window, bool afterTask, (long reset, int level)? prior)
    {
        int level = WarningLevel(window.remaining);
        return level > 0 && ((level == 1 && afterTask) || prior == null
            || prior.Value.reset != window.resetAt || level > prior.Value.level);
    }
    void Print(Bill? bill)
    {
        if (!ready) return;
        var warnings = new List<WindowQuota>();
        foreach (var w in latest?.windows ?? [])
        {
            int level = WarningLevel(w.remaining);
            bool seen = warned.TryGetValue(w.minutes, out var prior);
            if (ShouldPrintWarning(w, bill != null, seen ? prior : null)) warnings.Add(w);
            warned[w.minutes] = (w.resetAt, level);
        }
        var usage = bill ?? monitor.LastCall;
        var record = new
        {
            id = (++serial).ToString("D6"), snapshot = bill == null, hasUsage = usage != null, input = usage?.input ?? 0,
            cached = usage?.cached ?? 0, output = usage?.output ?? 0, partial = bill?.partial ?? false,
            printedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), windows = latest?.windows ?? [],
            quotaAt = latest?.at ?? 0, source = latest?.source ?? "log", warnings
        };
        Send(new { type = "receipt", record });
        // New bills are visible without stealing the current app's keyboard focus.
        if (!Visible) Show();
        WriteStatus();
    }
    void Send(object message) { if (ready && !IsDisposed) view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
    void SendStatus()
    {
        UpdateTrayStatus();
        var value = latest == null ? "等待额度数据" : string.Join(" · ", latest.windows.Select(w => $"{w.label} {w.remaining}%"));
        Send(new { type = "status", text = value + (accountState == "connected" ? " · 已连接" : " · 本地记录") });
    }
    void WriteStatus(string state = "running")
    {
        if (!Program.Diagnostics) return;
        try
        {
            File.WriteAllText(Path.Combine(Program.DataDirectory, "status.json"), JsonSerializer.Serialize(new
            {
                state, pid = Environment.ProcessId, uiReady = ready, renderedReceipts = printed, account = accountState,
                webviewRuntime = runtime, watchedFiles = monitor.WatchedFiles, quota = latest,
                paperOnly = true, paperRegionCount, trayVisible = tray.Visible,
                demoPrinted,
                demoWarningIds,
                showcasePrinted, soundEnabled, audioState,
                stackLayout,
                markerCheck,
                typography, viewportWidth,
                updatedAt = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
    }
}
