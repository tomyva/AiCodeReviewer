using System.Text;
using System.Text.Json;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class RepositoryToolboxTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ai-reviewer-tools-{Guid.NewGuid():N}");

    public RepositoryToolboxTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "src"));
        File.WriteAllText(
            Path.Combine(_temporaryDirectory, "src", "Service.cs"),
            "public class Service\n{\n    public void Execute() { }\n}",
            new UTF8Encoding(false));
    }

    [Fact]
    public void ListAndSearchReturnRepositoryEvidence()
    {
        var toolbox = new RepositoryToolbox(_temporaryDirectory, 5_000);

        var files = toolbox.Execute("list_files", "{\"pattern\":\"Service\"}");
        var search = toolbox.Execute("find_symbol", "{\"symbol\":\"Execute\"}");

        Assert.Contains("src/Service.cs", files);
        Assert.Contains("Execute", search);
        Assert.Contains("line", search);
    }

    [Fact]
    public void ReadFileRejectsTraversalOutsideRepository()
    {
        var toolbox = new RepositoryToolbox(_temporaryDirectory, 5_000);

        var output = toolbox.Execute("read_file", "{\"path\":\"../outside.cs\",\"startLine\":1,\"endLine\":20}");

        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ReadFileCapsRequestedLineRange()
    {
        var toolbox = new RepositoryToolbox(_temporaryDirectory, 5_000);

        var output = toolbox.Execute("read_file", "{\"path\":\"src/Service.cs\",\"startLine\":1,\"endLine\":9999}");

        Assert.Contains("public class Service", output);
        Assert.DoesNotContain("error", output, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
