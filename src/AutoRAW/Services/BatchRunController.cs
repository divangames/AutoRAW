using System.Diagnostics;

namespace AutoRAW.Services;

/// <summary>Пауза, отмена и учёт активного времени без пауз.</summary>
public sealed class BatchRunController : IDisposable
{
    private readonly ManualResetEventSlim _runGate = new(true);
    private readonly Stopwatch _activeClock = new();
    private CancellationTokenSource? _cts;
    private TimeSpan _accumulatedPause;
    private long _pauseStartTicks;

    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    public bool IsPaused => _pauseStartTicks != 0;

    public void Begin()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _accumulatedPause = TimeSpan.Zero;
        _pauseStartTicks = 0;
        _activeClock.Restart();
        _runGate.Set();
    }

    public void Pause()
    {
        if (_pauseStartTicks == 0)
            _pauseStartTicks = Stopwatch.GetTimestamp();
        _runGate.Reset();
    }

    public void Resume()
    {
        if (_pauseStartTicks != 0)
        {
            _accumulatedPause += Stopwatch.GetElapsedTime(_pauseStartTicks);
            _pauseStartTicks = 0;
        }

        _runGate.Set();
    }

    public void Cancel()
    {
        _cts?.Cancel();
        if (_pauseStartTicks != 0)
        {
            _accumulatedPause += Stopwatch.GetElapsedTime(_pauseStartTicks);
            _pauseStartTicks = 0;
        }

        _runGate.Set();
    }

    public TimeSpan ActiveElapsed
    {
        get
        {
            var paused = _accumulatedPause;
            if (_pauseStartTicks != 0)
                paused += Stopwatch.GetElapsedTime(_pauseStartTicks);
            return _activeClock.Elapsed - paused;
        }
    }

    public void WaitIfPausedOrCancelled()
    {
        Token.ThrowIfCancellationRequested();
        while (!_runGate.Wait(0, Token))
        {
            Token.ThrowIfCancellationRequested();
            _runGate.Wait(TimeSpan.FromMilliseconds(50), Token);
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
        _runGate.Dispose();
    }
}
