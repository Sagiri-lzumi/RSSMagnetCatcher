using System.Text;

namespace RSSMagnetCatcher.Infrastructure;

public sealed class Logger
{
    private readonly object _sync = new();
    private readonly string _appLogPath;
    private readonly string _errorLogPath;

    public Logger(string appLogPath, string errorLogPath)
    {
        _appLogPath = appLogPath;
        _errorLogPath = errorLogPath;
    }

    public void Info(string message)
    {
        Write(_appLogPath, "INFO", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var details = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write(_errorLogPath, "ERROR", details);
    }

    private void Write(string path, string level, string message)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
    }
}
