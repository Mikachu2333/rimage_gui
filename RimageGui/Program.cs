using System;
using System.Diagnostics;
using System.Windows;

namespace RimageGui
{
    /// <summary>
    /// The application's entry point, which exists only to get something on screen
    /// before WPF is loaded.
    /// </summary>
    /// <remarks>
    /// The SDK-generated <c>Main</c> constructs the <see cref="Application"/> first
    /// and only then shows a window; cold-starting WPF that way spends well over a
    /// second with nothing visible. Starting the splash here — a plain Win32 window
    /// on its own thread, see <see cref="SplashWindow"/> — puts the card on screen
    /// in a few tens of milliseconds, and WPF comes up behind it and takes over.
    /// </remarks>
    public static class Program
    {
        [STAThread]
        [DebuggerNonUserCode]
        public static void Main()
        {
            SplashWindow.Start();

            try
            {
                var application = new App();
                application.InitializeComponent();
                application.Run();
            }
            finally
            {
                // Never leave a splash thread behind, whatever happened to the app.
                SplashWindow.Close();
            }
        }
    }
}
