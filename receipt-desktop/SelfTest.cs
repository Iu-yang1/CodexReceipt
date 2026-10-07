using System.Text.Json;

namespace ReceiptTray;

static class SelfTest
{
    public static void Run()
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        AppPaths.ExtractAssets();
        Check(File.Exists(Path.Combine(AppPaths.AssetsDirectory, "index.html")) && File.Exists(Path.Combine(AppPaths.AssetsDirectory, "fonts", "VT323-Regular.ttf")), "embedded UI and fonts extract without the development folder");
        Check(!Directory.EnumerateFiles(AppPaths.AssetsDirectory, "*", SearchOption.AllDirectories).Any(p => Path.GetFileName(p) is "status.json" or "auth.json" or "last-receipt.png"), "embedded resources exclude runtime usage, credentials and screenshots");
        Check(CodexLocator.FindUnder(Path.Combine(Program.DataDirectory, "missing-install")) == null, "a missing Codex install is handled without crashing");
        var root = Path.Combine(Program.DataDirectory, "test-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "session.jsonl");
        string Event(string type, object? info = null) => JsonSerializer.Serialize(new { type = "event_msg", timestamp = DateTimeOffset.UtcNow, payload = new { type, info } }) + "\n";
        object Counts(int input, int output) => new { total_token_usage = new { input_tokens = input, cached_input_tokens = input / 2, output_tokens = output } };
        File.WriteAllText(path, Event("token_count", Counts(100, 10)));
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
        var bills = new List<Bill>();
        var monitor = new UsageMonitor(root, _ => { }, bills.Add);
        monitor.Poll(); Check(bills.Count == 0, "historical usage is not billed on launch");
        File.AppendAllText(path, Event("task_started") + Event("token_count", Counts(200, 30)) + Event("token_count", Counts(200, 30)) + Event("task_complete"));
        monitor.Poll(); Check(bills.Count == 1 && bills[0] == new Bill(100, 50, 20, false), "delta aggregation, cached subset and duplicate suppression");
        monitor.Poll(); Check(bills.Count == 1, "polling does not replay receipts");
        var partial = Event("token_count", Counts(220, 35));
        File.AppendAllText(path, partial[..20]); monitor.Poll();
        File.AppendAllText(path, partial[20..] + Event("turn_aborted")); monitor.Poll();
        Check(bills.Count == 2 && bills[1].input == 20 && bills[1].output == 5, "partial JSONL writes and aborted usage are handled");
        File.AppendAllText(path, Event("task_started") + Event("token_count", Counts(1, 1)) + Event("task_complete")); monitor.Poll();
        Check(bills.Count == 2, "counter reset cannot produce negative or fabricated usage");
        using var q = JsonDocument.Parse("{\"primary\":{\"usedPercent\":22,\"windowDurationMins\":10080,\"resetsAt\":1791610593},\"secondary\":null}");
        var quota = JsonValue.ReadQuota(q.RootElement, 1, "account");
        Check(quota?.windows[0].remaining == 78 && quota.windows[0].resetAt == 1791610593000, "remaining percent and Unix reset conversion");
        WindowQuota Warn(double remaining) => new("周额度", remaining, 1800000000000L, 10080);
        Check(!ReceiptWindow.ShouldPrintWarning(Warn(10.1), true, null), "quota above ten percent does not print yellow");
        Check(ReceiptWindow.ShouldPrintWarning(Warn(10), true, (1800000000000L, 1)), "exactly ten percent prints yellow after every task");
        Check(ReceiptWindow.ShouldPrintWarning(Warn(.1), true, (1800000000000L, 1)), "remaining positive quota below ten repeats yellow after tasks");
        Check(!ReceiptWindow.ShouldPrintWarning(Warn(10), false, (1800000000000L, 1)), "manual snapshots do not repeatedly append yellow warnings");
        Check(ReceiptWindow.ShouldPrintWarning(Warn(0), true, (1800000000000L, 1)) && ReceiptWindow.WarningLevel(0) == 2, "exhaustion upgrades yellow to red");
        Check(!ReceiptWindow.ShouldPrintWarning(Warn(0), true, (1800000000000L, 2)), "red warnings retain same-cycle deduplication");
        var resets = new QuotaResetTracker();
        const long boundary = 1800000000000L, period = 604800000L;
        Quota Sample(long at, long reset, double remaining = 0) => new([new("周额度", remaining, reset, 10080)], at, "account");
        Check(resets.Observe(Sample(boundary - 10000, boundary), false).Count == 0, "startup quota is a baseline, not a reset notification");
        Check(resets.Observe(Sample(boundary - 1000, boundary + 1000), true).Count == 0, "small timestamp corrections do not notify");
        Check(resets.Observe(Sample(boundary + 500, boundary + period, 100), true).Count == 0, "a future reset deadline alone is not proof before the old boundary");
        Check(resets.Observe(Sample(boundary + 2000, boundary + period, 100), true).Count == 1, "an early new-cycle sample does not consume the later reset event");
        // Independent tracker models a normal rollover, including use immediately after reset.
        resets = new(); resets.Observe(Sample(boundary - 10000, boundary), false);
        var resetEvents = resets.Observe(Sample(boundary + 2000, boundary + period, 97), true);
        Check(resetEvents.Count == 1 && resetEvents[0].resetAt == boundary && resetEvents[0].window.remaining == 97, "fresh new cycle prints one notice with observed remaining quota");
        Check(resets.Observe(Sample(boundary + 3000, boundary + period, 96), true).Count == 0, "same cycle does not print twice");
        Check(resets.Observe(Sample(boundary - 2000, boundary), true).Count == 0, "stale source samples cannot repeat a reset");
        Check(resets.Observe(Sample(boundary + 4000, boundary), true).Count == 0, "late samples from the old cycle cannot rewind tracking");
        Check(resets.Observe(Sample(boundary + 5000, boundary + period, 96), true).Count == 0, "returning from an old-cycle sample does not repeat the notice");
        Check(resets.Observe(Sample(boundary + period + 1000, boundary + 2 * period, 100), true).Count == 1, "the following actual reset notifies again");
        File.WriteAllText(Path.Combine(Program.DataDirectory, "self-test.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
