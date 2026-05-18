namespace DkcDesktopClient.App.Services;

/// <summary>
/// Platform-abstracted file picker service.
/// Wraps Avalonia's <c>StorageProvider</c> file APIs for use in ViewModels.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Shows a "Save File" dialog and returns the chosen path, or <c>null</c> if cancelled.
    /// </summary>
    /// <param name="suggestedFileName">Pre-filled file name (without path).</param>
    /// <param name="fileTypeFilter">
    /// Optional list of allowed file types, e.g. <c>[("CSV-Datei", "*.csv")]</c>.
    /// </param>
    Task<string?> PickSaveFileAsync(string suggestedFileName,
        IReadOnlyList<(string Name, string Pattern)>? fileTypeFilter = null);
}
