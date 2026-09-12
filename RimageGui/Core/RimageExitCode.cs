using System;

namespace RimageGui.Core
{
    /// <summary>
    /// Exit codes rimage uses to tell the caller which side a failure came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code is the GUI's cheapest signal: it says whether anything landed on
    /// disk without parsing a byte of output. That distinction is what decides
    /// whether a batch may be re-run.
    /// </para>
    /// <para>
    /// <see cref="Input"/> and <see cref="Output"/> mean the run was a clean
    /// no-op — nothing was written, so retrying the whole batch is safe.
    /// <see cref="Partial"/> is the opposite: some outputs exist already and a
    /// blind re-run would overwrite them.
    /// </para>
    /// </remarks>
    public enum RimageExitCode
    {
        /// <summary>Every input was processed.</summary>
        Success = 0,

        /// <summary>Nothing was touched: arguments, config or output layout were unusable.</summary>
        Usage = 2,

        /// <summary>At least one input could not be read and no output was written.</summary>
        Input = 3,

        /// <summary>At least one output could not be written and no output was written.</summary>
        Output = 4,

        /// <summary>Some files succeeded and some failed; the disk already changed.</summary>
        Partial = 5,

        /// <summary>A Rust panic escaped (101) or <c>panic = "abort"</c> fired (134).</summary>
        Panic = 101,

        /// <summary>SIGABRT from <c>panic = "abort"</c>.</summary>
        Abort = 134,

        /// <summary>Anything else: an unrecognised code, or the process never reported one.</summary>
        Unexpected = -1
    }

    public static class RimageExitCodes
    {
        /// <summary>Maps a raw process exit code onto the documented set.</summary>
        public static RimageExitCode Classify(int code)
        {
            switch (code)
            {
                case 0:
                    return RimageExitCode.Success;
                case 2:
                    return RimageExitCode.Usage;
                case 3:
                    return RimageExitCode.Input;
                case 4:
                    return RimageExitCode.Output;
                case 5:
                    return RimageExitCode.Partial;
                case 101:
                    return RimageExitCode.Panic;
                case 134:
                    return RimageExitCode.Abort;
                default:
                    return RimageExitCode.Unexpected;
            }
        }

        /// <summary>
        /// True when the same failure will repeat for every remaining chunk, so
        /// the run must stop instead of spawning the same broken command again.
        /// </summary>
        /// <remarks>
        /// <see cref="RimageExitCode.Usage"/> is the important one: it is decided
        /// before any file is opened, which makes it a property of the arguments
        /// rather than of the data.
        /// </remarks>
        public static bool IsFatal(RimageExitCode code)
        {
            return code == RimageExitCode.Usage
                   || code == RimageExitCode.Panic
                   || code == RimageExitCode.Abort
                   || code == RimageExitCode.Unexpected;
        }

        /// <summary>
        /// True when the run is known to have left the disk untouched, which
        /// makes retrying the whole batch safe.
        /// </summary>
        public static bool IsCleanNoOp(RimageExitCode code)
        {
            return code == RimageExitCode.Input || code == RimageExitCode.Output;
        }

        /// <summary>
        /// True when rimage is expected to have written a <c>--metadata</c> file.
        /// </summary>
        /// <remarks>
        /// Metadata is only produced when at least one image succeeded, so
        /// <see cref="RimageExitCode.Input"/> and <see cref="RimageExitCode.Output"/>
        /// never have one — and neither does a cancelled run. Callers must treat
        /// "metadata passed but file absent" as a normal outcome, not an error.
        /// </remarks>
        public static bool CarriesMetadata(RimageExitCode code)
        {
            return code == RimageExitCode.Success || code == RimageExitCode.Partial;
        }
    }
}
