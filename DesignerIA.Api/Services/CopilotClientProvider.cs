using System.Diagnostics;
using GitHub.Copilot;

namespace DesignerIA.Api.Services;

/// <summary>
/// Keeps one started SDK runtime for the API process. Starting a runtime and its first session costs
/// about two seconds per model call; every request still creates and disposes its own session, so no
/// conversation state is shared. The identity is the same logged-in user as before.
/// </summary>
public sealed class CopilotClientProvider : IAsyncDisposable
{
    private sealed class Runtime(CopilotClient client)
    {
        public CopilotClient Client { get; } = client;
        public int Users { get; set; }
        public bool Retired { get; set; }
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<CancellationToken, Task<CopilotClient>> _start;
    private readonly Func<CopilotClient, ValueTask> _stop;
    private Runtime? _current;

    public CopilotClientProvider() : this(StartClientAsync, client => client.DisposeAsync())
    {
    }

    // Lets the tests replace starting and stopping the SDK process.
    internal CopilotClientProvider(Func<CancellationToken, Task<CopilotClient>> start, Func<CopilotClient, ValueTask> stop)
    {
        _start = start;
        _stop = stop;
    }

    /// <summary>
    /// Runs SDK work on the shared runtime; <paramref name="startup"/> only advances when a runtime had to be started.
    /// A failure raised by <paramref name="work"/> retires that runtime so the next request starts a fresh one. The
    /// retired runtime is stopped when its last request leaves: stopping it earlier leaves the others waiting for
    /// the SDK timeout, and a late failure of one of them must not touch the runtime created afterwards.
    /// </summary>
    public async Task<T> UseAsync<T>(Stopwatch startup, Func<CopilotClient, Task<T>> work, CancellationToken cancellationToken)
    {
        var runtime = await AcquireAsync(startup, cancellationToken);
        var failed = false;
        try
        {
            return await work(runtime.Client);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            failed = true;
            throw;
        }
        finally
        {
            await ReleaseAsync(runtime, failed);
        }
    }

    private async Task<Runtime> AcquireAsync(Stopwatch startup, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_current is null)
            {
                startup.Start();
                try
                {
                    _current = new Runtime(await _start(cancellationToken));
                }
                finally
                {
                    startup.Stop();
                }
            }
            _current.Users++;
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReleaseAsync(Runtime runtime, bool failed)
    {
        var stop = false;
        await _gate.WaitAsync();
        try
        {
            runtime.Users--;
            if (failed)
            {
                runtime.Retired = true;
                if (ReferenceEquals(_current, runtime)) { _current = null; }
            }
            stop = runtime.Retired && runtime.Users == 0;
        }
        finally
        {
            _gate.Release();
        }
        if (stop) { await _stop(runtime.Client); }
    }

    private static async Task<CopilotClient> StartClientAsync(CancellationToken cancellationToken)
    {
        var client = new CopilotClient(new CopilotClientOptions { UseLoggedInUser = true });
        try
        {
            await client.StartAsync(cancellationToken);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        var runtime = Interlocked.Exchange(ref _current, null);
        if (runtime is not null) { await _stop(runtime.Client); }
        _gate.Dispose();
    }
}
