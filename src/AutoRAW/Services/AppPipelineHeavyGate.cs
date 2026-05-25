namespace AutoRAW.Services;

/// <summary>
/// Счётчик тяжёлых операций очереди/редактора (статусы, фоновая авто-подгонка): пакет кадрирования ждёт, пока все завершатся.
/// </summary>
public static class AppPipelineHeavyGate
{
    private static int _active;

    public static int ActiveCount => System.Threading.Volatile.Read(ref _active);

    public static bool HasHeavyWork => ActiveCount > 0;

    public static void Enter() => System.Threading.Interlocked.Increment(ref _active);

    public static void Leave() => System.Threading.Interlocked.Decrement(ref _active);

    public static async Task WaitUntilIdleAsync(CancellationToken cancellationToken)
    {
        while (HasHeavyWork)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(120, cancellationToken).ConfigureAwait(true);
        }
    }
}
