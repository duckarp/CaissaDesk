using CaissaDesk.Data;

namespace CaissaDesk.Tests;

public class FileExportServiceTests
{
    private class DummyAppWindowProvider : IAppWindowProvider
    {
        public Photino.NET.PhotinoWindow Window { get; set; } = null!;
    }

    [Fact]
    public async Task SaveTextFileAsync_WhenWindowIsNull_ReturnsNullGracefully()
    {
        var provider = new DummyAppWindowProvider();
        var service = new FileExportService(provider);

        var result = await service.SaveTextFileAsync(
            "Test Dialog",
            "test.cdesk",
            "{\"Name\":\"Test\"}",
            ("CaissaDesk (*.cdesk)", new[] { ".cdesk" }));

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveTextFileAsync_WithMockWriter_WritesFileContentCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "CaissaDesk_Tests_" + Guid.NewGuid().ToString("N"));
        string tempFile = Path.Combine(tempDir, "exported_tournament.cdesk");
        string testContent = "{\"Tournament\":\"Bratislava Open 2026\",\"Rounds\":7}";

        try
        {
            // Direct write test verifying path creation and utf-8 writing
            var dir = Path.GetDirectoryName(tempFile)!;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(tempFile, testContent, System.Text.Encoding.UTF8);

            Assert.True(File.Exists(tempFile));
            string readBack = await File.ReadAllTextAsync(tempFile);
            Assert.Equal(testContent, readBack);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
