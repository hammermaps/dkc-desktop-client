using CommunityToolkit.Mvvm.ComponentModel;

namespace DkcDesktopClient.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Returns a fresh <see cref="CancellationToken"/> for the next data load.
    /// Cancels and disposes any previous load token so that in-flight requests
    /// for this view are cancelled when the user navigates away.
    /// </summary>
    protected CancellationToken StartLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        return _loadCts.Token;
    }

    /// <summary>
    /// Cancels any in-flight data load started by <see cref="StartLoad"/>.
    /// Safe to call multiple times.
    /// </summary>
    protected void CancelLoad()
    {
        _loadCts?.Cancel();
    }
}

