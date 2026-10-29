using ChessGameD.Web.Services.Engine;
using Xunit;

namespace ChessGameD.Tests;

/// <summary>引擎队列(先进先出)的排队行为。</summary>
public sealed class FifoGateTests
{
    [Fact]
    public async Task 任务_按先来后到的顺序执行()
    {
        var gate = new FifoGate();
        var order = new List<int>();

        async Task Run(int id)
        {
            var ticket = gate.Enqueue();
            await gate.WaitAsync(ticket, CancellationToken.None);
            try
            {
                order.Add(id);
                await Task.Delay(10);
            }
            finally
            {
                gate.Release();
            }
        }

        var tasks = new[] { Run(1), Run(2), Run(3) };
        await Task.WhenAll(tasks);

        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public async Task 排队时_能查到自己的位次()
    {
        var gate = new FifoGate();

        var first = gate.Enqueue();
        var second = gate.Enqueue();
        var third = gate.Enqueue();

        // 第一个已经拿到通行证(正在跑),后面两个在排队
        Assert.Equal(0, first.Ahead);
        Assert.Equal(0, second.Ahead);
        Assert.Equal(1, third.Ahead);
        Assert.Equal(1, second.Position);
        Assert.Equal(2, third.Position);
        Assert.Equal(2, gate.WaitingCount);

        gate.Release();
        await gate.WaitAsync(first, CancellationToken.None);
        gate.Release();
    }

    [Fact]
    public async Task 前面的任务跑完_后面的人往前挪()
    {
        var gate = new FifoGate();

        var first = gate.Enqueue();
        var second = gate.Enqueue();
        var third = gate.Enqueue();
        Assert.Equal(1, third.Ahead);

        await gate.WaitAsync(first, CancellationToken.None);
        gate.Release();

        // 第一个放行后,第二个接上,第三个前面就没人了
        Assert.Equal(0, third.Ahead);

        await gate.WaitAsync(second, CancellationToken.None);
        gate.Release();
        await gate.WaitAsync(third, CancellationToken.None);
        gate.Release();
    }

    [Fact]
    public async Task 排队中的任务被取消_不会占着位置()
    {
        var gate = new FifoGate();

        var first = gate.Enqueue();
        var second = gate.Enqueue();
        using var cts = new CancellationTokenSource();

        var waiting = gate.WaitAsync(second, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // 取消之后队列里只剩一个
        Assert.Equal(0, gate.WaitingCount);

        await gate.WaitAsync(first, CancellationToken.None);
        gate.Release();
    }

    [Fact]
    public async Task 所有人都在排队时_队伍不会被放空()
    {
        var gate = new FifoGate();

        var ticket = gate.Enqueue();
        await gate.WaitAsync(ticket, CancellationToken.None);

        // 闸门被占着,再来的只能排队
        var next = gate.Enqueue();
        Assert.Equal(0, next.Ahead);
        Assert.Equal(1, next.Position);
        Assert.Equal(1, gate.WaitingCount);

        gate.Release();
        await gate.WaitAsync(next, CancellationToken.None);
        gate.Release();

        Assert.Equal(0, gate.WaitingCount);
    }
}
