using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace RimageGui.Models
{
    public enum FileStatus
    {
        Pending,
        Running,
        Done,
        Failed,
        Skipped
    }

    /// <summary>
    /// One row of the file table. Thousands of these are live at once, so the
    /// type stays small and raises change notifications only for the three
    /// properties the grid actually re-reads.
    /// </summary>
    public sealed class FileEntry : INotifyPropertyChanged
    {
        private bool _isChecked = true;
        private FileStatus _status = FileStatus.Pending;
        private string _outputPath;
        private string _error;

        public FileEntry(string fullPath)
        {
            FullPath = fullPath;
            Name = Path.GetFileName(fullPath);
            Directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        }

        /// <summary>Absolute input path; immutable and used as the identity key.</summary>
        public string FullPath { get; }

        public string Name { get; }

        public string Directory { get; }

        /// <summary>Whether this row participates in the next run.</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }

        public FileStatus Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public string OutputPath
        {
            get => _outputPath;
            set => Set(ref _outputPath, value);
        }

        /// <summary>Failure detail shown as the row tooltip; null when healthy.</summary>
        public string Error
        {
            get => _error;
            set => Set(ref _error, value);
        }

        public void ResetForRun()
        {
            Status = FileStatus.Pending;
            OutputPath = null;
            Error = null;
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
