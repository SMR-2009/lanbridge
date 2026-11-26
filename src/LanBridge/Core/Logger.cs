using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LanBridge.Core
{
    public interface ILogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception = null);
    }

    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();
        private NullLogger() { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception exception = null) { }
    }

    public sealed class FileLogger : ILogger, IDisposable
    {
        private readonly string _path;
        private readonly long _maxBytes;
        private readonly int _keepFiles;
        private readonly object _gate = new object();

        public FileLogger(string logPath, long maxBytes = 2 * 1024 * 1024, int keepFiles = 3)
        {
            _path = logPath ?? throw new ArgumentNullException(nameof(logPath));
            _maxBytes = maxBytes > 0 ? maxBytes : 2 * 1024 * 1024;
            _keepFiles = Math.Max(1, keepFiles);

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        public void Info(string message) => Write("INFO", message, null);
        public void Warn(string message) => Write("WARN", message, null);
        public void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

        private void Write(string level, string message, Exception exception)
        {
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O} [{1}] {2}{3}{4}",
                DateTimeOffset.UtcNow,
                level,
                message ?? string.Empty,
                exception == null ? string.Empty : Environment.NewLine,
                exception == null ? string.Empty : exception);

            lock (_gate)
            {
                try
                {
                    if (File.Exists(_path) && new FileInfo(_path).Length + line.Length >= _maxBytes)
                        Rotate();
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never take down the app.
                }
            }
        }

        private void Rotate()
        {
            var oldest = Numbered(_keepFiles);
            if (File.Exists(oldest)) File.Delete(oldest);

            for (var i = _keepFiles - 1; i >= 1; i--)
            {
                var source = Numbered(i);
                if (File.Exists(source)) File.Move(source, Numbered(i + 1));
            }

            if (File.Exists(_path)) File.Move(_path, Numbered(1));
        }

        private string Numbered(int index)
        {
            var dir = Path.GetDirectoryName(_path) ?? ".";
            var name = Path.GetFileNameWithoutExtension(_path);
            var ext = Path.GetExtension(_path);
            return Path.Combine(dir, name + "." + index.ToString(CultureInfo.InvariantCulture) + ext);
        }

        public void Dispose()
        {
        }
    }
}
