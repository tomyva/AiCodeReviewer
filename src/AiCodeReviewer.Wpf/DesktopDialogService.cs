using Microsoft.Win32;

namespace AiCodeReviewer.Wpf;

public interface IDesktopDialogService
{
    string? PickSourceFile();

    string? PickFolder();

    string? PickExportPath(string extension, string filter);
}

public sealed class DesktopDialogService : IDesktopDialogService
{
    public string? PickSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a source file",
            Filter = "Source files|*.cs;*.js;*.ts;*.py;*.java;*.go;*.rs|All files|*.*",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a repository folder", Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickExportPath(string extension, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export review report",
            DefaultExt = extension,
            Filter = filter,
            AddExtension = true,
            FileName = $"ai-code-review-{DateTime.Now:yyyyMMdd-HHmmss}{extension}"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
