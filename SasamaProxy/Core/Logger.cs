using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SasamaProxy.Core
{
    /// <summary>
    /// Central logger. UI-bound collection is capped so the log view
    /// never grows without bound during a long gaming session; a full
    /// copy of everything also goes to a rolling text file on disk so
    /// you can grab logs after something goes wrong even if the app
    /// has since been closed.
    /// </summary>
    public static class Logger
    {
        private const int MaxUiLines = 500;
        public static ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();

        private static readonly object FileLock = new object();
        private static string _logFilePath;
        private static Dispatcher _uiDispatcher;

        public static void Init(Dispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher;
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SasamaProxy");
                Directory.CreateDirectory(dir);
                _logFilePath = Path.Combine(dir, "sasama.log");
            }
            catch
            {
                _logFilePath = null;
            }
        }

        public static void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

            if (_uiDispatcher != null)
            {
                _uiDispatcher.BeginInvoke(new Action(() =>
                {
                    Lines.Add(line);
                    while (Lines.Count > MaxUiLines)
                        Lines.RemoveAt(0);
                }));
            }

            if (_logFilePath != null)
            {
                try
                {
                    lock (FileLock)
                    {
                        File.AppendAllText(_logFilePath, line + Environment.NewLine);
                    }
                }
                catch
                {
                    // Logging must never throw and never block the proxy - if the
                    // disk write fails for any reason we simply drop that line.
                }
            }
        }
    }
}
