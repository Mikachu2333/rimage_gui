using System;

namespace RimageGui.Core
{
    /// <summary>Raised when the rimage backend cannot be located, probed, or extracted.</summary>
    public sealed class BackendException : Exception
    {
        public BackendException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }
}
