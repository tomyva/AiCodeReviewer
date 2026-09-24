using System.Diagnostics;
using AiCodeReviewer.Core.Exceptions;

namespace AiCodeReviewer.GitHub;

public static class RemoteGitRepository
{
    private const string DirectoryPrefix = "ai-code-reviewer-";

    public static async Task<RemoteRepositoryCheckout> CloneAsync(
        string remoteUrl,
        CancellationToken cancellationToken = default)
    {
        var uri = ValidateRemoteUri(remoteUrl);
        var checkoutPath = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(checkoutPath);

        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("clone");
        startInfo.ArgumentList.Add("--depth");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--no-tags");
        startInfo.ArgumentList.Add(uri.AbsoluteUri);
        startInfo.ArgumentList.Add(checkoutPath);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new RepositoryException("Git could not be started.");
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new RepositoryException($"Git clone failed: {error.Trim()}");
            }

            return new RemoteRepositoryCheckout(checkoutPath);
        }
        catch
        {
            DeleteVerifiedCheckout(checkoutPath);
            throw;
        }
    }

    public static Uri ValidateRemoteUri(string remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new RepositoryException("Remote repositories must use an absolute HTTPS Git URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new RepositoryException("Do not place credentials in the repository URL; use a Git credential manager.");
        }

        return uri;
    }

    internal static void DeleteVerifiedCheckout(string checkoutPath)
    {
        var fullPath = Path.GetFullPath(checkoutPath);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith(DirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to remove a directory that is not an AI Code Reviewer checkout.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}

public sealed class RemoteRepositoryCheckout : IDisposable
{
    private bool _disposed;

    internal RemoteRepositoryCheckout(string path) => Path = path;

    public string Path { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        RemoteGitRepository.DeleteVerifiedCheckout(Path);
        _disposed = true;
    }
}
