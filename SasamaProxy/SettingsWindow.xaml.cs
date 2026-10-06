using System.Windows;
using SasamaProxy.Models;

namespace SasamaProxy
{
    public partial class SettingsWindow : Window
    {
        private readonly AppConfig _config;

        public SettingsWindow(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            RunOnStartupCheckBox.IsChecked = _config.RunOnStartup;
            StartMinimizedCheckBox.IsChecked = _config.StartMinimizedToTray;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _config.RunOnStartup = RunOnStartupCheckBox.IsChecked == true;
            _config.StartMinimizedToTray = StartMinimizedCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
