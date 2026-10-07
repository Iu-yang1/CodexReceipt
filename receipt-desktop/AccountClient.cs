using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace ReceiptTray;

sealed class AccountClient : IDisposable
{
    Process? process;
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    int sequence;
    readonly Action<Quota> changed;
    public AccountClient(Action<Quota> changed) { this.changed = changed; }
    public async Task Start()
    {
        var exe = CodexLocator.Find() ?? throw new FileNotFoundException("未找到 Codex，请先安装并登录，或从托盘选择 codex.exe。");
        process = Process.Start(new ProcessStartInfo(exe, "app-server")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppPaths.DataDirectory
        }) ?? throw new InvalidOperationException("无法启动额度读取进程");
        process.ErrorDataReceived += (_, _) => { }; // Drain, without persisting potential private diagnostics.
        process.BeginErrorReadLine();
        _ = Read(process);
        await Request("initialize", new { clientInfo = new { name = "codex_receipt", title = "Codex Receipt", version = "0.1.0" } });
        await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
        var account = await Request("account/read", new { refreshToken = false });
        if (account.Field("account").ValueKind != JsonValueKind.Object)
            throw new UnauthorizedAccessException("请先在本机 Codex 中登录，再点击托盘的重新连接。");
        await Refresh();
    }
    public async Task Refresh()
    {
        var result = await Request("account/rateLimits/read", new { });
        var map = result.Field("rateLimitsByLimitId");
        var limits = map.Field("codex");
        if (limits.ValueKind != JsonValueKind.Object) limits = result.Field("rateLimits");
        Apply(limits);
    }
    void Apply(JsonElement limits)
    {
        var id = limits.Field("limitId").Text();
        if (id is not ("" or "codex")) return;
        var q = JsonValue.ReadQuota(limits, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "account");
        if (q != null) changed(q);
        else throw new InvalidOperationException("此登录暂未提供订阅额度，可继续读取本机任务用量。");
    }
    async Task<JsonElement> Request(string method, object args)
    {
        int id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = args }));
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { pending.TryRemove(id, out _); }
    }
    async Task Read(Process p)
    {
        try
        {
            while (await p.StandardOutput.ReadLineAsync() is { } line)
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                int id = (int)root.Field("id").Number();
                if (id > 0 && pending.TryGetValue(id, out var tcs))
                {
                    if (root.Field("error").ValueKind == JsonValueKind.Object) tcs.TrySetException(new InvalidOperationException("账户接口返回错误"));
                    else tcs.TrySetResult(root.Field("result").Clone());
                }
                else if (root.Field("method").Text() == "account/rateLimits/updated") Apply(root.Field("params").Field("rateLimits"));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or ObjectDisposedException or InvalidOperationException) { }
        finally { foreach (var tcs in pending.Values) tcs.TrySetException(new IOException("账户连接已关闭")); }
    }
    public void Dispose()
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        process?.Dispose();
    }
}
