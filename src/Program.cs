using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private const string DefaultExe =
        @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool\b1_benchmark.exe";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var exe = GetArgument(args, "--exe") ?? DefaultExe;
        if (!File.Exists(exe))
        {
            Console.WriteLine($"Benchmark executable not found:\n{exe}");
            Console.WriteLine("Use: WukongBenchmarkRunner.exe --exe \"D:\\SteamLibrary\\steamapps\\common\\Black Myth Wukong Benchmark Tool\\b1_benchmark.exe\"");
            return 2;
        }

        var root = Directory.GetParent(exe)!.FullName;
        var ini = Path.Combine(root, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");

        if (!File.Exists(ini))
        {
            Console.WriteLine($"Configuration file not found:\n{ini}");
            return 3;
        }

        Console.WriteLine("Black Myth: Wukong Benchmark Runner");
        Console.WriteLine(new string('=', 44));
        Console.WriteLine($"EXE: {exe}");
        Console.WriteLine($"INI: {ini}");
        Console.WriteLine();

        var computer = HardwareInfo.Read();
        PrintHardware(computer);

        // One backup is kept for the entire run. It is restored in finally.
        var backup = ini + ".wukong-runner.bak";
        File.Copy(ini, backup, true);

        BenchmarkResult? cpu = null;
        BenchmarkResult? gpu = null;

        try
        {
            cpu = await RunPassAsync(
                "CPU",
                exe,
                ini,
                BenchmarkSettings.Cpu(),
                "LastCPUBenchmarkResult",
                TimeSpan.FromMinutes(10));

            gpu = await RunPassAsync(
                "GPU",
                exe,
                ini,
                BenchmarkSettings.Gpu(),
                "LastGPUBenchmarkResult",
                TimeSpan.FromMinutes(20));
        }
        finally
        {
            // Restore user's original settings, including their old benchmark values.
            File.Copy(backup, ini, true);
            File.Delete(backup);
        }

        var report = new BenchmarkReport
        {
            TimestampUtc = DateTime.UtcNow,
            Executable = exe,
            Computer = computer,
            CpuTest = cpu,
            GpuTest = gpu
        };

        var reportPath = Path.Combine(AppContext.BaseDirectory, "wukong-report.json");
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "wukong-report.html");

        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        await File.WriteAllTextAsync(htmlPath, HtmlReport.Render(report), Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine("Completed.");
        Console.WriteLine($"JSON : {reportPath}");
        Console.WriteLine($"HTML : {htmlPath}");
        Console.WriteLine();
        PrintResult(cpu);
        PrintResult(gpu);

        return 0;
    }

    private static async Task<BenchmarkResult> RunPassAsync(
        string name,
        string exe,
        string ini,
        BenchmarkSettings settings,
        string resultKey,
        TimeSpan timeout)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {name} TEST ---");
        Console.WriteLine(settings.Description);

        IniFile.Apply(ini, settings);
        IniFile.Set(ini, "LastCPUBenchmarkResult", "-1.000000");
        IniFile.Set(ini, "LastGPUBenchmarkResult", "-1.000000");

        var before = IniFile.Read(ini);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("Could not start b1_benchmark.exe.");

        // The standalone benchmark opens its main menu. The first menu item is
        // "Run Benchmark" / "Тест быстродействия" in the supplied screenshots.
        await WaitForMainWindowAsync(process, TimeSpan.FromSeconds(30));
        await Task.Delay(1500);

        WindowInput.BringToFront(process.MainWindowHandle);
        WindowInput.SendEnter();

        Console.WriteLine("Benchmark started; waiting for the result to be written to GameUserSettings.ini...");

        var sw = Stopwatch.StartNew();
        float? value = null;
        while (sw.Elapsed < timeout)
        {
            await Task.Delay(1000);

            var data = IniFile.Read(ini);
            if (data.TryGetValue(resultKey, out var raw) &&
                float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= 0)
            {
                value = parsed;
                break;
            }

            // If the game rewrites the file, keep waiting. We deliberately do
            // not infer completion from process exit because the result screen
            // can remain open after the benchmark.
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"{name} benchmark process exited before {resultKey} was written.");
        }

        if (value is null)
            throw new TimeoutException(
                $"{name} benchmark did not write {resultKey} within {timeout.TotalMinutes:0} minutes.");

        var after = IniFile.Read(ini);

        // Keep all metrics that the benchmark itself exposes in the INI.
        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[]
                 {
                     "LastCPUBenchmarkResult",
                     "LastGPUBenchmarkResult",
                     "LastGPUBenchmarkMultiplier",
                     "LastRecommendedScreenWidth",
                     "LastRecommendedScreenHeight"
                 })
        {
            if (after.TryGetValue(key, out var v))
                metrics[key] = v;
        }

        return new BenchmarkResult
        {
            Name = name,
            MetricKey = resultKey,
            MetricValue = value.Value,
            StartedUtc = DateTime.UtcNow - sw.Elapsed,
            FinishedUtc = DateTime.UtcNow,
            Settings = settings.ToDictionary(),
            Metrics = metrics
        };
    }

    private static async Task WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (process.HasExited)
                throw new InvalidOperationException("Benchmark process exited during startup.");

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
                return;

            await Task.Delay(250);
        }

        throw new TimeoutException("Benchmark window did not appear.");
    }

    private static void PrintHardware(ComputerInfo c)
    {
        Console.WriteLine("Computer:");
        Console.WriteLine($"  OS  : {c.Os}");
        Console.WriteLine($"  CPU : {c.Cpu}");
        Console.WriteLine($"  GPU : {c.Gpu}");
        Console.WriteLine($"  RAM : {c.RamGb:0.0} GB");
        Console.WriteLine($"  GPU driver: {c.GpuDriver}");
        Console.WriteLine();
    }

    private static void PrintResult(BenchmarkResult? r)
    {
        if (r is null) return;
        Console.WriteLine($"{r.Name}: {r.MetricKey} = {r.MetricValue:0.######}");
        foreach (var m in r.Metrics)
            Console.WriteLine($"  {m.Key} = {m.Value}");
    }

    private static string? GetArgument(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}

internal sealed record BenchmarkSettings
{
    public required string Description { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ResolutionQuality { get; init; }
    public int Quality { get; init; }
    public bool RayTracing { get; init; }

    public static BenchmarkSettings Cpu() => new()
    {
        Description = "1280x720, 100% resolution scale, all scalability groups Low, RT Off, VSync/FPS cap Off.",
        Width = 1280,
        Height = 720,
        ResolutionQuality = 100,
        Quality = 0,
        RayTracing = false
    };

    public static BenchmarkSettings Gpu() => new()
    {
        Description = "3840x2160, 100% resolution scale, all scalability groups Cinematic, RT On, VSync/FPS cap Off.",
        Width = 3840,
        Height = 2160,
        ResolutionQuality = 100,
        Quality = 4,
        RayTracing = true
    };

    public Dictionary<string, string> ToDictionary() => new()
    {
        ["Resolution"] = $"{Width}x{Height}",
        ["ResolutionQuality"] = ResolutionQuality.ToString(CultureInfo.InvariantCulture),
        ["ViewDistanceQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["AntiAliasingQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["ShadowQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["GlobalIlluminationQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["RayTracingQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["ReflectionQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["PostProcessQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["TextureQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["EffectsQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["FoliageQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["ShadingQuality"] = Quality.ToString(CultureInfo.InvariantCulture),
        ["RayTracing"] = RayTracing ? "On" : "Off",
        ["VSync"] = "Off",
        ["FrameRateLimit"] = "Unlimited"
    };
}

internal static class IniFile
{
    private static readonly string[] ScaleKeys =
    {
        "sg.ViewDistanceQuality",
        "sg.AntiAliasingQuality",
        "sg.ShadowQuality",
        "sg.GlobalIlluminationQuality",
        "sg.RayTracingQuality",
        "sg.ReflectionQuality",
        "sg.PostProcessQuality",
        "sg.TextureQuality",
        "sg.EffectsQuality",
        "sg.FoliageQuality",
        "sg.ShadingQuality"
    };

    public static void Apply(string path, BenchmarkSettings s)
    {
        Set(path, "ResolutionSizeX", s.Width.ToString(CultureInfo.InvariantCulture));
        Set(path, "ResolutionSizeY", s.Height.ToString(CultureInfo.InvariantCulture));
        Set(path, "LastUserConfirmedResolutionSizeX", s.Width.ToString(CultureInfo.InvariantCulture));
        Set(path, "LastUserConfirmedResolutionSizeY", s.Height.ToString(CultureInfo.InvariantCulture));
        Set(path, "DesiredScreenWidth", s.Width.ToString(CultureInfo.InvariantCulture));
        Set(path, "DesiredScreenHeight", s.Height.ToString(CultureInfo.InvariantCulture));

        Set(path, "FrameRateLimit", "0.000000");
        Set(path, "bUseVSync", "False");
        Set(path, "bUseDynamicResolution", "False");
        Set(path, "sg.ResolutionQuality", s.ResolutionQuality.ToString(CultureInfo.InvariantCulture));

        foreach (var key in ScaleKeys)
            Set(path, key, s.Quality.ToString(CultureInfo.InvariantCulture));

        Set(path, "r.RayTracing.EnableInGame", s.RayTracing ? "True" : "False");

        // These are useful if the file already contains an explicit renderer section.
        Set(path, "r.RayTracing", s.RayTracing ? "1" : "0");
    }

    public static Dictionary<string, string> Read(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('['))
                continue;

            var eq = t.IndexOf('=');
            if (eq <= 0) continue;

            result[t[..eq].Trim()] = t[(eq + 1)..].Trim();
        }
        return result;
    }

    public static void Set(string path, string key, string value)
    {
        var lines = File.ReadAllLines(path).ToList();
        var rx = new Regex(@"^(\s*)" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            if (rx.IsMatch(lines[i]))
            {
                var indent = Regex.Match(lines[i], @"^\s*").Value;
                lines[i] = indent + key + "=" + value;
                File.WriteAllLines(path, lines);
                return;
            }
        }

        // Put missing settings into the appropriate section.
        var section = key.StartsWith("sg.", StringComparison.OrdinalIgnoreCase)
            ? "[ScalabilityGroups]"
            : key.StartsWith("r.", StringComparison.OrdinalIgnoreCase)
                ? "[RayTracing]"
                : "[/Script/GSGameSettings.GSGameUserSettings]";

        var idx = lines.FindIndex(x => x.Trim().Equals(section, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            lines.Add("");
            lines.Add(section);
            lines.Add(key + "=" + value);
        }
        else
        {
            var insert = idx + 1;
            while (insert < lines.Count && !lines[insert].TrimStart().StartsWith("["))
                insert++;
            lines.Insert(insert, key + "=" + value);
        }

        File.WriteAllLines(path, lines);
    }
}

internal static class WindowInput
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int SW_RESTORE = 9;
    private const ushort KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN = 0x0D;

    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    public static void SendEnter()
    {
        var inputs = new[]
        {
            new INPUT
            {
                type = 1,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = VK_RETURN, dwFlags = 0 }
                }
            },
            new INPUT
            {
                type = 1,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = VK_RETURN, dwFlags = KEYEVENTF_KEYUP }
                }
            }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}

internal sealed record ComputerInfo
{
    public string Os { get; init; } = "";
    public string Cpu { get; init; } = "";
    public string Gpu { get; init; } = "";
    public string GpuDriver { get; init; } = "";
    public double RamGb { get; init; }

    public static ComputerInfo Read()
    {
        string Query(string q, string field)
        {
            try
            {
                using var s = new ManagementObjectSearcher(q);
                foreach (ManagementObject o in s.Get())
                    return o[field]?.ToString() ?? "Unknown";
            }
            catch { }
            return "Unknown";
        }

        var cpu = Query("SELECT Name FROM Win32_Processor", "Name");
        var gpu = Query("SELECT Name FROM Win32_VideoController", "Name");
        var driver = Query("SELECT DriverVersion FROM Win32_VideoController", "DriverVersion");
        var os = Query("SELECT Caption FROM Win32_OperatingSystem", "Caption");

        double ram = 0;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject o in s.Get())
            {
                if (double.TryParse(o["TotalPhysicalMemory"]?.ToString(), out var bytes))
                    ram = bytes / 1024 / 1024 / 1024;
                break;
            }
        }
        catch { }

        return new ComputerInfo { Cpu = cpu, Gpu = gpu, GpuDriver = driver, Os = os, RamGb = ram };
    }
}

internal sealed record BenchmarkReport
{
    public DateTime TimestampUtc { get; init; }
    public string Executable { get; init; } = "";
    public ComputerInfo Computer { get; init; } = new();
    public BenchmarkResult? CpuTest { get; init; }
    public BenchmarkResult? GpuTest { get; init; }
}

internal sealed record BenchmarkResult
{
    public string Name { get; init; } = "";
    public string MetricKey { get; init; } = "";
    public double MetricValue { get; init; }
    public DateTime StartedUtc { get; init; }
    public DateTime FinishedUtc { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
    public Dictionary<string, string> Metrics { get; init; } = new();
}

internal static class HtmlReport
{
    public static string Render(BenchmarkReport r)
    {
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        static string Table(Dictionary<string, string> d) =>
            string.Join("", d.Select(x => $"<tr><td>{E(x.Key)}</td><td>{E(x.Value)}</td></tr>"));

        string Result(BenchmarkResult? x)
        {
            if (x is null) return "<p>Not completed.</p>";
            return $"""
            <h3>{E(x.Name)} test</h3>
            <p class="metric">{E(x.MetricKey)} = <b>{x.MetricValue:0.######}</b></p>
            <h4>Settings</h4>
            <table>{Table(x.Settings)}</table>
            <h4>Benchmark metrics written by the game</h4>
            <table>{Table(x.Metrics)}</table>
            """;
        }

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Black Myth: Wukong Benchmark Report</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;max-width:1050px;margin:40px auto;padding:0 24px;background:#111;color:#eee}
h1{font-size:28px} h2{margin-top:36px} h3{margin-top:26px}
.card{background:#1b1b1b;border:1px solid #333;border-radius:10px;padding:20px;margin:18px 0}
table{border-collapse:collapse;width:100%}td{border-bottom:1px solid #333;padding:8px}td:first-child{width:38%;color:#aaa}
.metric{font-size:20px;background:#242424;padding:14px;border-radius:8px}
small{color:#999}
</style>
</head>
<body>
<h1>Black Myth: Wukong Benchmark Report</h1>
<p><small>Generated {{E(r.TimestampUtc.ToString("u"))}} UTC</small></p>

<div class="card">
<h2>Computer</h2>
<table>
<tr><td>OS</td><td>{{E(r.Computer.Os)}}</td></tr>
<tr><td>CPU</td><td>{{E(r.Computer.Cpu)}}</td></tr>
<tr><td>GPU</td><td>{{E(r.Computer.Gpu)}}</td></tr>
<tr><td>GPU driver</td><td>{{E(r.Computer.GpuDriver)}}</td></tr>
<tr><td>RAM</td><td>{{r.Computer.RamGb:0.0}} GB</td></tr>
</table>
</div>

<div class="card">
<h2>CPU test</h2>
{{Result(r.CpuTest)}}
</div>

<div class="card">
<h2>GPU test</h2>
{{Result(r.GpuTest)}}
</div>
</body>
</html>
""";
    }
}

