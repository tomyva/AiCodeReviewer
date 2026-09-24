using System.Text;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class SourceFileLoaderTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ai-reviewer-tests-{Guid.NewGuid():N}");

    public SourceFileLoaderTests() => Directory.CreateDirectory(_temporaryDirectory);

    [Fact]
    public async Task LoadAsyncLoadsValidCSharpFile()
    {
        var path = CreateFile("Example.cs", "public sealed class Example { }");
        var sourceFile = await new SourceFileLoader().LoadAsync(path);
        Assert.Equal(Path.GetFullPath(path), sourceFile.Path);
        Assert.Equal("Example.cs", sourceFile.Name);
        Assert.Contains("class Example", sourceFile.Content);
        Assert.Equal(ProgrammingLanguage.CSharp, sourceFile.Language.Language);
    }

    [Fact]
    public async Task LoadAsyncRejectsNonCSharpFile()
    {
        var path = CreateFile("Example.txt", "text");
        var exception = await Assert.ThrowsAsync<SourceFileException>(() => new SourceFileLoader().LoadAsync(path));
        Assert.Contains("does not support", exception.Message);
    }

    [Fact]
    public async Task LoadAsyncRejectsMissingFile()
    {
        var path = Path.Combine(_temporaryDirectory, "Missing.cs");
        var exception = await Assert.ThrowsAsync<SourceFileException>(() => new SourceFileLoader().LoadAsync(path));
        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task LoadAsyncRejectsFileOverConfiguredLimit()
    {
        var path = CreateFile("Large.cs", "public class Large {}");
        var exception = await Assert.ThrowsAsync<SourceFileException>(() => new SourceFileLoader(4).LoadAsync(path));
        Assert.Contains("too large", exception.Message);
    }

    [Fact]
    public async Task LoadAsyncRejectsWhitespaceOnlyFile()
    {
        var path = CreateFile("Empty.cs", "  \r\n  ");
        var exception = await Assert.ThrowsAsync<SourceFileException>(() => new SourceFileLoader().LoadAsync(path));
        Assert.Contains("no reviewable code", exception.Message);
    }

    [Fact]
    public async Task LoadAsyncRejectsInvalidUtf8()
    {
        var path = Path.Combine(_temporaryDirectory, "Invalid.cs");
        await File.WriteAllBytesAsync(path, [0xC3, 0x28]);
        var exception = await Assert.ThrowsAsync<SourceFileException>(() => new SourceFileLoader().LoadAsync(path));
        Assert.Contains("UTF-8", exception.Message);
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_temporaryDirectory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
