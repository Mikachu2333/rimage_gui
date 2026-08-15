use std::{
    collections::HashSet,
    fs,
    path::{Path, PathBuf},
};

use thiserror::Error;

pub use crate::metadata::path_key;
use crate::model::{BoundKind, JobSpec, OriginalPolicy, OutputMode, ResizeSpec, SizeBounds};

#[derive(Debug, Error, PartialEq, Eq)]
pub enum ValidationError {
    #[error("no input files")]
    NoFiles,
    #[error("quality must be between 1 and 100")]
    Quality,
    #[error("quantization must be between 1 and 100")]
    Quantization,
    #[error("dithering requires quantization and must be between 1 and 100")]
    Dithering,
    #[error("invalid suffix")]
    Suffix,
    #[error("backup policy cannot be combined with an output suffix")]
    BackupSuffixConflict,
    #[error("output directory is not selected")]
    OutputDirectory,
    #[error("invalid resize argument")]
    Resize,
    #[error("size bounds must be positive and single-direction")]
    SizeBounds,
    #[error("delete mode cannot use an output path equal to its input")]
    UnsafeDelete,
    #[error("multiple inputs resolve to the same output path: {0}")]
    DuplicateOutput(PathBuf),
    #[error("an output path would overwrite another batch input: {0}")]
    OutputOverwritesInput(PathBuf),
}

/// Windows device names are reserved even with an extension (for example
/// `CON`, `AUX`, `COM1`, `NUL.txt`), so they must not be used as a suffix.
#[must_use]
pub fn valid_component(value: &str) -> bool {
    const RESERVED: &[&str] = &[
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8",
        "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    ];
    let base = value.split('.').next().unwrap_or_default();
    !value.is_empty()
        && value != "."
        && value != ".."
        && !value.ends_with(['.', ' '])
        && !value.chars().any(|c| {
            c.is_control() || matches!(c, '/' | '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|')
        })
        && !RESERVED.iter().any(|name| base.eq_ignore_ascii_case(name))
}

/// # Errors
/// Returns the first invalid job setting.
pub fn validate_job(job: &JobSpec) -> Result<(), ValidationError> {
    if job.files.is_empty() {
        return Err(ValidationError::NoFiles);
    }
    let options = &job.options;
    if !(1..=100).contains(&options.quality) {
        return Err(ValidationError::Quality);
    }
    if options
        .quantization
        .is_some_and(|v| !(1..=100).contains(&v))
    {
        return Err(ValidationError::Quantization);
    }
    if options
        .dithering
        .is_some_and(|v| options.quantization.is_none() || !(1..=100).contains(&v))
    {
        return Err(ValidationError::Dithering);
    }
    if options
        .suffix
        .as_deref()
        .is_some_and(|s| !valid_component(s))
    {
        return Err(ValidationError::Suffix);
    }
    if options.original_policy == OriginalPolicy::Backup && options.suffix.is_some() {
        return Err(ValidationError::BackupSuffixConflict);
    }
    if let OutputMode::SelectedDir(path) = &options.output_mode
        && path.as_os_str().is_empty()
    {
        return Err(ValidationError::OutputDirectory);
    }
    validate_resize(&options.resize)?;
    validate_output_paths(job)?;
    Ok(())
}

/// Validates the configured resize mode. `Classic` arguments are normalized
/// to a canonical rimage form; `Bounds` must be positive and single-direction.
fn validate_resize(resize: &ResizeSpec) -> Result<(), ValidationError> {
    match resize {
        ResizeSpec::None => Ok(()),
        ResizeSpec::Classic { arg, .. } => {
            split_resize_args(arg)?;
            Ok(())
        }
        ResizeSpec::Bounds(bounds) => validate_bounds(*bounds),
    }
}

/// Predicts rimage 0.13.0's output path for one input.
#[must_use]
pub fn predicted_output_path(input: &Path, job: &JobSpec) -> PathBuf {
    let parent = input.parent().unwrap_or_else(|| Path::new("."));
    let output_dir = match &job.options.output_mode {
        OutputMode::OriginalDir => parent.to_path_buf(),
        OutputMode::SelectedDir(path) => path.clone(),
    };
    let stem = input.file_stem().unwrap_or_default().to_string_lossy();
    let file_name = job.options.suffix.as_ref().map_or_else(
        || format!("{stem}.{}", job.options.format.extension()),
        |suffix| format!("{stem}{suffix}.{}", job.options.format.extension()),
    );
    output_dir.join(file_name)
}

/// Rejects collisions before any backend process starts.
///
/// # Errors
/// Returns an error for duplicate outputs, outputs that overwrite another batch
/// input, or delete mode that resolves to the current input.
pub fn validate_output_paths(job: &JobSpec) -> Result<(), ValidationError> {
    let input_keys = job
        .files
        .iter()
        .map(|input| path_key(input))
        .collect::<HashSet<_>>();
    let mut output_keys = HashSet::with_capacity(job.files.len());
    for input in &job.files {
        let output = predicted_output_path(input, job);
        let input_key = path_key(input);
        let output_key = path_key(&output);
        if !output_keys.insert(output_key.clone()) {
            return Err(ValidationError::DuplicateOutput(output));
        }
        if output_key == input_key {
            if job.options.original_policy == OriginalPolicy::DeleteAfterVerifiedSuccess {
                return Err(ValidationError::UnsafeDelete);
            }
        } else if input_keys.contains(&output_key) {
            return Err(ValidationError::OutputOverwritesInput(output));
        }
    }
    Ok(())
}

/// # Errors
/// Returns an error when a bound is non-positive or when both a minimum and a
/// maximum are set (rimage cannot express a clamp in one batch invocation).
pub fn validate_bounds(bounds: SizeBounds) -> Result<(), ValidationError> {
    let positive = |bound: Option<BoundKind>| match bound {
        Some(BoundKind::LongestEdge(n)) => n > 0,
        None => true,
    };
    if !positive(bounds.min) || !positive(bounds.max) {
        return Err(ValidationError::SizeBounds);
    }
    if bounds.min.is_some() && bounds.max.is_some() {
        return Err(ValidationError::SizeBounds);
    }
    Ok(())
}

/// Normalizes a single classic resize argument accepted by the GUI.
///
/// Accepts `@1.5` (multiplier), `150%` (percentage), `1920x1080` (fixed),
/// `720w`/`720h` (one side, keep aspect), `1000l`/`500s` (longest/shortest
/// side), plus the Aardio-style `720x_` and `720x` spellings which normalize
/// to `720w`.
///
/// # Errors
/// Returns `ValidationError::Resize` for unrecognized or non-positive values.
pub fn normalize_resize_arg(input: &str) -> Result<String, ValidationError> {
    let value = input.trim();
    if value.is_empty() {
        return Err(ValidationError::Resize);
    }
    if let Some(rest) = value.strip_prefix('@') {
        let factor: f64 = rest.trim().parse().map_err(|_| ValidationError::Resize)?;
        if factor > 0.0 && factor.is_finite() {
            return Ok(format!("@{factor}"));
        }
        return Err(ValidationError::Resize);
    }
    if let Some(rest) = value.strip_suffix('%') {
        let percent: f64 = rest.trim().parse().map_err(|_| ValidationError::Resize)?;
        if percent > 0.0 && percent.is_finite() {
            return Ok(format!("{percent}%"));
        }
        return Err(ValidationError::Resize);
    }
    let lower = value.to_ascii_lowercase();
    if let Some(rest) = lower.strip_suffix('w') {
        return normalize_side(rest, 'w');
    }
    if let Some(rest) = lower.strip_suffix('h') {
        return normalize_side(rest, 'h');
    }
    if let Some(rest) = lower.strip_suffix('l') {
        return normalize_side(rest, 'l');
    }
    if let Some(rest) = lower.strip_suffix('s') {
        return normalize_side(rest, 's');
    }
    if let Some((width_part, height_part)) = lower.split_once('x') {
        let width: u32 = width_part
            .trim()
            .parse()
            .map_err(|_| ValidationError::Resize)?;
        if width == 0 {
            return Err(ValidationError::Resize);
        }
        let height_part = height_part.trim();
        if height_part.is_empty() || height_part == "_" {
            return Ok(format!("{width}w"));
        }
        let height: u32 = height_part.parse().map_err(|_| ValidationError::Resize)?;
        if height == 0 {
            return Err(ValidationError::Resize);
        }
        return Ok(format!("{width}x{height}"));
    }
    Err(ValidationError::Resize)
}

/// Normalizes a resize value anchored to one side (`w`, `h`, `l`, or `s`).
/// The digits must form a positive integer; anything else is rejected instead
/// of silently extracting the leading number.
fn normalize_side(digits: &str, marker: char) -> Result<String, ValidationError> {
    let length: u32 = digits.trim().parse().map_err(|_| ValidationError::Resize)?;
    if length == 0 {
        return Err(ValidationError::Resize);
    }
    Ok(format!("{length}{marker}"))
}

/// Splits a possibly-chained resize argument on whitespace and normalizes each
/// value. A blank chain or any invalid value is rejected.
///
/// # Errors
/// Returns `ValidationError::Resize` for a blank chain or an invalid value.
pub fn split_resize_args(input: &str) -> Result<Vec<String>, ValidationError> {
    let values = input.split_whitespace().collect::<Vec<_>>();
    if values.is_empty() {
        return Err(ValidationError::Resize);
    }
    values.into_iter().map(normalize_resize_arg).collect()
}

#[must_use]
pub fn safe_to_delete(input: &Path, output: &Path, cancelled: bool) -> bool {
    if cancelled || !output.is_file() || fs::metadata(output).map_or(true, |m| m.len() == 0) {
        return false;
    }
    let input_key = input.canonicalize().unwrap_or_else(|_| input.to_path_buf());
    let output_key = output
        .canonicalize()
        .unwrap_or_else(|_| output.to_path_buf());
    input_key != output_key
}
