using AiCodeReviewer.Application;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Blazor.Services;

public sealed class ReviewPageModel : IDisposable
{
    private readonly ICodeReviewerApplication _application;
    private readonly IUserStateStore? _userState;
    private CancellationTokenSource? _cancellation;
    private string _searchText = string.Empty;
    private ReviewSeverity? _severityFilter;
    private FindingSort _findingSort;

    public ReviewPageModel(ICodeReviewerApplication application, IUserStateStore? userState = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _userState = userState;
        Configuration = application.GetConfigurationStatus();
        Preferences = userState?.LoadPreferences() ?? new UserPreferences();
        Mode = Preferences.DefaultMode;
        MaximumFiles = Preferences.MaximumFiles;
        MaximumCharacters = Preferences.MaximumCharacters;
        PreferredExportFormat = Preferences.ExportFormat;
        Theme = Preferences.Theme;
        RecentReviews = userState?.LoadRecentReviews() ?? [];
    }

    public event EventHandler? Changed;

    public ReviewMode Mode { get; set; }

    public string Path { get; set; } = string.Empty;

    public string RemoteUrl { get; set; } = string.Empty;

    public string GitHubOwner { get; set; } = string.Empty;

    public string GitHubRepository { get; set; } = string.Empty;

    public int PullRequestNumber { get; set; }

    public int MaximumFiles { get; set; } = 50;

    public long MaximumCharacters { get; set; } = 250_000;

    public bool IsBusy { get; private set; }

    public bool IsConfirmingPublish { get; set; }

    public string Status { get; private set; } = "Ready";

    public int ProgressPercent { get; private set; }

    public ApplicationError? Error { get; private set; }

    public ReviewReport? Report { get; private set; }

    public ConfigurationStatus Configuration { get; }

    public UserPreferences Preferences { get; private set; }

    public IReadOnlyList<RecentReviewEntry> RecentReviews { get; private set; }

    public ExportFormat PreferredExportFormat { get; set; }

    public AppTheme Theme { get; set; }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnChanged(); }
    }

    public ReviewSeverity? SeverityFilter
    {
        get => _severityFilter;
        set { _severityFilter = value; OnChanged(); }
    }

    public FindingSort FindingSort
    {
        get => _findingSort;
        set { _findingSort = value; OnChanged(); }
    }

    public IReadOnlyList<ReviewFinding> FilteredFindings => Report is null
        ? []
        : ReviewFindingQuery.Apply(Report.Findings, new FindingQuery(SearchText, SeverityFilter, Sort: FindingSort));

    public IReadOnlyList<ReviewFinding> VisibleFindings => FilteredFindings.Take(200).ToArray();

    public bool HasHiddenFindings => FilteredFindings.Count > VisibleFindings.Count;

    public string ThemeClass => $"theme-{Theme.ToString().ToLowerInvariant()}";

    public bool UsesPath => Mode is ReviewMode.File or ReviewMode.LocalRepository or ReviewMode.RagRepository or ReviewMode.AgentRepository or ReviewMode.Specialists;

    public bool UsesRepositoryLimits => Mode is ReviewMode.LocalRepository or ReviewMode.RemoteRepository or ReviewMode.RagRepository;

    public bool UsesRemoteUrl => Mode == ReviewMode.RemoteRepository;

    public bool UsesPullRequest => Mode == ReviewMode.GitHubPullRequest;

    public string PathLabel => Mode is ReviewMode.File or ReviewMode.Specialists ? "Source file path" : "Local repository path";

    public string JsonDownloadUrl => Report is null ? string.Empty : CreateDataUrl(_application.ExportJson(Report), "application/json");

    public string MarkdownDownloadUrl => Report is null ? string.Empty : CreateDataUrl(_application.ExportMarkdown(Report), "text/markdown");

    public async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        Report = null;
        IsConfirmingPublish = false;
        Status = "Starting review";
        ProgressPercent = 0;
        _cancellation = new CancellationTokenSource();
        OnChanged();

        var progress = new Progress<ReviewProgress>(update =>
        {
            Status = update.Message;
            ProgressPercent = update.Percent ?? ProgressPercent;
            OnChanged();
        });
        var result = await _application.ReviewAsync(CreateRequest(), progress, _cancellation.Token);
        Report = result.Value;
        Error = result.Error;
        Status = result.IsSuccess ? "Review completed" : result.Error!.Message;
        ProgressPercent = result.IsSuccess ? 100 : ProgressPercent;
        IsBusy = false;
        _cancellation.Dispose();
        _cancellation = null;
        RecentReviews = _userState?.LoadRecentReviews() ?? RecentReviews;
        OnChanged();
    }

    public void Cancel() => _cancellation?.Cancel();

    public async Task PublishAsync()
    {
        if (Report?.PublicationDraft is null || !IsConfirmingPublish)
        {
            Error = new ApplicationError(ApplicationErrorCode.Validation, "Review the drafts and select the confirmation checkbox before publishing.");
            OnChanged();
            return;
        }

        IsBusy = true;
        Status = "Publishing approved comments";
        OnChanged();
        var result = await _application.PublishPullRequestReviewAsync(new PublishReviewRequest(Report.PublicationDraft, true));
        Error = result.Error;
        Status = result.IsSuccess ? "Published the approved GitHub review" : result.Error!.Message;
        IsBusy = false;
        IsConfirmingPublish = false;
        OnChanged();
    }

    public void Dispose() => _cancellation?.Dispose();

    public void SavePreferences()
    {
        Preferences = new UserPreferences(Mode, MaximumFiles, MaximumCharacters, PreferredExportFormat, Theme);
        _userState?.SavePreferences(Preferences);
        Status = "Preferences saved";
        OnChanged();
    }

    private ReviewRequest CreateRequest() => new(
        Mode,
        string.IsNullOrWhiteSpace(Path) ? null : Path,
        string.IsNullOrWhiteSpace(RemoteUrl) ? null : RemoteUrl,
        string.IsNullOrWhiteSpace(GitHubOwner) ? null : GitHubOwner,
        string.IsNullOrWhiteSpace(GitHubRepository) ? null : GitHubRepository,
        PullRequestNumber,
        MaximumFiles,
        MaximumCharacters);

    private static string CreateDataUrl(string content, string mediaType) =>
        $"data:{mediaType};charset=utf-8,{Uri.EscapeDataString(content)}";

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
