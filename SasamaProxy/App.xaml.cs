using System;
using System.Windows;
using System.Windows.Threading;
using SasamaProxy.Core;

namespace SasamaProxy
{
    public partial class App : Application
    {
        /// <summary>
        /// Set by MainWindow once it creates the NatEngine. If the app
        /// crashes for any reason we still want WinDivert closed rather
        /// than left holding traffic, so both crash handlers below call
        /// this before the process goes down.
        /// </summary>
        public static Action EmergencyCleanup;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Init(Dispatcher);

            DispatcherUnhandledException += (s, args) =>
            {
                Logger.Log("FATAL (UI thread): " + args.Exception);
                SafeEmergencyCleanup();
                // Let it continue to crash naturally afterwards so you get a
                // normal Windows/VS crash report too - we only guarantee the
                // driver gets released first.
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Logger.Log("FATAL (background thread): " + args.ExceptionObject);
                SafeEmergencyCleanup();
            };
        }

        private static void SafeEmergencyCleanup()
        {
            try { EmergencyCleanup?.Invoke(); } catch { /* nothing more we can do */ }
        }
    }
}
