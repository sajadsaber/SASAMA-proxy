using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using SasamaProxy.Core;
using SasamaProxy.Models;

namespace SasamaProxy
{
    public partial class MainWindow : Window
    {
        private readonly AppConfig _config;
        private readonly ObservableCollection<AppRule> _rules;

        private readonly NatEngine _natEngine = new NatEngine();
        private readonly TcpRelay _tcpRelay = new TcpRelay();
        private readonly ProxyMonitor _proxyMonitor = new ProxyMonitor();
        private readonly TrayIconManager _tray = new TrayIconManager();

        private bool _isRunning;
        private bool _reallyClosing;

        public MainWindow()
        {
            InitializeComponent();

            _config = ConfigStore.Load();
            _rules = new ObservableCollection<AppRule>(_config.Rules);
            ProgramListItemsControl.ItemsSource = _rules;

            ProxyNameTextBox.Text = _config.ProxyName;
            ProxyIpTextBox.Text = _config.ProxyIp;
            ProxyPortTextBox.Text = _config.ProxyPort.ToString();

            LogListBox.ItemsSource = Logger.Lines;
            Logger.Lines.CollectionChanged += (s, e) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            };

            _tray.Initialize();
            _tray.RestoreRequested += () => Dispatcher.Invoke(RestoreFromTray);
            _tray.ExitRequested += () => Dispatcher.Invoke(ExitApplication);

            _proxyMonitor.StatusChanged += (healthy, latencyMs) => Dispatcher.BeginInvoke(new Action(() => UpdateProxyStatusUi(healthy, latencyMs)));

            // If the app ever crashes, make sure WinDivert still gets closed.
            App.EmergencyCleanup = () => { try { _natEngine.EmergencyStop(); } catch { } };

            // Keep the scheduled task in sync with the saved setting even if it
            // somehow drifted (e.g. the task was removed manually).
            if (_config.RunOnStartup) StartupManager.Enable(); else StartupManager.Disable();

            Logger.Log("SASAMA Proxy ready. Press START PROXY to begin.");

            if (_config.StartMinimizedToTray)
            {
                Loaded += (s, e) => HideToTray();
            }
        }

        // ===================== Start / Stop =====================

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
                StartEverything();
            else
                StopEverything();
        }

        private void StartEverything()
        {
            if (!ushort.TryParse(ProxyPortTextBox.Text, out ushort port))
            {
                Logger.Log("Cannot start: proxy port is not a valid number.");
                MessageBox.Show(this, "The proxy port must be a number (e.g. 10808).", "SASAMA Proxy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string ip = ProxyIpTextBox.Text.Trim();

            SaveCurrentProxySettings();

            try
            {
                _proxyMonitor.Start(() => ProxyIpTextBox.Text.Trim(), () => port);
                IPAddress localIp = LocalNetwork.DetectLocalIPv4(); // detected ONCE, shared by both - see LocalNetwork.cs
                _tcpRelay.Start(ip, port, _natEngine.LookupNatEntry, localIp);
                _natEngine.IsProxyHealthy = () => _proxyMonitor.Healthy;
                _natEngine.Start(_rules.Where(r => r.Enabled).Select(r => r.ProcessName), _tcpRelay.Port, localIp);

                _isRunning = true;
                StartStopButton.Content = "STOP PROXY";
                StartStopButton.Background = (Brush)FindResource("AccentRedBrush");
                EngineStatusDot.Fill = (Brush)FindResource("OnlineGreenBrush");
                EngineStatusText.Text = "Connected";
                EngineStatusText.Foreground = (Brush)FindResource("OnlineGreenBrush");
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to start: " + ex.Message);
                MessageBox.Show(this, "Could not start the proxy engine:\n\n" + ex.Message +
                    "\n\nMake sure you launched SASAMA Proxy as Administrator.", "SASAMA Proxy", MessageBoxButton.OK, MessageBoxImage.Error);
                StopEverything();
            }
        }

        private void StopEverything()
        {
            _natEngine.Stop();
            _tcpRelay.Stop();
            _proxyMonitor.Stop();

            _isRunning = false;
            StartStopButton.Content = "START PROXY";
            StartStopButton.Background = (Brush)FindResource("AccentBlueBrush");
            EngineStatusDot.Fill = (Brush)FindResource("OfflineRedBrush");
            EngineStatusText.Text = "Disconnected";
            EngineStatusText.Foreground = (Brush)FindResource("OfflineRedBrush");

            ProxyStatusDot.Fill = (Brush)FindResource("OfflineRedBrush");
            ProxyStatusText.Text = "offline";
            LatencyText.Text = "Latency: --";
        }

        private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
        {
            // Deliberately does not wait for anything and does not throw,
            // regardless of current state - this is the "always works" button.
            try { _natEngine.EmergencyStop(); } catch { }
            try { _tcpRelay.Stop(); } catch { }
            try { _proxyMonitor.Stop(); } catch { }

            _isRunning = false;
            StartStopButton.Content = "START PROXY";
            StartStopButton.Background = (Brush)FindResource("AccentBlueBrush");
            EngineStatusDot.Fill = (Brush)FindResource("OfflineRedBrush");
            EngineStatusText.Text = "Disconnected";
            EngineStatusText.Foreground = (Brush)FindResource("OfflineRedBrush");
            Logger.Log("Emergency stop pressed - all redirection halted immediately.");
        }

        private void UpdateProxyStatusUi(bool healthy, int latencyMs)
        {
            if (healthy)
            {
                ProxyStatusDot.Fill = (Brush)FindResource("OnlineGreenBrush");
                ProxyStatusText.Text = "online";
                LatencyText.Text = $"Latency: {latencyMs} ms";
            }
            else
            {
                ProxyStatusDot.Fill = (Brush)FindResource("OfflineRedBrush");
                ProxyStatusText.Text = "offline";
                LatencyText.Text = "Latency: Time Out";
            }
        }

        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ushort.TryParse(ProxyPortTextBox.Text, out ushort port)) return;
            string ip = ProxyIpTextBox.Text.Trim();
            PingButton.IsEnabled = false;
            try
            {
                int latency = await _proxyMonitor.CheckOnceAsync(ip, port, CancellationToken.None);
                UpdateProxyStatusUi(latency >= 0, latency);
            }
            finally
            {
                PingButton.IsEnabled = true;
            }
        }

        // ===================== Program list =====================

        private void AddProgramButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new AddProcessWindow { Owner = this };
            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedProcessName))
            {
                string name = picker.SelectedProcessName.Trim();
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";

                if (_rules.Any(r => string.Equals(r.ProcessName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Log($"{name} is already in the list.");
                    return;
                }

                var rule = new AppRule(name, true);
                _rules.Add(rule);
                SaveRulesAndApply();
                Logger.Log($"Added {name} to the proxy list.");
            }
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is AppRule rule)
            {
                _rules.Remove(rule);
                SaveRulesAndApply();
                Logger.Log($"Removed {rule.ProcessName} from the proxy list.");
            }
        }

        private void RuleCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveRulesAndApply();
        }

        private void SaveRulesAndApply()
        {
            _config.Rules = _rules.ToList();
            ConfigStore.Save(_config);
            if (_isRunning)
                _natEngine.SetTrackedProcessNames(_rules.Where(r => r.Enabled).Select(r => r.ProcessName));
        }

        private void SaveCurrentProxySettings()
        {
            _config.ProxyName = ProxyNameTextBox.Text;
            _config.ProxyIp = ProxyIpTextBox.Text.Trim();
            if (int.TryParse(ProxyPortTextBox.Text, out int p)) _config.ProxyPort = p;
            ConfigStore.Save(_config);
        }

        // ===================== Settings / Donation =====================

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_config) { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                ConfigStore.Save(_config);
                if (_config.RunOnStartup) StartupManager.Enable(); else StartupManager.Disable();
            }
        }

        private void DonationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://reymit.ir/sajadsaber") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log("Could not open donation link: " + ex.Message);
            }
        }

        // ===================== Tray / window lifecycle =====================

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                HideToTray();
        }

        private void HideToTray()
        {
            Hide();
            _tray.Show();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _tray.Hide();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_reallyClosing) return;
            // The X button minimizes to tray instead of exiting outright - the
            // proxy is meant to keep running quietly in the background while
            // gaming. Use the tray icon's "Exit" to actually quit.
            e.Cancel = true;
            HideToTray();
        }

        private void ExitApplication()
        {
            _reallyClosing = true;
            try { _natEngine.Stop(); } catch { }
            try { _tcpRelay.Stop(); } catch { }
            try { _proxyMonitor.Stop(); } catch { }
            try { _tray.Dispose(); } catch { }
            Application.Current.Shutdown();
        }
    }
}
