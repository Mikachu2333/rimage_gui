using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace RimageGui.Theme
{
    /// <summary>
    /// Follows the Windows app theme.
    /// </summary>
    /// <remarks>
    /// WPF has no notion of a system theme, so this does the two halves by hand:
    /// the client area swaps a palette <see cref="ResourceDictionary"/> (every
    /// style references those keys via <c>DynamicResource</c>, so the swap is
    /// live), and the non-client title bar is switched through DWM. The DWM
    /// attribute does not exist before Windows 10 1809, where the call fails
    /// harmlessly and the title bar simply stays light.
    /// </remarks>
    public static class ThemeManager
    {
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

        private static ResourceDictionary _palette;
        private static bool _hooked;

        public static bool IsDark { get; private set; }

        /// <summary>Reads the current preference; defaults to light when unset.</summary>
        public static bool IsSystemDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    // Absent on Windows 7/8, where only a light theme exists.
                    if (key?.GetValue("AppsUseLightTheme") is int value)
                    {
                        return value == 0;
                    }
                }
            }
            catch (Exception)
            {
                // A locked or missing key just means "light".
            }

            return false;
        }

        /// <summary>
        /// Installs the palette and starts following system changes. Call once,
        /// before the main window is shown.
        /// </summary>
        public static void Initialize()
        {
            Apply(IsSystemDark());

            if (_hooked)
            {
                return;
            }

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _hooked = true;
        }

        public static void Shutdown()
        {
            if (!_hooked)
            {
                return;
            }

            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _hooked = false;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General)
            {
                return;
            }

            // Raised on a system thread; the dictionary swap must be on the UI one.
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dark = IsSystemDark();
                if (dark == IsDark && _palette != null)
                {
                    return;
                }

                Apply(dark);

                foreach (Window window in Application.Current.Windows)
                {
                    ApplyTitleBar(window);
                }
            }));
        }

        private static void Apply(bool dark)
        {
            var application = Application.Current;
            if (application == null)
            {
                return;
            }

            var source = dark ? "Dark" : "Light";
            var replacement = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Theme/Palette." + source + ".xaml", UriKind.Absolute)
            };

            var merged = application.Resources.MergedDictionaries;
            if (_palette != null && merged.Contains(_palette))
            {
                // Swapping in place keeps the palette ahead of Styles.xaml, so
                // the styles' DynamicResource lookups resolve against it.
                merged[merged.IndexOf(_palette)] = replacement;
            }
            else
            {
                merged.Insert(0, replacement);
            }

            _palette = replacement;
            IsDark = dark;
        }

        /// <summary>Paints the non-client title bar to match the current theme.</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var enabled = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    // Windows 10 builds before 20H1 used a different ordinal.
                    DwmSetWindowAttribute(
                        handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
                }
            }
            catch (Exception)
            {
                // dwmapi is absent or the attribute is unsupported on this build.
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr window, int attribute, ref int value, int size);
    }
}
