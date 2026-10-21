using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Data;

public class FileExportService : IFileExportService
{
    private readonly IAppWindowProvider _windowProvider;
    private readonly ILogger<FileExportService> _logger;

    public FileExportService(IAppWindowProvider windowProvider, ILogger<FileExportService>? logger = null)
    {
        _windowProvider = windowProvider;
        _logger = logger ?? NullLogger<FileExportService>.Instance;
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
            _logger.LogWarning(ex, "Native save dialog failed for {FileName}.", defaultFileName);
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
