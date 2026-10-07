using System.Text;
using System.Text.Json;

namespace ReceiptTray;

record WindowQuota(string label, double remaining, long resetAt, int minutes);
record Quota(WindowQuota[] windows, long at, string source);
record Bill(long input, long cached, long output, bool partial);

static class JsonValue
{
    public static JsonElement Field(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    public static string Text(this JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    public static long Number(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) ? n : 0;
    public static double Real(this JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;
    public static Quota? ReadQuota(JsonElement value, long at, string source)
    {
        var windows = new List<WindowQuota>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            var w = value.Field(key);
            if (w.ValueKind != JsonValueKind.Object) continue;
            bool snake = w.Field("used_percent").ValueKind == JsonValueKind.Number;
            var used = w.Field(snake ? "used_percent" : "usedPercent");
            if (used.ValueKind != JsonValueKind.Number) continue;
            int mins = (int)w.Field(snake ? "window_minutes" : "windowDurationMins").Number();
            string label = mins == 10080 ? "周额度" : mins == 300 ? "5 小时额度" : mins > 0 ? $"{mins / 60.0:g} 小时额度" : "额度";
            windows.Add(new(label, Math.Round(Math.Clamp(100 - used.Real(), 0, 100), 1), w.Field(snake ? "resets_at" : "resetsAt").Number() * 1000, mins));
        }
        return windows.Count > 0 ? new(windows.ToArray(), at, source) : null;
    }
}

// Reads only event metadata. Prompts, responses, credentials and API keys are never retained.
sealed class UsageMonitor
{
    sealed class Cursor
    {
        public long Offset;
        public string Pending = "";
        public Decoder Decoder = Encoding.UTF8.GetDecoder();
        public long Input, Cached, Output;
        public long DeltaInput, DeltaCached, DeltaOutput;
        public bool Known, Partial = true;
    }
    readonly Dictionary<string, Cursor> files = new(StringComparer.OrdinalIgnoreCase);
    readonly string root;
    readonly DateTime launched = DateTime.UtcNow;
    readonly Action<Quota> quota;
    readonly Action<Bill> bill;
    public int WatchedFiles => files.Count;
    public Bill? LastCall { get; private set; }
    public long LastCallAt { get; private set; }
    public UsageMonitor(string root, Action<Quota> quota, Action<Bill> bill) { this.root = root; this.quota = quota; this.bill = bill; }

    public void Poll()
    {
        // Include recent active sessions, even if the conversation was originally created long ago.
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
        {
            if (!files.ContainsKey(path) && File.GetLastWriteTimeUtc(path) < launched.AddDays(-2)) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!files.TryGetValue(path, out var c))
                {
                    c = new(); files[path] = c;
                    bool existing = File.GetCreationTimeUtc(path) < launched;
                    if (existing)
                    {
                        stream.Seek(Math.Max(0, stream.Length - 1048576), SeekOrigin.Begin);
                        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
                        if (stream.Position > 0) reader.ReadLine();
                        string? line;
                        while ((line = reader.ReadLine()) != null) Process(c, line, baseline: true);
                        c.Offset = stream.Length;
                        continue;
                    }
                }
                if (stream.Length < c.Offset) { c.Offset = 0; c.Pending = ""; c.Decoder.Reset(); c.Known = false; c.Partial = true; }
                stream.Position = c.Offset;
                var bytes = new byte[Math.Min(262144, stream.Length - c.Offset)];
                int read;
                while (bytes.Length > 0 && (read = stream.Read(bytes, 0, bytes.Length)) > 0)
                {
                    c.Offset += read;
                    var chars = new char[Encoding.UTF8.GetMaxCharCount(read)];
                    var n = c.Decoder.GetChars(bytes, 0, read, chars, 0, false);
                    c.Pending += new string(chars, 0, n);
                    int end;
                    while ((end = c.Pending.IndexOf('\n')) >= 0)
                    {
                        Process(c, c.Pending[..end], false); c.Pending = c.Pending[(end + 1)..];
                    }
                }
            }
            catch (IOException) { /* A live writer can briefly lock or rotate a file. Try next poll. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    void Process(Cursor c, string line, bool baseline)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            if (e.Field("type").Text() != "event_msg") return;
            var p = e.Field("payload"); var type = p.Field("type").Text();
            if (type == "token_count")
            {
                if (DateTimeOffset.TryParse(e.Field("timestamp").Text(), out var time))
                {
                    var last = p.Field("info").Field("last_token_usage");
                    if (last.ValueKind == JsonValueKind.Object && time.ToUnixTimeMilliseconds() >= LastCallAt)
                    {
                        LastCall = new(last.Field("input_tokens").Number(), last.Field("cached_input_tokens").Number(), last.Field("output_tokens").Number(), false);
                        LastCallAt = time.ToUnixTimeMilliseconds();
                    }
                    var limits = p.Field("rate_limits");
                    var id = limits.Field("limit_id").Text();
                    if (id is "" or "codex")
                    {
                        var q = JsonValue.ReadQuota(limits, time.ToUnixTimeMilliseconds(), "log");
                        if (q != null) quota(q);
                    }
                }
                var total = p.Field("info").Field("total_token_usage");
                if (total.ValueKind != JsonValueKind.Object) return;
                long input = total.Field("input_tokens").Number(), cached = total.Field("cached_input_tokens").Number(), output = total.Field("output_tokens").Number();
                if (!baseline && c.Known)
                {
                    // Counter resets are new baselines, never negative consumption.
                    if (input >= c.Input && output >= c.Output)
                    { c.DeltaInput += input - c.Input; c.DeltaCached += Math.Max(0, cached - c.Cached); c.DeltaOutput += output - c.Output; }
                    else c.Partial = true;
                }
                else if (!baseline && !c.Partial)
                { c.DeltaInput += input; c.DeltaCached += cached; c.DeltaOutput += output; }
                c.Input = input; c.Cached = cached; c.Output = output; c.Known = true;
            }
            if (baseline) return;
            if (type == "task_started")
            {
                // A new turn can begin after an aborted turn whose usage still needs settlement.
                Emit(c); c.Partial = false;
            }
            if (type is "task_complete" or "turn_aborted") { Emit(c); c.Partial = false; }
        }
        catch (JsonException) { }
    }
    void Emit(Cursor c)
    {
        if (c.DeltaInput + c.DeltaOutput > 0) bill(new(c.DeltaInput, c.DeltaCached, c.DeltaOutput, c.Partial));
        c.DeltaInput = c.DeltaCached = c.DeltaOutput = 0;
    }
}
