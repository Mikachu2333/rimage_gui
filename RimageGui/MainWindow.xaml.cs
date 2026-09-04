using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RimageGui.Core;
using RimageGui.I18n;
using RimageGui.Models;
using RimageGui.Theme;
using RimageGui.ViewModels;

namespace RimageGui
{
    /// <summary>
    /// Owns everything that needs a window: dialogs, drag-drop plumbing, the
    /// context menu and the log box. Decisions stay in the view model; this
    /// class only translates user gestures into view-model calls.
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        /// <summary>Oldest lines are dropped once the log grows past this.</summary>
        private const int MaxLogLines = 2000;

        /// <summary>Lines kept when the log is trimmed.</summary>
        private const int TrimmedLogLines = 1200;

        private const double CheckColumnWidth = 36;
        private const double StatusColumnWidth = 80;
        private const double TableLayoutPadding = 12;
        private const double NameColumnRatio = 0.42;

        private readonly MainViewModel _viewModel;
        private readonly IThemeService _themeService;

        public MainWindow()
            : this(new ThemeService())
        {
        }

        public MainWindow(IThemeService themeService)
        {
            InitializeComponent();

            _themeService = themeService;
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            WireViewModelHooks();

            Loaded += OnLoaded;
            SourceInitialized += (_, __) => _themeService.ApplyTitleBar(this);
        }

        private void WireViewModelHooks()
        {
            _viewModel.RequestAddFiles = PickInputFilesAsync;
            _viewModel.RequestAddFolder = PickInputFolderAsync;
            _viewModel.RequestBrowseOutput = PickOutputDirectory;
            _viewModel.ShowError = ShowMessageBox;
            _viewModel.Confirm = ConfirmDialog;
            _viewModel.LogAppended += AppendLog;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // The backend probe runs once per process; logging its result here
            // means the user sees the version verdict before any job starts.
            Loaded -= OnLoaded;
            _viewModel.RunSafely(() => _viewModel.InitializeBackendAsync());
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // A running rimage would otherwise outlive the window that spawned it.
            _viewModel.CancelJob();
            base.OnClosing(e);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _viewModel.Dispose();
        }

        // ------------------------------------------------------------------
        // view model hooks
        // ------------------------------------------------------------------

        private async Task PickInputFilesAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.I["AddFiles"],
                Filter = FileScanner.FileDialogFilter,
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true && dialog.FileNames.Length > 0)
            {
                await _viewModel.AddFromPathsAsync(dialog.FileNames);
            }
        }

        private async Task PickInputFolderAsync()
        {
            var path = PickFolder(Loc.I["AddFolder"]);
            if (!string.IsNullOrEmpty(path))
            {
                await _viewModel.AddFromPathsAsync(new[] { path });
            }
        }

        private void PickOutputDirectory()
        {
            var path = PickFolder(Loc.I["SelectedDir"]);
            if (!string.IsNullOrEmpty(path))
            {
                _viewModel.OutputDirectory = PathUtil.NormalizeDirectory(path);
            }
        }

        private string PickFolder(string title)
        {
            return FolderPicker.PickFolder(new WindowInteropHelper(this).Handle, title, null);
        }

        private void ShowMessageBox(string title, string message)
        {
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private bool ConfirmDialog(string message)
        {
            return MessageBox.Show(
                this,
                message,
                Loc.I["ConfirmTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        // ------------------------------------------------------------------
        // log
        // ------------------------------------------------------------------

        private void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            LogBox.AppendText(line + Environment.NewLine);

            if (LogBox.LineCount > MaxLogLines)
            {
                var keepFrom = LogBox.LineCount - TrimmedLogLines;
                var offset = LogBox.GetCharacterIndexFromLineIndex(keepFrom);
                if (offset > 0)
                {
                    // Replacing the selected range avoids building an explicit
                    // substring and lets the TextBox manage its own text storage.
                    LogBox.Select(0, offset);
                    LogBox.SelectedText = string.Empty;
                }
            }

            LogBox.ScrollToEnd();
        }

        // ------------------------------------------------------------------
        // drag and drop
        // ------------------------------------------------------------------

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnFilesDropped(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                _viewModel.RunSafely(() => _viewModel.AddFromPathsAsync(paths));
            }

            e.Handled = true;
        }

        // ------------------------------------------------------------------
        // file table layout
        // ------------------------------------------------------------------

        private void OnFileListSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // The check and status columns are fixed; the rest is split between
            // name and output path so the ellipsis only appears on long paths.
            var available = e.NewSize.Width
                            - CheckColumnWidth
                            - StatusColumnWidth
                            - SystemParameters.VerticalScrollBarWidth
                            - TableLayoutPadding;

            const double minimumName = 160;
            const double minimumOutput = 120;
            if (available < minimumName + minimumOutput)
            {
                return;
            }

            var nameWidth = Math.Floor(available * NameColumnRatio);
            NameColumn.Width = nameWidth;
            OutputColumn.Width = Math.Floor(available - nameWidth);
        }

        // ------------------------------------------------------------------
        // context menu
        // ------------------------------------------------------------------

        private void OnOpenContainingFolder(object sender, RoutedEventArgs e)
        {
            if (!(FileList.SelectedItem is FileEntry entry))
            {
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"/select,\"{entry.FullPath}\"");
            }
            catch (Exception exception)
            {
                ShowMessageBox(Loc.I["MsgTitleError"], exception.Message);
            }
        }

        private void OnCopyPath(object sender, RoutedEventArgs e)
        {
            if (!(FileList.SelectedItem is FileEntry entry))
            {
                return;
            }

            try
            {
                Clipboard.SetText(entry.FullPath);
            }
            catch (Exception)
            {
                // The clipboard can be locked by another process momentarily.
            }
        }

        private void OnRemoveHighlighted(object sender, RoutedEventArgs e)
        {
            _viewModel.RemoveEntries(FileList.SelectedItems.OfType<FileEntry>());
        }
    }
}
