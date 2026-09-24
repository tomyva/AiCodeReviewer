namespace AiCodeReviewer.Application;

public sealed class ApplicationRuntime : IDisposable
{
    private readonly HttpClient _httpClient;

    public ApplicationRuntime()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        UserState = JsonUserStateStore.CreateDefault();
        Application = new ReviewApplicationService(new DefaultReviewWorkflowDispatcher(_httpClient), UserState);
    }

    public ICodeReviewerApplication Application { get; }

    public IUserStateStore UserState { get; }

    public void Dispose() => _httpClient.Dispose();
}
