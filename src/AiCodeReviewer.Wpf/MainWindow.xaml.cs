using System.Windows;
using AiCodeReviewer.Application;

namespace AiCodeReviewer.Wpf;

public partial class MainWindow : Window, IDisposable
{
    private readonly ApplicationRuntime _runtime;
    private readonly DesktopReviewViewModel _viewModel;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _runtime = new ApplicationRuntime();
        _viewModel = new DesktopReviewViewModel(_runtime.Application, new DesktopDialogService(), _runtime.UserState);
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _viewModel.Dispose();
        _runtime.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
