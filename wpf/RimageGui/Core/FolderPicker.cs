using System;
using System.Runtime.InteropServices;

namespace RimageGui.Core
{
    /// <summary>
    /// Modern folder browser backed by the shell's <c>IFileOpenDialog</c> with
    /// <c>FOS_PICKFOLDERS</c>.
    /// </summary>
    /// <remarks>
    /// WPF ships no folder picker, and WinForms' <c>FolderBrowserDialog</c> on
    /// .NET Framework is still the cramped Windows-2000 tree view. This is the
    /// same dialog Explorer uses, available on Vista and later — which covers
    /// every OS this app supports — so there is no legacy fallback path.
    /// </remarks>
    public static class FolderPicker
    {
        /// <summary>Returns the chosen directory, or empty string when dismissed or failed.</summary>
        public static string PickFolder(IntPtr owner, string title, string initialDirectory)
        {
            IFileDialog dialog = null;
            try
            {
                dialog = (IFileDialog)new FileOpenDialogRcw();

                dialog.SetOptions(FileOpenOptions.PickFolders |
                                  FileOpenOptions.ForceFileSystem |
                                  FileOpenOptions.PathMustExist |
                                  FileOpenOptions.NoValidate);

                if (!string.IsNullOrEmpty(title))
                {
                    dialog.SetTitle(title);
                }

                if (!string.IsNullOrEmpty(initialDirectory))
                {
                    TrySetInitialFolder(dialog, initialDirectory);
                }

                // Cancellation is also a non-zero HRESULT (0x800704C7), so one
                // check covers both "cancelled" and "failed".
                var result = dialog.Show(owner);
                if (result != 0)
                {
                    return string.Empty;
                }

                dialog.GetResult(out var item);
                if (item == null)
                {
                    return string.Empty;
                }

                try
                {
                    item.GetDisplayName(SIGDN.FileSysPath, out var path);
                    return string.IsNullOrEmpty(path) ? null : path;
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            catch (Exception)
            {
                // A shell failure here should not take the app down; the caller
                // simply keeps the previously selected directory.
                return string.Empty;
            }
            finally
            {
                if (dialog != null)
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
        }

        private static void TrySetInitialFolder(IFileDialog dialog, string directory)
        {
            try
            {
                var hr = SHCreateItemFromParsingName(
                    directory, IntPtr.Zero, typeof(IShellItem).GUID, out var item);

                if (hr != 0 || item == null)
                {
                    return;
                }

                try
                {
                    dialog.SetFolder(item);
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            catch (Exception)
            {
                // A stale path just means the dialog opens at its default place.
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [Flags]
        private enum FileOpenOptions : uint
        {
            NoValidate = 0x00000100,
            PathMustExist = 0x00000800,
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040
        }

        private enum SIGDN : uint
        {
            FileSysPath = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialogRcw
        {
        }

        /// <summary>
        /// Only the declaration ORDER defines the COM vtable, so the members this
        /// code never calls are declared as correctly-positioned stubs.
        /// </summary>
        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            // --- IModalWindow ---
            [PreserveSig]
            int Show(IntPtr parent);

            // --- IFileDialog ---
            void SetFileTypes();

            void SetFileTypeIndex();

            void GetFileTypeIndex();

            void Advise();

            void Unadvise();

            void SetOptions(FileOpenOptions options);

            void GetOptions();

            void SetDefaultFolder();

            void SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItem folder);

            void GetFolder();

            void GetCurrentSelection();

            void SetFileName();

            void GetFileName();

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

            void SetOkButtonLabel();

            void SetFileNameLabel();

            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem item);

            void AddPlace();

            void SetDefaultExtension();

            void Close();

            void SetClientGuid();

            void ClearClientData();

            void SetFilter();
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();

            void GetParent();

            void GetDisplayName(SIGDN name, [MarshalAs(UnmanagedType.LPWStr)] out string value);

            void GetAttributes();

            void Compare();
        }
    }
}
