using System.Collections.Generic;
using RimageGui.Models;

namespace RimageGui.Core
{
    /// <summary>Identifies which payload a <see cref="JobReport"/> carries.</summary>
    public enum JobReportKind
    {
        Log,
        FileFinished,
        Progress
    }

    /// <summary>
    /// Base class for the per-event reports sent to the UI. Each concrete kind
    /// carries only the fields relevant to that kind, so a progress report never
    /// has unrelated file/output fields that are null by convention.
    /// </summary>
    public abstract class JobReport
    {
        protected JobReport(JobReportKind kind)
        {
            Kind = kind;
        }

        public JobReportKind Kind { get; }

        public virtual string Line => string.Empty;

        public virtual string Input => string.Empty;

        public virtual FileStatus Status => FileStatus.Pending;

        public virtual string Output => string.Empty;

        public virtual string FailureText => string.Empty;

        public virtual int Done => 0;

        public virtual int Total => 0;
    }

    /// <summary>A command line or backend output line.</summary>
    public sealed class LogJobReport : JobReport
    {
        public LogJobReport(string line)
            : base(JobReportKind.Log)
        {
            Line = line ?? string.Empty;
        }

        public override string Line { get; }
    }

    /// <summary>Batch progress after a chunk has been processed.</summary>
    public sealed class ProgressJobReport : JobReport
    {
        public ProgressJobReport(int done, int total)
            : base(JobReportKind.Progress)
        {
            Done = done;
            Total = total;
        }

        public override int Done { get; }

        public override int Total { get; }
    }

    /// <summary>The outcome of one input on the file table.</summary>
    public sealed class FileFinishedJobReport : JobReport
    {
        public FileFinishedJobReport(string input, FileStatus status, string output = null, string error = null)
            : base(JobReportKind.FileFinished)
        {
            Input = input ?? string.Empty;
            Status = status;
            Output = output ?? string.Empty;
            FailureText = error ?? string.Empty;
        }

        public override string Input { get; }

        public override FileStatus Status { get; }

        public override string Output { get; }

        public override string FailureText { get; }
    }

    /// <summary>One failed input with its reason, for the end-of-run log.</summary>
    public sealed class FailedFile
    {
        public string Input { get; set; }

        public string Error { get; set; }
    }

    /// <summary>Aggregate result of a full batch run.</summary>
    public sealed class JobSummary
    {
        public int Succeeded { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        public bool Cancelled { get; set; }

        /// <summary>Every failed input with its reason, for the end-of-run log.</summary>
        public List<FailedFile> FailedItems { get; } = new List<FailedFile>();
    }
}
