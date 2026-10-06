using System.Windows;
using SasamaProxy.Core;

namespace SasamaProxy
{
    public partial class AddProcessWindow : Window
    {
        public string SelectedProcessName { get; private set; }

        public AddProcessWindow()
        {
            InitializeComponent();
            RunningProcessesListBox.ItemsSource = RunningProcessLister.GetRunningProcessNames();
        }

        private void RunningProcessesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RunningProcessesListBox.SelectedItem is string name)
                ManualNameTextBox.Text = name;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            string name = ManualNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Type a program name or pick one from the list.", "Add Program", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedProcessName = name;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
