use std::{
    collections::HashSet,
    path::{Path, PathBuf},
};

use walkdir::WalkDir;

pub const SUPPORTED_EXTENSIONS: &[&str] = &[
    "avif", "bmp", "ff", "hdr", "jpg", "jpeg", "jxl", "png", "ppm", "psd", "qoi", "tif", "tiff",
    "webp",
];

#[must_use]
pub fn is_supported(path: &Path) -> bool {
    path.extension().and_then(|e| e.to_str()).is_some_and(|e| {
        SUPPORTED_EXTENSIONS
            .iter()
            .any(|known| e.eq_ignore_ascii_case(known))
    })
}

#[must_use]
pub fn normalize_existing(path: &Path) -> PathBuf {
    path.canonicalize().unwrap_or_else(|_| path.to_path_buf())
}

/// Converts a Windows verbatim path into a friendlier display-only string.
#[must_use]
pub fn display_path(path: &Path) -> String {
    display_text(&path.to_string_lossy())
}

/// Removes Windows verbatim prefixes from diagnostic text for display only.
#[must_use]
pub fn display_text(text: &str) -> String {
    text.replace(r"\\?\UNC\", r"\\").replace(r"\\?\", "")
}

#[must_use]
pub fn collect_paths(roots: &[PathBuf]) -> Vec<PathBuf> {
    let mut seen = HashSet::new();
    let mut result = Vec::new();
    for root in roots {
        if root.is_dir() {
            for entry in WalkDir::new(root)
                .follow_links(false)
                .into_iter()
                .filter_map(Result::ok)
            {
                add(entry.path(), &mut seen, &mut result);
            }
        } else {
            add(root, &mut seen, &mut result);
        }
    }
    result
}

fn add(path: &Path, seen: &mut HashSet<String>, output: &mut Vec<PathBuf>) {
    if !path.is_file() || !is_supported(path) {
        return;
    }
    let normalized = normalize_existing(path);
    let mut key = normalized.to_string_lossy().into_owned();
    if cfg!(windows) {
        key.make_ascii_lowercase();
    }
    if seen.insert(key) {
        output.push(normalized);
    }
}
