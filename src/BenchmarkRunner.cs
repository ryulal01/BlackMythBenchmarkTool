using System.Diagnostics;

public static class BenchmarkRunner
{
    public static void Run(string exePath, string arguments, int timeoutSeconds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill();
            throw new TimeoutException("Benchmark did not finish in time.");
        }

        if (process.ExitCode != 0)
        {
            throw new Exception($"Benchmark failed with exit code {process.ExitCode}");
        }
    }
}

