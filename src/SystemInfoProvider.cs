using System.Management;

public static class SystemInfoProvider
{
    public static (string Cpu, string Gpu, long RamGb) GetInfo()
    {
        var cpu = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor")
            .Get().Cast<ManagementObject>().FirstOrDefault()?["Name"]?.ToString() ?? "Unknown";

        var gpu = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController")
            .Get().Cast<ManagementObject>().FirstOrDefault()?["Name"]?.ToString() ?? "Unknown";

        var ram = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory")
            .Get().Cast<ManagementObject>().Sum(mo => Convert.ToInt64(mo["Capacity"])) / (1024 * 1024 * 1024);

        return (cpu, gpu, ram);
    }
}

