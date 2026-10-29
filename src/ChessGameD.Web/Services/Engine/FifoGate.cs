namespace ChessGameD.Web.Services.Engine;

/// <summary>
/// 先进先出的排队闸门。
///
/// 引擎进程只有一份,所有分析请求(即时分析、整局分析)都从这里过:
/// 先来后到,同一时刻只放一个进去。排队时能查到自己前面还有几个人,
/// 不用再报「引擎忙」。
/// </summary>
public sealed class FifoGate
{
    private readonly object _lock = new();
    private readonly Queue<Waiter> _waiters = new();
    private bool _held;

    /// <summary>排队的凭条:拿在手里可以随时问「我前面还有几个」。</summary>
    public sealed class Ticket
    {
        private readonly FifoGate _gate;
        private readonly Waiter _waiter;

        internal Ticket(FifoGate gate, Waiter waiter)
        {
            _gate = gate;
            _waiter = waiter;
        }

        /// <summary>前面还有几个任务在等(0 表示马上轮到)。</summary>
        public int Ahead => _gate.CountAhead(_waiter);
        /// <summary>自己的位次(1 起)。</summary>
        public int Position => Ahead + 1;

        /// <summary>队伍里一共几个人。</summary>
        public int Waiting => _gate.WaitingCount;

        /// <summary>内部用的排队项。</summary>
        internal Waiter WaiterInternal => _waiter;
    }

    internal sealed class Waiter
    {
        public TaskCompletionSource Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>已经排队取消掉的,放行时直接跳过。</summary>
        public bool Cancelled;
    }

    /// <summary>当前还在排队的数量(不含正在跑的那个,也不含已经取消的)。</summary>
    public int WaitingCount
    {
        get
        {
            lock (_lock)
            {
                return _waiters.Count(w => !w.Cancelled);
            }
        }
    }

    /// <summary>取号:拿到一张排队凭条,还没轮到时要自己调 <see cref="WaitAsync"/>。</summary>
    public Ticket Enqueue()
    {
        var waiter = new Waiter();
        bool grantNow;

        lock (_lock)
        {
            grantNow = !_held;
            if (grantNow)
            {
                _held = true;
            }
            else
            {
                _waiters.Enqueue(waiter);
            }
        }

        if (grantNow)
        {
            waiter.Source.TrySetResult();
        }

        return new Ticket(this, waiter);
    }

    /// <summary>等到轮到自己。被取消时抛 <see cref="OperationCanceledException"/>。</summary>
    public async Task WaitAsync(Ticket ticket, CancellationToken ct)
    {
        var waiter = ticket.WaiterInternal;

        if (!ct.CanBeCanceled)
        {
            await waiter.Source.Task.ConfigureAwait(false);
            return;
        }

        using var registration = ct.Register(() => Cancel(waiter));
        await waiter.Source.Task.ConfigureAwait(false);
    }

    /// <summary>放行:把闸门交给队首(没人排队就把闸门放开)。</summary>
    public void Release()
    {
        Waiter? next = null;

        lock (_lock)
        {
            while (_waiters.Count > 0)
            {
                var candidate = _waiters.Dequeue();
                if (candidate.Cancelled)
                {
                    continue;
                }

                next = candidate;
                break;
            }

            if (next is null)
            {
                _held = false;
            }
        }

        next?.Source.TrySetResult();
    }

    /// <summary>排队中的任务被取消:从队伍里摘掉,放行时直接跳过。</summary>
    private void Cancel(Waiter waiter)
    {
        lock (_lock)
        {
            // 已经放行给这个请求了,让它自己走完(它会在 finally 里 Release)
            if (waiter.Source.Task.IsCompleted)
            {
                return;
            }

            waiter.Cancelled = true;
        }

        waiter.Source.TrySetCanceled();
    }

    private int CountAhead(Waiter waiter)
    {
        lock (_lock)
        {
            var index = 0;
            foreach (var candidate in _waiters)
            {
                if (candidate.Cancelled)
                {
                    continue;
                }

                if (ReferenceEquals(candidate, waiter))
                {
                    return index;
                }

                index++;
            }

            return 0;
        }
    }
}
