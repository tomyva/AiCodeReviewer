using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiCodeReviewer.Application;

public enum ExportFormat
{
    Json,
    Markdown
}

public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed record UserPreferences(
    ReviewMode DefaultMode = ReviewMode.File,
    int MaximumFiles = 50,
    long MaximumCharacters = 250_000,
    ExportFormat ExportFormat = ExportFormat.Markdown,
    AppTheme Theme = AppTheme.System);

public sealed record RecentReviewEntry(
    Guid Id,
    DateTimeOffset CompletedAt,
    ReviewMode Mode,
    int FindingCount,
    string? ReportPath);

public interface IUserStateStore
{
    UserPreferences LoadPreferences();

    void SavePreferences(UserPreferences preferences);

    IReadOnlyList<RecentReviewEntry> LoadRecentReviews();

    void AddRecentReview(RecentReviewEntry entry);
}

public sealed class JsonUserStateStore : IUserStateStore
{
    private const int MaximumHistoryEntries = 20;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _preferencesPath;
    private readonly string _historyPath;

    public JsonUserStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _preferencesPath = Path.Combine(directory, "preferences.json");
        _historyPath = Path.Combine(directory, "recent-reviews.json");
    }

    public static IUserStateStore CreateDefault()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        try
        {
            return new JsonUserStateStore(Path.Combine(root, "AiCodeReviewer"));
        }
        catch (UnauthorizedAccessException)
        {
            return new MemoryUserStateStore();
        }
        catch (IOException)
        {
            return new MemoryUserStateStore();
        }
    }

    public UserPreferences LoadPreferences() => Read(_preferencesPath, new UserPreferences());

    public void SavePreferences(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.MaximumFiles <= 0 || preferences.MaximumCharacters <= 0)
        {
            throw new ArgumentException("Preference limits must be positive integers.");
        }

        Write(_preferencesPath, preferences);
    }

    public IReadOnlyList<RecentReviewEntry> LoadRecentReviews() =>
        Read<IReadOnlyList<RecentReviewEntry>>(_historyPath, []);

    public void AddRecentReview(RecentReviewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var history = LoadRecentReviews()
            .Where(item => item.Id != entry.Id)
            .Prepend(entry)
            .OrderByDescending(item => item.CompletedAt)
            .Take(MaximumHistoryEntries)
            .ToArray();
        Write(_historyPath, history);
    }

    private static T Read<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    private static void Write<T>(string path, T value)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, SerializerOptions));
        File.Move(temporaryPath, path, true);
    }
}

public sealed class MemoryUserStateStore : IUserStateStore
{
    private UserPreferences _preferences = new();
    private readonly List<RecentReviewEntry> _history = [];

    public UserPreferences LoadPreferences() => _preferences;

    public void SavePreferences(UserPreferences preferences) =>
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

    public IReadOnlyList<RecentReviewEntry> LoadRecentReviews() => _history.ToArray();

    public void AddRecentReview(RecentReviewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _history.Insert(0, entry);
        if (_history.Count > 20)
        {
            _history.RemoveRange(20, _history.Count - 20);
        }
    }
}
