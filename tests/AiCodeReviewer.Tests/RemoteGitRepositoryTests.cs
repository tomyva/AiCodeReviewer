using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.GitHub;

namespace AiCodeReviewer.Tests;

public sealed class RemoteGitRepositoryTests
{
    [Fact]
    public void ValidateRemoteUriAcceptsHttpsRepository()
    {
        var uri = RemoteGitRepository.ValidateRemoteUri("https://github.com/example/project.git");
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
    }

    [Theory]
    [InlineData("http://github.com/example/project.git")]
    [InlineData("git@github.com:example/project.git")]
    [InlineData("https://token@github.com/example/project.git")]
    public void ValidateRemoteUriRejectsUnsafeUrls(string url)
    {
        Assert.Throws<RepositoryException>(() => RemoteGitRepository.ValidateRemoteUri(url));
    }
}
