using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReceiptTray;

sealed partial class ReceiptWindow
{
    readonly NotifyIcon tray = new();
    readonly ContextMenuStrip trayMenu = new();
    Icon? trayIcon;
    System.Windows.Forms.Timer? smokeTimer;
    string connectionDetail = "正在连接本机 Codex…";
    void InitializeTray()
    {
        trayIcon = CreateReceiptIcon(); Icon = trayIcon;
        tray.Icon = trayIcon; tray.Text = "Codex 小票 · 正在连接"; tray.ContextMenuStrip = trayMenu; tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowReceipts();
        trayMenu.Opening += (_, _) => BuildTrayMenu();
    }
    void ShowReceipts()
    {
        if (!ready) { tray.ShowBalloonTip(6000, "Codex 小票", connectionDetail, ToolTipIcon.Info); return; }
        Show();
        if (paperRegionCount == 0) Print(null);
        else Send(new { type = "action", action = "show" });
    }
    void BuildTrayMenu()
    {
        trayMenu.Items.Clear();
        trayMenu.Items.Add(connectionDetail).Enabled = false;
        trayMenu.Items.Add("显示小票", null, (_, _) => ShowReceipts()).Enabled = ready;
        trayMenu.Items.Add("隐藏小票（保持后台运行）", null, (_, _) => Hide()).Enabled = ready;
        trayMenu.Items.Add("重新连接 Codex", null, async (_, _) => { await RefreshAccount(); if (ready && latest != null) Print(null); }).Enabled = ready;
        trayMenu.Items.Add("选择 Codex 程序…", null, async (_, _) => {
            using var dialog = new OpenFileDialog { Title = "选择 Codex 命令行程序 codex.exe", Filter = "Codex 命令行程序 (codex.exe)|codex.exe", CheckFileExists = true };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            AppPaths.SaveCodex(dialog.FileName); account?.Dispose(); account = null; accountState = "connecting";
            await RefreshAccount();
        });
        if (accountState == "webview-missing") trayMenu.Items.Add("安装 WebView2（微软官网）", null, (_, _) =>
            Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }));
        trayMenu.Items.Add(new ToolStripSeparator());
        var soundItem = new ToolStripMenuItem("打印音效") { CheckOnClick = true, Checked = soundEnabled, Enabled = ready };
        soundItem.Click += (_, _) => Send(new { type = "action", action = "sound", enabled = soundItem.Checked });
        trayMenu.Items.Add(soundItem);
        trayMenu.Items.Add("打印当前额度", null, async (_, _) => { await RefreshAccount(); if (ready) { Show(); Print(null); } }).Enabled = ready;
        foreach (var (label, tone) in new[] { ("打印黄色小票", "yellow"), ("打印红色小票", "red"), ("打印青色小票", "cyan") })
            trayMenu.Items.Add(label, null, (_, _) => { Show(); Send(new { type = "printSpecial", tone }); }).Enabled = ready;
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Close());
    }
    void UpdateTrayStatus()
    {
        var label = "Codex 小票 · " + connectionDetail;
        tray.Text = label.Length > 63 ? label[..63] : label;
    }
    void ConnectionFailed(Exception error)
    {
        account?.Dispose(); account = null;
        accountState = "log-fallback";
        connectionDetail = error is FileNotFoundException ? "未找到 Codex：请安装登录或手动选择程序"
            : error is UnauthorizedAccessException ? "请先在 Codex 登录，再重新连接"
            : "额度连接暂不可用，继续读取本机任务记录";
        UpdateTrayStatus();
    }
    static Icon CreateReceiptIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var ink = new Pen(Color.FromArgb(43, 52, 48), 1.6f);
        PointF[] paper = [new(7, 2), new(25, 2), new(25, 29), new(22, 27), new(19, 29), new(16, 27), new(13, 29), new(10, 27), new(7, 29)];
        g.FillPolygon(Brushes.Ivory, paper); g.DrawPolygon(ink, paper);
        using var accent = new SolidBrush(Color.FromArgb(49, 148, 130));
        g.FillRectangle(accent, 11, 7, 10, 4);
        g.DrawLine(ink, 11, 16, 21, 16); g.DrawLine(ink, 11, 21, 18, 21);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
