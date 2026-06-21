using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookTrak.Hosting;

internal sealed record LockInfo(int Port, int ProcessId)
{
    public static LockInfo? TryRead()
    {
        if (!File.Exists(AppPaths.LockFile))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.LockFile);
            return JsonSerializer.Deserialize(json, LockInfoJsonContext.Default.LockInfo);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class InstanceLock
{
    /// <summary>
    /// True if the lockfile points at a live process that is actually BookTrak (not just a PID
    /// that happens to have been reused by an unrelated process).
    /// </summary>
    public static bool IsLive(LockInfo lockInfo)
    {
        try
        {
            var process = Process.GetProcessById(lockInfo.ProcessId);
            return string.Equals(process.ProcessName, "BookTrak", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // No process with that id.
            return false;
        }
    }

    public static void Write(int port, int processId)
    {
        var info = new LockInfo(port, processId);
        var json = JsonSerializer.Serialize(info, LockInfoJsonContext.Default.LockInfo);
        File.WriteAllText(AppPaths.LockFile, json);
    }

    public static void Delete()
    {
        try
        {
            File.Delete(AppPaths.LockFile);
        }
        catch (IOException)
        {
            // Best-effort cleanup; nothing actionable if this fails on shutdown.
        }
    }
}

[JsonSerializable(typeof(LockInfo))]
internal partial class LockInfoJsonContext : JsonSerializerContext
{
}
