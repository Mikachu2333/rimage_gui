namespace RimageGui.Models
{
    /// <summary>
    /// rimage can only constrain one direction per invocation, so a limit is
    /// either an upper bound (shrink only) or a lower bound (enlarge only).
    /// </summary>
    public enum BoundDirection
    {
        Maximum,
        Minimum
    }
}
