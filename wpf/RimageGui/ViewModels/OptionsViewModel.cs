using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RimageGui.I18n;
using RimageGui.Models;

namespace RimageGui.ViewModels
{
    /// <summary>
    /// Owns the editable conversion settings and snapshots them into a
    /// <see cref="ProcessingOptions"/> for a job. Keeping this separate from
    /// <see cref="MainViewModel"/> lets the file/progress concerns stay in the
    /// main view model without turning it into a settings holder as well.
    /// </summary>
    public sealed class OptionsViewModel : INotifyPropertyChanged
    {
        [SuppressMessage("Performance", "CA1822:MarkMembersAsStatic",
            Justification = "The host view model forwards these as instance bindable properties.")]
        public IReadOnlyList<OutputFormat> Formats => FormatInfo.All;

        [SuppressMessage("Performance", "CA1822:MarkMembersAsStatic",
            Justification = "The host view model forwards these as instance bindable properties.")]
        public IReadOnlyList<ResizeFilter> Filters => FilterInfo.All;

        private OutputFormat _format = ProcessingOptions.Defaults.Format;

        public OutputFormat Format
        {
            get => _format;
            set
            {
                if (!Set(ref _format, value))
                {
                    return;
                }

                Raise(nameof(FormatHint));
            }
        }

        /// <summary>One-line capability note for the selected codec, from the rimage docs.</summary>
        public string FormatHint => Loc.I["FormatHint" + Format];

        private int _quality = ProcessingOptions.Defaults.Quality;

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

        public bool QuantizationInputEnabled => _quantizationEnabled;

        private int _quantization = ProcessingOptions.Defaults.Quantization;

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
        public bool DitheringToggleEnabled => _quantizationEnabled;

        public bool DitheringInputEnabled => DitheringToggleEnabled && _ditheringEnabled;

        private int _dithering = ProcessingOptions.Defaults.Dithering;

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
                if (value && OriginalPolicy == OriginalPolicy.Backup)
                {
                    OriginalPolicy = OriginalPolicy.Keep;
                }

                Raise(nameof(SuffixInputEnabled));
            }
        }

        public bool SuffixInputEnabled => _suffixEnabled;

        private string _suffix = ProcessingOptions.Defaults.Suffix;

        public string Suffix
        {
            get => _suffix;
            set => Set(ref _suffix, value);
        }

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
            }
        }

        public bool IsResizeClassic => _resizeMode == ResizeMode.Classic;

        public bool IsResizeBounds => _resizeMode == ResizeMode.Bounds;

        private string _resizeArgs = ProcessingOptions.Defaults.ResizeArgs;

        public string ResizeArgs
        {
            get => _resizeArgs;
            set => Set(ref _resizeArgs, value);
        }

        private ResizeFilter _filter = ProcessingOptions.Defaults.Filter;

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

        private int _boundValue = ProcessingOptions.Defaults.BoundValue;

        public int BoundValue
        {
            get => _boundValue;
            set => Set(ref _boundValue, value);
        }

        private bool _hiddenExecute = true;

        public bool HideBackendWindow
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

        public bool ThreadsInputEnabled => !_autoThreads;

        private int _threads = ProcessingOptions.Defaults.Threads;

        public int Threads
        {
            get => _threads;
            set => Set(ref _threads, value);
        }

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
            HideBackendWindow = HideBackendWindow
        };

        public event PropertyChangedEventHandler PropertyChanged;

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
    }
}
