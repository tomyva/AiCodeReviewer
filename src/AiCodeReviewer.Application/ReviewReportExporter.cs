using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace AiCodeReviewer.Application;

public static class ReviewReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(ReviewReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ToMarkdown(ReviewReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder().AppendLine("# AI Code Review").AppendLine();
        AppendInvariant(builder, $"- **Mode:** {report.Mode}");
        AppendInvariant(builder, $"- **Subject:** {report.Subject}");
        AppendInvariant(builder, $"- **Completed:** {report.CompletedAt:O}");
        AppendInvariant(builder, $"- **Model:** {report.Model}");
        AppendInvariant(builder, $"- **Findings:** {report.Findings.Count}");
        builder.AppendLine();

        if (report.Usage is not null)
        {
            AppendInvariant(builder, $"- **Tokens:** input {Format(report.Usage.InputTokens)}, output {Format(report.Usage.OutputTokens)}, total {Format(report.Usage.TotalTokens)}");
            builder.AppendLine();
        }

        builder.AppendLine("## Findings").AppendLine();
        if (report.Findings.Count == 0)
        {
            builder.AppendLine("No actionable findings were returned.");
        }

        for (var index = 0; index < report.Findings.Count; index++)
        {
            var finding = report.Findings[index];
            AppendInvariant(builder, $"### {index + 1}. {finding.Severity} — {finding.Category}");
            builder.AppendLine();
            AppendInvariant(builder, $"- **File:** {finding.File}");
            AppendInvariant(builder, $"- **Location:** {finding.Location ?? "Not specified"}");
            builder.AppendLine().AppendLine(finding.Explanation).AppendLine();
            AppendInvariant(builder, $"**Suggested improvement:** {finding.SuggestedImprovement}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendInvariant(StringBuilder builder, FormattableString value) =>
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
}
