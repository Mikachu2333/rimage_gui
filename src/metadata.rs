use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
};

use serde::Deserialize;
use thiserror::Error;

#[derive(Debug, Deserialize)]
pub struct Metadata {
    #[serde(default)]
    pub images: Vec<ImageMetadata>,
}

#[derive(Debug, Deserialize)]
pub struct ImageMetadata {
    pub input: PathBuf,
    pub output: PathBuf,
}

#[derive(Debug, Error)]
pub enum MetadataError {
    #[error("metadata could not be read: {0}")]
    Io(#[from] std::io::Error),
    #[error("metadata is invalid: {0}")]
    Json(#[from] serde_json::Error),
    #[error("metadata does not contain the current input")]
    MissingInput,
}

pub(crate) fn path_key(path: &Path) -> String {
    let normalized = path.canonicalize().unwrap_or_else(|_| path.to_path_buf());
    let key = normalized.to_string_lossy().into_owned();
    if cfg!(windows) {
        key.to_lowercase()
    } else {
        key
    }
}

/// Reads rimage metadata and returns the actual output for the requested input.
///
/// # Errors
/// Returns an error for unreadable/invalid metadata or when no input matches.
pub fn output_for_input(path: &Path, input: &Path) -> Result<PathBuf, MetadataError> {
    let input_key = path_key(input);
    load_output_map(path)?
        .remove(&input_key)
        .ok_or(MetadataError::MissingInput)
}

/// Reads a metadata file once and returns a map from canonical input-path key
/// to actual output path, so a batch invocation only parses the JSON once.
///
/// # Errors
/// Returns an error for unreadable or invalid metadata.
pub fn load_output_map(path: &Path) -> Result<HashMap<String, PathBuf>, MetadataError> {
    let bytes = fs::read(path)?;
    let metadata: Metadata = serde_json::from_slice(&bytes)?;
    Ok(metadata
        .images
        .into_iter()
        .map(|entry| (path_key(&entry.input), entry.output))
        .collect())
}
