using System.Collections.Generic;

namespace RimageGui.Models
{
    /// <summary>A validated-ready job: the input list plus its options snapshot.</summary>
    public sealed class JobSpec
    {
        public JobSpec(IReadOnlyList<string> files, ProcessingOptions options)
        {
            Files = files;
            Options = options;
        }

        public IReadOnlyList<string> Files { get; }

        public ProcessingOptions Options { get; }
    }
}
