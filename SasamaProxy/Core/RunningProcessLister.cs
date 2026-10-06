using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SasamaProxy.Core
{
    public static class RunningProcessLister
    {
        /// <summary>Distinct, sorted list of "name.exe" for currently running processes with a main window.</summary>
        public static List<string> GetRunningProcessNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        // Prefer processes with a visible window - these are the ones a
                        // non-technical user will actually recognize (a browser, a game).
                        // Background services still get caught once they're picked from
                        // an active connection anyway via the flow tracker.
                        if (!string.IsNullOrWhiteSpace(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero)
                            names.Add(p.ProcessName + ".exe");
                    }
                    catch { /* some processes deny access to their info - skip */ }
                    finally { p.Dispose(); }
                }
            }
            catch { /* return whatever we managed to collect */ }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
