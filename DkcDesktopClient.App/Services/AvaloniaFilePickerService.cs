using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DkcDesktopClient.App.Services;

/// <summary>
/// Avalonia-backed implementation of <see cref="IFilePickerService"/>.
/// Obtains the active window's <c>StorageProvider</c> to show native file dialogs.
/// </summary>
public class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickSaveFileAsync(
        string suggestedFileName,
        IReadOnlyList<(string Name, string Pattern)>? fileTypeFilter = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var fileTypes = fileTypeFilter?
            .Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = new[] { f.Pattern }
            })
            .ToList<FilePickerFileType>();

        var options = new FilePickerSaveOptions
        {
            Title                = "Datei speichern",
            SuggestedFileName    = suggestedFileName,
            FileTypeChoices      = fileTypes,
            DefaultExtension     = fileTypeFilter?.FirstOrDefault().Pattern?.TrimStart('*', '.'),
            ShowOverwritePrompt  = true
        };

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        if (result == null) return null;

        // Resolve the local path from the storage item
        return result.TryGetLocalPath();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static TopLevel? GetTopLevel()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
