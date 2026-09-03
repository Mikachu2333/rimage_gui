using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RimageGui.Core;
using RimageGui.I18n;
using RimageGui.Models;

namespace RimageGui.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly Dictionary<FileEntry, bool> _checkedShadow = new Dictionary<FileEntry, bool>();

        private CancellationTokenSource _jobCancellation;
        private CancellationTokenSource _scanCancellation;
        private string _backendPath;

        public MainViewModel()
        {
            Files = new RangeObservableCollection<FileEntry>();
            Files.CollectionChanged += OnFilesChanged;

            AddFilesCommand = new RelayCommand(() => RequestAddFiles?.Invoke(), () => !IsRunning);
            AddFolderCommand = new RelayCommand(() => RequestAddFolder?.Invoke(), () => !IsRunning);
            BrowseOutputCommand = new RelayCommand(() => RequestBrowseOutput?.Invoke(), () => !IsRunning);
            SelectAllCommand = new RelayCommand(() => SetAllChecked(true), () => !IsRunning && TotalCount > 0);
            DeselectAllCommand = new RelayCommand(() => SetAllChecked(false), () => !IsRunning && TotalCount > 0);
            RemoveCheckedCommand = new RelayCommand(RemoveChecked, () => !IsRunning && CheckedCount > 0);
            ClearCommand = new RelayCommand(ClearFiles, () => !IsRunning && TotalCount > 0);
            StartCommand = new RelayCommand(() => _ = ToggleRunAsync());
        }

        // ------------------------------------------------------------------
        // View hooks. The view owns dialogs and the file pickers; the view model
        // owns the decisions. Keeping the split here is what lets every rule
        // below stay testable without a window.
        // ------------------------------------------------------------------

        public Action RequestAddFiles;
        public Action RequestAddFolder;
        public Action RequestBrowseOutput;
        public Action<string, string> ShowError;
        public Func<string, bool> Confirm;
        public event Action<string> LogAppended;

        public RangeObservableCollection<FileEntry> Files { get; }

        public RelayCommand AddFilesCommand { get; }

        public RelayCommand AddFolderCommand { get; }

        public RelayCommand BrowseOutputCommand { get; }

        public RelayCommand SelectAllCommand { get; }

        public RelayCommand DeselectAllCommand { get; }

        public RelayCommand RemoveCheckedCommand { get; }

        public RelayCommand ClearCommand { get; }

        public RelayCommand StartCommand { get; }

        // ------------------------------------------------------------------
        // file list
        // ------------------------------------------------------------------

        private int _checkedCount;

        public int TotalCount => Files.Count;

        public int CheckedCount
        {
            get => _checkedCount;
            private set
            {
                if (_checkedCount == value)
                {
                    return;
                }

                _checkedCount = value;
                Raise();
                Raise(nameof(SelectedCountText));
                Refresh();
            }
        }

        public string SelectedCountText =>
            Loc.I["SelectedCount"] + ": " + CheckedCount + "/" + TotalCount;

        public void AddPaths(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return;
            }

            var known = new HashSet<string>(Files.Select(f => PathUtil.Key(f.FullPath)), StringComparer.Ordinal);
            var additions = new List<FileEntry>(paths.Count);

            foreach (var path in paths)
            {
                if (known.Add(PathUtil.Key(path)))
                {
                    additions.Add(new FileEntry(path));
                }
            }

            if (additions.Count == 0)
            {
                Log(Loc.I["DropHint"]);
                return;
            }

            Files.AddRange(additions);
        }

        private void SetAllChecked(bool value)
        {
            foreach (var entry in Files)
            {
                entry.IsChecked = value;
            }
        }

        private void RemoveChecked()
        {
            var doomed = Files.Where(f => f.IsChecked).ToList();
            if (doomed.Count == 0)
            {
                return;
            }

            Files.RemoveRange(doomed);
        }

        private void ClearFiles() => Files.Clear();

        public void RemoveEntries(IEnumerable<FileEntry> entries)
        {
            var doomed = entries?.ToList();
            if (doomed == null || doomed.Count == 0)
            {
                return;
            }

            Files.RemoveRange(doomed);
        }

        private void OnFilesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FileEntry entry in e.OldItems)
                {
                    entry.PropertyChanged -= OnEntryChanged;
                    _checkedShadow.Remove(entry);
                }
            }

            if (e.NewItems != null)
            {
                foreach (FileEntry entry in e.NewItems)
                {
                    entry.PropertyChanged += OnEntryChanged;
                    _checkedShadow[entry] = entry.IsChecked;
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // A bulk add or removal reports no items, so the subscriptions and
                // the tally are rebuilt from the collection itself.
                foreach (var entry in _checkedShadow.Keys.ToList())
                {
                    entry.PropertyChanged -= OnEntryChanged;
                }

                _checkedShadow.Clear();
                foreach (var entry in Files)
                {
                    entry.PropertyChanged += OnEntryChanged;
                    _checkedShadow[entry] = entry.IsChecked;
                }
            }

            RecountChecked();
            Raise(nameof(TotalCount));
            Raise(nameof(SelectedCountText));
            Refresh();
        }

        private void OnEntryChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FileEntry.IsChecked) || !(sender is FileEntry entry))
            {
                return;
            }

            // Tracked incrementally: rescanning the whole list on every checkbox
            // click is what made large lists stutter.
            if (_checkedShadow.TryGetValue(entry, out var previous) && previous == entry.IsChecked)
            {
                return;
            }

            _checkedShadow[entry] = entry.IsChecked;
            CheckedCount += entry.IsChecked ? 1 : -1;
        }

        private void RecountChecked()
        {
            var count = 0;
            foreach (var entry in Files)
            {
                if (entry.IsChecked)
                {
                    count++;
                }
            }

            CheckedCount = count;
        }

        // ------------------------------------------------------------------
        // encoding
        // ------------------------------------------------------------------

        public OutputFormat[] Formats => FormatInfo.All;

        public ResizeFilter[] Filters => FilterInfo.All;

        private OutputFormat _format = OutputFormat.MozJpeg;

        public OutputFormat Format
        {
            get => _format;
            set
            {
                if (!Set(ref _format, value))
                {
                    return;
                }

                Raise(nameof(QualityEnabled));
            }
        }

        /// <summary>Lossless codecs reject <c>--quality</c>, so the field is disabled rather than ignored.</summary>
        public bool QualityEnabled => !IsRunning && _format.SupportsQuality();

        private int _quality = 85;

        public int Quality
        {
            get => _quality;
            set => Set(ref _quality, value);
        }

        private bool _quantizationEnabled;

        public bool QuantizationEnabled
        {
            get => _quantizationEnabled;
            set
            {
                if (!Set(ref _quantizationEnabled, value))
                {
                    return;
                }

                Raise(nameof(QuantizationInputEnabled));
                Raise(nameof(DitheringToggleEnabled));
                Raise(nameof(DitheringInputEnabled));
            }
        }

        public bool QuantizationInputEnabled => !IsRunning && _quantizationEnabled;

        private int _quantization = 90;

        public int Quantization
        {
            get => _quantization;
            set => Set(ref _quantization, value);
        }

        private bool _ditheringEnabled;

        public bool DitheringEnabled
        {
            get => _ditheringEnabled;
            set
            {
                if (!Set(ref _ditheringEnabled, value))
                {
                    return;
                }

                Raise(nameof(DitheringInputEnabled));
            }
        }

        /// <summary>rimage only applies dithering alongside quantization.</summary>
        public bool DitheringToggleEnabled => !IsRunning && _quantizationEnabled;

        public bool DitheringInputEnabled => DitheringToggleEnabled && _ditheringEnabled;

        private int _dithering = 90;

        public int Dithering
        {
            get => _dithering;
            set => Set(ref _dithering, value);
        }

        private bool _suffixEnabled = true;
        private bool _savedSuffixEnabled = true;

        public bool SuffixEnabled
        {
            get => _suffixEnabled;
            set
            {
                if (!Set(ref _suffixEnabled, value))
                {
                    return;
                }

                // Backup already renames the original; a suffix on top would give
                // one file two naming schemes, so enabling it drops the policy
                // back to Keep instead of producing a rejected combination.
                if (value && _originalPolicy == OriginalPolicy.Backup)
                {
                    OriginalPolicy = OriginalPolicy.Keep;
                }

                Raise(nameof(SuffixInputEnabled));
            }
        }

        public bool SuffixInputEnabled => !IsRunning && _suffixEnabled;

        private string _suffix = "_new";

        public string Suffix
        {
            get => _suffix;
            set => Set(ref _suffix, value);
        }

        // ------------------------------------------------------------------
        // output location
        // ------------------------------------------------------------------

        private OutputMode _outputMode = OutputMode.OriginalDir;

        public OutputMode OutputMode
        {
            get => _outputMode;
            set
            {
                if (!Set(ref _outputMode, value))
                {
                    return;
                }

                Raise(nameof(IsSelectedDir));
                Raise(nameof(PreserveStructureEnabled));
            }
        }

        public bool IsSelectedDir => _outputMode == OutputMode.SelectedDir;

        private string _outputDirectory;

        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                if (!Set(ref _outputDirectory, value))
                {
                    return;
                }

                Raise(nameof(OutputDirectoryDisplay));
            }
        }

        public string OutputDirectoryDisplay =>
            string.IsNullOrEmpty(_outputDirectory)
                ? Loc.I["OutputDirPlaceholder"]
                : _outputDirectory;

        private bool _preserveStructure;

        public bool PreserveStructure
        {
            get => _preserveStructure;
            set => Set(ref _preserveStructure, value);
        }

        public bool PreserveStructureEnabled => !IsRunning && IsSelectedDir;

        // ------------------------------------------------------------------
        // original files
        // ------------------------------------------------------------------

        private OriginalPolicy _originalPolicy = OriginalPolicy.Keep;

        public OriginalPolicy OriginalPolicy
        {
            get => _originalPolicy;
            set
            {
                var previous = _originalPolicy;
                if (!Set(ref _originalPolicy, value))
                {
                    return;
                }

                // Entering Backup parks the suffix and remembers its state;
                // leaving Backup restores whatever the user had before.
                if (value == OriginalPolicy.Backup && previous != OriginalPolicy.Backup)
                {
                    _savedSuffixEnabled = _suffixEnabled;
                    _suffixEnabled = false;
                    Raise(nameof(SuffixEnabled));
                    Raise(nameof(SuffixInputEnabled));
                }
                else if (previous == OriginalPolicy.Backup && value != OriginalPolicy.Backup)
                {
                    _suffixEnabled = _savedSuffixEnabled;
                    Raise(nameof(SuffixEnabled));
                    Raise(nameof(SuffixInputEnabled));
                }

                Raise(nameof(IsBackup));
            }
        }

        public bool IsBackup => _originalPolicy == OriginalPolicy.Backup;

        // ------------------------------------------------------------------
        // size and resize
        // ------------------------------------------------------------------

        private ResizeMode _resizeMode = ResizeMode.None;

        public ResizeMode ResizeMode
        {
            get => _resizeMode;
            set
            {
                if (!Set(ref _resizeMode, value))
                {
                    return;
                }

                Raise(nameof(IsResizeClassic));
                Raise(nameof(IsResizeBounds));
                Raise(nameof(FilterEnabled));
            }
        }

        public bool IsResizeClassic => _resizeMode == ResizeMode.Classic;

        public bool IsResizeBounds => _resizeMode == ResizeMode.Bounds;

        /// <summary>The filter only reaches rimage when a resize step exists.</summary>
        public bool FilterEnabled => !IsRunning && _resizeMode != ResizeMode.None;

        private string _resizeArgs = "1920l";

        public string ResizeArgs
        {
            get => _resizeArgs;
            set => Set(ref _resizeArgs, value);
        }

        private ResizeFilter _filter = ResizeFilter.Lanczos3;

        public ResizeFilter Filter
        {
            get => _filter;
            set => Set(ref _filter, value);
        }

        private BoundDirection _boundDirection = BoundDirection.Maximum;

        public BoundDirection BoundDirection
        {
            get => _boundDirection;
            set => Set(ref _boundDirection, value);
        }

        private BoundEdge _boundEdge = BoundEdge.Longest;

        public BoundEdge BoundEdge
        {
            get => _boundEdge;
            set => Set(ref _boundEdge, value);
        }

        private int _boundValue = 1920;

        public int BoundValue
        {
            get => _boundValue;
            set => Set(ref _boundValue, value);
        }

        // ------------------------------------------------------------------
        // execution
        // ------------------------------------------------------------------

        private bool _hiddenExecute = true;

        public bool HiddenExecute
        {
            get => _hiddenExecute;
            set => Set(ref _hiddenExecute, value);
        }

        private bool _autoThreads = true;

        public bool AutoThreads
        {
            get => _autoThreads;
            set
            {
                if (!Set(ref _autoThreads, value))
                {
                    return;
                }

                Raise(nameof(ThreadsInputEnabled));
            }
        }

        public bool ThreadsInputEnabled => !IsRunning && !_autoThreads;

        private int _threads = 4;

        public int Threads
        {
            get => _threads;
            set => Set(ref _threads, value);
        }

        // ------------------------------------------------------------------
        // run state
        // ------------------------------------------------------------------

        private bool _isRunning;

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (!Set(ref _isRunning, value))
                {
                    return;
                }

                // Every "enabled" flag folds IsRunning in, so the whole settings
                // panel locks and unlocks from this single transition.
                Raise(nameof(CanEditSettings));
                Raise(nameof(QualityEnabled));
                Raise(nameof(QuantizationInputEnabled));
                Raise(nameof(DitheringToggleEnabled));
                Raise(nameof(DitheringInputEnabled));
                Raise(nameof(SuffixInputEnabled));
                Raise(nameof(PreserveStructureEnabled));
                Raise(nameof(FilterEnabled));
                Raise(nameof(ThreadsInputEnabled));
                Raise(nameof(StartButtonText));
                Raise(nameof(StartButtonTip));
                Refresh();
            }
        }

        public bool CanEditSettings => !_isRunning;

        public string StartButtonText => _isRunning ? Loc.I["Cancel"] : Loc.I["Start"];

        public string StartButtonTip => _isRunning ? Loc.I["CancelTip"] : Loc.I["StartTip"];

        private bool _isScanning;

        public bool IsScanning
        {
            get => _isScanning;
            private set => Set(ref _isScanning, value);
        }

        private double _progressValue;

        public double ProgressValue
        {
            get => _progressValue;
            private set => Set(ref _progressValue, value);
        }

        private string _progressText = Loc.I["Idle"] + " 0%";

        public string ProgressText
        {
            get => _progressText;
            private set => Set(ref _progressText, value);
        }

        // ------------------------------------------------------------------
        // backend
        // ------------------------------------------------------------------

        public async Task InitializeBackendAsync()
        {
            try
            {
                _backendPath = await BackendExtractor.PrepareAsync().ConfigureAwait(true);
                Log(Loc.I["BackendReady"] + ": " + _backendPath);
            }
            catch (Exception exception)
            {
                _backendPath = null;
                Log(Loc.I["BackendFailed"] + ": " + exception.Message);
            }
        }

        // ------------------------------------------------------------------
        // scanning
        // ------------------------------------------------------------------

        public async Task AddFromPathsAsync(IReadOnlyList<string> roots)
        {
            if (roots == null || roots.Count == 0 || IsRunning)
            {
                return;
            }

            _scanCancellation?.Cancel();
            _scanCancellation = new CancellationTokenSource();
            var token = _scanCancellation.Token;

            IsScanning = true;
            ProgressText = Loc.I["Scanning"];

            try
            {
                var progress = new Progress<int>(count =>
                    ProgressText = Loc.I["Scanning"] + " " + count);

                var found = await Task.Run(
                    () => FileScanner.Collect(roots, c => ((IProgress<int>)progress).Report(c), token),
                    token).ConfigureAwait(true);

                AddPaths(found);
                Log(Loc.I["SelectedCount"] + ": +" + found.Count);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer drop; the newer scan reports instead.
            }
            catch (Exception exception)
            {
                Log(exception.Message);
            }
            finally
            {
                IsScanning = false;
                ResetProgressText();
            }
        }

        // ------------------------------------------------------------------
        // job
        // ------------------------------------------------------------------

        private async Task ToggleRunAsync()
        {
            if (IsRunning)
            {
                _jobCancellation?.Cancel();
                Log(Loc.I["Cancelled"]);
                return;
            }

            await StartAsync().ConfigureAwait(true);
        }

        private async Task StartAsync()
        {
            var selected = Files.Where(f => f.IsChecked).ToList();
            var options = BuildOptions();
            var job = new JobSpec(selected.Select(f => f.FullPath).ToList(), options);

            var validation = Validator.ValidateJob(job);
            if (!validation.IsValid)
            {
                var message = Loc.I[validation.MessageKey];
                if (!string.IsNullOrEmpty(validation.Detail))
                {
                    message += ": " + validation.Detail;
                }

                ShowError?.Invoke(Loc.I["MsgTitleError"], message);
                Log(message);
                return;
            }

            if (string.IsNullOrEmpty(_backendPath))
            {
                await InitializeBackendAsync().ConfigureAwait(true);
                if (string.IsNullOrEmpty(_backendPath))
                {
                    ShowError?.Invoke(Loc.I["MsgTitleError"], Loc.I["BackendFailed"]);
                    return;
                }
            }

            // Deleting originals is irreversible and skips the Recycle Bin, so it
            // is the one setting that asks before it runs.
            if (options.OriginalPolicy == OriginalPolicy.DeleteAfterVerifiedSuccess &&
                Confirm != null && !Confirm(Loc.I["ConfirmDeletePolicy"]))
            {
                return;
            }

            foreach (var entry in selected)
            {
                entry.ResetForRun();
            }

            var byPath = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
            foreach (var entry in selected)
            {
                byPath[PathUtil.Key(entry.FullPath)] = entry;
            }

            _jobCancellation = new CancellationTokenSource();
            IsRunning = true;
            ProgressValue = 0;
            ProgressText = Loc.I["Running"] + " 0%";

            var progress = new Progress<JobReport>(report => Apply(report, byPath));

            try
            {
                var summary = await JobRunner
                    .RunAsync(job, _backendPath, progress, _jobCancellation.Token)
                    .ConfigureAwait(true);

                var text = Loc.I["Summary"] + ": " +
                           summary.Succeeded + " " + Loc.I["SummarySucceeded"] + ", " +
                           summary.Failed + " " + Loc.I["SummaryFailed"] + ", " +
                           summary.Skipped + " " + Loc.I["SummarySkipped"];

                Log(text);
                ProgressText = (summary.Cancelled ? Loc.I["Cancelled"] : Loc.I["Finished"]) + " " +
                               ProgressPercent() + "%";
            }
            catch (Exception exception)
            {
                Log(exception.Message);
                ShowError?.Invoke(Loc.I["MsgTitleError"], exception.Message);
            }
            finally
            {
                IsRunning = false;
                _jobCancellation?.Dispose();
                _jobCancellation = null;
            }
        }

        private void Apply(JobReport report, IReadOnlyDictionary<string, FileEntry> byPath)
        {
            switch (report.Kind)
            {
                case JobReportKind.Log:
                    Log(report.Line);
                    return;

                case JobReportKind.Progress:
                    ProgressValue = report.Total == 0 ? 0 : report.Done * 100.0 / report.Total;
                    ProgressText = Loc.I["Running"] + " " + ProgressPercent() + "%";
                    return;

                case JobReportKind.FileFinished:
                    if (byPath.TryGetValue(PathUtil.Key(report.Input), out var entry))
                    {
                        entry.Status = report.Status;
                        if (report.Output != null)
                        {
                            entry.OutputPath = report.Output;
                        }

                        entry.Error = report.Error;
                    }

                    return;
            }
        }

        private int ProgressPercent() =>
            (int)Math.Round(ProgressValue, MidpointRounding.AwayFromZero);

        private void ResetProgressText() =>
            ProgressText = Loc.I["Idle"] + " " + ProgressPercent() + "%";

        /// <summary>Snapshots the editable state so a running job is immune to later edits.</summary>
        public ProcessingOptions BuildOptions() => new ProcessingOptions
        {
            Format = Format,
            Quality = Quality,
            Quantization = QuantizationEnabled ? Quantization : (int?)null,
            Dithering = QuantizationEnabled && DitheringEnabled ? Dithering : (int?)null,
            Suffix = SuffixEnabled ? Suffix : null,
            OutputMode = OutputMode,
            OutputDirectory = OutputDirectory,
            PreserveStructure = IsSelectedDir && PreserveStructure,
            OriginalPolicy = OriginalPolicy,
            ResizeMode = ResizeMode,
            ResizeArgs = ResizeArgs,
            Filter = Filter,
            BoundDirection = BoundDirection,
            BoundEdge = BoundEdge,
            BoundValue = BoundValue,
            Threads = AutoThreads ? (int?)null : Threads,
            Hidden = HiddenExecute
        };

        /// <summary>Command line the current settings would produce, for the log.</summary>
        public string PreviewCommandLine()
        {
            var args = CommandBuilder.BuildArgs(BuildOptions(), "file.list", "metadata.json");
            return PathUtil.DisplayCommandLine(_backendPath ?? "rimage.exe", args);
        }

        private void Log(string line)
        {
            if (!string.IsNullOrEmpty(line))
            {
                LogAppended?.Invoke(line);
            }
        }

        private void Refresh()
        {
            SelectAllCommand.RaiseCanExecuteChanged();
        }

        // ------------------------------------------------------------------

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            Raise(name);
            return true;
        }

        private void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
