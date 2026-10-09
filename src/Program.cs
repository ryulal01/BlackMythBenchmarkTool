
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string BenchmarkDirectory =
        @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool";

    private const string LauncherPath =
        BenchmarkDirectory + @"\b1_benchmark.exe";

    private const string GamePath =
        BenchmarkDirectory + @"\b1\Binaries\Win64\b1-Win64-Shipping.exe";

    private const string IniPath =
        BenchmarkDirectory + @"\b1\Saved\Config\Windows\GameUserSettings.ini";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan BenchmarkTimeout = TimeSpan.FromMinutes(15);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("Black Myth: Wukong Benchmark Runner");
        Console.WriteLine("===================================\n");

        string launcher = args.Length >= 2 && args[0] == "--exe"
            ? Path.GetFullPath(args[1])
            : LauncherPath;

        string directory = Path.GetDirectoryName(launcher)!;
        string ini = Path.Combine(
            directory, @"b1\Saved\Config\Windows\GameUserSettings.ini");

        if (!File.Exists(launcher))
        {
            Console.Error.WriteLine($"Launcher not found: {launcher}");
            return 1;
        }

        if (!File.Exists(GamePath))
        {
            Console.Error.WriteLine($"Benchmark executable not found: {GamePath}");
            return 1;
        }

        if (!File.Exists(ini))
        {
            Console.Error.WriteLine($"Configuration file not found: {ini}");
            Console.Error.WriteLine(
                "Launch the Benchmark Tool manually once, then try again.");
            return 1;
        }

        Console.WriteLine($"Launcher: {launcher}");
        Console.WriteLine($"Game:     {GamePath}");
        Console.WriteLine($"INI:      {ini}\n");

        Console.WriteLine("Computer:");
        Console.WriteLine($"OS : {GetPowerShellValue(
            "(Get-CimInstance Win32_OperatingSystem).Caption")}");
        Console.WriteLine($"CPU: {GetPowerShellValue(
            "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)")}");
        Console.WriteLine($"GPU: {GetPowerShellValue(
            "(Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty Name)")}");
        Console.WriteLine($"RAM: {GetPowerShellValue(
            "[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1).ToString() + ' GB'")}");
        Console.WriteLine($"GPU driver: {GetPowerShellValue(
            "(Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty DriverVersion)")}");
        Console.WriteLine();

        string backupPath = ini + ".wukong-backup";
        string originalIni = File.ReadAllText(ini);

        if (!File.Exists(backupPath))
            File.Copy(ini, backupPath);

        var results = new List<PassResult>();

        try
        {
            var cpuSettings = new Dictionary<string, string>
            {
                ["ResolutionSizeX"] = "1280",
                ["ResolutionSizeY"] = "720",
                ["LastUserConfirmedResolutionSizeX"] = "1280",
                ["LastUserConfirmedResolutionSizeY"] = "720",
                ["sg.ResolutionQuality"] = "100.000000",
                ["sg.ViewDistanceQuality"] = "0",
                ["sg.AntiAliasingQuality"] = "0",
                ["sg.ShadowQuality"] = "0",
                ["sg.PostProcessQuality"] = "0",
                ["sg.TextureQuality"] = "0",
                ["sg.EffectsQuality"] = "0",
                ["sg.FoliageQuality"] = "0",
                ["sg.ShadingQuality"] = "0",
                ["bUseVSync"] = "False",
                ["FrameRateLimit"] = "0.000000"
            };

            Console.WriteLine("--- CPU TEST ---");
            Console.WriteLine(
                "Target: 1280x720, Low graphics, 100% resolution scale, VSync off.");
            Console.WriteLine(
                "Note: ray-tracing settings can vary by Benchmark Tool version.");

            var cpuResult = await RunPassAsync(
                "CPU",
                launcher,
                ini,
                cpuSettings,
                "LastCPUBenchmarkResult");

            results.Add(cpuResult);

            Console.WriteLine("\n--- GPU TEST ---");

            var gpuSettings = new Dictionary<string, string>
            {
                ["ResolutionSizeX"] = "3840",
                ["ResolutionSizeY"] = "2160",
                ["LastUserConfirmedResolutionSizeX"] = "3840",
                ["LastUserConfirmedResolutionSizeY"] = "2160",
                ["sg.ResolutionQuality"] = "100.000000",
                ["sg.ViewDistanceQuality"] = "4",
                ["sg.AntiAliasingQuality"] = "4",
                ["sg.ShadowQuality"] = "4",
                ["sg.PostProcessQuality"] = "4",
                ["sg.TextureQuality"] = "4",
                ["sg.EffectsQuality"] = "4",
                ["sg.FoliageQuality"] = "4",
                ["sg.ShadingQuality"] = "4",
                ["bUseVSync"] = "False",
                ["FrameRateLimit"] = "0.000000"
            };

            Console.WriteLine(
                "Target: 3840x2160, Cinematic graphics, 100% resolution scale.");

            var gpuResult = await RunPassAsync(
                "GPU",
                launcher,
                ini,
                gpuSettings,
                "LastGPUBenchmarkResult");

            results.Add(gpuResult);

            var report = new
            {
                GeneratedAt = DateTimeOffset.Now,
                Computer = new
                {
                    OS = GetPowerShellValue(
                        "(Get-CimInstance Win32_OperatingSystem).Caption"),
                    CPU = GetPowerShellValue(
                        "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)"),
                    GPU = GetPowerShellValue(
                        "(Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty Name)"),
                    RAM = GetPowerShellValue(
                        "[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1).ToString() + ' GB'"),
                    GPUDriver = GetPowerShellValue(
                        "(Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty DriverVersion)")
                },
                Results = results
            };

            string jsonPath = Path.Combine(
                AppContext.BaseDirectory, "wukong-report.json");

            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));

            string htmlPath = Path.Combine(
                AppContext.BaseDirectory, "wukong-report.html");

            File.WriteAllText(htmlPath, BuildHtml(report.Computer, results));

            Console.WriteLine("\nBoth passes finished.");
            Console.WriteLine($"JSON report: {jsonPath}");
            Console.WriteLine($"HTML report: {htmlPath}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nERROR: {ex.Message}");
            return 1;
        }
        finally
        {
            CloseBenchmarkProcesses();

            try
            {
                File.WriteAllText(ini, originalIni);
                Console.WriteLine("Original GameUserSettings.ini restored.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"WARNING: Could not restore INI: {ex.Message}");
                Console.Error.WriteLine($"Backup: {backupPath}");
            }
        }
    }

    private static async Task<PassResult> RunPassAsync(
        string name,
        string launcher,
        string ini,
        Dictionary<string, string> settings,
        string resultKey)
    {
        CloseBenchmarkProcesses();

        string before = File.ReadAllText(ini);
        string? previousResult = GetIniValue(before, resultKey);

        File.WriteAllText(ini, ApplyIniSettings(before, settings));

        Console.WriteLine("Settings written to GameUserSettings.ini.");
        Console.WriteLine("Starting Benchmark Tool...");

        Process.Start(new ProcessStartInfo
        {
            FileName = launcher,
            WorkingDirectory = Path.GetDirectoryName(launcher)!,
            UseShellExecute = true
        });

        using Process benchmark = await WaitForBenchmarkWindowAsync(
            StartupTimeout);

        Console.WriteLine(
            $"Benchmark window found. PID: {benchmark.Id}");

        await Task.Delay(1500);

        WindowInput.BringToFront(benchmark.MainWindowHandle);
        await Task.Delay(500);
        WindowInput.SendEnter();

        Console.WriteLine(
            "Enter sent. Waiting for the benchmark result to update...");

        var timer = Stopwatch.StartNew();

        while (timer.Elapsed < BenchmarkTimeout)
        {
            await Task.Delay(2000);

            string currentIni;

            try
            {
                currentIni = File.ReadAllText(ini);
            }
            catch (IOException)
            {
                continue;
            }

            string? currentResult = GetIniValue(currentIni, resultKey);

            if (!string.IsNullOrWhiteSpace(currentResult) &&
                currentResult != previousResult)
            {
                Console.WriteLine($"{resultKey} = {currentResult}");

                return new PassResult(
                    name,
                    resultKey,
                    currentResult,
                    settings);
            }

            if (benchmark.HasExited)
            {
                // Give the game a short opportunity to flush its result.
                await Task.Delay(1500);
                currentIni = File.ReadAllText(ini);
                currentResult = GetIniValue(currentIni, resultKey);

                if (!string.IsNullOrWhiteSpace(currentResult) &&
                    currentResult != previousResult)
                {
                    return new PassResult(
                        name, resultKey, currentResult, settings);
                }

                throw new InvalidOperationException(
                    $"{name} benchmark process exited before {resultKey} changed. " +
                    "The test may not have started, or this version may store results elsewhere.");
            }
        }

        throw new TimeoutException(
            $"{name} benchmark result was not updated within {BenchmarkTimeout.TotalMinutes} minutes. " +
            "Check whether the benchmark actually started and whether this INI contains the expected result key.");
    }

    private static async Task<Process> WaitForBenchmarkWindowAsync(
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();

        while (timer.Elapsed < timeout)
        {
            foreach (Process process in Process.GetProcessesByName(
                         "b1-Win64-Shipping"))
            {
                try
                {
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    process.Refresh();

                    string? path = null;

                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch
                    {
                        // Windows may deny access while the process starts.
                    }

                    if (path is not null &&
                        string.Equals(
                            Path.GetFullPath(path),
                            Path.GetFullPath(GamePath),
                            StringComparison.OrdinalIgnoreCase) &&
                        process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process;
                    }

                    process.Dispose();
                }
                catch
                {
                    process.Dispose();
                }
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            "The main window of b1-Win64-Shipping.exe did not appear. " +
            "Try launching the Benchmark Tool manually and check for startup errors.");
    }

    private static void CloseBenchmarkProcesses()
    {
        foreach (Process process in Process.GetProcessesByName(
                     "b1-Win64-Shipping"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();

                    if (!process.WaitForExit(3000))
                        process.Kill(true);
                }
            }
            catch
            {
                // Ignore processes that already exited.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string ApplyIniSettings(
        string ini,
        Dictionary<string, string> settings)
    {
        const string section = "[/Script/Engine.GameUserSettings]";

        var lines = ini.Replace("\r\n", "\n").Split('\n').ToList();

        int sectionIndex = lines.FindIndex(
            line => line.Trim().Equals(
                section, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            lines.Add(section);
            sectionIndex = lines.Count - 1;
        }

        int endIndex = lines.Count;

        for (int i = sectionIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith('['))
            {
                endIndex = i;
                break;
            }
        }

        foreach (var setting in settings)
        {
            bool replaced = false;

            for (int i = sectionIndex + 1; i < endIndex; i++)
            {
                string trimmed = lines[i].Trim();

                if (trimmed.StartsWith(
                        setting.Key + "=",
                        StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{setting.Key}={setting.Value}";
                    replaced = true;
                }
            }

            if (!replaced)
            {
                lines.Insert(endIndex, $"{setting.Key}={setting.Value}");
                endIndex++;
            }
        }

        return string.Join("\r\n", lines);
    }

    private static string? GetIniValue(string ini, string key)
    {
        foreach (string line in ini.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith(
                    key + "=",
                    StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(trimmed.IndexOf('=') + 1)..].Trim();
            }
        }

        return null;
    }

    private static string GetPowerShellValue(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                            command.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
                return "Unknown";

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return string.IsNullOrWhiteSpace(output) ? "Unknown" : output;
        }
        catch
        {
            return "Unknown";
        }
    }
    
    private static string BuildHtml(
        object computer,
        List<PassResult> results)
    {
        string json = JsonSerializer.Serialize(
            new { Computer = computer, Results = results },
            new JsonSerializerOptions { WriteIndented = true });
    
        string escaped = System.Net.WebUtility.HtmlEncode(json);
    
        var html = new StringBuilder();
    
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\">");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("  <title>Black Myth: Wukong Benchmark Report</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    body { font: 16px/1.5 system-ui, sans-serif; max-width: 1000px; margin: 40px auto; padding: 0 20px; }");
        html.AppendLine("    pre { white-space: pre-wrap; overflow-wrap: anywhere; background: #f3f4f6; padding: 20px; border-radius: 8px; }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("  <h1>Black Myth: Wukong Benchmark Report</h1>");
        html.AppendLine("  <p>Benchmark results and computer information:</p>");
        html.AppendLine("  <pre>");
        html.AppendLine(escaped);
        html.AppendLine("  </pre>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
    
        return html.ToString();
    }
    private sealed record PassResult(
        string Test,
        string ResultKey,
        string ResultValue,
        Dictionary<string, string> Settings);

    private static class WindowInput
    {
        private const int SW_RESTORE = 9;
        private const byte VK_RETURN = 0x0D;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern void keybd_event(
            byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public static void BringToFront(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Benchmark window handle is empty.");

            ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
        }

        public static void SendEnter()
        {
            keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
            keybd_event(
                VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
