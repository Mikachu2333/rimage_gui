using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace RimageGui
{
    /// <summary>
    /// A small window that is on screen long before the main window can be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cold-starting WPF on .NET Framework costs roughly 1.2 s on the development
    /// machine before even an empty window appears, and the real window needs
    /// another 1.5 s of XAML parsing, layout and first render. None of that can be
    /// accelerated from inside WPF, so this window deliberately is not WPF: it is a
    /// plain Win32 window on its own thread, drawn with GDI, which needs only a
    /// window handle and a message pump. It appears a few tens of milliseconds
    /// after the process starts, while the main thread is still loading
    /// PresentationFramework.
    /// </para>
    /// <para>
    /// The card is a colour-keyed layered window: the background is filled with
    /// <see cref="KeyColor"/>, which the window manager treats as transparent, and
    /// everything drawn on top of it is opaque. Per-pixel alpha
    /// (<c>UpdateLayeredWindow</c>) would look slightly better at the corners, but
    /// it is not used because it fails with <c>ERROR_GEN_FAILURE</c> (31) on some
    /// systems — including the one this was developed on — where the colour key
    /// path works everywhere. Nothing here is anti-aliased, since blended edges
    /// against the key colour would fringe.
    /// </para>
    /// <para>
    /// It is created with <c>WS_EX_NOACTIVATE</c> and shown with
    /// <c>SW_SHOWNOACTIVATE</c> so it never steals focus from the window that is
    /// about to appear behind it, and every native failure path is swallowed: a
    /// splash that cannot be shown must never be the reason the application does
    /// not start.
    /// </para>
    /// </remarks>
    internal static class SplashWindow
    {
        private const int CardWidth = 460;
        private const int CardHeight = 180;
        private const int IconSize = 64;
        private const int IconLeft = 32;
        private const int IconTop = 58;

        /// <summary>Drawn as transparent by the window manager; never used for content.</summary>
        private const int KeyColor = 0x00FF00FF;

        /// <summary>How long the message loop stays alive without being closed.</summary>
        private const int LifetimeMilliseconds = 30000;

        private static SplashThread _splash;

        /// <summary>The native handle once it exists, for tests; <see cref="IntPtr.Zero"/> before that.</summary>
        public static IntPtr Handle => _splash?.Handle ?? IntPtr.Zero;

        /// <summary>
        /// Puts the splash on screen and returns immediately. Safe to call more than
        /// once: only the first call starts a thread.
        /// </summary>
        public static void Start()
        {
            if (_splash != null)
            {
                return;
            }

            var splash = new SplashThread();
            _splash = splash;
            new Thread(splash.Run)
            {
                IsBackground = true,
                Name = "rimage-gui splash"
            }.Start();
        }

        /// <summary>Closes the splash and waits for its thread to end. Idempotent.</summary>
        public static void Close()
        {
            var splash = Interlocked.Exchange(ref _splash, null);
            if (splash == null)
            {
                return;
            }

            splash.SignalClose();
            splash.Join();
        }

        /// <summary>
        /// The splash's own thread: create the window, draw it, then pump messages
        /// until the UI thread signals that the main window is up.
        /// </summary>
        private sealed class SplashThread
        {
            private IntPtr _window;
            private volatile bool _closing;
            private Thread _thread;

            public IntPtr Handle => _window;

            public void Run()
            {
                _thread = Thread.CurrentThread;

                try
                {
                    CreateAndPump();
                }
                catch (Exception)
                {
                    // A splash that cannot be shown is not a reason to fail startup.
                }
            }

            public void SignalClose()
            {
                _closing = true;
            }

            public void Join()
            {
                var thread = _thread;
                if (thread == null || thread == Thread.CurrentThread)
                {
                    return;
                }

                try
                {
                    thread.Join(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // Already gone.
                }
            }

            private void CreateAndPump()
            {
                // A predefined system class, deliberately: the splash needs no message
                // handling of its own — it is closed by this thread, not by the user —
                // so a custom window class would only add a WNDCLASSEX to marshal and
                // another way for startup to fail.
                _window = CreateWindowEx(
                    WindowExLayered | WindowExNoActivate | WindowExTopmost | WindowExToolWindow,
                    "Button",
                    "Rimage GUI",
                    WindowPopup | ButtonOwnerDraw,
                    0, 0, CardWidth, CardHeight,
                    IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

                if (_window == IntPtr.Zero)
                {
                    return;
                }

                var width = GetSystemMetrics(SystemMetricCxScreen);
                var height = GetSystemMetrics(SystemMetricCyScreen);
                SetWindowPos(
                    _window, IntPtr.Zero,
                    (width - CardWidth) / 2, (height - CardHeight) / 2, 0, 0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate);

                if (!SetLayeredWindowAttributes(_window, KeyColor, 0, LwaColorKey))
                {
                    DestroyWindow(_window);
                    _window = IntPtr.Zero;
                    return;
                }

                // Made visible before it is drawn: pixels written to a layered
                // window's DC before it is shown are not kept, which shows up as a
                // fully transparent card.
                ShowWindow(_window, SwShowNoActivate);

                var drawn = Draw();
                if (drawn != null)
                {
                    DestroyWindow(_window);
                    _window = IntPtr.Zero;
                    return;
                }

                PumpMessages();

                DestroyWindow(_window);
                _window = IntPtr.Zero;
            }

            /// <summary>
            /// Draws the card into an off-screen bitmap and copies it to the window
            /// in one go, avoiding a visible half-painted state.
            /// </summary>
            /// <returns>Null on success, otherwise why the card could not be drawn.</returns>
            private string Draw()
            {
                var windowDc = GetDC(_window);
                if (windowDc == IntPtr.Zero)
                {
                    return $"window-dc win32={Marshal.GetLastWin32Error()}";
                }

                var memoryDc = CreateCompatibleDC(windowDc);
                if (memoryDc == IntPtr.Zero)
                {
                    _ = ReleaseDC(_window, windowDc);
                    return $"memory-dc win32={Marshal.GetLastWin32Error()}";
                }

                var bitmap = CreateCompatibleBitmap(windowDc, CardWidth, CardHeight);
                if (bitmap == IntPtr.Zero)
                {
                    DeleteDC(memoryDc);
                    _ = ReleaseDC(_window, windowDc);
                    return $"bitmap win32={Marshal.GetLastWin32Error()}";
                }

                var previous = SelectObject(memoryDc, bitmap);
                try
                {
                    var dark = IsSystemDark();
                    var keyBrush = CreateSolidBrush(KeyColor);
                    var cardBrush = CreateSolidBrush(dark ? 0x00202020 : 0x00FAFAFA);
                    var textBrush = CreateSolidBrush(dark ? 0x00F0F0F0 : 0x001C1C1C);

                    try
                    {
                        var card = new NativeRect { right = CardWidth, bottom = CardHeight };
                        _ = FillRect(memoryDc, ref card, keyBrush);

                        var inner = new NativeRect { left = 1, top = 1, right = CardWidth - 1, bottom = CardHeight - 1 };
                        _ = FillRect(memoryDc, ref inner, cardBrush);
                    }
                    finally
                    {
                        DeleteObject(keyBrush);
                        DeleteObject(cardBrush);
                    }

                    try
                    {
                        DrawIcon(memoryDc);
                        DrawText(memoryDc, textBrush, dark);
                    }
                    finally
                    {
                        DeleteObject(textBrush);
                    }

                    var copied = BitBlt(windowDc, 0, 0, CardWidth, CardHeight, memoryDc, 0, 0, SrcCopy);
                    return copied ? null : $"blit win32={Marshal.GetLastWin32Error()}";
                }
                finally
                {
                    SelectObject(memoryDc, previous);
                    DeleteObject(bitmap);
                    DeleteDC(memoryDc);
                    _ = ReleaseDC(_window, windowDc);
                }
            }

            /// <summary>
            /// Draws this executable's own icon with the shell's loader, so no GDI+
            /// and no resource-stream plumbing is needed on a thread that runs before
            /// the application is initialized.
            /// </summary>
            private static void DrawIcon(IntPtr dc)
            {
                try
                {
                    var icon = LoadImage(GetModuleHandle(null), "#32512", ImageIcon, IconSize, IconSize, LrShared);
                    if (icon == IntPtr.Zero)
                    {
                        return;
                    }

                    _ = DrawIconEx(dc, IconLeft, IconTop, icon, IconSize, IconSize, 0, IntPtr.Zero, DiNormal);
                }
                catch (Exception)
                {
                    // The icon is decoration; a card without it is still a splash.
                }
            }

            private static void DrawText(IntPtr dc, IntPtr brush, bool dark)
            {
                var previousFont = IntPtr.Zero;
                var titleFont = IntPtr.Zero;
                var bodyFont = IntPtr.Zero;

                try
                {
                    _ = SetBkMode(dc, Transparent);
                    var previousColor = SetTextColor(dc, dark ? 0x00F0F0F0 : 0x001C1C1C);

                    titleFont = CreateFont(
                        -30, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, DefaultQuality, 0, "Microsoft YaHei UI");
                    bodyFont = CreateFont(
                        -19, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, DefaultQuality, 0, "Microsoft YaHei UI");

                    var textLeft = IconLeft + IconSize + 24;

                    previousFont = SelectObject(dc, titleFont);
                    WriteLine(dc, textLeft, 48, ProductName());

                    SelectObject(dc, bodyFont);
                    WriteLine(dc, textLeft, 90, VersionText());
                    WriteLine(dc, textLeft, 114, I18n.Loc.I["StartingUp"]);

                    _ = SetTextColor(dc, previousColor);
                }
                catch (Exception)
                {
                    // Text is decoration too.
                }
                finally
                {
                    if (previousFont != IntPtr.Zero)
                    {
                        SelectObject(dc, previousFont);
                    }

                    if (titleFont != IntPtr.Zero)
                    {
                        DeleteObject(titleFont);
                    }

                    if (bodyFont != IntPtr.Zero)
                    {
                        DeleteObject(bodyFont);
                    }
                }
            }

            private static void WriteLine(IntPtr dc, int x, int y, string text)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    _ = TextOut(dc, x, y, text, text.Length);
                }
            }

            private void PumpMessages()
            {
                var stopwatch = Stopwatch.StartNew();
                var message = default(NativeMessage);

                while (!_closing && stopwatch.ElapsedMilliseconds < LifetimeMilliseconds)
                {
                    while (PeekMessage(out message, IntPtr.Zero, 0, 0, PeekRemove))
                    {
                        if (message.Message == WmQuit)
                        {
                            return;
                        }

                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
                    }

                    Thread.Sleep(20);
                }
            }
        }

        private static string ProductName()
        {
            try
            {
                var product = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyProductAttribute>()?.Product;
                return string.IsNullOrEmpty(product) ? "Rimage GUI" : product;
            }
            catch (Exception)
            {
                return "Rimage GUI";
            }
        }

        private static string VersionText()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                if (!string.IsNullOrEmpty(informational))
                {
                    // Strip the source-revision suffix the SDK appends.
                    var plus = informational.IndexOf('+');
                    return plus > 0 ? informational.Substring(0, plus) : informational;
                }

                return assembly.GetName().Version?.ToString(3) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads the app theme the same way the WPF theme service does, so the
        /// splash does not flash light in front of a dark window.
        /// </summary>
        private static bool IsSystemDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
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

        // ------------------------------------------------------------------
        // win32
        // ------------------------------------------------------------------

        private const int WindowPopup = unchecked((int)0x80000000);
        private const int ButtonOwnerDraw = 0x0000000B;
        private const int WindowExLayered = 0x00080000;
        private const int WindowExNoActivate = 0x08000000;
        private const int WindowExTopmost = 0x00000008;
        private const int WindowExToolWindow = 0x00000080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const int SwShowNoActivate = 4;
        private const uint PeekRemove = 0x0001;
        private const uint WmQuit = 0x0012;
        private const int SystemMetricCxScreen = 0;
        private const int SystemMetricCyScreen = 1;
        private const uint LwaColorKey = 0x00000001;
        private const uint SrcCopy = 0x00CC0020;
        private const int Transparent = 1;
        private const uint DefaultQuality = 0;
        private const uint ImageIcon = 1;
        private const uint LrShared = 0x00008000;
        private const uint DiNormal = 0x0003;
        private const string IconResource = "#32512";

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Window;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PointX;
            public int PointY;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int extendedStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(
            out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr dc, ref NativeRect rect, IntPtr brush);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(
            IntPtr instance, string name, uint type, int width, int height, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DrawIconEx(
            IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, uint flags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int color);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFont(
            int height, int width, int angle, int orientation, int weight, uint italic,
            uint underline, uint strike, uint charSet, uint precision, uint clip, uint quality,
            uint pitchAndFamily, string face);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TextOut(IntPtr dc, int x, int y, string text, int length);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr dc, int mode);

        [DllImport("gdi32.dll")]
        private static extern int SetTextColor(IntPtr dc, int color);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(
            IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }
}
