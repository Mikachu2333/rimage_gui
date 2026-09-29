using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace RimageGui.Core
{
    /// <summary>
    /// Reads the version resource Windows keeps inside a PE image without ever
    /// executing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading the resource is a couple of file-mapped reads — microseconds —
    /// while asking the program itself costs a full process launch, which on a
    /// cold 25 MB binary behind an on-access virus scanner that rescans
    /// %LocalAppData% can take seconds. The GUI only needs to know which build it
    /// is looking at, so it asks Windows instead of running the program.
    /// </para>
    /// <para>
    /// The value reported is the resource's <c>ProductVersion</c> string
    /// (<c>0.14.0</c>), a separate field from <c>FileVersion</c>, so the answer
    /// reads exactly like the program's own <c>--version</c> output rather than
    /// the numeric <c>VS_FIXEDFILEINFO</c> whose trailing zeros would spell the
    /// same build as <c>0.14.0.0</c>.
    /// </para>
    /// <para>
    /// The class is public so the test project can prove the reader against the
    /// real backend; nothing outside <see cref="BackendExtractor"/> and the tests
    /// is meant to call it.
    /// </para>
    /// </remarks>
    public static class FileVersionResource
    {
        /// <summary>UTF-16; the only code page this reader queries.</summary>
        private const ushort UnicodeCodePage = 1200;

        /// <summary>Returns <c>ProductName</c> of the image, or null when absent.</summary>
        public static string ReadProductName(string path)
        {
            return ReadString(path, "ProductName");
        }

        /// <summary>Returns <c>ProductVersion</c> of the image, or null when absent.</summary>
        public static string ReadProductVersion(string path)
        {
            return ReadString(path, "ProductVersion");
        }

        /// <summary>
        /// Reads one string from the image's version resource, returning null for
        /// anything that is not a readable PE image carrying that field — a missing
        /// file, a truncated download, a foreign file, or a resource laid out
        /// differently than this reader expects. Callers treat null as "unknown"
        /// and fall back to the slow, definitive launch probe.
        /// </summary>
        private static string ReadString(string path, string field)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(field))
            {
                return null;
            }

            try
            {
                return ReadStringCore(path, field);
            }
            catch (Exception exception) when (
                exception is EntryPointNotFoundException ||
                exception is DllNotFoundException ||
                exception is BadImageFormatException ||
                exception is MarshalDirectiveException ||
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is OutOfMemoryException)
            {
                return null;
            }
        }

        private static string ReadStringCore(string path, string field)
        {
            var size = GetFileVersionInfoSize(path, out _);
            if (size == 0)
            {
                return null;
            }

            var block = new byte[size];
            if (!GetFileVersionInfo(path, 0, size, block))
            {
                return null;
            }

            // VerQueryValue hands back pointers *into* block, so the array has to
            // stay pinned for as long as those pointers are dereferenced — not
            // merely for the duration of the query call.
            var handle = GCHandle.Alloc(block, GCHandleType.Pinned);
            try
            {
                var root = handle.AddrOfPinnedObject();

                // Several language/charset blocks can coexist; the first UTF-16 one
                // wins, which is what the toolchain emits for the backend.
                if (!TryFindUnicodeTranslation(root, out var language, out var codePage))
                {
                    return null;
                }

                return QueryString(root, language, codePage, field);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Finds the first UTF-16 translation in the resource's
        /// <c>\VarFileInfo\Translation</c> table.
        /// </summary>
        private static bool TryFindUnicodeTranslation(
            IntPtr root, out ushort language, out ushort codePage)
        {
            language = 0;
            codePage = 0;

            if (!VerQueryValue(root, @"\VarFileInfo\Translation", out var data, out var length))
            {
                return false;
            }

            // Each translation is a pair of 16-bit language/code-page identifiers.
            for (uint offset = 0; offset + 4 <= length; offset += 4)
            {
                var candidateLanguage = unchecked((ushort)Marshal.ReadInt16(data, (int)offset));
                var candidateCodePage = unchecked((ushort)Marshal.ReadInt16(data, (int)offset + 2));
                if (candidateCodePage == UnicodeCodePage)
                {
                    language = candidateLanguage;
                    codePage = candidateCodePage;
                    return true;
                }
            }

            return false;
        }

        private static string QueryString(
            IntPtr root, ushort language, ushort codePage, string field)
        {
            var subBlock = string.Format(
                CultureInfo.InvariantCulture,
                @"\StringFileInfo\{0:x4}{1:x4}\{2}",
                language,
                codePage,
                field);

            if (!VerQueryValue(root, subBlock, out var data, out var length) || length == 0)
            {
                return null;
            }

            // The reported length is in characters and includes the trailing NUL.
            var value = Marshal.PtrToStringUni(data, (int)length);
            if (value == null)
            {
                return null;
            }

            value = value.TrimEnd('\0').Trim();
            return value.Length == 0 ? null : value;
        }

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileVersionInfoSize(string fileName, out uint handle);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileVersionInfo(string fileName, uint handle, uint length, byte[] data);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VerQueryValue(IntPtr block, string subBlock, out IntPtr buffer, out uint length);
    }
}
