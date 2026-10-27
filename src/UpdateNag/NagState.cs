using System.Text.Json;

namespace UpdateNag;

/// <summary>Result of one Windows Update check.</summary>
public sealed record CheckResult(int UpdateCount, List<string> Titles, bool RebootPending)
{
    public bool AnythingPending => UpdateCount > 0 || RebootPending;
}

/// <summary>State persisted to %ProgramData%\UpdateNag\state.json so restarts don't re-nag or skip a check.</summary>
public sealed class NagState
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UpdateNag");

    private static string FilePath => Path.Combine(DataDirectory, "state.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DateTimeOffset? LastCheckUtc { get; set; }
    public CheckResult? LastResult { get; set; }

    /// <summary>Lower-cased DOMAIN\user → local date (yyyy-MM-dd) they were last nagged.</summary>
    public Dictionary<string, string> NaggedOn { get; set; } = new();

    public static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public bool WasNaggedToday(string user) =>
        NaggedOn.TryGetValue(user.ToLowerInvariant(), out var date) && date == Today;

    public void MarkNagged(string user) => NaggedOn[user.ToLowerInvariant()] = Today;

    public static NagState Load()
    {
        if (!File.Exists(FilePath))
            return new NagState();
        return JsonSerializer.Deserialize<NagState>(File.ReadAllText(FilePath), JsonOptions) ?? new NagState();
    }

    public void Save()
    {
        // Only today's entries matter; drop the rest so the file stays small.
        NaggedOn = NaggedOn.Where(kv => kv.Value == Today).ToDictionary(kv => kv.Key, kv => kv.Value);

        Directory.CreateDirectory(DataDirectory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
