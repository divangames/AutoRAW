using Microsoft.ML.OnnxRuntime;

namespace AutoRAW.Services.SubjectDetection;

/// <summary>Создание ONNX-сессий: DirectML (GPU) на Windows, иначе CPU.</summary>
public static class OnnxSessionFactory
{
    private static string? _activeProvider;

    public static string ActiveProvider => _activeProvider ?? "CPU";

    public static InferenceSession CreateSession(string modelPath)
    {
        Exception? last = null;
        foreach (var factory in ProviderFactories())
        {
            try
            {
                var opts = factory();
                var session = new InferenceSession(modelPath, opts);
                _activeProvider = factory.Method.Name switch
                {
                    nameof(CreateDirectMl) => "DirectML",
                    _ => "CPU"
                };
                return session;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("ONNX session failed");
    }

    private static IEnumerable<Func<SessionOptions>> ProviderFactories()
    {
        if (OperatingSystem.IsWindows())
            yield return CreateDirectMl;
        yield return CreateCpu;
    }

    private static SessionOptions CreateDirectMl()
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        opts.AppendExecutionProvider_DML(0);
        return opts;
    }

    private static SessionOptions CreateCpu()
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        opts.AppendExecutionProvider_CPU();
        return opts;
    }
}
