using System.Collections.Concurrent;

namespace ChessGameD.Web.Services.Auth;

/// <summary>
/// 注册频率限制(进程内计数,按 IP + 小时窗口)。
///
/// 单机部署够用;以后要多实例部署再换成共享存储。
/// </summary>
public sealed class RegistrationLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    /// <summary>取一次注册额度;超了返回 false。</summary>
    public bool TryAcquire(string key, int limit, TimeSpan window)
    {
        if (limit <= 0)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { WindowStart = now });

        lock (bucket)
        {
            if (now - bucket.WindowStart >= window)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= limit)
            {
                return false;
            }

            bucket.Count++;
            return true;
        }
    }

    /// <summary>清掉过期窗口,避免字典无限增长。</summary>
    public void Prune(TimeSpan window)
    {
        var threshold = DateTime.UtcNow - window;
        foreach (var kv in _buckets)
        {
            lock (kv.Value)
            {
                if (kv.Value.WindowStart < threshold)
                {
                    _buckets.TryRemove(kv.Key, out _);
                }
            }
        }
    }

    private sealed class Bucket
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }
}
