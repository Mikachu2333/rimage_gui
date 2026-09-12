using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimageGui.I18n;
using RimageGui.Models;

namespace RimageGui.Core
{
    /// <summary>
    /// Drives rimage over a batch of inputs and reports per-file outcomes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Work is split into chunks rather than sent as one giant invocation. A
    /// single call would be marginally faster but reports nothing until it ends,
    /// so a thousand-file run would sit at 0% for minutes and could not be
    /// cancelled at a clean boundary. Chunking bounds the progress granularity to
    /// roughly <see cref="ProgressSteps"/> updates while keeping process spawns
    /// negligible next to the encoding work.
    /// </para>
    /// <para>
    /// Within a chunk, rimage itself parallelises across <c>--threads</c>.
    /// </para>
    /// <para>
    /// Every chunk is judged on three things, in order of authority: the
    /// <c>--metadata</c> JSON, which lists the outputs that really exist; the
    /// per-file error lines on stderr, which name the file and carry a stable
    /// slug; and finally the process exit code. Reading all three is what lets a
    /// partially successful chunk report successes and failures separately
    /// instead of collapsing into one verdict.
    /// </para>
    /// </remarks>
    public static class JobRunner
    {
        /// <summary>Upper bound on how many progress updates a run produces.</summary>
        private const int ProgressSteps = 50;

        /// <summary>Output lines kept per chunk for error attribution.</summary>
        private const int MaxDiagnosticLines = 4096;

        /// <summary>Poll interval while waiting for a rimage process to exit.</summary>
        private const int ProcessPollIntervalMilliseconds = 100;

        /// <summary>Additional grace period after killing a process.</summary>
        private const int CancellationGraceMilliseconds = 5000;

        public static async Task<JobSummary> RunAsync(
            JobSpec job,
            string backendPath,
            IProgress<JobReport> progress,
            CancellationToken token)
        {
            var summary = new JobSummary();
            var files = job.Files;
            var total = files.Count;
            var options = job.Options;

            progress?.Report(new ProgressJobReport(0, total));

            var workingRoot = options.OutputMode == OutputMode.SelectedDir && options.PreserveStructure
                ? CommonRoot(files)
                : null;

            var scratch = Path.Combine(Path.GetTempPath(), $"rimage-gui-{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratch);

            try
            {
                var chunkSize = ResolveChunkSize(total);
                var done = 0;
                var loggedCommand = false;

                for (var offset = 0; offset < total; offset += chunkSize)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    var chunk = new List<string>(chunkSize);
                    for (var index = offset; index < Math.Min(offset + chunkSize, total); index++)
                    {
                        chunk.Add(files[index]);
                    }

                    foreach (var input in chunk)
                    {
                        progress?.Report(new FileFinishedJobReport(input, FileStatus.Running));
                    }

                    var context = new ChunkContext(
                        chunk, options, backendPath, scratch, workingRoot,
                        progress, !loggedCommand, token);
                    var outcome = await RunChunkAsync(context).ConfigureAwait(false);

                    loggedCommand = true;

                    // A fatal code is a property of the arguments, not of the
                    // data: every later chunk would fail identically, so the run
                    // stops instead of respawning the same broken command.
                    if (!token.IsCancellationRequested && !string.IsNullOrEmpty(outcome.FatalError))
                    {
                        foreach (var input in chunk)
                        {
                            summary.Failed++;
                            summary.FailedItems.Add(new FailedFile
                            {
                                Input = input,
                                Error = outcome.FatalError
                            });
                            progress?.Report(new FileFinishedJobReport(
                                input, FileStatus.Failed, null, outcome.FatalError));
                        }

                        summary.Aborted = true;
                        summary.FatalError = outcome.FatalError;
                        progress?.Report(new LogJobReport(outcome.FatalError));
                        done += chunk.Count;
                        progress?.Report(new ProgressJobReport(done, total));
                        break;
                    }

                    if (outcome.MetadataMissing)
                    {
                        progress?.Report(new LogJobReport(Loc.I["ErrMetadataMissing"]));
                    }

                    foreach (var input in chunk)
                    {
                        ProcessFileResult(input, options, outcome, token.IsCancellationRequested, summary, progress);
                    }

                    done += chunk.Count;
                    progress?.Report(new ProgressJobReport(done, total));
                }

                if (token.IsCancellationRequested)
                {
                    summary.Cancelled = true;
                    ReportTail(files, total, summary, progress);
                }
                else if (summary.Aborted)
                {
                    ReportTail(files, total, summary, progress);
                }
            }
            finally
            {
                TryDeleteDirectory(scratch);
            }

            return summary;
        }

        /// <summary>
        /// Chunk size that yields at most <see cref="ProgressSteps"/> updates.
        /// Small batches use one file per invocation so every row reports
        /// individually; the extra spawns cost far less than the encoding.
        /// </summary>
        private static int ResolveChunkSize(int total)
        {
            if (total <= ProgressSteps)
            {
                return 1;
            }

            return (int)Math.Ceiling(total / (double)ProgressSteps);
        }

        private static void ProcessFileResult(
            string input,
            ProcessingOptions options,
            ChunkOutcome outcome,
            bool cancelled,
            JobSummary summary,
            IProgress<JobReport> progress)
        {
            var result = Resolve(input, options, outcome);

            if (result.Status == FileStatus.Done &&
                options.OriginalPolicy == OriginalPolicy.DeleteAfterVerifiedSuccess)
            {
                result = ApplyDeletePolicy(input, result, cancelled);
            }

            if (result.Status == FileStatus.Done)
            {
                summary.Succeeded++;
            }
            else
            {
                summary.Failed++;
                summary.FailedItems.Add(new FailedFile
                {
                    Input = input,
                    Error = result.Error
                });
            }

            progress?.Report(new FileFinishedJobReport(
                input, result.Status, result.Output, result.Error));

            if (result.Status == FileStatus.Failed && !string.IsNullOrEmpty(result.Error))
            {
                progress?.Report(new LogJobReport(
                    $"{Path.GetFileName(input)}: {result.Error}"));
            }
        }

        private static void ReportTail(
            IReadOnlyList<string> files,
            int total,
            JobSummary summary,
            IProgress<JobReport> progress)
        {
            summary.Skipped = total - summary.Succeeded - summary.Failed;

            for (var index = total - summary.Skipped; index < total; index++)
            {
                progress?.Report(new FileFinishedJobReport(files[index], FileStatus.Skipped));
            }
        }

        private sealed class ChunkOutcome
        {
            public RimageExitCode Kind { get; set; } = RimageExitCode.Success;

            public Dictionary<string, string> Outputs { get; set; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Failures rimage named, keyed by <see cref="PathUtil.Key"/> of the input.</summary>
            public Dictionary<string, BackendError> ErrorsByInput { get; } =
                new Dictionary<string, BackendError>(StringComparer.Ordinal);

            /// <summary>Failures whose file could not be identified.</summary>
            public List<BackendError> Unattributed { get; } = new List<BackendError>();

            /// <summary>
            /// Set when the failure invalidates the whole run rather than these
            /// files; non-null means "stop, do not send another chunk".
            /// </summary>
            public string FatalError { get; set; }

            /// <summary>True when rimage should have written metadata but did not.</summary>
            public bool MetadataMissing { get; set; }

            public string CommandLine { get; set; }
        }

        private sealed class FileResult
        {
            public FileStatus Status { get; set; }

            public string Output { get; set; }

            public string Error { get; set; }
        }

        private sealed class ChunkContext
        {
            public ChunkContext(
                List<string> chunk,
                ProcessingOptions options,
                string backendPath,
                string scratch,
                string workingRoot,
                IProgress<JobReport> progress,
                bool logCommand,
                CancellationToken token)
            {
                Chunk = chunk;
                Options = options;
                BackendPath = backendPath;
                Scratch = scratch;
                WorkingRoot = workingRoot;
                Progress = progress;
                LogCommand = logCommand;
                Token = token;
            }

            public List<string> Chunk { get; }

            public ProcessingOptions Options { get; }

            public string BackendPath { get; }

            public string Scratch { get; }

            public string WorkingRoot { get; }

            public IProgress<JobReport> Progress { get; }

            public bool LogCommand { get; }

            public CancellationToken Token { get; }
        }

        private static async Task<ChunkOutcome> RunChunkAsync(ChunkContext context)
        {
            var outcome = new ChunkOutcome();

            // rimage recognises the input list by its literal file name, so each
            // chunk gets its own directory holding a file called "file.list".
            var chunkDirectory = Path.Combine(context.Scratch, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(chunkDirectory);

            var listPath = Path.Combine(chunkDirectory, "file.list");
            var metadataPath = Path.Combine(chunkDirectory, "metadata.json");

            // A BOM would become part of the first path rimage reads.
            File.WriteAllText(listPath, string.Join("\r\n", context.Chunk), new UTF8Encoding(false));

            var args = CommandBuilder.BuildArgs(context.Options, listPath, metadataPath);
            outcome.CommandLine = PathUtil.DisplayCommandLine(context.BackendPath, args);

            if (context.LogCommand)
            {
                context.Progress?.Report(new LogJobReport(outcome.CommandLine));
            }

            // Always redirected: --quiet leaves stdout empty, so the only thing
            // worth seeing is stderr, and capturing it is what gives each failed
            // row a real reason instead of a generic one.
            var startInfo = new ProcessStartInfo(context.BackendPath, PathUtil.BuildArgumentString(args))
            {
                UseShellExecute = false,
                CreateNoWindow = context.Options.HideBackendWindow,
                WorkingDirectory = context.WorkingRoot ?? chunkDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!context.Options.HideBackendWindow)
            {
                // The console the user asked for stays empty under --quiet, so
                // its real value is the verbose log; this is the knob rimage
                // itself suggests when a run needs to be reproduced.
                startInfo.EnvironmentVariables["RUST_LOG"] = "debug";
            }

            var lines = new List<string>();
            var exitCode = -1;

            try
            {
                using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false })
                {
                    DataReceivedEventHandler onData = (_, e) =>
                    {
                        if (e.Data == null)
                        {
                            return;
                        }

                        lock (lines)
                        {
                            if (lines.Count < MaxDiagnosticLines)
                            {
                                lines.Add(e.Data);
                            }
                        }

                        context.Progress?.Report(new LogJobReport(e.Data));
                    };

                    process.OutputDataReceived += onData;
                    process.ErrorDataReceived += onData;

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await WaitAsync(process, context.Token).ConfigureAwait(false);

                    // A killed process may still be winding down, and reading
                    // ExitCode before it has would throw.
                    exitCode = process.HasExited ? process.ExitCode : -1;
                }
            }
            catch (Exception exception)
            {
                outcome.Kind = RimageExitCode.Unexpected;
                outcome.FatalError = exception.Message;
                lock (lines)
                {
                    lines.Add(exception.ToString());
                }

                context.Progress?.Report(new LogJobReport(exception.Message));
            }

            string[] captured;
            lock (lines)
            {
                captured = lines.ToArray();
            }

            if (outcome.FatalError == null)
            {
                outcome.Kind = RimageExitCodes.Classify(exitCode);

                foreach (var error in RimageErrors.Parse(captured, context.Chunk))
                {
                    if (string.IsNullOrEmpty(error.Path))
                    {
                        outcome.Unattributed.Add(error);
                    }
                    else
                    {
                        outcome.ErrorsByInput[PathUtil.Key(error.Path)] = error;
                    }
                }

                if (RimageExitCodes.IsFatal(outcome.Kind))
                {
                    outcome.FatalError = RimageErrors.DescribeFatal(outcome.Kind);
                }

                if (RimageExitCodes.CarriesMetadata(outcome.Kind))
                {
                    outcome.Outputs = MetadataReader.LoadOutputMap(metadataPath);
                    // Images can all succeed while the summary file itself fails
                    // to write; that still reports Partial, so an absent JSON is
                    // a normal outcome and not a reason to fail the files.
                    outcome.MetadataMissing = outcome.Outputs.Count == 0;
                }
            }

            TryDeleteDirectory(chunkDirectory);
            return outcome;
        }

        /// <summary>
        /// Waits for exit while honouring cancellation. .NET Framework has no
        /// asynchronous WaitForExit, so the process is polled and killed on
        /// cancellation; already-written outputs survive.
        /// </summary>
        private static async Task WaitAsync(Process process, CancellationToken token)
        {
            while (!process.WaitForExit(ProcessPollIntervalMilliseconds))
            {
                if (!token.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // Exited between the poll and the kill.
                }

                process.WaitForExit(CancellationGraceMilliseconds);
                return;
            }

            // The polling overload returns as soon as the process dies, which is
            // before the asynchronous readers have drained. The parameterless
            // overload waits for stdout/stderr to reach EOF, and without it the
            // final error lines — the ones that explain the last failure in a
            // chunk — can be lost.
            process.WaitForExit();
            await Task.Yield();
        }

        /// <summary>
        /// Decides one input's outcome from the three signals the chunk produced.
        /// </summary>
        private static FileResult Resolve(string input, ProcessingOptions options, ChunkOutcome outcome)
        {
            if (outcome.Outputs.TryGetValue(PathUtil.Key(input), out var reported) &&
                IsUsableOutput(reported))
            {
                return new FileResult { Status = FileStatus.Done, Output = reported };
            }

            if (outcome.ErrorsByInput.TryGetValue(PathUtil.Key(input), out var failure))
            {
                return new FileResult
                {
                    Status = FileStatus.Failed,
                    Error = RimageErrors.Describe(failure)
                };
            }

            // No metadata entry and no error line: either rimage wrote the file
            // and failed to record it, or the predicted path is the only evidence
            // left. Existence is checked rather than assumed.
            var predicted = Validator.PredictedOutputPath(input, options);
            if (IsUsableOutput(predicted))
            {
                return new FileResult { Status = FileStatus.Done, Output = predicted };
            }

            return new FileResult
            {
                Status = FileStatus.Failed,
                Error = FallbackError(outcome)
            };
        }

        private static string FallbackError(ChunkOutcome outcome)
        {
            // A failure rimage printed without a path we recognised is still the
            // best explanation available; otherwise the exit code is all there is.
            return outcome.Unattributed.Count > 0
                ? RimageErrors.Describe(outcome.Unattributed[0])
                : RimageErrors.DescribeExit(outcome.Kind);
        }

        private static FileResult ApplyDeletePolicy(string input, FileResult result, bool cancelled)
        {
            if (!Validator.SafeToDelete(input, result.Output, cancelled))
            {
                return new FileResult
                {
                    Status = FileStatus.Failed,
                    Output = result.Output,
                    Error = "refused to delete the original: the output is missing, empty, " +
                            "or resolves to the input itself"
                };
            }

            try
            {
                File.Delete(input);
                return result;
            }
            catch (Exception exception)
            {
                return new FileResult
                {
                    Status = FileStatus.Failed,
                    Output = result.Output,
                    Error = $"output written but deleting the original failed: {exception.Message}"
                };
            }
        }

        private static bool IsUsableOutput(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                var info = new FileInfo(path);
                return info.Exists && info.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string[] SplitSegments(string file)
        {
            return (Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Deepest directory containing every input, used as the working
        /// directory so rimage's <c>-r</c> mirrors the folder layout the user
        /// actually sees instead of resolving against an arbitrary CWD.
        /// </summary>
        private static string CommonRoot(IReadOnlyList<string> files)
        {
            if (files.Count == 0)
            {
                return null;
            }

            var common = SplitSegments(files[0]);

            for (var index = 1; index < files.Count && common.Length > 0; index++)
            {
                var current = SplitSegments(files[index]);
                var shared = 0;
                var limit = Math.Min(common.Length, current.Length);

                while (shared < limit &&
                       string.Equals(common[shared], current[shared], StringComparison.OrdinalIgnoreCase))
                {
                    shared++;
                }

                common = common.Take(shared).ToArray();
            }

            if (common.Length == 0)
            {
                // Inputs span several drives; -r has no shared base to mirror.
                return null;
            }

            var root = string.Join(Path.DirectorySeparatorChar.ToString(), common);
            if (common.Length == 1)
            {
                // A bare drive letter needs its trailing separator to be a path.
                root += Path.DirectorySeparatorChar;
            }

            return Directory.Exists(root) ? root : null;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception)
            {
                // Temp leftovers are cleaned by the OS.
            }
        }
    }
}
