using System.Windows;
using System.Windows.Input;

namespace Cashflow.Windows
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        private void CloseSplash_Click(object sender, RoutedEventArgs e) => Close();

        private void SplashWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
