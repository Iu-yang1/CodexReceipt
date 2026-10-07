namespace ReceiptTray;

record QuotaReset(WindowQuota window, long resetAt);

// Require a fresh sample in a new quota window, rather than treating a local timer as proof.
sealed class QuotaResetTracker
{
    readonly Dictionary<int, (WindowQuota window, long at)> previous = new();
    readonly Dictionary<int, long> notified = new();

    public List<QuotaReset> Observe(Quota quota, bool notify)
    {
        var resets = new List<QuotaReset>();
        foreach (var window in quota.windows)
        {
            if (previous.TryGetValue(window.minutes, out var prior))
            {
                if (quota.at <= prior.at) continue;
                if (window.resetAt - prior.window.resetAt >= Math.Max(60000L, window.minutes * 30000L)
                    && quota.at < prior.window.resetAt) continue;
                // Small reset timestamp corrections are not a new cycle.
                bool rolled = window.minutes > 0 && prior.window.resetAt > 0
                    && quota.at >= prior.window.resetAt && window.resetAt > quota.at
                    && window.resetAt - prior.window.resetAt >= Math.Max(60000L, window.minutes * 30000L);
                if (rolled && notify && (!notified.TryGetValue(window.minutes, out var last) || last < window.resetAt))
                {
                    resets.Add(new(window, prior.window.resetAt));
                    notified[window.minutes] = window.resetAt;
                }
                // Ignore samples that would move an already observed cycle backwards.
                if (window.resetAt > 0 && prior.window.resetAt > window.resetAt + 60000L) continue;
            }
            previous[window.minutes] = (window, quota.at);
        }
        return resets;
    }
}
