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
        private Action? _identitySetter;
        private Func<Task>? _startupDelay;
        private Func<MainWindow>? _windowFactory;

        public App(Action? identitySetter = null, Func<Task>? startupDelay = null, Func<MainWindow>? windowFactory = null)
        {
            _identitySetter = identitySetter;
            _startupDelay = startupDelay;
            _windowFactory = windowFactory;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        private void SetApplicationIdentity()
        {
            if (_identitySetter != null) _identitySetter();
            else SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }

        private Task DelayStartupAsync() => _startupDelay?.Invoke() ?? Task.Delay(850);

        private MainWindow CreateMainWindow() => _windowFactory?.Invoke() ?? new MainWindow();

        protected override async void OnStartup(StartupEventArgs e)
        {
            var culture = CultureInfo.GetCultureInfo("es-AR");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            try
            {
                SetApplicationIdentity();
            }
            catch
            {
                // La identidad visual no debe impedir el uso de la calculadora.
            }

            base.OnStartup(e);

            var splash = new SplashWindow();
            splash.Show();

            await DelayStartupAsync();

            if (!splash.IsVisible)
            {
                Shutdown();
                return;
            }

            var window = CreateMainWindow();
            MainWindow = window;
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            splash.Close();
            window.Activate();
        }
    }
}
