using System;
using System.Collections.Generic;
using SurfOS.Storage;

namespace SurfOS.Logging
{
    public sealed class BufferedKernelLogger : IKernelLogger
    {
        private readonly List<string> _pending = new List<string>();
        private IFileSystemService _fileSystem;
        private string _logPath;

        public void Attach(IFileSystemService fileSystem, string logPath)
        {
            _fileSystem = fileSystem;
            _logPath = logPath;
            for (int index = 0; index < _pending.Count; index++)
            {
                WriteToDisk(_pending[index]);
            }
            _pending.Clear();
        }

        public void Info(string message) { Write("INFO", message); }
        public void Warning(string message) { Write("WARN", message); }
        public void Error(string message) { Write("ERROR", message); }

        private void Write(string level, string message)
        {
            string entry = "[" + DateTime.UtcNow.ToString("s") + "] [" + level + "] " + message;
            if (_fileSystem == null)
            {
                _pending.Add(entry);
                return;
            }
            WriteToDisk(entry);
        }

        private void WriteToDisk(string entry)
        {
            try
            {
                _fileSystem.AppendAllText(_logPath, entry + "\r\n");
            }
            catch
            {
                // Logging must never turn a recoverable disk problem into a kernel panic.
            }
        }
    }
}
