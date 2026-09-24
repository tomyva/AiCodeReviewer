using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AiCodeReviewer.Application;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Wpf;

public sealed class DesktopReviewViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICodeReviewerApplication _application;
    private readonly IDesktopDialogService _dialogs;
    private readonly IUserStateStore? _userState;
    private CancellationTokenSource? _cancellation;
    private ReviewMode _mode;
    private string _path = string.Empty;
    private string _remoteUrl = string.Empty;
    private string _gitHubOwner = string.Empty;
    private string _gitHubRepository = string.Empty;
    private int _pullRequestNumber;
    private int _maximumFiles = 50;
    private long _maximumCharacters = 250_000;
    private bool _isBusy;
    private bool _publishConfirmed;
    private string _status = "Ready";
    private int _progressPercent;
    private ApplicationError? _error;
    private ReviewReport? _report;
    private string _searchText = string.Empty;
    private ReviewSeverity? _severityFilter;
    private FindingSort _findingSort;

    public DesktopReviewViewModel(ICodeReviewerApplication application, IDesktopDialogService dialogs, IUserStateStore? userState = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _userState = userState;
        Configuration = application.GetConfigurationStatus();
        Preferences = userState?.LoadPreferences() ?? new UserPreferences();
        _mode = Preferences.DefaultMode;
        _maximumFiles = Preferences.MaximumFiles;
        _maximumCharacters = Preferences.MaximumCharacters;
        PreferredExportFormat = Preferences.ExportFormat;
        Theme = Preferences.Theme;
        RecentReviews = userState?.LoadRecentReviews() ?? [];
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy && UsesPath);
        ExportJsonCommand = new AsyncRelayCommand(() => ExportAsync(true), () => Report is not null && !IsBusy);
        ExportMarkdownCommand = new AsyncRelayCommand(() => ExportAsync(false), () => Report is not null && !IsBusy);
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => Report?.PublicationDraft is not null && PublishConfirmed && !IsBusy);
        SavePreferencesCommand = new RelayCommand(SavePreferences);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ReviewMode> Modes { get; } = Enum.GetValues<ReviewMode>();

    public ReviewMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                OnPropertyChanged(nameof(UsesPath));
                OnPropertyChanged(nameof(UsesRemoteUrl));
                OnPropertyChanged(nameof(UsesPullRequest));
                OnPropertyChanged(nameof(UsesRepositoryLimits));
                OnPropertyChanged(nameof(PathLabel));
                RefreshCommands();
            }
        }
    }

    public string Path { get => _path; set => Set(ref _path, value); }

    public string RemoteUrl { get => _remoteUrl; set => Set(ref _remoteUrl, value); }

    public string GitHubOwner { get => _gitHubOwner; set => Set(ref _gitHubOwner, value); }

    public string GitHubRepository { get => _gitHubRepository; set => Set(ref _gitHubRepository, value); }

    public int PullRequestNumber { get => _pullRequestNumber; set => Set(ref _pullRequestNumber, value); }

    public int MaximumFiles { get => _maximumFiles; set => Set(ref _maximumFiles, value); }

    public long MaximumCharacters { get => _maximumCharacters; set => Set(ref _maximumCharacters, value); }

    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) RefreshCommands(); } }

    public bool PublishConfirmed { get => _publishConfirmed; set { if (Set(ref _publishConfirmed, value)) RefreshCommands(); } }

    public string Status { get => _status; private set => Set(ref _status, value); }

    public int ProgressPercent { get => _progressPercent; private set => Set(ref _progressPercent, value); }

    public ApplicationError? Error { get => _error; private set => Set(ref _error, value); }

    public ReviewReport? Report
    {
        get => _report;
        private set
        {
            if (Set(ref _report, value))
            {
                OnPropertyChanged(nameof(HasReport));
                OnPropertyChanged(nameof(HasPublicationDraft));
                OnPropertyChanged(nameof(FilteredFindings));
                RefreshCommands();
            }
        }
    }

    public ConfigurationStatus Configuration { get; }

    public UserPreferences Preferences { get; private set; }

    public IReadOnlyList<RecentReviewEntry> RecentReviews { get; private set; }

    public ExportFormat PreferredExportFormat { get; set; }

    public AppTheme Theme { get; set; }

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) OnPropertyChanged(nameof(FilteredFindings)); }
    }

    public ReviewSeverity? SeverityFilter
    {
        get => _severityFilter;
        set { if (Set(ref _severityFilter, value)) OnPropertyChanged(nameof(FilteredFindings)); }
    }

    public FindingSort FindingSort
    {
        get => _findingSort;
        set { if (Set(ref _findingSort, value)) OnPropertyChanged(nameof(FilteredFindings)); }
    }

    public IReadOnlyList<ReviewSeverity> Severities { get; } = Enum.GetValues<ReviewSeverity>();

    public IReadOnlyList<FindingSort> FindingSorts { get; } = Enum.GetValues<FindingSort>();

    public IReadOnlyList<ExportFormat> ExportFormats { get; } = Enum.GetValues<ExportFormat>();

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    public IReadOnlyList<ReviewFinding> FilteredFindings => Report is null
        ? []
        : ReviewFindingQuery.Apply(Report.Findings, new FindingQuery(SearchText, SeverityFilter, Sort: FindingSort));

    public bool UsesPath => Mode is ReviewMode.File or ReviewMode.LocalRepository or ReviewMode.RagRepository or ReviewMode.AgentRepository or ReviewMode.Specialists;

    public bool UsesRemoteUrl => Mode == ReviewMode.RemoteRepository;

    public bool UsesPullRequest => Mode == ReviewMode.GitHubPullRequest;

    public bool UsesRepositoryLimits => Mode is ReviewMode.LocalRepository or ReviewMode.RemoteRepository or ReviewMode.RagRepository;

    public bool HasReport => Report is not null;

    public bool HasPublicationDraft => Report?.PublicationDraft is not null;

    public string PathLabel => Mode is ReviewMode.File or ReviewMode.Specialists ? "Source file" : "Repository folder";

    public ICommand StartCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand BrowseCommand { get; }

    public ICommand ExportJsonCommand { get; }

    public ICommand ExportMarkdownCommand { get; }

    public ICommand PublishCommand { get; }

    public ICommand SavePreferencesCommand { get; }

    public async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        Report = null;
        PublishConfirmed = false;
        ProgressPercent = 0;
        Status = "Starting review";
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<ReviewProgress>(update =>
        {
            Status = update.Message;
            ProgressPercent = update.Percent ?? ProgressPercent;
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
        OnPropertyChanged(nameof(RecentReviews));
    }

    public void Cancel() => _cancellation?.Cancel();

    public void Browse()
    {
        var selected = Mode is ReviewMode.File or ReviewMode.Specialists ? _dialogs.PickSourceFile() : _dialogs.PickFolder();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            Path = selected;
        }
    }

    public async Task PublishAsync()
    {
        if (Report?.PublicationDraft is null || !PublishConfirmed)
        {
            Error = new ApplicationError(ApplicationErrorCode.Validation, "Fresh confirmation is required before publishing.");
            return;
        }

        IsBusy = true;
        Status = "Publishing approved comments";
        var result = await _application.PublishPullRequestReviewAsync(new PublishReviewRequest(Report.PublicationDraft, true));
        Error = result.Error;
        Status = result.IsSuccess ? "Published the approved GitHub review" : result.Error!.Message;
        PublishConfirmed = false;
        IsBusy = false;
    }

    public void Dispose() => _cancellation?.Dispose();

    public void SavePreferences()
    {
        Preferences = new UserPreferences(Mode, MaximumFiles, MaximumCharacters, PreferredExportFormat, Theme);
        _userState?.SavePreferences(Preferences);
        Status = "Preferences saved";
    }

    private async Task ExportAsync(bool json)
    {
        if (Report is null)
        {
            return;
        }

        var path = json
            ? _dialogs.PickExportPath(".json", "JSON report|*.json")
            : _dialogs.PickExportPath(".md", "Markdown report|*.md");
        if (path is null)
        {
            return;
        }

        var content = json ? _application.ExportJson(Report) : _application.ExportMarkdown(Report);
        await System.IO.File.WriteAllTextAsync(path, content);
        Status = $"Exported report to {path}";
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

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RefreshCommands()
    {
        ((AsyncRelayCommand)StartCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        ((RelayCommand)BrowseCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExportJsonCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExportMarkdownCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PublishCommand).NotifyCanExecuteChanged();
    }
}
