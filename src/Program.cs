using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration.Json; 
using Microsoft.Extensions.Configuration;

class Program
{
    static void Main()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config/appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var (cpu, gpu, ram) = SystemInfoProvider.GetInfo();
        Console.WriteLine($"System: CPU={cpu}, GPU={gpu}, RAM={ram} GB");

        var cpuArgs = config.GetSection("Tests:Cpu:Arguments").Value;
        var gpuArgs = config.GetSection("Tests:Gpu:Arguments").Value;
        var exePath = config.GetSection("Benchmark:Path").Value;
        var timeout = config.GetValue<int>("Benchmark:TimeoutSeconds");

        Console.WriteLine("Running CPU test...");
        BenchmarkRunner.Run(exePath, cpuArgs, timeout);
        var cpuRes = ResultParser.Parse(config.GetSection("Benchmark:ResultFile").Value);

        Console.WriteLine("Running GPU test...");
        BenchmarkRunner.Run(exePath, gpuArgs, timeout);
        var gpuRes = ResultParser.Parse(config.GetSection("Benchmark:ResultFile").Value);

        Console.WriteLine($"CPU Test: Avg={cpuRes.AvgFps}, Min={cpuRes.MinFps}, Max={cpuRes.MaxFps}");
        Console.WriteLine($"GPU Test: Avg={gpuRes.AvgFps}, Min={gpuRes.MinFps}, Max={gpuRes.MaxFps}");
    }
}

