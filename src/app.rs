use std::{
    collections::{HashSet, VecDeque},
    path::PathBuf,
    sync::mpsc,
    thread,
};

use eframe::egui;

use crate::{
    backend::{WorkerEvent, WorkerHandle, start_job},
    i18n::{Language, Text, tr, validation_message},
    input::{collect_paths, display_path, display_text},
    model::{
        BoundKind, JobSpec, OriginalPolicy, OutputFormat, OutputMode, ProcessingOptions,
        ResizeFilter, ResizeSpec, SizeBounds,
    },
    validation::validate_job,
};

const MAX_LOG_LINES: usize = 2_000;

#[derive(Clone, Copy, PartialEq, Eq)]
enum BoundUi {
    Disabled,
    Longest,
    WidthHeight,
}
#[derive(Clone)]
struct BoundForm {
    mode: BoundUi,
    first: u32,
    second: u32,
}
impl Default for BoundForm {
    fn default() -> Self {
        Self {
            mode: BoundUi::Disabled,
            first: 1920,
            second: 1080,
        }
    }
}
impl BoundForm {
    fn value(&self) -> Option<BoundKind> {
        match self.mode {
            BoundUi::Disabled => None,
            BoundUi::Longest => Some(BoundKind::LongestEdge(self.first)),
            BoundUi::WidthHeight => Some(BoundKind::WidthHeight(self.first, self.second)),
        }
    }
}

/// Which resize configuration the GUI edits. `Classic` and `Bounds` are
/// mutually exclusive so a job can never receive both resize sources.
#[derive(Clone, Copy, PartialEq, Eq, Default)]
enum ResizeUi {
    #[default]
    Off,
    Classic,
    Bounds,
}

/// Clamps the initial window and its minimum size to the available monitor
/// area so small screens never overflow and high-DPI logical points stay
/// correct. Returns `(inner_size, min_inner_size)`.
#[must_use]
pub fn clamped_window_size(
    desired: egui::Vec2,
    monitor: egui::Vec2,
    margin: f32,
    minimum: egui::Vec2,
) -> (egui::Vec2, egui::Vec2) {
    let available = egui::vec2(
        (monitor.x - margin).max(320.0),
        (monitor.y - margin).max(320.0),
    );
    let size = egui::vec2(desired.x.min(available.x), desired.y.min(available.y));
    let min = egui::vec2(minimum.x.min(size.x), minimum.y.min(size.y));
    (size, min)
}

/// Backup-suffix linkage. Selecting Backup closes the suffix and remembers
/// its previous enabled state; leaving Backup restores it. Returns
/// `(suffix_enabled, saved_suffix_enabled)` for the caller to store.
#[must_use]
pub fn adjust_suffix_for_policy(
    current: OriginalPolicy,
    next: OriginalPolicy,
    suffix_enabled: bool,
    saved: bool,
) -> (bool, bool) {
    if next == OriginalPolicy::Backup && current != OriginalPolicy::Backup {
        (false, suffix_enabled)
    } else if current == OriginalPolicy::Backup && next != OriginalPolicy::Backup {
        (saved, true)
    } else {
        (suffix_enabled, saved)
    }
}

#[allow(clippy::struct_excessive_bools)]
pub struct RimageApp {
    language: Language,
    files: Vec<PathBuf>,
    selected: HashSet<usize>,
    format: OutputFormat,
    quality: u8,
    quant_enabled: bool,
    quantization: u8,
    dither_enabled: bool,
    dithering: u8,
    suffix_enabled: bool,
    saved_suffix_enabled: bool,
    suffix: String,
    output_choice: u8,
    output_dir: PathBuf,
    subfolder: String,
    policy: OriginalPolicy,
    resize_mode: ResizeUi,
    resize_arg: String,
    filter: ResizeFilter,
    min_bound: BoundForm,
    max_bound: BoundForm,
    hidden: bool,
    worker: Option<WorkerHandle>,
    scan_rx: Option<mpsc::Receiver<Vec<PathBuf>>>,
    logs: VecDeque<String>,
    status: Text,
    completed: usize,
    total: usize,
    window_sized: bool,
}

impl Default for RimageApp {
    fn default() -> Self {
        Self {
            language: Language::System,
            files: vec![],
            selected: HashSet::new(),
            format: OutputFormat::Jpeg,
            quality: 85,
            quant_enabled: false,
            quantization: 90,
            dither_enabled: false,
            dithering: 90,
            suffix_enabled: true,
            saved_suffix_enabled: true,
            suffix: "_new".into(),
            output_choice: 0,
            output_dir: PathBuf::new(),
            subfolder: "converted".into(),
            policy: OriginalPolicy::Keep,
            resize_mode: ResizeUi::Off,
            resize_arg: String::new(),
            filter: ResizeFilter::default(),
            min_bound: BoundForm::default(),
            max_bound: BoundForm::default(),
            hidden: true,
            worker: None,
            scan_rx: None,
            logs: VecDeque::new(),
            status: Text::Idle,
            completed: 0,
            total: 0,
            window_sized: false,
        }
    }
}

impl RimageApp {
    fn text(&self, key: Text) -> &'static str {
        tr(self.language, key)
    }
    fn push_log(&mut self, line: impl Into<String>) {
        if self.logs.len() == MAX_LOG_LINES {
            self.logs.pop_front();
        }
        self.logs.push_back(line.into());
    }
    fn begin_scan(&mut self, roots: Vec<PathBuf>) {
        if roots.is_empty() || self.scan_rx.is_some() || self.worker.is_some() {
            return;
        }
        let (tx, rx) = mpsc::channel();
        self.scan_rx = Some(rx);
        self.status = Text::Scanning;
        thread::spawn(move || {
            let _ = tx.send(collect_paths(&roots));
        });
    }
    fn merge_scanned(&mut self, paths: Vec<PathBuf>) {
        let mut keys: HashSet<String> = self
            .files
            .iter()
            .map(|p| p.to_string_lossy().to_ascii_lowercase())
            .collect();
        for path in paths {
            let key = path.to_string_lossy().to_ascii_lowercase();
            if keys.insert(key) {
                let index = self.files.len();
                self.files.push(path);
                self.selected.insert(index);
            }
        }
        // A conversion or an explicit failure may have happened while the scan
        // was still running; do not overwrite those states with Idle.
        if self.status == Text::Scanning {
            self.status = Text::Idle;
        }
    }
    fn selected_files(&self) -> Vec<PathBuf> {
        self.files
            .iter()
            .enumerate()
            .filter(|(index, _)| self.selected.contains(index))
            .map(|(_, path)| path.clone())
            .collect()
    }
    fn set_policy(&mut self, next: OriginalPolicy) {
        let (enabled, saved) = adjust_suffix_for_policy(
            self.policy,
            next,
            self.suffix_enabled,
            self.saved_suffix_enabled,
        );
        self.policy = next;
        self.suffix_enabled = enabled;
        self.saved_suffix_enabled = saved;
    }
    fn job(&self) -> JobSpec {
        JobSpec {
            files: self.selected_files(),
            options: ProcessingOptions {
                format: self.format,
                quality: self.quality,
                quantization: self.quant_enabled.then_some(self.quantization),
                dithering: (self.quant_enabled && self.dither_enabled).then_some(self.dithering),
                suffix: if self.policy == OriginalPolicy::Backup {
                    None
                } else {
                    (self.suffix_enabled && !self.suffix.is_empty()).then(|| self.suffix.clone())
                },
                output_mode: match self.output_choice {
                    1 => OutputMode::SelectedDir(self.output_dir.clone()),
                    2 => OutputMode::OriginalSubfolder(self.subfolder.clone()),
                    _ => OutputMode::OriginalDir,
                },
                original_policy: self.policy,
                resize: match self.resize_mode {
                    ResizeUi::Off => ResizeSpec::None,
                    ResizeUi::Classic => ResizeSpec::Classic {
                        arg: self.resize_arg.clone(),
                        filter: self.filter,
                    },
                    ResizeUi::Bounds => ResizeSpec::Bounds(SizeBounds {
                        min: self.min_bound.value(),
                        max: self.max_bound.value(),
                    }),
                },
                hidden: self.hidden,
            },
        }
    }
    fn start(&mut self) {
        let job = self.job();
        if job.files.is_empty() {
            self.status = Text::Idle;
            self.push_log(self.text(Text::ErrorNoFiles));
            return;
        }
        match validate_job(&job) {
            Ok(()) => {
                self.logs.clear();
                self.completed = 0;
                self.total = job.files.len();
                self.status = Text::Running;
                self.worker = Some(start_job(job));
            }
            Err(e) => {
                self.status = Text::Failed;
                self.push_log(validation_message(self.language, &e));
            }
        }
    }
    fn poll(&mut self, ctx: &egui::Context) {
        if let Some(rx) = &self.scan_rx
            && let Ok(paths) = rx.try_recv()
        {
            self.scan_rx = None;
            self.merge_scanned(paths);
        }
        let events: Vec<_> = self.worker.as_ref().map_or_else(Vec::new, |worker| {
            worker.events.try_iter().take(128).collect()
        });
        for event in events {
            match event {
                WorkerEvent::Started { total } => self.total = total,
                WorkerEvent::FileStarted { index, input } => self.push_log(format!(
                    "[{}/{}] {}",
                    index + 1,
                    self.total,
                    display_path(&input)
                )),
                WorkerEvent::Log(line) => self.push_log(display_text(&line)),
                WorkerEvent::ValidationFailed(error) => {
                    self.push_log(validation_message(self.language, &error));
                }
                WorkerEvent::FileSucceeded { input, output } => {
                    self.completed += 1;
                    self.push_log(format!(
                        "{}: {} -> {}",
                        tr(self.language, Text::SuccessPrefix),
                        display_path(&input),
                        display_path(&output)
                    ));
                }
                WorkerEvent::FileFailed { input, error } => {
                    self.completed += 1;
                    self.push_log(format!(
                        "{}: {}: {}",
                        tr(self.language, Text::ErrorPrefix),
                        display_path(&input),
                        display_text(&error)
                    ));
                }
                WorkerEvent::Finished {
                    succeeded,
                    failed,
                    skipped,
                    cancelled,
                } => {
                    // A finished job has reached a terminal state even when
                    // cancellation left some inputs intentionally unprocessed.
                    // Keeping the bar below 100% after the worker has exited is
                    // misleading; the summary preserves the processed/skipped
                    // distinction.
                    self.completed = self.total;
                    self.status = if cancelled {
                        Text::Cancelled
                    } else if failed > 0 {
                        Text::Failed
                    } else {
                        Text::Finished
                    };
                    self.push_log(format!(
                        "{}: {}={succeeded}, {}={failed}, {}={skipped}",
                        tr(self.language, Text::Summary),
                        tr(self.language, Text::SummarySucceeded),
                        tr(self.language, Text::SummaryFailed),
                        tr(self.language, Text::SummarySkipped),
                    ));
                    if cancelled {
                        self.push_log(tr(self.language, Text::Cancelled));
                    }
                    self.worker = None;
                }
            }
        }
        if self.worker.is_some() || self.scan_rx.is_some() {
            ctx.request_repaint_after(std::time::Duration::from_millis(50));
        }
    }
    fn remove_selected(&mut self) {
        let selected = &self.selected;
        self.files = self
            .files
            .iter()
            .enumerate()
            .filter(|(index, _)| !selected.contains(index))
            .map(|(_, path)| path.clone())
            .collect();
        self.selected.clear();
    }

    fn bound_ui(ui: &mut egui::Ui, language: Language, title: Text, bound: &mut BoundForm) {
        ui.horizontal_wrapped(|ui| {
            ui.label(tr(language, title))
                .on_hover_text(tr(language, Text::SizeTip));
            egui::ComboBox::from_id_salt(title as u8)
                .selected_text(match bound.mode {
                    BoundUi::Disabled => tr(language, Text::Disabled),
                    BoundUi::Longest => tr(language, Text::LongestEdge),
                    BoundUi::WidthHeight => tr(language, Text::WidthHeight),
                })
                .show_ui(ui, |ui| {
                    ui.selectable_value(
                        &mut bound.mode,
                        BoundUi::Disabled,
                        tr(language, Text::Disabled),
                    );
                    ui.selectable_value(
                        &mut bound.mode,
                        BoundUi::Longest,
                        tr(language, Text::LongestEdge),
                    );
                    ui.selectable_value(
                        &mut bound.mode,
                        BoundUi::WidthHeight,
                        tr(language, Text::WidthHeight),
                    );
                });
            if bound.mode != BoundUi::Disabled {
                ui.add(egui::DragValue::new(&mut bound.first).range(1..=65_535));
            }
            if bound.mode == BoundUi::WidthHeight {
                ui.label("×");
                ui.add(egui::DragValue::new(&mut bound.second).range(1..=65_535));
            }
        });
    }
}

impl Drop for RimageApp {
    fn drop(&mut self) {
        if let Some(worker) = &self.worker {
            worker.cancel();
        }
    }
}

impl eframe::App for RimageApp {
    #[allow(clippy::too_many_lines, clippy::cast_precision_loss)]
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        if !self.window_sized {
            self.window_sized = true;
            if let Some(monitor) = ctx.input(|i| i.viewport().monitor_size) {
                let (size, min) = clamped_window_size(
                    egui::vec2(1_020.0, 660.0),
                    monitor,
                    40.0,
                    egui::vec2(620.0, 420.0),
                );
                ctx.send_viewport_cmd(egui::ViewportCommand::MinInnerSize(min));
                ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(size));
            }
        }
        self.poll(ctx);
        let conversion_busy = self.worker.is_some();
        let scan_busy = self.scan_rx.is_some();
        let busy = conversion_busy || scan_busy;
        let dropped: Vec<PathBuf> = ctx.input(|i| {
            i.raw
                .dropped_files
                .iter()
                .filter_map(|f| f.path.clone())
                .collect()
        });
        if !dropped.is_empty() && !busy {
            self.begin_scan(dropped);
        }
        egui::TopBottomPanel::top("top").show(ctx, |ui| {
            ui.horizontal_wrapped(|ui| {
                ui.heading(self.text(Text::AppTitle));
                ui.separator();
                ui.label(self.text(Text::Language));
                egui::ComboBox::from_id_salt("language")
                    .selected_text(match self.language {
                        Language::System => "System",
                        Language::Chinese => "中文",
                        Language::English => "English",
                    })
                    .show_ui(ui, |ui| {
                        ui.selectable_value(&mut self.language, Language::System, "System");
                        ui.selectable_value(&mut self.language, Language::Chinese, "中文");
                        ui.selectable_value(&mut self.language, Language::English, "English");
                    });
            });
        });
        let options_width = (ctx.available_rect().width() * 0.40).clamp(280.0, 410.0);
        egui::SidePanel::right("options")
            .resizable(false)
            .exact_width(options_width)
            .show(ctx, |ui| {
                // Parameter controls are intentionally roomier than the main
                // file list: this is the app's primary interaction surface.
                ui.spacing_mut().item_spacing = egui::vec2(12.0, 10.0);
                ui.spacing_mut().interact_size.y = 38.0;
                // Keep a visual gutter between the controls and the right
                // window edge. The scroll bar remains inside this gutter.
                ui.set_max_width((ui.available_width() - 22.0).max(0.0));
                ui.add_space(4.0);
                ui.heading(self.text(Text::Options));
                ui.add_space(4.0);
                // Reserve the complete action row, separator, spacing and the
                // panel's bottom margin. This prevents the thick button frame
                // from extending below the viewport on short windows.
                let options_height = (ui.available_height() - 76.0).max(0.0);
                egui::ScrollArea::vertical()
                    .id_salt("options-scroll")
                    .auto_shrink([false, false])
                    .max_height(options_height)
                    .show(ui, |ui| {
                        ui.add_enabled_ui(!busy, |ui| {
                            egui::CollapsingHeader::new(self.text(Text::EncodingGroup))
                                .default_open(true)
                                .show(ui, |ui| {
                                    let backup_active = self.policy == OriginalPolicy::Backup;
                                    egui::Grid::new("encoding-grid")
                                        .num_columns(2)
                                        .spacing([20.0, 11.0])
                                        .show(ui, |ui| {
                                            ui.label(self.text(Text::Format))
                                                .on_hover_text(self.text(Text::FormatTip));
                                            egui::ComboBox::from_id_salt("format")
                                                .selected_text(self.format.cli_name())
                                                .width(190.0)
                                                .show_ui(ui, |ui| {
                                                    for f in OutputFormat::ALL {
                                                        ui.selectable_value(
                                                            &mut self.format,
                                                            f,
                                                            f.cli_name(),
                                                        );
                                                    }
                                                })
                                                .response
                                                .on_hover_text(self.text(Text::FormatTip));
                                            ui.end_row();
                                            ui.label(self.text(Text::Quality))
                                                .on_hover_text(self.text(Text::QualityTip));
                                            ui.add_enabled(
                                                self.format.supports_quality(),
                                                egui::DragValue::new(&mut self.quality)
                                                    .range(1..=100),
                                            )
                                            .on_hover_text(self.text(Text::QualityTip));
                                            ui.end_row();
                                            ui.checkbox(
                                                &mut self.quant_enabled,
                                                tr(self.language, Text::Quantization),
                                            )
                                            .on_hover_text(self.text(Text::QuantizationTip));
                                            ui.add_enabled(
                                                self.quant_enabled,
                                                egui::DragValue::new(&mut self.quantization)
                                                    .range(1..=100),
                                            );
                                            ui.end_row();
                                            ui.checkbox(
                                                &mut self.dither_enabled,
                                                tr(self.language, Text::Dithering),
                                            )
                                            .on_hover_text(self.text(Text::DitheringTip));
                                            ui.add_enabled(
                                                self.quant_enabled && self.dither_enabled,
                                                egui::DragValue::new(&mut self.dithering)
                                                    .range(1..=100),
                                            );
                                            ui.end_row();
                                            let mut suffix_want = self.suffix_enabled;
                                            let suffix_check = ui
                                                .add(egui::Checkbox::new(
                                                    &mut suffix_want,
                                                    tr(self.language, Text::Suffix),
                                                ))
                                                .on_hover_text(self.text(Text::SuffixTip));
                                            if suffix_check.changed() {
                                                if suffix_want && backup_active {
                                                    self.set_policy(OriginalPolicy::Keep);
                                                    self.suffix_enabled = true;
                                                } else {
                                                    self.suffix_enabled = suffix_want;
                                                }
                                            }
                                            ui.add_enabled(
                                                self.suffix_enabled && !backup_active,
                                                egui::TextEdit::singleline(&mut self.suffix)
                                                    .desired_width(140.0),
                                            )
                                            .on_hover_text(self.text(Text::SuffixTip));
                                            ui.end_row();
                                        });
                                    if backup_active {
                                        // Keep this variable-width hint outside
                                        // the two-column grid. Otherwise its
                                        // long text widens the first column and
                                        // shifts every control in the group.
                                        ui.add_space(2.0);
                                        ui.label(self.text(Text::SuffixBackupHint));
                                    }
                                });
                            egui::CollapsingHeader::new(self.text(Text::OutputLocationGroup))
                                .default_open(true)
                                .show(ui, |ui| {
                                    ui.label(self.text(Text::OutputMode))
                                        .on_hover_text(self.text(Text::OutputModeTip));
                                    ui.horizontal_wrapped(|ui| {
                                        ui.selectable_value(
                                            &mut self.output_choice,
                                            0,
                                            tr(self.language, Text::OriginalDir),
                                        )
                                        .on_hover_text(self.text(Text::OriginalDirTip));
                                        ui.selectable_value(
                                            &mut self.output_choice,
                                            1,
                                            tr(self.language, Text::SelectedDir),
                                        )
                                        .on_hover_text(self.text(Text::SelectedDirTip));
                                        ui.selectable_value(
                                            &mut self.output_choice,
                                            2,
                                            tr(self.language, Text::Subfolder),
                                        )
                                        .on_hover_text(self.text(Text::SubfolderTip));
                                    });
                                    if self.output_choice == 1 {
                                        ui.horizontal(|ui| {
                                            ui.add(
                                                egui::TextEdit::singleline(
                                                    &mut self
                                                        .output_dir
                                                        .to_string_lossy()
                                                        .into_owned(),
                                                )
                                                .desired_width(260.0)
                                                .interactive(false),
                                            );
                                            let browse_clicked = ui
                                                .scope(|ui| {
                                                    ui.spacing_mut().interact_size.y = 24.0;
                                                    ui.add_sized(
                                                        egui::vec2(72.0, 24.0),
                                                        egui::Button::new(self.text(Text::Browse)),
                                                    )
                                                })
                                                .inner
                                                .clicked();
                                            if browse_clicked
                                                && let Some(p) =
                                                    rfd::FileDialog::new().pick_folder()
                                            {
                                                self.output_dir = p;
                                            }
                                        });
                                    } else if self.output_choice == 2 {
                                        ui.horizontal(|ui| {
                                            ui.label(self.text(Text::Subfolder));
                                            ui.add(
                                                egui::TextEdit::singleline(&mut self.subfolder)
                                                    .desired_width(200.0),
                                            )
                                            .on_hover_text(self.text(Text::SubfolderTip));
                                        });
                                    }
                                });
                            egui::CollapsingHeader::new(self.text(Text::OriginalFilesGroup))
                                .default_open(true)
                                .show(ui, |ui| {
                                    ui.label(self.text(Text::OriginalPolicy));
                                    ui.horizontal_wrapped(|ui| {
                                        let mut policy = self.policy;
                                        if ui
                                            .selectable_value(
                                                &mut policy,
                                                OriginalPolicy::Keep,
                                                tr(self.language, Text::Keep),
                                            )
                                            .on_hover_text(self.text(Text::KeepTip))
                                            .changed()
                                        {
                                            self.set_policy(policy);
                                        }
                                        if ui
                                            .selectable_value(
                                                &mut policy,
                                                OriginalPolicy::Backup,
                                                tr(self.language, Text::Backup),
                                            )
                                            .on_hover_text(self.text(Text::BackupTip))
                                            .changed()
                                        {
                                            self.set_policy(policy);
                                        }
                                        if ui
                                            .selectable_value(
                                                &mut policy,
                                                OriginalPolicy::DeleteAfterVerifiedSuccess,
                                                tr(self.language, Text::Delete),
                                            )
                                            .on_hover_text(self.text(Text::DeleteTip))
                                            .changed()
                                        {
                                            self.set_policy(policy);
                                        }
                                    });
                                });
                            egui::CollapsingHeader::new(self.text(Text::SizeLimitsGroup))
                                .default_open(true)
                                .show(ui, |ui| {
                                    ui.label(self.text(Text::ResizeMode))
                                        .on_hover_text(self.text(Text::ResizeModeTip));
                                    ui.horizontal_wrapped(|ui| {
                                        ui.selectable_value(
                                            &mut self.resize_mode,
                                            ResizeUi::Off,
                                            tr(self.language, Text::ResizeNone),
                                        );
                                        ui.selectable_value(
                                            &mut self.resize_mode,
                                            ResizeUi::Classic,
                                            tr(self.language, Text::ResizeClassic),
                                        );
                                        ui.selectable_value(
                                            &mut self.resize_mode,
                                            ResizeUi::Bounds,
                                            tr(self.language, Text::ResizeBounds),
                                        );
                                    });
                                    match self.resize_mode {
                                        ResizeUi::Off => {}
                                        ResizeUi::Classic => {
                                            ui.horizontal(|ui| {
                                                ui.label(self.text(Text::ResizeArgs))
                                                    .on_hover_text(self.text(Text::ResizeArgsTip));
                                                ui.add(
                                                    egui::TextEdit::singleline(
                                                        &mut self.resize_arg,
                                                    )
                                                    .desired_width(220.0)
                                                    .hint_text("1920x1080 / 720w / @1.5 / 150%"),
                                                )
                                                .on_hover_text(self.text(Text::ResizeArgsTip));
                                            });
                                            ui.horizontal(|ui| {
                                                ui.label(self.text(Text::Filter))
                                                    .on_hover_text(self.text(Text::FilterTip));
                                                egui::ComboBox::from_id_salt("resize-filter")
                                                    .selected_text(self.filter.cli_name())
                                                    .width(200.0)
                                                    .show_ui(ui, |ui| {
                                                        for filter in ResizeFilter::ALL {
                                                            ui.selectable_value(
                                                                &mut self.filter,
                                                                filter,
                                                                filter.cli_name(),
                                                            );
                                                        }
                                                    })
                                                    .response
                                                    .on_hover_text(self.text(Text::FilterTip));
                                            });
                                        }
                                        ResizeUi::Bounds => {
                                            Self::bound_ui(
                                                ui,
                                                self.language,
                                                Text::MinSize,
                                                &mut self.min_bound,
                                            );
                                            Self::bound_ui(
                                                ui,
                                                self.language,
                                                Text::MaxSize,
                                                &mut self.max_bound,
                                            );
                                        }
                                    }
                                });
                            egui::CollapsingHeader::new(self.text(Text::ExecutionGroup))
                                .default_open(true)
                                .show(ui, |ui| {
                                    ui.checkbox(
                                        &mut self.hidden,
                                        tr(self.language, Text::HiddenExecute),
                                    )
                                    .on_hover_text(self.text(Text::HiddenExecuteTip));
                                });
                        });
                    });
                ui.separator();
                let start_size = egui::vec2(ui.available_width(), 48.0);
                let other_action_size = egui::vec2(ui.available_width(), 38.0);
                let action_stroke = egui::Stroke::new(2.0_f32, ui.visuals().strong_text_color());
                if conversion_busy {
                    if ui
                        .add_sized(
                            other_action_size,
                            egui::Button::new(
                                egui::RichText::new(self.text(Text::Cancel))
                                    .size(18.0)
                                    .strong(),
                            )
                            .stroke(action_stroke),
                        )
                        .on_hover_text(self.text(Text::CancelTip))
                        .clicked()
                        && let Some(worker) = &self.worker
                    {
                        worker.cancel();
                    }
                } else if ui
                    .add_enabled_ui(!self.selected.is_empty(), |ui| {
                        ui.add_sized(
                            start_size,
                            egui::Button::new(
                                egui::RichText::new(self.text(Text::Start))
                                    .size(18.0)
                                    .strong(),
                            )
                            .stroke(action_stroke),
                        )
                    })
                    .inner
                    .on_hover_text(self.text(Text::StartTip))
                    .clicked()
                {
                    self.start();
                }
            });
        egui::CentralPanel::default().show(ctx, |ui| {
            ui.spacing_mut().item_spacing.x = 14.0;
            ui.spacing_mut().interact_size.y = 24.0;
            let file_action_size = egui::vec2(104.0, 28.0);
            ui.horizontal_wrapped(|ui| {
                if ui
                    .add_enabled_ui(!busy, |ui| {
                        ui.add_sized(
                            file_action_size,
                            egui::Button::new(self.text(Text::AddFiles)),
                        )
                    })
                    .inner
                    .on_hover_text(self.text(Text::AddFilesTip))
                    .clicked()
                {
                    let files = rfd::FileDialog::new().pick_files().unwrap_or_default();
                    self.begin_scan(files);
                }
                if ui
                    .add_enabled_ui(!busy, |ui| {
                        ui.add_sized(
                            file_action_size,
                            egui::Button::new(self.text(Text::AddFolder)),
                        )
                    })
                    .inner
                    .on_hover_text(self.text(Text::AddFolderTip))
                    .clicked()
                    && let Some(dir) = rfd::FileDialog::new().pick_folder()
                {
                    self.begin_scan(vec![dir]);
                }
                if ui
                    .add_enabled_ui(!busy && !self.files.is_empty(), |ui| {
                        ui.add_sized(
                            file_action_size,
                            egui::Button::new(self.text(Text::SelectAll)),
                        )
                    })
                    .inner
                    .on_hover_text(self.text(Text::SelectAllTip))
                    .clicked()
                {
                    self.selected = (0..self.files.len()).collect();
                }
                if ui
                    .add_enabled_ui(!busy && !self.selected.is_empty(), |ui| {
                        ui.add_sized(
                            file_action_size,
                            egui::Button::new(self.text(Text::DeselectAll)),
                        )
                    })
                    .inner
                    .on_hover_text(self.text(Text::DeselectAllTip))
                    .clicked()
                {
                    self.selected.clear();
                }
                if ui
                    .add_enabled_ui(!busy && !self.selected.is_empty(), |ui| {
                        ui.add_sized(file_action_size, egui::Button::new(self.text(Text::Remove)))
                    })
                    .inner
                    .on_hover_text(self.text(Text::RemoveTip))
                    .clicked()
                {
                    self.remove_selected();
                }
                if ui
                    .add_enabled_ui(!busy && !self.files.is_empty(), |ui| {
                        ui.add_sized(file_action_size, egui::Button::new(self.text(Text::Clear)))
                    })
                    .inner
                    .on_hover_text(self.text(Text::ClearTip))
                    .clicked()
                {
                    self.files.clear();
                    self.selected.clear();
                }
                ui.separator();
                ui.label(format!(
                    "{}: {}/{}",
                    self.text(Text::SelectedCount),
                    self.selected.len(),
                    self.files.len()
                ));
            });
            ui.label(self.text(Text::DropHint));
            let row_height = ui.text_style_height(&egui::TextStyle::Body) + 6.0;
            // Divide the actual remaining height between the two regions.
            // Account explicitly for the progress row, labels, separators,
            // spacing, and both group-frame borders/padding. Without this,
            // those decorations are added after the split and can push the log
            // below the viewport when the window is made short.
            let available = ui.available_height().max(0.0);
            let fixed_rows = 112.0;
            let frame_chrome = 16.0;
            let region_height = (available - fixed_rows).max(0.0);
            let log_outer_height = (region_height * 0.35).min(250.0);
            let list_outer_height = (region_height - log_outer_height).max(0.0);
            let log_height = (log_outer_height - frame_chrome).max(0.0);
            let list_height = (list_outer_height - frame_chrome).max(0.0);
            egui::Frame::group(ui.style()).show(ui, |ui| {
                ui.set_height(list_height);
                egui::ScrollArea::both()
                    .id_salt("files")
                    .auto_shrink([false, false])
                    .max_height(list_height)
                    .show_rows(ui, row_height, self.files.len(), |ui, range| {
                        for i in range {
                            ui.horizontal(|ui| {
                                let mut selected = self.selected.contains(&i);
                                if ui
                                    .add_enabled(!busy, egui::Checkbox::without_text(&mut selected))
                                    .changed()
                                {
                                    if selected {
                                        self.selected.insert(i);
                                    } else {
                                        self.selected.remove(&i);
                                    }
                                }
                                ui.add(
                                    egui::Label::new(display_path(&self.files[i]))
                                        .truncate()
                                        .selectable(false),
                                )
                                .on_hover_text(display_path(&self.files[i]));
                            });
                        }
                    });
            });
            ui.separator();
            ui.horizontal(|ui| {
                ui.label(format!(
                    "{}: {}",
                    self.text(Text::Progress),
                    self.text(self.status)
                ));
                let fraction = if self.total == 0 {
                    0.0
                } else {
                    self.completed as f32 / self.total as f32
                };
                let progress_width = ui.available_width().max(80.0);
                ui.add_sized(
                    egui::vec2(progress_width, 24.0),
                    egui::ProgressBar::new(fraction).show_percentage(),
                );
            });
            ui.label(self.text(Text::Log));
            egui::Frame::group(ui.style()).show(ui, |ui| {
                ui.set_height(log_height);
                egui::ScrollArea::both()
                    .stick_to_bottom(true)
                    .auto_shrink([false, false])
                    .max_height(log_height)
                    .show(ui, |ui| {
                        for line in &self.logs {
                            ui.label(line);
                        }
                    });
            });
        });
    }
}

#[cfg(test)]
mod tests {
    use eframe::egui;

    use super::{adjust_suffix_for_policy, clamped_window_size};
    use crate::model::OriginalPolicy;

    #[test]
    fn window_size_never_exceeds_monitor_and_stays_positive() {
        let (size, min) = clamped_window_size(
            egui::vec2(1_020.0, 660.0),
            egui::vec2(800.0, 600.0),
            40.0,
            egui::vec2(620.0, 420.0),
        );
        assert!(size.x <= 760.0 && size.y <= 560.0);
        assert!(min.x <= size.x && min.y <= size.y);
        assert!(min.x > 0.0 && min.y > 0.0);

        let (size, min) = clamped_window_size(
            egui::vec2(1_020.0, 660.0),
            egui::vec2(3_840.0, 2_160.0),
            40.0,
            egui::vec2(620.0, 420.0),
        );
        assert_eq!(size, egui::vec2(1_020.0, 660.0));
        assert_eq!(min, egui::vec2(620.0, 420.0));
    }

    #[test]
    fn backup_policy_disables_and_restores_suffix_state() {
        use OriginalPolicy::{Backup, DeleteAfterVerifiedSuccess, Keep};

        let (enabled, saved) = adjust_suffix_for_policy(Keep, Backup, true, true);
        assert_eq!((enabled, saved), (false, true));
        let (enabled, saved) = adjust_suffix_for_policy(Backup, Keep, false, true);
        assert_eq!((enabled, saved), (true, true));
        let (enabled, saved) = adjust_suffix_for_policy(Keep, Keep, false, false);
        assert_eq!((enabled, saved), (false, false));
        let (enabled, saved) =
            adjust_suffix_for_policy(Keep, DeleteAfterVerifiedSuccess, true, true);
        assert_eq!((enabled, saved), (true, true));
    }
}
