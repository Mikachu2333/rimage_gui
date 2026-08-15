use std::{
    ffi::{OsStr, OsString},
    path::Path,
};

use crate::model::{BoundKind, JobSpec, OriginalPolicy, OutputMode, ResizeFilter, ResizeSpec};

/// Builds one rimage invocation for the whole batch.
///
/// All input paths are passed through a `file.list`, so rimage reads them from
/// a single UTF-8 list instead of positional arguments. A fixed `--threads 1`
/// keeps memory usage predictable; the one `--metadata` file carries every
/// successful input/output pair for the batch.
#[must_use]
pub fn build_args(job: &JobSpec, file_list: &Path, metadata: &Path) -> Vec<OsString> {
    let options = &job.options;
    let mut args = vec![OsString::from(options.format.cli_name())];
    if options.format.supports_quality() {
        args.extend([
            OsString::from("--quality"),
            OsString::from(options.quality.to_string()),
        ]);
    }
    // `OriginalDir` keeps rimage's in-place output (beside each input);
    // `SelectedDir` funnels every output into one directory. This is the only
    // way to express the output location without per-file `--directory` values.
    if let OutputMode::SelectedDir(dir) = &options.output_mode {
        args.extend([OsString::from("--directory"), dir.as_os_str().to_owned()]);
    }
    if let Some(suffix) = &options.suffix {
        args.extend([OsString::from("--suffix"), OsString::from(suffix)]);
    }
    if options.original_policy == OriginalPolicy::Backup {
        args.push(OsString::from("--backup"));
    }
    if let Some(quantization) = options.quantization {
        args.extend([
            OsString::from("--quantization"),
            OsString::from(quantization.to_string()),
        ]);
        if let Some(dithering) = options.dithering {
            args.extend([
                OsString::from("--dithering"),
                OsString::from(dithering.to_string()),
            ]);
        }
    }
    push_resize_args(&mut args, &options.resize);
    let threads = options.threads.map_or_else(thread_count, usize::from);
    args.extend([
        OsString::from("--threads"),
        OsString::from(threads.to_string()),
        OsString::from("--no-progress"),
        OsString::from("--metadata"),
        metadata.as_os_str().to_owned(),
    ]);
    args.push(file_list.as_os_str().to_owned());
    args
}

/// Returns the rimage worker count: one less than the system's logical CPU
/// count, with a floor of one.
#[must_use]
fn thread_count() -> usize {
    std::thread::available_parallelism()
        .map_or(1, std::num::NonZeroUsize::get)
        .saturating_sub(1)
        .max(1)
}

/// Emits the resize preprocessing arguments shared by every input in the batch.
fn push_resize_args(args: &mut Vec<OsString>, resize: &ResizeSpec) {
    match resize {
        ResizeSpec::None => {}
        ResizeSpec::Classic { arg, filter } => {
            // `validate_job` normalizes and rejects malformed chains first, so
            // re-splitting here yields the same canonical values.
            for value in crate::validation::split_resize_args(arg).unwrap_or_default() {
                args.push(OsString::from("--resize"));
                args.push(OsString::from(value));
            }
            args.push(OsString::from("--filter"));
            args.push(OsString::from(filter.cli_name()));
        }
        ResizeSpec::Bounds(bounds) => {
            let (value, flag) = match (bounds.min, bounds.max) {
                (Some(BoundKind::LongestEdge(n)), None) => (format!("{n}l"), "--enlarge-only"),
                (Some(BoundKind::ShortestEdge(n)), None) => (format!("{n}s"), "--enlarge-only"),
                (None, Some(BoundKind::LongestEdge(n))) => (format!("{n}l"), "--reduce-only"),
                (None, Some(BoundKind::ShortestEdge(n))) => (format!("{n}s"), "--reduce-only"),
                _ => return,
            };
            args.push(OsString::from("--resize"));
            args.push(OsString::from(value));
            args.push(OsString::from(flag));
            args.push(OsString::from("--filter"));
            args.push(OsString::from(ResizeFilter::Lanczos3.cli_name()));
        }
    }
}

/// Formats a copyable Windows command line for diagnostics.
///
/// Arguments are quoted using the escaping rules understood by the Microsoft
/// C runtime (`CommandLineToArgvW`-compatible): backslashes before quotes and
/// before the closing quote are doubled (a quote preceded by `n` backslashes
/// is emitted as `2n + 1` backslashes plus the quote). Display uses lossy
/// Unicode only for logging; the actual process still receives the original
/// `OsString` values.
#[must_use]
pub fn format_command_line(executable: &Path, args: &[OsString]) -> String {
    std::iter::once(executable.as_os_str())
        .chain(args.iter().map(OsString::as_os_str))
        .map(quote_windows_arg)
        .collect::<Vec<_>>()
        .join(" ")
}

fn quote_windows_arg(argument: &OsStr) -> String {
    let value = argument.to_string_lossy();
    if !value.is_empty()
        && !value
            .chars()
            .any(|character| matches!(character, ' ' | '\t' | '"'))
    {
        return value.into_owned();
    }

    let mut quoted = String::with_capacity(value.len() + 2);
    quoted.push('"');
    let mut backslashes = 0;
    for character in value.chars() {
        if character == '\\' {
            backslashes += 1;
        } else {
            if character == '"' {
                quoted.extend(std::iter::repeat_n('\\', backslashes * 2 + 1));
            } else {
                quoted.extend(std::iter::repeat_n('\\', backslashes));
            }
            backslashes = 0;
            quoted.push(character);
        }
    }
    quoted.extend(std::iter::repeat_n('\\', backslashes * 2));
    quoted.push('"');
    quoted
}

#[cfg(test)]
mod tests {
    use std::path::Path;

    use super::*;
    use crate::model::{
        BoundKind, OriginalPolicy, OutputFormat, OutputMode, ProcessingOptions, ResizeFilter,
        ResizeSpec, SizeBounds,
    };

    fn job(output_mode: OutputMode, resize: ResizeSpec) -> JobSpec {
        JobSpec {
            files: vec!["C:\\输入 图片.jpg".into()],
            options: ProcessingOptions {
                format: OutputFormat::Jpeg,
                quality: 85,
                quantization: None,
                dithering: None,
                suffix: None,
                output_mode,
                original_policy: OriginalPolicy::Keep,
                resize,
                threads: None,
                hidden: true,
            },
        }
    }

    fn args_text(job: &JobSpec) -> Vec<String> {
        let args = build_args(
            job,
            Path::new("C:\\临时\\file.list"),
            Path::new("C:\\元 数据.json"),
        );
        args.iter()
            .map(|arg| arg.to_string_lossy().into_owned())
            .collect()
    }

    #[test]
    fn diagnostic_command_line_quotes_copyably() {
        let args = vec![
            OsString::from("mozjpeg"),
            OsString::from("--directory"),
            OsString::from("C:\\输出 目录\\"),
            OsString::from("say\"hello"),
            OsString::from(r#"C:\say\"hello"#),
            OsString::from("tail\\"),
        ];
        let line = format_command_line(Path::new("C:\\Program Files\\rimage.exe"), &args);
        assert_eq!(
            line,
            r#""C:\Program Files\rimage.exe" mozjpeg --directory "C:\输出 目录\\" "say\"hello" "C:\say\\\"hello" tail\"#
        );
    }

    #[test]
    fn original_dir_omits_directory_flag() {
        let text = args_text(&job(OutputMode::OriginalDir, ResizeSpec::None));
        assert!(!text.iter().any(|arg| arg == "--directory"));
    }

    #[test]
    fn selected_dir_keeps_directory_flag() {
        let text = args_text(&job(
            OutputMode::SelectedDir("C:\\输出 目录".into()),
            ResizeSpec::None,
        ));
        let index = text.iter().position(|arg| arg == "--directory").unwrap();
        assert_eq!(text[index + 1], "C:\\输出 目录");
    }

    #[test]
    fn batch_args_end_with_file_list_and_metadata() {
        let mut spec = job(OutputMode::OriginalDir, ResizeSpec::None);
        spec.options.suffix = Some("updated".into());
        let text = args_text(&spec);
        assert_eq!(text[0], "mozjpeg");
        let threads_index = text.iter().position(|a| a == "--threads").unwrap();
        assert_eq!(text[threads_index + 1], thread_count().to_string());
        assert!(text.iter().any(|arg| arg == "--no-progress"));
        assert!(text.iter().any(|arg| arg == "--suffix"));
        assert!(text.iter().any(|arg| arg == "updated"));
        assert!(text.iter().any(|arg| arg == "--metadata"));
        assert_eq!(text.last().map(String::as_str), Some("C:\\临时\\file.list"));
    }

    #[test]
    fn manual_threads_override_automatic_value() {
        let mut spec = job(OutputMode::OriginalDir, ResizeSpec::None);
        spec.options.threads = Some(3);
        let text = args_text(&spec);
        let threads_index = text.iter().position(|arg| arg == "--threads").unwrap();
        assert_eq!(text[threads_index + 1], "3");
    }

    #[test]
    fn classic_resize_emits_chained_flags_and_filter() {
        let spec = job(
            OutputMode::OriginalDir,
            ResizeSpec::Classic {
                arg: "720w 1000l".into(),
                filter: ResizeFilter::Mitchell,
            },
        );
        let text = args_text(&spec);
        let resize: Vec<&str> = text
            .iter()
            .filter(|arg| arg.as_str() == "--resize")
            .map(String::as_str)
            .collect();
        assert_eq!(resize.len(), 2);
        assert!(text.iter().any(|arg| arg == "720w"));
        assert!(text.iter().any(|arg| arg == "1000l"));
        assert!(text.iter().any(|arg| arg == "--filter"));
        assert!(text.iter().any(|arg| arg == "mitchell"));
    }

    #[test]
    fn bounds_resize_emits_reduce_only_direction() {
        let spec = job(
            OutputMode::OriginalDir,
            ResizeSpec::Bounds(SizeBounds {
                min: None,
                max: Some(BoundKind::LongestEdge(1000)),
            }),
        );
        let text = args_text(&spec);
        assert!(text.iter().any(|arg| arg == "--resize"));
        assert!(text.iter().any(|arg| arg == "1000l"));
        assert!(text.iter().any(|arg| arg == "--reduce-only"));
        assert!(text.iter().any(|arg| arg == "--filter"));
        assert!(text.iter().any(|arg| arg == "lanczos3"));
    }

    #[test]
    fn bounds_resize_emits_shortest_edge_enlarge_only() {
        let spec = job(
            OutputMode::OriginalDir,
            ResizeSpec::Bounds(SizeBounds {
                min: Some(BoundKind::ShortestEdge(500)),
                max: None,
            }),
        );
        let text = args_text(&spec);
        assert!(text.iter().any(|arg| arg == "--resize"));
        assert!(text.iter().any(|arg| arg == "500s"));
        assert!(text.iter().any(|arg| arg == "--enlarge-only"));
        assert!(text.iter().any(|arg| arg == "--filter"));
        assert!(text.iter().any(|arg| arg == "lanczos3"));
    }
}
