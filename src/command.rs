use std::{
    ffi::{OsStr, OsString},
    path::Path,
};

use crate::metadata::path_key;
use crate::model::{JobSpec, OriginalPolicy, PreparedFile};

/// Builds one rimage invocation for a single input file.
///
/// Every invocation runs serially: exactly one input file per process and a
/// fixed `--threads 1`, so rimage never processes images in parallel. Each
/// invocation also gets its own `--metadata` file so results are matched
/// exactly to the current input.
#[must_use]
pub fn build_args(job: &JobSpec, file: &PreparedFile, metadata: &Path) -> Vec<OsString> {
    let options = &job.options;
    let mut args = vec![OsString::from(options.format.cli_name())];
    if options.format.supports_quality() {
        args.extend([
            OsString::from("--quality"),
            OsString::from(options.quality.to_string()),
        ]);
    }
    // rimage's `--backup` fails (exit 1, no diagnostic) when an explicit
    // `--directory` resolves to the input's own directory: it renames the
    // input to `<stem>@backup.<ext>` and then cannot publish the output.
    // Omitting `--directory` in that case selects the identical output
    // location while keeping the native in-place backup behavior.
    let backup_outputs_in_place = job.options.original_policy == OriginalPolicy::Backup
        && path_key(&file.output_dir)
            == path_key(file.input.parent().unwrap_or_else(|| Path::new(".")));
    if !backup_outputs_in_place {
        args.extend([
            OsString::from("--directory"),
            file.output_dir.as_os_str().to_owned(),
        ]);
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
    if let Some(resize) = &file.resize {
        for value in &resize.args {
            args.extend([OsString::from("--resize"), OsString::from(value.clone())]);
        }
        args.extend([
            OsString::from("--filter"),
            OsString::from(resize.filter.cli_name()),
        ]);
    }
    args.extend([
        OsString::from("--threads"),
        OsString::from("1"),
        OsString::from("--no-progress"),
        OsString::from("--metadata"),
        metadata.as_os_str().to_owned(),
    ]);
    args.push(file.input.as_os_str().to_owned());
    args
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
        OutputFormat, OutputMode, ProcessingOptions, ResizeFilter, ResizeSpec, ResizeTarget,
    };

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
    fn backup_in_original_directory_omits_directory_flag() {
        use crate::model::{OutputMode, ProcessingOptions, ResizeSpec};

        let job = JobSpec {
            files: vec!["C:\\a.jpg".into()],
            options: ProcessingOptions {
                format: OutputFormat::Jpeg,
                quality: 85,
                quantization: None,
                dithering: None,
                suffix: None,
                output_mode: OutputMode::OriginalDir,
                original_policy: OriginalPolicy::Backup,
                resize: ResizeSpec::None,
                hidden: true,
            },
        };
        let file = PreparedFile {
            input: "C:\\a.jpg".into(),
            output_dir: "C:\\".into(),
            resize: None,
        };
        let args = build_args(&job, &file, Path::new("C:\\meta.json"));
        let text: Vec<String> = args
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect();
        assert!(!text.contains(&"--directory".to_string()));
        assert!(text.contains(&"--backup".to_string()));
    }

    #[test]
    fn backup_in_selected_directory_keeps_directory_flag() {
        use crate::model::{OutputMode, ProcessingOptions, ResizeSpec};

        let job = JobSpec {
            files: vec!["C:\\a.jpg".into()],
            options: ProcessingOptions {
                format: OutputFormat::Jpeg,
                quality: 85,
                quantization: None,
                dithering: None,
                suffix: None,
                output_mode: OutputMode::SelectedDir("C:\\out".into()),
                original_policy: OriginalPolicy::Backup,
                resize: ResizeSpec::None,
                hidden: true,
            },
        };
        let file = PreparedFile {
            input: "C:\\a.jpg".into(),
            output_dir: "C:\\out".into(),
            resize: None,
        };
        let args = build_args(&job, &file, Path::new("C:\\meta.json"));
        let text: Vec<String> = args
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect();
        assert!(text.contains(&"--directory".to_string()));
        assert!(text.contains(&"C:\\out".to_string()));
    }

    #[test]
    fn single_file_args_are_serial_and_include_filter() {
        let job = JobSpec {
            files: vec!["C:\\a.jpg".into()],
            options: ProcessingOptions {
                format: OutputFormat::Jpeg,
                quality: 85,
                quantization: None,
                dithering: None,
                suffix: Some("updated".into()),
                output_mode: OutputMode::OriginalDir,
                original_policy: OriginalPolicy::Keep,
                resize: ResizeSpec::None,
                hidden: true,
            },
        };
        let file = PreparedFile {
            input: "C:\\a.jpg".into(),
            output_dir: "C:\\a".into(),
            resize: Some(ResizeTarget {
                args: vec!["720w".into()],
                filter: ResizeFilter::Mitchell,
            }),
        };
        let args = build_args(&job, &file, Path::new("C:\\meta.json"));
        let text: Vec<String> = args
            .iter()
            .map(|a| a.to_string_lossy().into_owned())
            .collect();
        assert_eq!(text[0], "mozjpeg");
        let threads_index = text.iter().position(|a| a == "--threads").unwrap();
        assert_eq!(text[threads_index + 1], "1");
        assert!(text.contains(&"--filter".to_string()));
        assert!(text.contains(&"mitchell".to_string()));
        assert!(text.contains(&"720w".to_string()));
        assert!(text.contains(&"C:\\meta.json".to_string()));
        assert_eq!(text.iter().filter(|a| a.as_str() == "C:\\a.jpg").count(), 1);
    }
}
