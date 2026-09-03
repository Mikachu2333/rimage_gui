using System;
using System.Windows;
using System.Windows.Threading;
using RimageGui.I18n;
using RimageGui.Theme;

namespace RimageGui
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Resolved once from the OS UI culture; there is no in-app switch.
            Loc.I.Current = Language.System;

            // Must run before any window is realised so the first frame is
            // already painted in the right theme instead of flashing light.
            ThemeManager.Initialize();

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
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
            ThemeManager.Shutdown();
            base.OnExit(e);
        }
    }
}
