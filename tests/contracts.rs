use std::{ffi::OsString, fs};

use rimage_gui::{
    backend::{WorkerEvent, extract_backend, start_job, verify_backend},
    command::build_args,
    input::{collect_paths, display_path, display_text, is_supported},
    metadata::output_for_input,
    model::{
        BoundKind, JobSpec, OriginalPolicy, OutputFormat, OutputMode, PreparedFile,
        ProcessingOptions, ResizeFilter, ResizeSpec, ResizeTarget, SizeBounds,
    },
    validation::{
        ValidationError, calculate_resize, normalize_resize_arg, path_key, predicted_output_path,
        prepare_job_files, reject_unsafe_delete, safe_to_delete, valid_component, validate_bounds,
        validate_job, validate_output_paths,
    },
};

fn job(format: OutputFormat) -> JobSpec {
    JobSpec {
        files: vec!["C:\\输入 图片.jpg".into()],
        options: ProcessingOptions {
            format,
            quality: 85,
            quantization: Some(80),
            dithering: Some(75),
            suffix: Some("updated".into()),
            output_mode: OutputMode::SelectedDir("C:\\输出 目录".into()),
            original_policy: OriginalPolicy::Keep,
            resize: ResizeSpec::None,
            hidden: true,
        },
    }
}

#[test]
fn format_mapping_and_quality_contract() {
    let cases = [
        (OutputFormat::Jpeg, "mozjpeg", true),
        (OutputFormat::Png, "oxipng", false),
        (OutputFormat::JpegXl, "jpeg_xl", false),
        (OutputFormat::WebP, "webp", true),
        (OutputFormat::Avif, "avif", true),
    ];
    for (format, name, quality) in cases {
        let j = job(format);
        let p = PreparedFile {
            input: j.files[0].clone(),
            output_dir: "C:\\输出 目录".into(),
            resize: None,
        };
        let args = build_args(&j, &p, std::path::Path::new("meta.json"));
        assert_eq!(args[0], OsString::from(name));
        assert_eq!(args.iter().any(|a| a == "--quality"), quality);
        assert_eq!(
            format.extension(),
            match format {
                OutputFormat::Jpeg => "jpg",
                OutputFormat::Png => "png",
                OutputFormat::JpegXl => "jxl",
                OutputFormat::WebP => "webp",
                OutputFormat::Avif => "avif",
            }
        );
    }
}

#[test]
fn argv_is_structured_and_contains_expected_options() {
    let mut j = job(OutputFormat::Jpeg);
    j.options.original_policy = OriginalPolicy::Backup;
    let p = PreparedFile {
        input: j.files[0].clone(),
        output_dir: "C:\\输出 目录".into(),
        resize: Some(ResizeTarget {
            arg: "800x600".into(),
            filter: ResizeFilter::Lanczos3,
        }),
    };
    let args = build_args(&j, &p, std::path::Path::new("C:\\元 数据.json"));
    assert!(args.contains(&OsString::from("--backup")));
    assert!(args.contains(&OsString::from("--resize")));
    assert!(args.contains(&OsString::from("800x600")));
    assert!(args.contains(&OsString::from("--filter")));
    assert!(args.contains(&OsString::from("lanczos3")));
    assert!(args.contains(&OsString::from("--metadata")));
    assert!(args.contains(&OsString::from("--threads")));
    assert!(args.contains(&OsString::from("1")));
    assert!(
        args.iter()
            .all(|arg| !arg.to_string_lossy().starts_with('"'))
    );
}

#[test]
fn bounds_round_safely_and_detect_conflict() {
    let downscaled = calculate_resize(
        4001,
        3000,
        SizeBounds {
            min: None,
            max: Some(BoundKind::LongestEdge(1920)),
        },
    )
    .unwrap();
    assert_eq!(downscaled, Some((1920, 1439)));

    let grown = calculate_resize(
        101,
        51,
        SizeBounds {
            min: Some(BoundKind::WidthHeight(200, 100)),
            max: None,
        },
    )
    .unwrap()
    .unwrap();
    assert!(grown.0 >= 200 && grown.1 >= 100);

    assert_eq!(
        calculate_resize(
            800,
            600,
            SizeBounds {
                min: Some(BoundKind::LongestEdge(400)),
                max: Some(BoundKind::WidthHeight(1_000, 1_000)),
            },
        )
        .unwrap(),
        None
    );
    assert!(
        calculate_resize(
            100,
            50,
            SizeBounds {
                min: Some(BoundKind::LongestEdge(200)),
                max: Some(BoundKind::WidthHeight(250, 150)),
            },
        )
        .is_ok()
    );
    assert_eq!(
        calculate_resize(
            100,
            100,
            SizeBounds {
                min: Some(BoundKind::LongestEdge(200)),
                max: Some(BoundKind::LongestEdge(150)),
            },
        ),
        Err(ValidationError::SizeBounds)
    );
    assert_eq!(
        calculate_resize(
            400,
            100,
            SizeBounds {
                min: Some(BoundKind::WidthHeight(200, 200)),
                max: Some(BoundKind::WidthHeight(500, 150)),
            },
        ),
        Err(ValidationError::SizeBounds)
    );
}

#[test]
fn validates_components_and_job_contract() {
    assert!(valid_component("updated"));
    for bad in [
        "", ".", "..", "a/b", "a\\b", "bad.", "a:b", "CON", "com1", "NUL.txt",
    ] {
        assert!(!valid_component(bad), "{bad}");
    }
    let mut j = job(OutputFormat::Jpeg);
    j.options.dithering = Some(0);
    assert_eq!(validate_job(&j), Err(ValidationError::Dithering));
    assert_eq!(
        validate_bounds(SizeBounds {
            min: Some(BoundKind::LongestEdge(500)),
            max: Some(BoundKind::LongestEdge(400)),
        }),
        Err(ValidationError::SizeBounds)
    );
    assert_eq!(
        validate_bounds(SizeBounds {
            min: Some(BoundKind::WidthHeight(800, 300)),
            max: Some(BoundKind::WidthHeight(700, 400)),
        }),
        Err(ValidationError::SizeBounds)
    );
}

#[test]
fn classic_resize_is_normalized_and_validated() {
    let mut j = job(OutputFormat::Jpeg);
    j.options.resize = ResizeSpec::Classic {
        arg: "bogus".into(),
        filter: ResizeFilter::Box,
    };
    assert_eq!(validate_job(&j), Err(ValidationError::Resize));
    j.options.resize = ResizeSpec::Classic {
        arg: "720x_".into(),
        filter: ResizeFilter::Box,
    };
    assert!(validate_job(&j).is_ok());
}

#[test]
fn resize_arg_normalizes_aardio_formats() {
    assert_eq!(normalize_resize_arg("@1.5").unwrap(), "@1.5");
    assert_eq!(normalize_resize_arg("@2.0").unwrap(), "@2");
    assert_eq!(normalize_resize_arg("150%").unwrap(), "150%");
    assert_eq!(normalize_resize_arg("150 %").unwrap(), "150%");
    assert_eq!(normalize_resize_arg("1920x1080").unwrap(), "1920x1080");
    assert_eq!(normalize_resize_arg("720x_").unwrap(), "720w");
    assert_eq!(normalize_resize_arg("720x").unwrap(), "720w");
    assert_eq!(normalize_resize_arg("720w").unwrap(), "720w");
    assert_eq!(normalize_resize_arg("720h").unwrap(), "720h");
    for bad in ["", "abc", "0", "0x100", "x1080", "-5%", "1.5"] {
        assert_eq!(
            normalize_resize_arg(bad),
            Err(ValidationError::Resize),
            "{bad}"
        );
    }
}

#[test]
fn resize_filter_maps_to_cli_names() {
    let names: Vec<&str> = ResizeFilter::ALL.iter().map(|f| f.cli_name()).collect();
    assert_eq!(
        names,
        [
            "nearest",
            "box",
            "bilinear",
            "hamming",
            "catmull-rom",
            "mitchell",
            "lanczos3"
        ]
    );
    assert_eq!(ResizeFilter::default().cli_name(), "lanczos3");
}

#[test]
fn per_image_bounds_conflict_is_detected_during_preflight() {
    let dir = tempfile::tempdir().unwrap();
    let input = dir.path().join("wide.png");
    image::RgbImage::from_pixel(400, 100, image::Rgb([1, 2, 3]))
        .save(&input)
        .unwrap();
    let mut spec = job(OutputFormat::Png);
    spec.files = vec![input.clone()];
    spec.options.output_mode = OutputMode::OriginalDir;
    spec.options.resize = ResizeSpec::Bounds(SizeBounds {
        min: Some(BoundKind::WidthHeight(200, 200)),
        max: Some(BoundKind::LongestEdge(500)),
    });
    assert_eq!(
        prepare_job_files(&spec),
        Err(ValidationError::DimensionsConflict(input))
    );
}

#[test]
fn classic_resize_skips_dimension_reading() {
    let dir = tempfile::tempdir().unwrap();
    // A file rimage understands but the image crate cannot decode still works
    // with a classic resize argument because no dimensions are required.
    let input = dir.path().join("opaque.avif");
    fs::write(&input, b"not a real avif").unwrap();
    let mut spec = job(OutputFormat::Jpeg);
    spec.files = vec![input.clone()];
    spec.options.output_mode = OutputMode::OriginalDir;
    spec.options.resize = ResizeSpec::Classic {
        arg: "720x_".into(),
        filter: ResizeFilter::Lanczos3,
    };
    let prepared = prepare_job_files(&spec).unwrap();
    assert_eq!(
        prepared[0].resize.as_ref().map(|r| r.arg.as_str()),
        Some("720w")
    );
    assert_eq!(
        prepared[0].resize.as_ref().map(|r| r.filter),
        Some(ResizeFilter::Lanczos3)
    );
}

#[test]
fn display_paths_hide_windows_verbatim_prefixes() {
    assert_eq!(
        display_path(std::path::Path::new(r"\\?\C:\Pictures\a.png")),
        r"C:\Pictures\a.png"
    );
    assert_eq!(
        display_path(std::path::Path::new(r"\\?\UNC\server\share\a.png")),
        r"\\server\share\a.png"
    );
    assert_eq!(
        display_text(r"error: \\?\C:\Pictures\a.png: decode failed"),
        r"error: C:\Pictures\a.png: decode failed"
    );
}

#[test]
fn deletion_requires_nonempty_distinct_output() {
    let dir = tempfile::tempdir().unwrap();
    let input = dir.path().join("in.jpg");
    let output = dir.path().join("out.jpg");
    fs::write(&input, b"input").unwrap();
    fs::write(&output, b"output").unwrap();
    assert!(safe_to_delete(&input, &output, false));
    assert!(!safe_to_delete(&input, &input, false));
    assert!(!safe_to_delete(&input, &output, true));
    fs::write(&output, []).unwrap();
    assert!(!safe_to_delete(&input, &output, false));

    let mut delete_job = job(OutputFormat::Jpeg);
    delete_job.options.original_policy = OriginalPolicy::DeleteAfterVerifiedSuccess;
    delete_job.options.output_mode = OutputMode::OriginalDir;
    delete_job.options.suffix = None;
    let prepared = PreparedFile {
        input: dir.path().join("same.jpg"),
        output_dir: dir.path().to_path_buf(),
        resize: None,
    };
    assert_eq!(
        reject_unsafe_delete(&prepared, &delete_job),
        Err(ValidationError::UnsafeDelete)
    );
}

#[test]
fn output_preflight_rejects_collisions() {
    let dir = tempfile::tempdir().unwrap();
    let left = dir.path().join("left");
    let right = dir.path().join("right");
    let output = dir.path().join("output");
    fs::create_dir_all(&left).unwrap();
    fs::create_dir_all(&right).unwrap();
    fs::create_dir_all(&output).unwrap();
    let first = left.join("same.jpg");
    let second = right.join("same.png");
    fs::write(&first, b"a").unwrap();
    fs::write(&second, b"b").unwrap();

    let mut collision = job(OutputFormat::Jpeg);
    collision.files = vec![first.clone(), second.clone()];
    collision.options.output_mode = OutputMode::SelectedDir(output.clone());
    collision.options.suffix = None;
    assert_eq!(
        validate_output_paths(&collision),
        Err(ValidationError::DuplicateOutput(output.join("same.jpg")))
    );

    let other_input = output.join("first.jpg");
    fs::write(&other_input, b"input").unwrap();
    let source = left.join("first.png");
    fs::write(&source, b"source").unwrap();
    collision.files = vec![source, other_input.clone()];
    assert_eq!(
        validate_output_paths(&collision),
        Err(ValidationError::OutputOverwritesInput(other_input))
    );
}

#[test]
fn predicted_paths_follow_suffix_extension_and_delete_rules() {
    let dir = tempfile::tempdir().unwrap();
    let input = dir.path().join("Photo.PNG");
    fs::write(&input, b"x").unwrap();
    let mut spec = job(OutputFormat::WebP);
    spec.files = vec![input.clone()];
    spec.options.output_mode = OutputMode::OriginalDir;
    spec.options.suffix = Some("small".into());
    assert_eq!(
        predicted_output_path(&input, &spec),
        dir.path().join("Photosmall.webp")
    );

    spec.options.format = OutputFormat::Png;
    spec.options.suffix = None;
    spec.options.original_policy = OriginalPolicy::DeleteAfterVerifiedSuccess;
    assert_eq!(
        validate_output_paths(&spec),
        Err(ValidationError::UnsafeDelete)
    );

    let upper = dir.path().join("CASE.JPG");
    let lower = dir.path().join("case.jpg");
    if cfg!(windows) {
        assert_eq!(path_key(&upper), path_key(&lower));
    }
}

#[test]
fn metadata_must_match_input() {
    let dir = tempfile::tempdir().unwrap();
    let input = dir.path().join("in.jpg");
    let output = dir.path().join("out.jpg");
    fs::write(&input, b"x").unwrap();
    let metadata = dir.path().join("m.json");
    fs::write(
        &metadata,
        format!(
            r#"{{"future":true,"images":[{{"input":{},"output":{}}}]}}"#,
            serde_json::to_string(&input).unwrap(),
            serde_json::to_string(&output).unwrap()
        ),
    )
    .unwrap();
    assert_eq!(output_for_input(&metadata, &input).unwrap(), output);
}

#[test]
fn backup_policy_conflicts_with_suffix() {
    let mut j = job(OutputFormat::Jpeg);
    j.options.original_policy = OriginalPolicy::Backup;
    j.options.suffix = Some("backup".into());
    assert_eq!(validate_job(&j), Err(ValidationError::BackupSuffixConflict));
    j.options.suffix = None;
    assert!(validate_job(&j).is_ok());
}

#[test]
fn metadata_map_resolves_each_input() {
    let dir = tempfile::tempdir().unwrap();
    let first = dir.path().join("a.jpg");
    let second = dir.path().join("b.jpg");
    let output_a = dir.path().join("a.webp");
    let output_b = dir.path().join("b.webp");
    for p in [&first, &second] {
        fs::write(p, b"x").unwrap();
    }
    let metadata = dir.path().join("batch.json");
    fs::write(
        &metadata,
        format!(
            r#"{{"images":[{{"input":{},"output":{}}},{{"input":{},"output":{}}}]}}"#,
            serde_json::to_string(&first).unwrap(),
            serde_json::to_string(&output_a).unwrap(),
            serde_json::to_string(&second).unwrap(),
            serde_json::to_string(&output_b).unwrap()
        ),
    )
    .unwrap();
    let map = rimage_gui::metadata::load_output_map(&metadata).unwrap();
    assert_eq!(map.len(), 2);
    let a_key = path_key(&first);
    assert_eq!(map.get(&a_key).unwrap(), &output_a);
}

#[test]
fn input_filter_is_case_insensitive_and_deduplicates() {
    let dir = tempfile::tempdir().unwrap();
    let file = dir.path().join("A.JpG");
    fs::write(&file, b"x").unwrap();
    assert!(is_supported(&file));
    let found = collect_paths(&[dir.path().to_path_buf(), file]);
    assert_eq!(found.len(), 1);
}

#[test]
fn embedded_backend_converts_each_image_serially() {
    let backend = extract_backend().expect("embedded backend should extract");
    verify_backend(&backend).expect("embedded backend should report the supported version");

    let dir = tempfile::tempdir().unwrap();
    let inputs: Vec<std::path::PathBuf> = (0..3)
        .map(|i| dir.path().join(format!("输入 图片 {i}.png")))
        .collect();
    for (index, input) in inputs.iter().enumerate() {
        image::RgbImage::from_pixel(
            3 + u32::try_from(index).expect("test index fits in u32"),
            2,
            image::Rgb([32, 128, 224]),
        )
        .save(input)
        .unwrap();
    }
    let output_dir = dir.path().join("输出 folder");

    let conversion = JobSpec {
        files: inputs.clone(),
        options: ProcessingOptions {
            format: OutputFormat::Png,
            quality: 85,
            quantization: None,
            dithering: None,
            suffix: Some("verified".into()),
            output_mode: OutputMode::SelectedDir(output_dir.clone()),
            original_policy: OriginalPolicy::Keep,
            resize: ResizeSpec::None,
            hidden: true,
        },
    };

    let worker = start_job(conversion);
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(60);
    let mut successful_outputs = Vec::new();
    let mut started = 0;
    loop {
        let remaining = deadline.saturating_duration_since(std::time::Instant::now());
        assert!(!remaining.is_zero(), "real backend conversion timed out");
        match worker.events.recv_timeout(remaining).unwrap() {
            WorkerEvent::FileStarted { .. } => started += 1,
            WorkerEvent::FileSucceeded { input, output } => {
                successful_outputs.push((input, output));
            }
            WorkerEvent::FileFailed { input, error } => {
                panic!(
                    "real backend conversion failed for {}: {error}",
                    input.display()
                )
            }
            WorkerEvent::Finished {
                succeeded,
                failed,
                skipped,
                cancelled,
            } => {
                assert_eq!((succeeded, failed, skipped, cancelled), (3, 0, 0, false));
                break;
            }
            WorkerEvent::Started { .. }
            | WorkerEvent::Log(_)
            | WorkerEvent::ValidationFailed(_) => {}
        }
    }

    assert_eq!(started, 3);
    assert_eq!(successful_outputs.len(), 3);
    for (index, input) in inputs.iter().enumerate() {
        let expected = output_dir
            .join(format!("输入 图片 {index}verified.png"))
            .canonicalize()
            .unwrap();
        let (actual_input, output) = successful_outputs
            .iter()
            .find(|(actual, _)| actual.canonicalize().unwrap() == input.canonicalize().unwrap())
            .expect("conversion must report each input");
        assert_eq!(output.canonicalize().unwrap(), expected);
        assert!(output.is_file());
        assert!(fs::metadata(output).unwrap().len() > 0);
        assert!(input.is_file());
        assert!(actual_input.is_file());
    }
}
