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
    public enum JobReportKind
    {
        Log,
        FileFinished,
        Progress
    }

    public sealed class JobReport
    {
        public JobReportKind Kind { get; set; }

        public string Line { get; set; }

        public string Input { get; set; }

        public FileStatus Status { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }

        public int Done { get; set; }

        public int Total { get; set; }
    }

    public sealed class JobSummary
    {
        public int Succeeded { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        public bool Cancelled { get; set; }
    }

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

            progress?.Report(new JobReport
            {
                Kind = JobReportKind.Progress,
                Done = 0,
                Total = total
            });

            var workingRoot = options.OutputMode == OutputMode.SelectedDir && options.PreserveStructure
                ? CommonRoot(files)
                : null;

            var scratch = Path.Combine(Path.GetTempPath(), "rimage-gui-" + Guid.NewGuid().ToString("N"));
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
                        progress?.Report(new JobReport
                        {
                            Kind = JobReportKind.FileFinished,
                            Input = input,
                            Status = FileStatus.Running
                        });
                    }

                    var outcome = await RunChunkAsync(
                        chunk, options, backendPath, scratch, workingRoot,
                        progress, !loggedCommand, token).ConfigureAwait(false);

                    loggedCommand = true;

                    foreach (var input in chunk)
                    {
                        var result = Resolve(input, options, outcome);

                        if (result.Status == FileStatus.Done &&
                            options.OriginalPolicy == OriginalPolicy.DeleteAfterVerifiedSuccess)
                        {
                            result = ApplyDeletePolicy(input, result, token.IsCancellationRequested);
                        }

                        if (result.Status == FileStatus.Done)
                        {
                            summary.Succeeded++;
                        }
                        else
                        {
                            summary.Failed++;
                        }

                        progress?.Report(new JobReport
                        {
                            Kind = JobReportKind.FileFinished,
                            Input = input,
                            Status = result.Status,
                            Output = result.Output,
                            Error = result.Error
                        });

                        if (result.Status == FileStatus.Failed && !string.IsNullOrEmpty(result.Error))
                        {
                            progress?.Report(new JobReport
                            {
                                Kind = JobReportKind.Log,
                                Line = Path.GetFileName(input) + ": " + result.Error
                            });
                        }
                    }

                    done += chunk.Count;
                    progress?.Report(new JobReport
                    {
                        Kind = JobReportKind.Progress,
                        Done = done,
                        Total = total
                    });
                }

                if (token.IsCancellationRequested)
                {
                    summary.Cancelled = true;
                    summary.Skipped = total - summary.Succeeded - summary.Failed;

                    for (var index = total - summary.Skipped; index < total; index++)
                    {
                        progress?.Report(new JobReport
                        {
                            Kind = JobReportKind.FileFinished,
                            Input = files[index],
                            Status = FileStatus.Skipped
                        });
                    }
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

        private sealed class ChunkOutcome
        {
            public bool ProcessSucceeded { get; set; }

            public Dictionary<string, string> Outputs { get; set; }

            public string Diagnostic { get; set; }

            public string CommandLine { get; set; }
        }

        private sealed class FileResult
        {
            public FileStatus Status { get; set; }

            public string Output { get; set; }

            public string Error { get; set; }
        }

        private static async Task<ChunkOutcome> RunChunkAsync(
            List<string> chunk,
            ProcessingOptions options,
            string backendPath,
            string scratch,
            string workingRoot,
            IProgress<JobReport> progress,
            bool logCommand,
            CancellationToken token)
        {
            var outcome = new ChunkOutcome();

            // rimage recognises the input list by its literal file name, so each
            // chunk gets its own directory holding a file called "file.list".
            var chunkDirectory = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(chunkDirectory);

            var listPath = Path.Combine(chunkDirectory, "file.list");
            var metadataPath = Path.Combine(chunkDirectory, "metadata.json");

            // A BOM would become part of the first path rimage reads.
            File.WriteAllText(listPath, string.Join("\r\n", chunk), new UTF8Encoding(false));

            var args = CommandBuilder.BuildArgs(options, listPath, metadataPath);
            outcome.CommandLine = PathUtil.DisplayCommandLine(backendPath, args);

            if (logCommand)
            {
                progress?.Report(new JobReport
                {
                    Kind = JobReportKind.Log,
                    Line = outcome.CommandLine
                });
            }

            // Redirecting is what feeds the in-app log, but it also swallows the
            // console the user asked to see when "hide rimage window" is off.
            var redirect = options.Hidden;

            var startInfo = new ProcessStartInfo(backendPath, PathUtil.BuildArgumentString(args))
            {
                UseShellExecute = false,
                CreateNoWindow = options.Hidden,
                WorkingDirectory = workingRoot ?? chunkDirectory,
                RedirectStandardOutput = redirect,
                RedirectStandardError = redirect
            };

            if (redirect)
            {
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            var diagnostic = new StringBuilder();

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

                            lock (diagnostic)
                            {
                                if (diagnostic.Length < MaxDiagnosticChars)
                                {
                                    diagnostic.AppendLine(e.Data);
                                }
                            }

                            progress?.Report(new JobReport
                            {
                                Kind = JobReportKind.Log,
                                Line = e.Data
                            });
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

                    await WaitAsync(process, token).ConfigureAwait(false);

                    outcome.ProcessSucceeded = !token.IsCancellationRequested && process.ExitCode == 0;
                }
            }
            catch (Exception exception)
            {
                outcome.ProcessSucceeded = false;
                diagnostic.AppendLine(exception.Message);
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
            while (!process.WaitForExit(100))
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

                process.WaitForExit(5000);
                return;
            }

            // Lets the redirected readers drain before the handles close.
            await Task.Yield();
        }

        private static FileResult Resolve(string input, ProcessingOptions options, ChunkOutcome outcome)
        {
            if (outcome.ProcessSucceeded && outcome.Outputs != null &&
                outcome.Outputs.TryGetValue(PathUtil.Key(input), out var reported) &&
                IsUsableOutput(reported))
            {
                return new FileResult { Status = FileStatus.Done, Output = reported };
            }

            // rimage writes no metadata for a failed batch, so fall back to the
            // predicted path: a file that exists and is non-empty means this
            // particular input made it through even though the chunk did not.
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
                error += "; " + Truncate(outcome.Diagnostic, 600);
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
                    Error = "output written but deleting the original failed: " + exception.Message
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

            string[] Segments(string file) =>
                (Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

            var common = Segments(files[0]);

            for (var index = 1; index < files.Count && common.Length > 0; index++)
            {
                var current = Segments(files[index]);
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
            value.Length <= max ? value : value.Substring(0, max) + "…";

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
