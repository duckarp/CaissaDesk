using CaissaDesk.Logging;
using Microsoft.Extensions.Logging;

namespace CaissaDesk.Tests;

public class FileLoggerProviderTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), $"caissadesk_logs_{Guid.NewGuid():N}");

    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    [Fact]
    public void WritesDailyFile_WithLevelCategoryAndException()
    {
        string dir = NewTempDir();
        var now = new DateTime(2026, 10, 21, 14, 5, 9, 123);
        try
        {
            using var provider = new FileLoggerProvider(dir, clock: () => now);
            var logger = provider.CreateLogger("CaissaDesk.Test");

            logger.LogInformation("Hello {Name}", "arbiter");
            logger.LogError(new InvalidOperationException("boom"), "Something failed");
            logger.LogDebug("filtered out");

            string path = Path.Combine(dir, "caissadesk-20261021.log");
            Assert.Equal(path, provider.CurrentFilePath);
            string content = ReadShared(path);
            Assert.Contains("2026-10-21 14:05:09.123 [INF] CaissaDesk.Test: Hello arbiter", content);
            Assert.Contains("[ERR] CaissaDesk.Test: Something failed", content);
            Assert.Contains("System.InvalidOperationException: boom", content);
            Assert.DoesNotContain("filtered out", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FrameworkCategories_UseHigherThreshold()
    {
        string dir = NewTempDir();
        try
        {
            using var provider = new FileLoggerProvider(dir);
            Assert.False(provider.CreateLogger("Microsoft.AspNetCore.Components").IsEnabled(LogLevel.Information));
            Assert.True(provider.CreateLogger("Microsoft.AspNetCore.Components").IsEnabled(LogLevel.Warning));
            Assert.True(provider.CreateLogger("CaissaDesk.Data").IsEnabled(LogLevel.Information));
            Assert.False(provider.CreateLogger("CaissaDesk.Data").IsEnabled(LogLevel.None));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RollsOverByDate_AndKeepsOnlyRetainedFiles()
    {
        string dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var start = new DateTime(2026, 10, 1, 9, 0, 0);
            for (int i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(dir, FileLoggerProvider.GetFileName(start.AddDays(i))), "old");
            }
            File.WriteAllText(Path.Combine(dir, "unrelated.txt"), "keep me");

            var now = start.AddDays(10);
            using var provider = new FileLoggerProvider(dir, retainedFileCount: 7, clock: () => now);
            var logger = provider.CreateLogger("Roll");
            logger.LogWarning("day 11");

            now = now.AddDays(1);
            logger.LogWarning("day 12");

            var logs = Directory.GetFiles(dir, "caissadesk-*.log").Select(Path.GetFileName).OrderBy(n => n).ToList();
            Assert.Equal(7, logs.Count);
            Assert.Equal(FileLoggerProvider.GetFileName(start.AddDays(11)), logs[^1]);
            Assert.Equal(FileLoggerProvider.GetFileName(start.AddDays(5)), logs[0]);
            Assert.True(File.Exists(Path.Combine(dir, "unrelated.txt")));

            Assert.Contains("day 11", ReadShared(Path.Combine(dir, FileLoggerProvider.GetFileName(start.AddDays(10)))));
            Assert.Contains("day 12", ReadShared(Path.Combine(dir, FileLoggerProvider.GetFileName(start.AddDays(11)))));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ConcurrentWrites_AreAllPersistedAsWholeLines()
    {
        string dir = NewTempDir();
        try
        {
            using var provider = new FileLoggerProvider(dir);
            const int threads = 8, perThread = 200;

            await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
            {
                var logger = provider.CreateLogger($"Worker{t}");
                for (int i = 0; i < perThread; i++)
                {
                    logger.LogInformation("message {Thread}-{Index}", t, i);
                }
            })));

            var lines = ReadShared(provider.CurrentFilePath!)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .ToList();
            Assert.Equal(threads * perThread, lines.Count);
            Assert.All(lines, l => Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INF\] Worker\d: message \d-\d+$", l));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NeverThrows_WhenDirectoryIsUnusable_OrAfterDispose()
    {
        string blocker = Path.Combine(Path.GetTempPath(), $"caissadesk_blocker_{Guid.NewGuid():N}");
        string okDir = NewTempDir();
        File.WriteAllText(blocker, "a file, not a directory");
        try
        {
            var provider = new FileLoggerProvider(Path.Combine(blocker, "logs"));
            var logger = provider.CreateLogger("Broken");
            logger.LogError(new Exception("x"), "cannot be written");
            logger.LogInformation("still fine");
            Assert.Null(provider.CurrentFilePath);

            provider.Dispose();
            provider.Dispose();
            logger.LogCritical("after dispose");
            Assert.False(logger.IsEnabled(LogLevel.Critical));

            // A throwing formatter must not escape either.
            using (var ok = new FileLoggerProvider(okDir))
            {
                ok.CreateLogger("Fmt").Log<object?>(LogLevel.Warning, default, null, null, (_, _) => throw new InvalidOperationException());
                Assert.Contains("log formatting failed", ReadShared(ok.CurrentFilePath!));
            }
        }
        finally
        {
            File.Delete(blocker);
            if (Directory.Exists(okDir)) Directory.Delete(okDir, true);
        }
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalApplicationData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaissaDesk", "logs");
        Assert.Equal(expected, FileLoggerProvider.GetDefaultLogDirectory());
    }
}
