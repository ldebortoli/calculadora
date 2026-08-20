using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Cashflow.Windows
{
    public partial class App : Application
    {
        private const string AppUserModelId = "Local.Calculadora.Desktop";

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        protected override async void OnStartup(StartupEventArgs e)
        {
            var culture = CultureInfo.GetCultureInfo("es-AR");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            try
            {
                SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            }
            catch
            {
                // La identidad visual no debe impedir el uso de la calculadora.
            }

            base.OnStartup(e);

            var splash = new SplashWindow();
            splash.Show();

            await Task.Delay(850);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            splash.Close();
            window.Activate();
        }
    }
}
