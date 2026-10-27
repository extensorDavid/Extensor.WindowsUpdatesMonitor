namespace UpdateNag;

/// <summary>Minimal daily-rolling text file logger: logs\updatenag-yyyyMMdd.log, keeps 14 days.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainDays = 14;
    private readonly string _directory;
    private readonly object _lock = new();
    private string? _currentFile;

    public FileLoggerProvider(string directory) => _directory = directory;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                var file = Path.Combine(_directory, $"updatenag-{DateTime.Now:yyyyMMdd}.log");
                if (file != _currentFile)
                {
                    Directory.CreateDirectory(_directory);
                    _currentFile = file;
                    DeleteOldFiles();
                }
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the service down (e.g. no write access in --console mode).
            }
        }
    }

    private void DeleteOldFiles()
    {
        foreach (var file in Directory.GetFiles(_directory, "updatenag-*.log"))
        {
            if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-RetainDays))
                File.Delete(file);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception != null)
                line += Environment.NewLine + exception;
            _provider.Write(line);
        }
    }
}
