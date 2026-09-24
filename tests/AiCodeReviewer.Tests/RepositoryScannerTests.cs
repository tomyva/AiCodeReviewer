using System.Text;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class RepositoryScannerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ai-reviewer-repo-{Guid.NewGuid():N}");

    public RepositoryScannerTests() => Directory.CreateDirectory(_temporaryDirectory);

    [Fact]
    public void ScanFindsSupportedFilesAndIgnoresNoise()
    {
        CreateFile("src/Service.cs", "class Service {}");
        CreateFile("src/worker.py", "def work(): pass");
        CreateFile("app.js", "export const value = 1;");
        CreateFile("app.min.js", "const x=1;");
        CreateFile("README.txt", "documentation");
        CreateFile("bin/Generated.cs", "class Generated {}");
        CreateFile("node_modules/package.js", "module.exports = {};");

        var result = new RepositoryScanner().Scan(_temporaryDirectory);

        Assert.Equal(5, result.FilesDiscovered);
        Assert.Equal(3, result.Files.Count);
        Assert.Equal(2, result.FilesSkipped);
        Assert.Equal(["app.js", "src/Service.cs", "src/worker.py"], result.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void ScanUsesDeterministicRelativePaths()
    {
        CreateFile("z/Last.java", "class Last {}");
        CreateFile("a/First.cpp", "int main() {}");

        var result = new RepositoryScanner().Scan(_temporaryDirectory);

        Assert.Equal(["a/First.cpp", "z/Last.java"], result.Files.Select(file => file.RelativePath));
    }

    private void CreateFile(string relativePath, string content)
    {
        var path = Path.Combine(_temporaryDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
