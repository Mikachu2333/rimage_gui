using System;
using System.Windows;
using System.Windows.Threading;
using RimageGui.I18n;
using RimageGui.Theme;

namespace RimageGui
{
    public partial class App : Application
    {
        static App()
        {
            // This used to live in App.config so WPF would honour the manifest's
            // PerMonitorV2 declaration. Moving it into the executable removes the
            // need for a side-car .exe.config file.
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
        }

        private readonly ThemeService _themeService = new ThemeService();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Resolved once from the OS UI culture; there is no in-app switch.
            Loc.I.Current = Language.System;

            // Must run before any window is realised so the first frame is
            // already painted in the right theme instead of flashing light.
            _themeService.Initialize();

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var window = new MainWindow(_themeService);
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Unrecoverable faults should let the process die instead of being
            // swallowed by the normal "keep the shell alive" handler.
            if (e.Exception is OutOfMemoryException || e.Exception is StackOverflowException)
            {
                return;
            }

            // A shell that spawns a converter should surface a fault and keep the
            // queued file list alive rather than vanish with it.
            MessageBox.Show(
                e.Exception.Message,
                Loc.I["MsgTitleError"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _themeService.Shutdown();
            base.OnExit(e);
        }
    }
}
