namespace RimageGui.Models
{
    /// <summary>
    /// The encoders the GUI offers. Niche intermediate formats (qoi, ppm,
    /// farbfeld) are deliberately not offered even though rimage supports them.
    /// <see cref="CliName"/> is the literal command word; <see cref="Extension"/>
    /// must match what rimage actually writes, because the output path is
    /// predicted from it before the job starts.
    /// </summary>
    public enum OutputFormat
    {
        MozJpeg,
        Jpeg,
        OxiPng,
        Png,
        WebP,
        Avif,
        JpegXl
    }
}
