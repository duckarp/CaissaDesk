using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CaissaDesk.Logging;

/// <summary>
/// Minimal rolling file logger. Writes one file per local calendar day
/// (<c>caissadesk-YYYYMMDD.log</c>) and keeps only the newest
/// <see cref="RetainedFileCount"/> files. All public members are thread-safe
/// and never throw: a logging failure must never take the application down.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const string FilePrefix = "caissadesk-";
    public const string FileExtension = ".log";

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();
    private readonly Func<DateTime> _clock;
    private StreamWriter? _writer;
    private string? _currentFilePath;
    private volatile bool _disposed;

    public string LogDirectory { get; }
    public LogLevel MinimumLevel { get; }

    /// <summary>Minimum level for noisy framework categories (Microsoft.*, System.*).</summary>
    public LogLevel FrameworkMinimumLevel { get; }

    public int RetainedFileCount { get; }

    public FileLoggerProvider(
        string? logDirectory = null,
        LogLevel minimumLevel = LogLevel.Information,
        int retainedFileCount = 7,
        Func<DateTime>? clock = null,
        LogLevel frameworkMinimumLevel = LogLevel.Warning)
    {
        LogDirectory = string.IsNullOrWhiteSpace(logDirectory) ? GetDefaultLogDirectory() : logDirectory;
        MinimumLevel = minimumLevel;
        FrameworkMinimumLevel = frameworkMinimumLevel;
        RetainedFileCount = Math.Max(1, retainedFileCount);
        _clock = clock ?? (() => DateTime.Now);
    }

    public static string GetDefaultLogDirectory()
    {
        string baseDir;
        try
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        catch
        {
            baseDir = string.Empty;
        }

        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        return Path.Combine(baseDir, "CaissaDesk", "logs");
    }

    public static string GetFileName(DateTime localDate) =>
        FilePrefix + localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileExtension;

    /// <summary>Path of the file the provider is writing to right now (null before the first write).</summary>
    public string? CurrentFilePath
    {
        get { lock (_writeLock) return _currentFilePath; }
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName ?? string.Empty, name => new FileLogger(this, name));

    internal bool IsEnabled(string category, LogLevel level)
    {
        if (level == LogLevel.None || _disposed) return false;
        bool isFramework = category.StartsWith("Microsoft.", StringComparison.Ordinal)
                           || category.StartsWith("System.", StringComparison.Ordinal);
        return level >= (isFramework ? FrameworkMinimumLevel : MinimumLevel);
    }

    internal void WriteEntry(string category, LogLevel level, EventId eventId, string? message, Exception? exception)
    {
        try
        {
            DateTime now = _clock();
            var sb = new StringBuilder(256);
            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append(" [").Append(LevelTag(level)).Append("] ")
              .Append(category);
            if (eventId.Id != 0)
            {
                sb.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            sb.Append(": ").Append(message ?? string.Empty);
            if (exception != null)
            {
                sb.AppendLine().Append(exception);
            }

            string line = sb.ToString();

            lock (_writeLock)
            {
                if (_disposed) return;
                EnsureWriter(now);
                if (_writer == null) return;
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }
        catch
        {
            // Never let logging break the app. Drop the writer so the next entry retries.
            lock (_writeLock)
            {
                CloseWriterNoThrow();
            }
        }
    }

    // Caller must hold _writeLock.
    private void EnsureWriter(DateTime now)
    {
        string path = Path.Combine(LogDirectory, GetFileName(now));
        if (_writer != null && string.Equals(path, _currentFilePath, StringComparison.Ordinal))
        {
            return;
        }

        CloseWriterNoThrow();
        Directory.CreateDirectory(LogDirectory);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _currentFilePath = path;
        CleanupOldFiles(path);
    }

    private void CleanupOldFiles(string activePath)
    {
        try
        {
            var files = Directory.GetFiles(LogDirectory, FilePrefix + "*" + FileExtension)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();

            foreach (var old in files.Skip(RetainedFileCount))
            {
                if (string.Equals(old, activePath, StringComparison.Ordinal)) continue;
                try { File.Delete(old); } catch { /* best effort */ }
            }
        }
        catch
        {
            // best effort
        }
    }

    private void CloseWriterNoThrow()
    {
        try { _writer?.Dispose(); } catch { /* ignore */ }
        _writer = null;
        _currentFilePath = null;
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
            CloseWriterNoThrow();
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string? message;
            try
            {
                message = formatter?.Invoke(state, exception);
            }
            catch (Exception ex)
            {
                message = $"<log formatting failed: {ex.GetType().Name}>";
            }
            provider.WriteEntry(category, logLevel, eventId, message, exception);
        }
    }
}
