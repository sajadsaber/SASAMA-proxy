using System;
using System.Diagnostics;

namespace SasamaProxy.Core
{
    /// <summary>
    /// IMPORTANT DEVIATION FROM THE ORIGINAL SPEC, ON PURPOSE:
    /// A plain "run at Windows startup" is usually done with a value under
    /// HKCU\...\Run. That does NOT work cleanly for an app whose manifest
    /// requests administrator rights (which this app must have, for
    /// WinDivert) - Windows will either silently fail to launch it at logon,
    /// or show a UAC prompt on every single boot, which is exactly the kind
    /// of confusing experience the non-technical target users of this app
    /// should never see.
    ///
    /// The correct fix is a scheduled task registered to run "with highest
    /// privileges" at logon - this is the standard, documented way apps that
    /// need admin rights auto-start without a UAC prompt every time. We
    /// shell out to schtasks.exe (built into every Windows install, no extra
    /// dependency) rather than pull in a Task Scheduler library.
    /// </summary>
    public static class StartupManager
    {
        private const string TaskName = "SasamaProxy";

        public static bool Enable()
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";
            return RunSchtasks(args);
        }

        public static bool Disable()
        {
            string args = $"/Delete /TN \"{TaskName}\" /F";
            return RunSchtasks(args);
        }

        private static bool RunSchtasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode != 0)
                    {
                        string err = proc.StandardError.ReadToEnd();
                        Logger.Log("StartupManager: schtasks failed - " + err.Trim());
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("StartupManager: could not run schtasks - " + ex.Message);
                return false;
            }
        }
    }
}
