using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using SKRMTeam.Services;

namespace SKRMTeam
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settings;

        public SettingsWindow(SettingsService settings)
        {
            InitializeComponent();
            _settings = settings;

            TokenBox.Text = _settings.Settings.LztToken;
            TopmostCheck.IsChecked = _settings.Settings.Topmost;
            LockKeyBox.Text = _settings.Settings.LockKey;
            HideKeyBox.Text = _settings.Settings.HideKey;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            _settings.Settings.LztToken = TokenBox.Text.Trim();
            _settings.Settings.Topmost = TopmostCheck.IsChecked == true;
            _settings.Settings.LockKey = LockKeyBox.Text.Trim();
            _settings.Settings.HideKey = HideKeyBox.Text.Trim();
            _settings.Save();
            Close();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void LogBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow main)
            {
                var logs = main.GetLogs();
                var logWin = new LogWindow(logs);
                logWin.Owner = this;
                logWin.Show();
            }
        }

        private void TopmostCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (Owner != null)
                Owner.Topmost = TopmostCheck.IsChecked == true;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
