namespace CaissaDesk.Data;

public interface IFileExportService
{
    Task<string?> SaveTextFileAsync(
        string dialogTitle,
        string defaultFileName,
        string content,
        params (string Description, string[] Extensions)[] filters);
}
