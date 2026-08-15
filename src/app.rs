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

/// Default inner size and minimum size for the main window. Both are
/// re-clamped to the monitor on the first frame.
pub const WINDOW_INNER_SIZE: egui::Vec2 = egui::Vec2::new(860.0, 660.0);
pub const WINDOW_MIN_INNER_SIZE: egui::Vec2 = egui::Vec2::new(860.0, 500.0);
/// Margin kept between the clamped window and the monitor edges.
const WINDOW_MARGIN: f32 = 40.0;

/// Width of the right-hand options panel.
const OPTIONS_WIDTH: f32 = 300.0;
/// Height of the primary start button.
const ACTION_BUTTON_HEIGHT: f32 = 42.0;
/// Height of the secondary (cancel) button.
const OTHER_ACTION_BUTTON_HEIGHT: f32 = 32.0;

/// The log region receives this share of the remaining central-panel height.
const LOG_REGION_SHARE: f32 = 0.35;
const LOG_REGION_MIN_HEIGHT: f32 = 80.0;
const LOG_REGION_MAX_HEIGHT: f32 = 250.0;
/// The file list never shrinks below this height while the window is usable.
const LIST_REGION_MIN_HEIGHT: f32 = 40.0;

/// Size of the file-list action buttons in the central panel. The height
/// matches the buttons' natural height (12pt text + vertical padding), so the
/// fixed-size cells never overflow and horizontal rows stay symmetric.
const FILE_ACTION_SIZE: egui::Vec2 = egui::Vec2::new(96.0, 30.0);
const PROGRESS_BAR_HEIGHT: f32 = 16.0;

#[derive(Clone, Copy, PartialEq, Eq)]
enum BoundUi {
    Disabled,
    Longest,
    Shortest,
}
#[derive(Clone)]
struct BoundForm {
    mode: BoundUi,
    value: u32,
}
impl Default for BoundForm {
    fn default() -> Self {
        Self {
            mode: BoundUi::Disabled,
            value: 1920,
        }
    }
}
impl BoundForm {
    fn value(&self) -> Option<BoundKind> {
        match self.mode {
            BoundUi::Disabled => None,
            BoundUi::Longest => Some(BoundKind::LongestEdge(self.value)),
            BoundUi::Shortest => Some(BoundKind::ShortestEdge(self.value)),
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

/// Height reserved for the log region below the file list. The proportional
/// share is bounded so both regions stay useful at any window size, and the
/// list always keeps a minimum slice when the window is short.
#[must_use]
fn log_region_height(available: f32) -> f32 {
    (available * LOG_REGION_SHARE)
        .clamp(LOG_REGION_MIN_HEIGHT, LOG_REGION_MAX_HEIGHT)
        .min((available - LIST_REGION_MIN_HEIGHT).max(0.0))
}

#[allow(clippy::struct_excessive_bools)]
pub struct RimageApp {
    /// Resolved system language at startup; the UI offers no language switch.
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
    policy: OriginalPolicy,
    resize_mode: ResizeUi,
    resize_arg: String,
    filter: ResizeFilter,
    min_bound: BoundForm,
    max_bound: BoundForm,
    threads_auto: bool,
    threads: u8,
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
            language: Language::System.effective(),
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
            policy: OriginalPolicy::Keep,
            resize_mode: ResizeUi::Off,
            resize_arg: String::new(),
            filter: ResizeFilter::default(),
            min_bound: BoundForm::default(),
            max_bound: BoundForm::default(),
            threads_auto: true,
            threads: 1,
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
    /// Re-enabling the suffix while Backup is active switches the policy back
    /// to Keep instead of allowing both to fight over the same files.
    fn set_suffix_enabled(&mut self, want: bool) {
        if want && self.policy == OriginalPolicy::Backup {
            self.set_policy(OriginalPolicy::Keep);
            self.suffix_enabled = true;
        } else {
            self.suffix_enabled = want;
        }
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
                threads: if self.threads_auto {
                    None
                } else {
                    Some(self.threads)
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
        if let Some(rx) = &self.scan_rx {
            match rx.try_recv() {
                Ok(paths) => {
                    self.scan_rx = None;
                    self.merge_scanned(paths);
                }
                Err(std::sync::mpsc::TryRecvError::Disconnected) => {
                    // The scan thread dropped its sender without delivering a
                    // result (for example it panicked). Do not stay stuck in
                    // the Scanning state.
                    self.scan_rx = None;
                    if self.status == Text::Scanning {
                        self.status = Text::Idle;
                    }
                }
                Err(std::sync::mpsc::TryRecvError::Empty) => {}
            }
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
                    BoundUi::Shortest => tr(language, Text::ShortestEdge),
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
                        BoundUi::Shortest,
                        tr(language, Text::ShortestEdge),
                    );
                });
            if bound.mode != BoundUi::Disabled {
                ui.add(egui::DragValue::new(&mut bound.value).range(1..=65_535));
            }
        });
    }

    // ---- UI sections ----

    fn options_panel_ui(&mut self, ui: &mut egui::Ui, busy: bool) {
        // Parameter controls are intentionally roomier than the main file
        // list: this is the app's primary interaction surface.
        ui.spacing_mut().item_spacing = egui::vec2(10.0, 7.0);
        ui.spacing_mut().interact_size.y = 34.0;
        // Keep a visual gutter between the controls and the right window
        // edge. The scroll bar remains inside this gutter.
        ui.set_max_width((ui.available_width() - 22.0).max(0.0));
        ui.add_space(4.0);
        ui.heading(self.text(Text::Options));
        ui.add_space(4.0);
        // Pin the action row to the bottom of the panel with its own panel.
        // Without this isolation, an over-wide scroll content (for example
        // longer English labels) widens the scroll area itself and would push
        // the buttons past the window's right edge.
        egui::TopBottomPanel::bottom("options-actions")
            .frame(egui::Frame::NONE)
            .show_inside(ui, |ui| {
                ui.separator();
                self.action_buttons_ui(ui, self.worker.is_some());
            });
        let options_height = ui.available_height().max(0.0);
        egui::ScrollArea::vertical()
            .id_salt("options-scroll")
            .auto_shrink([false, false])
            .max_height(options_height)
            .show(ui, |ui| {
                ui.add_enabled_ui(!busy, |ui| {
                    egui::CollapsingHeader::new(self.text(Text::EncodingGroup))
                        .default_open(true)
                        .show(ui, |ui| self.encoding_group_ui(ui));
                    egui::CollapsingHeader::new(self.text(Text::OutputLocationGroup))
                        .default_open(true)
                        .show(ui, |ui| self.output_location_group_ui(ui));
                    egui::CollapsingHeader::new(self.text(Text::OriginalFilesGroup))
                        .default_open(true)
                        .show(ui, |ui| self.original_policy_group_ui(ui));
                    egui::CollapsingHeader::new(self.text(Text::SizeLimitsGroup))
                        .default_open(true)
                        .show(ui, |ui| self.size_limits_group_ui(ui));
                    egui::CollapsingHeader::new(self.text(Text::ExecutionGroup))
                        .default_open(true)
                        .show(ui, |ui| self.execution_group_ui(ui));
                });
            });
    }

    fn encoding_group_ui(&mut self, ui: &mut egui::Ui) {
        let backup_active = self.policy == OriginalPolicy::Backup;
        egui::Grid::new("encoding-grid")
            .num_columns(2)
            .spacing([14.0, 8.0])
            .show(ui, |ui| {
                ui.label(self.text(Text::Format))
                    .on_hover_text(self.text(Text::FormatTip));
                egui::ComboBox::from_id_salt("format")
                    .selected_text(self.format.cli_name())
                    .width(170.0)
                    .show_ui(ui, |ui| {
                        for f in OutputFormat::ALL {
                            ui.selectable_value(&mut self.format, f, f.cli_name());
                        }
                    })
                    .response
                    .on_hover_text(self.text(Text::FormatTip));
                ui.end_row();
                ui.label(self.text(Text::Quality))
                    .on_hover_text(self.text(Text::QualityTip));
                ui.add_enabled(
                    self.format.supports_quality(),
                    egui::DragValue::new(&mut self.quality).range(1..=100),
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
                    egui::DragValue::new(&mut self.quantization).range(1..=100),
                );
                ui.end_row();
                ui.checkbox(&mut self.dither_enabled, tr(self.language, Text::Dithering))
                    .on_hover_text(self.text(Text::DitheringTip));
                ui.add_enabled(
                    self.quant_enabled && self.dither_enabled,
                    egui::DragValue::new(&mut self.dithering).range(1..=100),
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
                    self.set_suffix_enabled(suffix_want);
                }
                ui.add_enabled(
                    self.suffix_enabled && !backup_active,
                    egui::TextEdit::singleline(&mut self.suffix).desired_width(140.0),
                )
                .on_hover_text(self.text(Text::SuffixTip));
                ui.end_row();
            });
        if backup_active {
            // Keep this variable-width hint outside the two-column grid.
            // Otherwise its long text widens the first column and shifts every
            // control in the group.
            ui.add_space(2.0);
            ui.label(self.text(Text::SuffixBackupHint));
        }
    }

    fn output_location_group_ui(&mut self, ui: &mut egui::Ui) {
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
        });
        if self.output_choice == 1 {
            ui.horizontal(|ui| {
                ui.add(
                    egui::TextEdit::singleline(&mut self.output_dir.to_string_lossy().into_owned())
                        .desired_width(210.0)
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
                if browse_clicked && let Some(p) = rfd::FileDialog::new().pick_folder() {
                    self.output_dir = p;
                }
            });
        }
    }

    fn original_policy_group_ui(&mut self, ui: &mut egui::Ui) {
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
    }

    fn size_limits_group_ui(&mut self, ui: &mut egui::Ui) {
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
                        egui::TextEdit::singleline(&mut self.resize_arg)
                            .desired_width(180.0)
                            .hint_text("1920x1080 / 720w / 1000l / 500s / @1.5 / 150%"),
                    )
                    .on_hover_text(self.text(Text::ResizeArgsTip));
                });
                ui.horizontal(|ui| {
                    ui.label(self.text(Text::Filter))
                        .on_hover_text(self.text(Text::FilterTip));
                    egui::ComboBox::from_id_salt("resize-filter")
                        .selected_text(self.filter.cli_name())
                        .width(170.0)
                        .show_ui(ui, |ui| {
                            for filter in ResizeFilter::ALL {
                                ui.selectable_value(&mut self.filter, filter, filter.cli_name());
                            }
                        })
                        .response
                        .on_hover_text(self.text(Text::FilterTip));
                });
            }
            ResizeUi::Bounds => {
                Self::bound_ui(ui, self.language, Text::MinSize, &mut self.min_bound);
                Self::bound_ui(ui, self.language, Text::MaxSize, &mut self.max_bound);
            }
        }
    }

    fn execution_group_ui(&mut self, ui: &mut egui::Ui) {
        ui.checkbox(&mut self.hidden, tr(self.language, Text::HiddenExecute))
            .on_hover_text(self.text(Text::HiddenExecuteTip));
        ui.horizontal(|ui| {
            ui.checkbox(&mut self.threads_auto, tr(self.language, Text::AutoThreads))
                .on_hover_text(self.text(Text::ThreadsTip));
            if !self.threads_auto {
                let max = std::thread::available_parallelism().map_or(1, |parallelism| {
                    u8::try_from(parallelism.get().min(u8::MAX as usize)).unwrap_or(u8::MAX)
                });
                ui.add(egui::DragValue::new(&mut self.threads).range(1..=max))
                    .on_hover_text(self.text(Text::ThreadsTip));
            }
        });
    }

    fn action_buttons_ui(&mut self, ui: &mut egui::Ui, conversion_busy: bool) {
        let start_size = egui::vec2(ui.available_width(), ACTION_BUTTON_HEIGHT);
        let other_action_size = egui::vec2(ui.available_width(), OTHER_ACTION_BUTTON_HEIGHT);
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
    }

    fn central_panel_ui(&mut self, ui: &mut egui::Ui, busy: bool) {
        ui.spacing_mut().item_spacing.x = 8.0;
        ui.spacing_mut().item_spacing.y = 6.0;
        ui.spacing_mut().interact_size.y = 16.0;
        self.file_actions_ui(ui, busy);
        self.selection_actions_ui(ui, busy);
        ui.label(self.text(Text::DropHint));
        // The file list and log fill the left column like the progress row.
        // Their frames derive the width from the panel itself, so they can
        // never overflow under the options panel.
        let available = ui.available_height().max(0.0);
        let list_height = (available - log_region_height(available)).max(0.0);
        self.file_list_ui(ui, busy, list_height);
        ui.separator();
        self.progress_row_ui(ui);
        ui.label(self.text(Text::Log));
        let log_height = ui.available_height().max(0.0);
        self.log_ui(ui, log_height);
    }

    fn file_action_button(
        ui: &mut egui::Ui,
        enabled: bool,
        text: &str,
        size: egui::Vec2,
    ) -> egui::Response {
        ui.add_enabled_ui(enabled, |ui| ui.add_sized(size, egui::Button::new(text)))
            .inner
    }

    fn file_actions_ui(&mut self, ui: &mut egui::Ui, busy: bool) {
        // Row 1: add actions plus the live selection counter.
        ui.horizontal(|ui| {
            if Self::file_action_button(ui, !busy, self.text(Text::AddFiles), FILE_ACTION_SIZE)
                .on_hover_text(self.text(Text::AddFilesTip))
                .clicked()
            {
                let files = rfd::FileDialog::new().pick_files().unwrap_or_default();
                self.begin_scan(files);
            }
            if Self::file_action_button(ui, !busy, self.text(Text::AddFolder), FILE_ACTION_SIZE)
                .on_hover_text(self.text(Text::AddFolderTip))
                .clicked()
                && let Some(dir) = rfd::FileDialog::new().pick_folder()
            {
                self.begin_scan(vec![dir]);
            }
            ui.add_space(4.0);
            ui.label(format!(
                "{}: {}/{}",
                self.text(Text::SelectedCount),
                self.selected.len(),
                self.files.len()
            ));
        });
    }

    fn selection_actions_ui(&mut self, ui: &mut egui::Ui, busy: bool) {
        // Row 2: list-wide selection actions.
        ui.horizontal(|ui| {
            if Self::file_action_button(
                ui,
                !busy && !self.files.is_empty(),
                self.text(Text::SelectAll),
                FILE_ACTION_SIZE,
            )
            .on_hover_text(self.text(Text::SelectAllTip))
            .clicked()
            {
                self.selected = (0..self.files.len()).collect();
            }
            if Self::file_action_button(
                ui,
                !busy && !self.selected.is_empty(),
                self.text(Text::DeselectAll),
                FILE_ACTION_SIZE,
            )
            .on_hover_text(self.text(Text::DeselectAllTip))
            .clicked()
            {
                self.selected.clear();
            }
            if Self::file_action_button(
                ui,
                !busy && !self.selected.is_empty(),
                self.text(Text::Remove),
                FILE_ACTION_SIZE,
            )
            .on_hover_text(self.text(Text::RemoveTip))
            .clicked()
            {
                self.remove_selected();
            }
            if Self::file_action_button(
                ui,
                !busy && !self.files.is_empty(),
                self.text(Text::Clear),
                FILE_ACTION_SIZE,
            )
            .on_hover_text(self.text(Text::ClearTip))
            .clicked()
            {
                self.files.clear();
                self.selected.clear();
            }
        });
    }

    fn file_list_ui(&mut self, ui: &mut egui::Ui, busy: bool, height: f32) {
        let row_height = ui.text_style_height(&egui::TextStyle::Body) + 6.0;
        let frame = egui::Frame::group(ui.style());
        let content_height = (height - frame.total_margin().sum().y).max(0.0);
        frame.show(ui, |ui| {
            ui.set_height(content_height);
            egui::ScrollArea::both()
                .id_salt("files")
                .auto_shrink([false, false])
                .max_height(content_height)
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
    }

    #[allow(clippy::cast_precision_loss)]
    fn progress_row_ui(&mut self, ui: &mut egui::Ui) {
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
                egui::vec2(progress_width, PROGRESS_BAR_HEIGHT),
                egui::ProgressBar::new(fraction).show_percentage(),
            );
        });
    }

    fn log_ui(&mut self, ui: &mut egui::Ui, height: f32) {
        let frame = egui::Frame::group(ui.style());
        let content_height = (height - frame.total_margin().sum().y).max(0.0);
        frame.show(ui, |ui| {
            ui.set_height(content_height);
            egui::ScrollArea::both()
                .id_salt("log")
                .stick_to_bottom(true)
                .auto_shrink([false, false])
                .max_height(content_height)
                .show(ui, |ui| {
                    for line in &self.logs {
                        ui.label(line);
                    }
                });
        });
    }

    fn clamp_window_once(&mut self, ctx: &egui::Context) {
        if self.window_sized {
            return;
        }
        self.window_sized = true;
        if let Some(monitor) = ctx.input(|i| i.viewport().monitor_size) {
            let (size, min) = clamped_window_size(
                WINDOW_INNER_SIZE,
                monitor,
                WINDOW_MARGIN,
                WINDOW_MIN_INNER_SIZE,
            );
            ctx.send_viewport_cmd(egui::ViewportCommand::MinInnerSize(min));
            ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(size));
        }
    }

    fn handle_dropped_files(&mut self, ctx: &egui::Context, busy: bool) {
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
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.clamp_window_once(ctx);
        self.poll(ctx);
        let busy = self.worker.is_some() || self.scan_rx.is_some();
        self.handle_dropped_files(ctx, busy);
        egui::SidePanel::right("options")
            .resizable(false)
            .exact_width(OPTIONS_WIDTH)
            .show(ctx, |ui| self.options_panel_ui(ui, busy));
        // The central panel keeps the same fill as the right options panel so
        // the window reads as one background. Its frame has no border; only a
        // small inset keeps the left column content off the window edges.
        egui::CentralPanel::default()
            .frame(
                egui::Frame::new()
                    .fill(ctx.style().visuals.panel_fill)
                    .inner_margin(2.0),
            )
            .show(ctx, |ui| self.central_panel_ui(ui, busy));
    }
}

#[cfg(test)]
mod tests {
    use eframe::egui;

    use super::{
        LIST_REGION_MIN_HEIGHT, LOG_REGION_MAX_HEIGHT, LOG_REGION_MIN_HEIGHT, RimageApp,
        adjust_suffix_for_policy, clamped_window_size, log_region_height,
    };
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

    #[test]
    fn log_region_height_reserves_bounded_share() {
        // Tall window: the share is capped by the maximum.
        let capped = log_region_height(2_000.0);
        assert!((capped - LOG_REGION_MAX_HEIGHT).abs() <= f32::EPSILON);
        // Medium window: proportional share inside the bounds.
        let mid = log_region_height(600.0);
        assert!((LOG_REGION_MIN_HEIGHT..=LOG_REGION_MAX_HEIGHT).contains(&mid));
        assert!((mid - 210.0).abs() < 1.0);
        // Short window: the list keeps its minimum slice.
        let short = log_region_height(100.0);
        assert!(short <= 100.0 - LIST_REGION_MIN_HEIGHT);
        // Very short window: the reservation never exceeds what is available.
        assert!(log_region_height(30.0) <= 30.0);
    }

    #[test]
    fn suffix_enabled_returns_to_keep_when_backup_active() {
        let mut app = RimageApp::default();
        app.set_suffix_enabled(false);
        assert!(!app.suffix_enabled);

        app.policy = OriginalPolicy::Backup;
        app.set_suffix_enabled(true);
        assert_eq!(app.policy, OriginalPolicy::Keep);
        assert!(app.suffix_enabled);
    }
}
