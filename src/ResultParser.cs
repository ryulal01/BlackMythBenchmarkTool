using System.Text.Json;

public static class ResultParser
{
    public static (double AvgFps, double MinFps, double MaxFps) Parse(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var avg = root.TryGetProperty("average_fps", out var avgProp) ? avgProp.GetDouble() : 0;
        var min = root.TryGetProperty("min_fps", out var minProp) ? minProp.GetDouble() : 0;
        var max = root.TryGetProperty("max_fps", out var maxProp) ? maxProp.GetDouble() : 0;

        return (avg, min, max);
    }
}

