using System.Text;

namespace CaissaDesk.Data;

public class FileExportService : IFileExportService
{
    private readonly IAppWindowProvider _windowProvider;

    public FileExportService(IAppWindowProvider windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public async Task<string?> SaveTextFileAsync(
        string dialogTitle,
        string defaultFileName,
        string content,
        params (string Description, string[] Extensions)[] filters)
    {
        string? targetPath = null;
        try
        {
            if (_windowProvider?.Window != null)
            {
                targetPath = await _windowProvider.Window.ShowSaveFileAsync(
                    dialogTitle,
                    defaultFileName,
                    filters);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileExportService] Native save dialog error: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(targetPath, content, Encoding.UTF8);
        return targetPath;
    }
}
