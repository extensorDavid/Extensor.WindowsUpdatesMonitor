using System.Text.Json;

namespace Extensor.WindowsUpdatesMonitor;

/// <summary>Result of one Windows Update check.</summary>
public sealed record CheckResult(int UpdateCount, List<string> Titles, bool RebootPending)
{
    public bool AnythingPending => UpdateCount > 0 || RebootPending;
}

/// <summary>State persisted to %ProgramData%\Extensor.WindowsUpdatesMonitor\state.json so restarts don't notify twice or skip a check.</summary>
public sealed class MonitorState
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Extensor.WindowsUpdatesMonitor");

    private static string FilePath => Path.Combine(DataDirectory, "state.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DateTimeOffset? LastCheckUtc { get; set; }
    public CheckResult? LastResult { get; set; }

    /// <summary>Lower-cased DOMAIN\user → local date (yyyy-MM-dd) they were last notified.</summary>
    public Dictionary<string, string> NotifiedOn { get; set; } = new();

    public static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public bool WasNotifiedToday(string user) =>
        NotifiedOn.TryGetValue(user.ToLowerInvariant(), out var date) && date == Today;

    public void MarkNotified(string user) => NotifiedOn[user.ToLowerInvariant()] = Today;

    public static MonitorState Load()
    {
        if (!File.Exists(FilePath))
            return new MonitorState();
        return JsonSerializer.Deserialize<MonitorState>(File.ReadAllText(FilePath), JsonOptions) ?? new MonitorState();
    }

    public void Save()
    {
        // Only today's entries matter; drop the rest so the file stays small.
        NotifiedOn = NotifiedOn.Where(kv => kv.Value == Today).ToDictionary(kv => kv.Key, kv => kv.Value);

        Directory.CreateDirectory(DataDirectory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
