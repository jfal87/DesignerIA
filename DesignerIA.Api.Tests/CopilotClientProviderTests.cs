using System.Diagnostics;
using DesignerIA.Api.Services;
using GitHub.Copilot;
using Xunit;

namespace DesignerIA.Api.Tests;

/// <summary>
/// One SDK runtime is shared by concurrent requests. With the real runtime, stopping it while another request waited
/// left that request hanging until the SDK timeout (60 s), and a late failure dropped the runtime created afterwards.
/// The clients here are never started: they only identify which runtime each request received.
/// </summary>
public sealed class CopilotClientProviderTests
{
    private sealed class Runtimes
    {
        public List<CopilotClient> Started { get; } = [];
        public List<CopilotClient> Stopped { get; } = [];
        public int FailingStarts { get; set; }

        public CopilotClientProvider Provider() => new(_ =>
        {
            if (FailingStarts > 0)
            {
                FailingStarts--;
                throw new InvalidOperationException("runtime did not start");
            }
            var client = new CopilotClient(new CopilotClientOptions());
            lock (Started) { Started.Add(client); }
            return Task.FromResult(client);
        }, client =>
        {
            lock (Stopped) { Stopped.Add(client); }
            return ValueTask.CompletedTask;
        });
    }

    private static Task<CopilotClient> Use(CopilotClientProvider provider, Func<CopilotClient, Task>? work = null, CancellationToken token = default) =>
        provider.UseAsync(new Stopwatch(), async client =>
        {
            if (work is not null) { await work(client); }
            return client;
        }, token);

    [Fact]
    public async Task ConcurrentRequestsShareOneRuntime()
    {
        var runtimes = new Runtimes();
        await using var provider = runtimes.Provider();
        var clients = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Use(provider))));
        Assert.Single(runtimes.Started);
        Assert.All(clients, client => Assert.Same(runtimes.Started[0], client));
        Assert.Empty(runtimes.Stopped);
    }

    [Fact]
    public async Task AFailedRequestDoesNotStopTheRuntimeAnotherRequestIsStillUsing()
    {
        var runtimes = new Runtimes();
        await using var provider = runtimes.Provider();
        var otherIsInside = new TaskCompletionSource();
        var releaseOther = new TaskCompletionSource();
        var other = Use(provider, async _ =>
        {
            otherIsInside.SetResult();
            await releaseOther.Task;
        });
        await otherIsInside.Task;

        await Assert.ThrowsAsync<IOException>(() => Use(provider, _ => throw new IOException("sdk failure")));
        Assert.Empty(runtimes.Stopped);

        // The next request does not reuse the runtime that failed.
        var fresh = await Use(provider);
        Assert.NotSame(runtimes.Started[0], fresh);

        releaseOther.SetResult();
        Assert.Same(runtimes.Started[0], await other);
        Assert.Equal([runtimes.Started[0]], runtimes.Stopped);
    }

    [Fact]
    public async Task ALateFailureOnARetiredRuntimeDoesNotTouchTheOneCreatedAfterwards()
    {
        var runtimes = new Runtimes();
        await using var provider = runtimes.Provider();
        var lateIsInside = new TaskCompletionSource();
        var failLate = new TaskCompletionSource();
        var late = Use(provider, async _ =>
        {
            lateIsInside.SetResult();
            await failLate.Task;
            throw new TimeoutException("sdk timeout");
        });
        await lateIsInside.Task;
        await Assert.ThrowsAsync<IOException>(() => Use(provider, _ => throw new IOException("sdk failure")));
        var fresh = await Use(provider);

        failLate.SetResult();
        await Assert.ThrowsAsync<TimeoutException>(() => late);

        Assert.Same(fresh, await Use(provider));
        Assert.Equal(2, runtimes.Started.Count);
        Assert.Equal([runtimes.Started[0]], runtimes.Stopped);
    }

    [Fact]
    public async Task ManyRequestsFailingTogetherRetireTheRuntimeOnceAndRecover()
    {
        var runtimes = new Runtimes();
        await using var provider = runtimes.Provider();
        var inside = 0;
        var allInside = new TaskCompletionSource();
        var failing = Enumerable.Range(0, 8).Select(_ => Use(provider, async _ =>
        {
            if (Interlocked.Increment(ref inside) == 8) { allInside.SetResult(); }
            await allInside.Task;
            throw new IOException("sdk failure");
        })).ToArray();
        foreach (var request in failing) { await Assert.ThrowsAsync<IOException>(() => request); }

        var recovered = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Use(provider))));
        Assert.Equal(2, runtimes.Started.Count);
        Assert.All(recovered, client => Assert.Same(runtimes.Started[1], client));
        Assert.Equal([runtimes.Started[0]], runtimes.Stopped);
    }

    [Fact]
    public async Task ACancelledRequestDoesNotRetireTheRuntime()
    {
        var runtimes = new Runtimes();
        await using var provider = runtimes.Provider();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Use(provider, _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }, cancellation.Token));
        Assert.Same(runtimes.Started[0], await Use(provider));
        Assert.Single(runtimes.Started);
        Assert.Empty(runtimes.Stopped);
    }

    [Fact]
    public async Task ARuntimeThatDoesNotStartIsNotKept()
    {
        var runtimes = new Runtimes { FailingStarts = 1 };
        await using var provider = runtimes.Provider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Use(provider));
        var client = await Use(provider);
        Assert.Same(Assert.Single(runtimes.Started), client);
    }
}
