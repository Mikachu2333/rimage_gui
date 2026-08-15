use std::path::PathBuf;

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum OutputFormat {
    #[default]
    Jpeg,
    Png,
    JpegXl,
    WebP,
    Avif,
}

impl OutputFormat {
    pub const ALL: [Self; 5] = [Self::Jpeg, Self::Png, Self::JpegXl, Self::WebP, Self::Avif];

    #[must_use]
    pub const fn cli_name(self) -> &'static str {
        match self {
            Self::Jpeg => "mozjpeg",
            Self::Png => "oxipng",
            Self::JpegXl => "jpeg_xl",
            Self::WebP => "webp",
            Self::Avif => "avif",
        }
    }

    #[must_use]
    pub const fn extension(self) -> &'static str {
        match self {
            Self::Jpeg => "jpg",
            Self::Png => "png",
            Self::JpegXl => "jxl",
            Self::WebP => "webp",
            Self::Avif => "avif",
        }
    }

    #[must_use]
    pub const fn supports_quality(self) -> bool {
        matches!(self, Self::Jpeg | Self::WebP | Self::Avif)
    }
}

/// Resize filter passed to rimage as `--filter`; ignored when no resize is set.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ResizeFilter {
    Nearest,
    Box,
    Bilinear,
    Hamming,
    CatmullRom,
    Mitchell,
    #[default]
    Lanczos3,
}

impl ResizeFilter {
    pub const ALL: [Self; 7] = [
        Self::Nearest,
        Self::Box,
        Self::Bilinear,
        Self::Hamming,
        Self::CatmullRom,
        Self::Mitchell,
        Self::Lanczos3,
    ];

    #[must_use]
    pub const fn cli_name(self) -> &'static str {
        match self {
            Self::Nearest => "nearest",
            Self::Box => "box",
            Self::Bilinear => "bilinear",
            Self::Hamming => "hamming",
            Self::CatmullRom => "catmull-rom",
            Self::Mitchell => "mitchell",
            Self::Lanczos3 => "lanczos3",
        }
    }
}

/// How resizing is configured. `Classic` forwards the user's raw argument to
/// rimage (`@1.5`, `150%`, `1920x1080`, `720w`/`720h`, `1000l`/`500s`, and
/// whitespace-separated chains); `Bounds` computes an aspect-ratio-preserving
/// target size. The two modes are mutually exclusive.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub enum ResizeSpec {
    #[default]
    None,
    Classic {
        arg: String,
        filter: ResizeFilter,
    },
    Bounds(SizeBounds),
}

/// Final `--resize` values and `--filter` for one prepared file. Chained values
/// are emitted as successive `--resize` flags and each one maps the size the
/// previous value produced.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ResizeTarget {
    pub args: Vec<String>,
    pub filter: ResizeFilter,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum OutputMode {
    OriginalDir,
    SelectedDir(PathBuf),
    OriginalSubfolder(String),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OriginalPolicy {
    Keep,
    Backup,
    DeleteAfterVerifiedSuccess,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BoundKind {
    LongestEdge(u32),
    WidthHeight(u32, u32),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SizeBounds {
    pub min: Option<BoundKind>,
    pub max: Option<BoundKind>,
}

#[derive(Debug, Clone)]
pub struct ProcessingOptions {
    pub format: OutputFormat,
    pub quality: u8,
    pub quantization: Option<u8>,
    pub dithering: Option<u8>,
    pub suffix: Option<String>,
    pub output_mode: OutputMode,
    pub original_policy: OriginalPolicy,
    pub resize: ResizeSpec,
    /// Whether the backend runs with a hidden console window.
    pub hidden: bool,
}

#[derive(Debug, Clone)]
pub struct JobSpec {
    pub files: Vec<PathBuf>,
    pub options: ProcessingOptions,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PreparedFile {
    pub input: PathBuf,
    pub output_dir: PathBuf,
    pub resize: Option<ResizeTarget>,
}
