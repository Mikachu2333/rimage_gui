using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// </remarks>
    public static class JobRunner
    {
        /// <summary>Upper bound on how many progress updates a run produces.</summary>
        private const int ProgressSteps = 50;

        /// <summary>Trailing stdout/stderr kept per chunk for failure diagnostics.</summary>
        private const int MaxDiagnosticChars = 8 * 1024;

        /// <summary>How many chars of chunk diagnostics are copied into a file error.</summary>
        private const int MaxResizeDiagnosticChars = 600;

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
                        progress, !loggedCommand, done, total, token);
                    var outcome = await RunChunkAsync(context).ConfigureAwait(false);

                    loggedCommand = true;

                    foreach (var input in chunk)
                    {
                        ProcessFileResult(input, options, outcome, token.IsCancellationRequested, summary, progress);
                    }

                    done += chunk.Count;
                    progress?.Report(new ProgressJobReport(done, total));
                }

                if (token.IsCancellationRequested)
                {
                    ReportCancelledTail(files, total, summary, progress);
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

        private static void ReportCancelledTail(
            IReadOnlyList<string> files,
            int total,
            JobSummary summary,
            IProgress<JobReport> progress)
        {
            summary.Cancelled = true;
            summary.Skipped = total - summary.Succeeded - summary.Failed;

            for (var index = total - summary.Skipped; index < total; index++)
            {
                progress?.Report(new FileFinishedJobReport(files[index], FileStatus.Skipped));
            }
        }

        private sealed class ChunkOutcome
        {
            public bool ProcessSucceeded { get; set; }

            public Dictionary<string, string> Outputs { get; set; }

            /// <summary>
            /// Actual output paths parsed from rimage's per-file summary lines,
            /// keyed by <see cref="PathUtil.Key"/>. Used when rimage writes no
            /// metadata because one file in the chunk failed.
            /// </summary>
            public Dictionary<string, string> ReportedOutputs { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public string Diagnostic { get; set; }

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
                int baseDone,
                int total,
                CancellationToken token)
            {
                Chunk = chunk;
                Options = options;
                BackendPath = backendPath;
                Scratch = scratch;
                WorkingRoot = workingRoot;
                Progress = progress;
                LogCommand = logCommand;
                BaseDone = baseDone;
                Total = total;
                Token = token;
            }

            public List<string> Chunk { get; }

            public ProcessingOptions Options { get; }

            public string BackendPath { get; }

            public string Scratch { get; }

            public string WorkingRoot { get; }

            public IProgress<JobReport> Progress { get; }

            public bool LogCommand { get; }

            public int BaseDone { get; }

            public int Total { get; }

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

            // Redirecting is what feeds the in-app log, but it also swallows the
            // console the user asked to see when "hide rimage window" is off.
            var redirect = context.Options.HideBackendWindow;

            var startInfo = new ProcessStartInfo(context.BackendPath, PathUtil.BuildArgumentString(args))
            {
                UseShellExecute = false,
                CreateNoWindow = context.Options.HideBackendWindow,
                WorkingDirectory = context.WorkingRoot ?? chunkDirectory,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect
            };

            if (redirect)
            {
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            var diagnostic = new StringBuilder();
            var completedInChunk = 0;

            try
            {
                using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false })
                {
                    if (redirect)
                    {
                        DataReceivedEventHandler onData = (_, e) =>
                        {
                            if (e.Data == null)
                            {
                                return;
                            }

                            var isFileResult = LooksLikeFileResult(e.Data);
                            var completedNow = 0;
                            lock (diagnostic)
                            {
                                if (diagnostic.Length < MaxDiagnosticChars)
                                {
                                    diagnostic.AppendLine(e.Data);
                                }

                                if (isFileResult)
                                {
                                    completedInChunk++;
                                    completedNow = completedInChunk;
                                    var reported = ExtractOutputPath(e.Data);
                                    if (reported != null)
                                    {
                                        outcome.ReportedOutputs[PathUtil.Key(reported)] = reported;
                                    }
                                }
                            }

                            context.Progress?.Report(new LogJobReport(e.Data));
                            if (completedNow > 0)
                            {
                                context.Progress?.Report(new ProgressJobReport(
                                    context.BaseDone + completedNow, context.Total));
                            }
                        };

                        process.OutputDataReceived += onData;
                        process.ErrorDataReceived += onData;
                    }

                    process.Start();

                    if (redirect)
                    {
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                    }

                    await WaitAsync(process, context.Token).ConfigureAwait(false);

                    outcome.ProcessSucceeded = !context.Token.IsCancellationRequested && process.ExitCode == 0;
                }
            }
            catch (Exception exception)
            {
                outcome.ProcessSucceeded = false;
                diagnostic.AppendLine(exception.ToString());
            }

            lock (diagnostic)
            {
                outcome.Diagnostic = diagnostic.ToString().Trim();
            }

            if (outcome.ProcessSucceeded)
            {
                outcome.Outputs = MetadataReader.LoadOutputMap(metadataPath);
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

            // Lets the redirected readers drain before the handles close.
            await Task.Yield();
        }

        private static bool LooksLikeFileResult(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            if (line.StartsWith("File", StringComparison.Ordinal) ||
                line.StartsWith("Total:", StringComparison.Ordinal))
            {
                return false;
            }

            return line.IndexOf(" kB > ", StringComparison.Ordinal) > 0;
        }

        private static string ExtractOutputPath(string line)
        {
            var marker = line.IndexOf(" kB > ", StringComparison.Ordinal);
            if (marker <= 0)
            {
                return null;
            }

            var path = line.Substring(0, marker).Trim();
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                path = path.Substring(4);
            }

            return path;
        }

        private static string FindReportedOutput(
            string input,
            ProcessingOptions options,
            ChunkOutcome outcome)
        {
            var predicted = Validator.PredictedOutputPath(input, options);
            if (outcome.ReportedOutputs.TryGetValue(PathUtil.Key(predicted), out var exact))
            {
                return exact;
            }

            var predictedName = Path.GetFileName(predicted);
            foreach (var pair in outcome.ReportedOutputs)
            {
                if (string.Equals(Path.GetFileName(pair.Value), predictedName, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        private static FileResult Resolve(string input, ProcessingOptions options, ChunkOutcome outcome)
        {
            if (outcome.ProcessSucceeded &&
                outcome.Outputs.TryGetValue(PathUtil.Key(input), out var reported) &&
                IsUsableOutput(reported))
            {
                return new FileResult { Status = FileStatus.Done, Output = reported };
            }

            // rimage writes no metadata for a failed batch, so fall back to the
            // per-file summary lines it still emits. Those give the real output
            // path; the predicted path remains a final existence check.
            var reportedOutput = FindReportedOutput(input, options, outcome);
            if (reportedOutput != null && IsUsableOutput(reportedOutput))
            {
                return new FileResult { Status = FileStatus.Done, Output = reportedOutput };
            }

            var predicted = Validator.PredictedOutputPath(input, options);
            if (IsUsableOutput(predicted))
            {
                return new FileResult { Status = FileStatus.Done, Output = predicted };
            }

            var error = outcome.ProcessSucceeded
                ? "rimage reported success but produced no usable output for this file"
                : "rimage exited with a failure";

            if (!string.IsNullOrEmpty(outcome.Diagnostic))
            {
                error += $"; {Truncate(outcome.Diagnostic, MaxResizeDiagnosticChars)}";
            }

            return new FileResult { Status = FileStatus.Failed, Error = error };
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

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : $"{value.Substring(0, max)}…";

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
