using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly Dictionary<FileEntry, bool> _checkedShadow = new Dictionary<FileEntry, bool>();
        private readonly OptionsViewModel _options = new OptionsViewModel();

        private CancellationTokenSource _jobCancellation;
        private CancellationTokenSource _scanCancellation;
        private string _backendPath;
        private readonly Lazy<Task<string>> _backendTask = new Lazy<Task<string>>(
            () => BackendExtractor.PrepareAsync(), LazyThreadSafetyMode.ExecutionAndPublication);

        public MainViewModel()
        {
            Files = new RangeObservableCollection<FileEntry>();
            Files.CollectionChanged += OnFilesChanged;
            _options.PropertyChanged += (_, e) => Raise(e.PropertyName);

            AddFilesCommand = new RelayCommand(() => RunSafely(RequestAddFiles), () => !IsRunning);
            AddFolderCommand = new RelayCommand(() => RunSafely(RequestAddFolder), () => !IsRunning);
            BrowseOutputCommand = new RelayCommand(() => RequestBrowseOutput?.Invoke(), () => !IsRunning);
            SelectAllCommand = new RelayCommand(() => SetAllChecked(true), () => !IsRunning && TotalCount > 0);
            DeselectAllCommand = new RelayCommand(() => SetAllChecked(false), () => !IsRunning && TotalCount > 0);
            RemoveCheckedCommand = new RelayCommand(RemoveChecked, () => !IsRunning && CheckedCount > 0);
            ClearCommand = new RelayCommand(ClearFiles, () => !IsRunning && TotalCount > 0);
            StartCommand = new RelayCommand(() => RunSafely(ToggleRunAsync));
        }

        // ------------------------------------------------------------------
        // View hooks. The view owns dialogs and the file pickers; the view model
        // owns the decisions. Keeping the split here is what lets every rule
        // below stay testable without a window.
        // ------------------------------------------------------------------

        public Func<Task> RequestAddFiles { get; set; }

        public Func<Task> RequestAddFolder { get; set; }

        public Action RequestBrowseOutput { get; set; }

        public Action<string, string> ShowError { get; set; }

        public Func<string, bool> Confirm { get; set; }
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
            $"{Loc.I["SelectedCount"]}: {CheckedCount}/{TotalCount}";

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

        [SuppressMessage("Performance", "CA1822:MarkMembersAsStatic",
            Justification = "WPF data binding requires an instance property.")]
        public IReadOnlyList<OutputFormat> Formats => _options.Formats;

        [SuppressMessage("Performance", "CA1822:MarkMembersAsStatic",
            Justification = "WPF data binding requires an instance property.")]
        public IReadOnlyList<ResizeFilter> Filters => _options.Filters;

        public OutputFormat Format
        {
            get => _options.Format;
            set
            {
                if (Format == value)
                {
                    return;
                }

                _options.Format = value;
                Raise(nameof(QualityEnabled));
                Raise(nameof(FormatHint));
            }
        }

        public string FormatHint => _options.FormatHint;

        /// <summary>Lossless codecs reject <c>--quality</c>, so the field is disabled rather than ignored.</summary>
        public bool QualityEnabled => !IsRunning && _options.Format.SupportsQuality();

        public int Quality
        {
            get => _options.Quality;
            set => _options.Quality = value;
        }

        public bool QuantizationEnabled
        {
            get => _options.QuantizationEnabled;
            set => _options.QuantizationEnabled = value;
        }

        public bool QuantizationInputEnabled => !IsRunning && _options.QuantizationInputEnabled;

        public int Quantization
        {
            get => _options.Quantization;
            set => _options.Quantization = value;
        }

        public bool DitheringEnabled
        {
            get => _options.DitheringEnabled;
            set => _options.DitheringEnabled = value;
        }

        public bool DitheringToggleEnabled => !IsRunning && _options.DitheringToggleEnabled;

        public bool DitheringInputEnabled => !IsRunning && _options.DitheringInputEnabled;

        public int Dithering
        {
            get => _options.Dithering;
            set => _options.Dithering = value;
        }

        public bool SuffixEnabled
        {
            get => _options.SuffixEnabled;
            set => _options.SuffixEnabled = value;
        }

        public bool SuffixInputEnabled => !IsRunning && _options.SuffixInputEnabled;

        public string Suffix
        {
            get => _options.Suffix;
            set => _options.Suffix = value;
        }

        // ------------------------------------------------------------------
        // output location
        // ------------------------------------------------------------------

        public OutputMode OutputMode
        {
            get => _options.OutputMode;
            set
            {
                if (OutputMode == value)
                {
                    return;
                }

                _options.OutputMode = value;
                Raise(nameof(PreserveStructureEnabled));
            }
        }

        public bool IsSelectedDir => _options.IsSelectedDir;

        public string OutputDirectory
        {
            get => _options.OutputDirectory;
            set => _options.OutputDirectory = value;
        }

        public string OutputDirectoryDisplay => _options.OutputDirectoryDisplay;

        public bool PreserveStructure
        {
            get => _options.PreserveStructure;
            set => _options.PreserveStructure = value;
        }

        public bool PreserveStructureEnabled => !IsRunning && _options.IsSelectedDir;

        // ------------------------------------------------------------------
        // original files
        // ------------------------------------------------------------------

        public OriginalPolicy OriginalPolicy
        {
            get => _options.OriginalPolicy;
            set => _options.OriginalPolicy = value;
        }

        public bool IsBackup => _options.IsBackup;

        // ------------------------------------------------------------------
        // size and resize
        // ------------------------------------------------------------------

        public ResizeMode ResizeMode
        {
            get => _options.ResizeMode;
            set
            {
                if (ResizeMode == value)
                {
                    return;
                }

                _options.ResizeMode = value;
                Raise(nameof(FilterEnabled));
            }
        }

        public bool IsResizeClassic => _options.IsResizeClassic;

        public bool IsResizeBounds => _options.IsResizeBounds;

        /// <summary>The filter only reaches rimage when a resize step exists.</summary>
        public bool FilterEnabled => !IsRunning && _options.ResizeMode != ResizeMode.None;

        public string ResizeArgs
        {
            get => _options.ResizeArgs;
            set => _options.ResizeArgs = value;
        }

        public ResizeFilter Filter
        {
            get => _options.Filter;
            set => _options.Filter = value;
        }

        public BoundDirection BoundDirection
        {
            get => _options.BoundDirection;
            set => _options.BoundDirection = value;
        }

        public BoundEdge BoundEdge
        {
            get => _options.BoundEdge;
            set => _options.BoundEdge = value;
        }

        public int BoundValue
        {
            get => _options.BoundValue;
            set => _options.BoundValue = value;
        }

        // ------------------------------------------------------------------
        // execution
        // ------------------------------------------------------------------

        public bool HideBackendWindow
        {
            get => _options.HideBackendWindow;
            set => _options.HideBackendWindow = value;
        }

        public bool AutoThreads
        {
            get => _options.AutoThreads;
            set => _options.AutoThreads = value;
        }

        public bool ThreadsInputEnabled => !IsRunning && _options.ThreadsInputEnabled;

        public int Threads
        {
            get => _options.Threads;
            set => _options.Threads = value;
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

        private string _progressText = $"{Loc.I["Idle"]} 0%";

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
                var path = await EnsureBackendAsync().ConfigureAwait(true);
                Log($"{Loc.I["BackendReady"]}: {path}");
            }
            catch (Exception exception)
            {
                _backendPath = null;
                Log($"{Loc.I["BackendFailed"]}: {exception.Message}");
            }
        }

        private async Task<string> EnsureBackendAsync()
        {
            if (!string.IsNullOrEmpty(_backendPath))
            {
                return _backendPath;
            }

            var path = await _backendTask.Value.ConfigureAwait(true);
            _backendPath = path;
            return path;
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

            var previousScan = _scanCancellation;
            previousScan?.Cancel();
            previousScan?.Dispose();
            var currentScan = new CancellationTokenSource();
            _scanCancellation = currentScan;
            var token = currentScan.Token;

            IsScanning = true;
            ProgressText = Loc.I["Scanning"];

            try
            {
                var progress = new Progress<int>(count =>
                    ProgressText = $"{Loc.I["Scanning"]} {count}");

                var result = await Task.Run(
                    () => FileScanner.Collect(roots, c => ((IProgress<int>)progress).Report(c), token),
                    token).ConfigureAwait(true);

                AddPaths(result.Found);
                var scanSummary = $"{Loc.I["SelectedCount"]}: +{result.Found.Count}";
                if (result.Skipped > 0)
                {
                    scanSummary += $"  {Loc.I.Format("SkippedUnsupported", result.Skipped)}";
                }

                Log(scanSummary);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer drop; the newer scan reports instead.
            }
            catch (Exception exception)
            {
                Log(exception.ToString());
            }
            finally
            {
                IsScanning = false;
                ResetProgressText();

                if (ReferenceEquals(_scanCancellation, currentScan))
                {
                    currentScan.Dispose();
                    _scanCancellation = null;
                }
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

        /// <summary>
        /// Requests cancellation of a running job or scan; safe to call while idle.
        /// </summary>
        public void CancelJob()
        {
            _jobCancellation?.Cancel();
            _scanCancellation?.Cancel();
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
                    message += $": {validation.Detail}";
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
            ProgressText = $"{Loc.I["Running"]} 0%";

            var progress = new Progress<JobReport>(report => Apply(report, byPath));

            try
            {
                var summary = await JobRunner
                    .RunAsync(job, _backendPath, progress, _jobCancellation.Token)
                    .ConfigureAwait(true);

                LogRunSummary(summary);

                ProgressText = $"{(summary.Cancelled ? Loc.I["Cancelled"] : Loc.I["Finished"])} {ProgressPercent()}%";

                // The run is over; the next one starts from a clean list.
                Files.Clear();
            }
            catch (Exception exception)
            {
                Log(exception.ToString());
                ShowError?.Invoke(Loc.I["MsgTitleError"], exception.Message);
            }
            finally
            {
                IsRunning = false;
                _jobCancellation?.Dispose();
                _jobCancellation = null;
            }
        }

        private void LogRunSummary(JobSummary summary)
        {
            var text = $"{Loc.I["Summary"]}: {summary.Succeeded} {Loc.I["SummarySucceeded"]}, " +
                       $"{summary.Failed} {Loc.I["SummaryFailed"]}, " +
                       $"{summary.Skipped} {Loc.I["SummarySkipped"]}";

            Log(text);

            foreach (var failure in summary.FailedItems)
            {
                var detail = !string.IsNullOrEmpty(failure.Error) ? $" - {failure.Error}" : string.Empty;
                Log($"{Loc.I["SummaryFailed"]}: {failure.Input}{detail}");
            }
        }

        private void Apply(JobReport report, Dictionary<string, FileEntry> byPath)
        {
            switch (report.Kind)
            {
                case JobReportKind.Log:
                    Log(report.Line);
                    return;

                case JobReportKind.Progress:
                    ProgressValue = report.Total == 0 ? 0 : report.Done * 100.0 / report.Total;
                    ProgressText = $"{Loc.I["Running"]} {ProgressPercent()}%";
                    return;

                case JobReportKind.FileFinished:
                    if (byPath.TryGetValue(PathUtil.Key(report.Input), out var entry))
                    {
                        entry.Status = report.Status;
                        if (!string.IsNullOrEmpty(report.Output))
                        {
                            entry.OutputPath = report.Output;
                        }

                        entry.Error = report.FailureText;
                    }

                    return;

                default:
                    throw new InvalidOperationException($"Unexpected job report kind: {report.Kind}");
            }
        }

        private int ProgressPercent() =>
            (int)Math.Round(ProgressValue, MidpointRounding.AwayFromZero);

        private void ResetProgressText() =>
            ProgressText = $"{Loc.I["Idle"]} {ProgressPercent()}%";

        /// <summary>Snapshots the editable state so a running job is immune to later edits.</summary>
        public ProcessingOptions BuildOptions() => _options.BuildOptions();

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

        /// <summary>
        /// Starts an async view hook while ensuring any thrown exception is
        /// observed and surfaced on the UI, instead of becoming an unobserved
        /// task exception or an <c>async void</c> crash.
        /// </summary>
        public void RunSafely(Func<Task> action)
        {
            if (action == null)
            {
                return;
            }

            Task task;
            try
            {
                task = action();
            }
            catch (Exception exception)
            {
                HandleSafely(exception);
                return;
            }

            task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        HandleSafely(completed.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown async failure"));
                    }
                },
                TaskScheduler.Default);
        }

        private void HandleSafely(Exception exception)
        {
            Log(exception.ToString());
            ShowError?.Invoke(Loc.I["MsgTitleError"], exception.Message);
        }

        private static void Refresh()
        {
            RelayCommand.RaiseCanExecuteChanged();
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

        public void Dispose()
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
