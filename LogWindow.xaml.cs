using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace SKRMTeam
{
    public partial class LogWindow : Window
    {
        public LogWindow(List<string> logs)
        {
            InitializeComponent();
            LogBox.Text = string.Join("\n", logs);
            Loaded += (s, e) => LogScroll.ScrollToEnd();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    }
}
