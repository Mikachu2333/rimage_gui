#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Language {
    System,
    Chinese,
    English,
}

impl Language {
    #[must_use]
    pub fn effective(self) -> Self {
        match self {
            Self::System => {
                if sys_locale::get_locale()
                    .is_some_and(|v| v.to_ascii_lowercase().starts_with("zh"))
                {
                    Self::Chinese
                } else {
                    Self::English
                }
            }
            other => other,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Text {
    AppTitle,
    Files,
    AddFiles,
    AddFolder,
    Remove,
    SelectAll,
    DeselectAll,
    SelectedCount,
    Clear,
    Options,
    Format,
    Quality,
    Quantization,
    Dithering,
    Suffix,
    Enable,
    OutputMode,
    OriginalDir,
    SelectedDir,
    Browse,
    OriginalPolicy,
    Keep,
    Backup,
    Delete,
    MinSize,
    MaxSize,
    Disabled,
    LongestEdge,
    ShortestEdge,
    ResizeMode,
    ResizeNone,
    ResizeClassic,
    ResizeBounds,
    ResizeArgs,
    ResizeArgsTip,
    Filter,
    FilterTip,
    ResizeModeTip,
    Start,
    Cancel,
    Log,
    Idle,
    Scanning,
    Running,
    Finished,
    Failed,
    Cancelled,
    Language,
    DropHint,
    BackupTip,
    DeleteTip,
    SizeTip,
    FormatTip,
    QualityTip,
    QuantizationTip,
    DitheringTip,
    SuffixTip,
    SuffixBackupHint,
    OutputModeTip,
    OriginalDirTip,
    SelectedDirTip,
    KeepTip,
    AddFilesTip,
    AddFolderTip,
    RemoveTip,
    ClearTip,
    SelectAllTip,
    DeselectAllTip,
    StartTip,
    CancelTip,
    Progress,
    SuccessPrefix,
    ErrorPrefix,
    Summary,
    SummarySucceeded,
    SummaryFailed,
    SummarySkipped,
    ErrorNoFiles,
    ErrorQuality,
    ErrorQuantization,
    ErrorDithering,
    ErrorSuffix,
    ErrorOutputDirectory,
    ErrorSizeBounds,
    ErrorResize,
    ErrorUnsafeDelete,
    ErrorDuplicateOutput,
    ErrorOutputOverwritesInput,
    ErrorBackupSuffixConflict,
    EncodingGroup,
    OutputLocationGroup,
    OriginalFilesGroup,
    SizeLimitsGroup,
    ExecutionGroup,
    HiddenExecute,
    HiddenExecuteTip,
    AutoThreads,
    ThreadsTip,
}

#[must_use]
#[allow(clippy::too_many_lines)]
pub fn tr(language: Language, key: Text) -> &'static str {
    let zh = language.effective() == Language::Chinese;
    match (zh, key) {
        (true, Text::AppTitle) => "Rimage 图像转换",
        (false, Text::AppTitle) => "Rimage Image Converter",
        (true, Text::Files) => "文件列表",
        (false, Text::Files) => "Files",
        (true, Text::AddFiles) => "添加文件",
        (false, Text::AddFiles) => "Add files",
        (true, Text::AddFolder) => "添加文件夹",
        (false, Text::AddFolder) => "Add folder",
        (true, Text::Remove) => "移除选中",
        (false, Text::Remove) => "Remove selected",
        (true, Text::SelectAll) => "全选",
        (false, Text::SelectAll) => "Select all",
        (true, Text::DeselectAll) => "取消全选",
        (false, Text::DeselectAll) => "Deselect all",
        (true, Text::SelectedCount) => "已选",
        (false, Text::SelectedCount) => "Selected",
        (true, Text::Clear) => "清空",
        (false, Text::Clear) => "Clear",
        (true, Text::Options) => "转换选项",
        (false, Text::Options) => "Options",
        (true, Text::Format) => "输出格式",
        (false, Text::Format) => "Output format",
        (true, Text::Quality) => "质量",
        (false, Text::Quality) => "Quality",
        (true, Text::Quantization) => "量化",
        (false, Text::Quantization) => "Quantization",
        (true, Text::Dithering) => "抖动",
        (false, Text::Dithering) => "Dithering",
        (true, Text::Suffix) => "输出后缀",
        (false, Text::Suffix) => "Suffix",
        (true, Text::Enable) => "启用",
        (false, Text::Enable) => "Enable",
        (true, Text::OutputMode | Text::OutputLocationGroup) => "输出位置",
        (false, Text::OutputMode | Text::OutputLocationGroup) => "Output location",
        (true, Text::OriginalDir) => "原目录",
        (false, Text::OriginalDir) => "Original directory",
        (true, Text::SelectedDir) => "指定目录",
        (false, Text::SelectedDir) => "Selected directory",
        (true, Text::Browse) => "浏览",
        (false, Text::Browse) => "Browse",
        (true, Text::OriginalPolicy | Text::OriginalFilesGroup) => "原件处理",
        (false, Text::OriginalPolicy) => "Original file policy",
        (true, Text::Keep) => "保留",
        (false, Text::Keep) => "Keep",
        (true, Text::Backup) => "创建 @backup 备份",
        (false, Text::Backup) => "Create @backup copy",
        (true, Text::Delete) => "验证成功后删除",
        (false, Text::Delete) => "Delete after verified success",
        (true, Text::MinSize) => "最小尺寸",
        (false, Text::MinSize) => "Minimum size",
        (true, Text::MaxSize) => "最大尺寸",
        (false, Text::MaxSize) => "Maximum size",
        (true, Text::Disabled) => "不限制",
        (false, Text::Disabled) => "Disabled",
        (true, Text::LongestEdge) => "最长边",
        (false, Text::LongestEdge) => "Longest edge",
        (true, Text::ShortestEdge) => "最短边",
        (false, Text::ShortestEdge) => "Shortest edge",
        (true, Text::ResizeMode) => "缩放方式",
        (false, Text::ResizeMode) => "Resize mode",
        (true, Text::ResizeNone) => "不使用",
        (false, Text::ResizeNone) => "None",
        (true, Text::ResizeClassic) => "经典参数",
        (false, Text::ResizeClassic) => "Classic args",
        (true, Text::ResizeBounds) => "尺寸限制",
        (false, Text::ResizeBounds) => "Size limits",
        (true, Text::ResizeArgs) => "参数",
        (false, Text::ResizeArgs) => "Argument",
        (true, Text::ResizeArgsTip) => {
            "传给 rimage 的原始缩放参数，可用空格串联多个步骤（按顺序组合）：\n@1.5 倍数、150% 百分比、1920x1080 固定宽高、720w/720h 指定一边、1000l/500s 指定最长/最短边。兼容 720x_ 并自动规范为 720w。"
        }
        (false, Text::ResizeArgsTip) => {
            "Raw resize arguments passed to rimage; separate steps with spaces to chain them in order:\n@1.5 multiplier, 150% percentage, 1920x1080 fixed, 720w/720h one-side, 1000l/500s longest/shortest side. Aardio-style 720x_ is normalized to 720w."
        }
        (true, Text::Filter) => "缩放滤镜",
        (false, Text::Filter) => "Filter",
        (true, Text::FilterTip) => "缩放使用的滤镜，默认 Lanczos3。",
        (false, Text::FilterTip) => "Filter used when resizing; default is Lanczos3.",
        (true, Text::ResizeModeTip) => {
            "经典参数直接交给 rimage；尺寸限制使用 rimage 原生的最长边/最短边放大或缩小（只能选一个方向）。两种方式互斥。"
        }
        (false, Text::ResizeModeTip) => {
            "Classic args are passed to rimage as-is; size limits use rimage's native longest/shortest-edge enlarge or reduce (single direction only). The two modes are mutually exclusive."
        }
        (true, Text::Start) => "开始",
        (false, Text::Start) => "Start",
        (true, Text::Cancel) => "取消",
        (false, Text::Cancel) => "Cancel",
        (true, Text::Log) => "日志",
        (false, Text::Log) => "Log",
        (true, Text::Idle) => "就绪",
        (false, Text::Idle) => "Ready",
        (true, Text::Scanning) => "正在扫描…",
        (false, Text::Scanning) => "Scanning…",
        (true, Text::Running) => "正在转换…",
        (false, Text::Running) => "Converting…",
        (true, Text::Finished) => "完成",
        (false, Text::Finished) => "Finished",
        (true, Text::Failed | Text::SummaryFailed) => "失败",
        (false, Text::Failed) => "Failed",
        (true, Text::Cancelled) => "已取消",
        (false, Text::Cancelled) => "Cancelled",
        (true, Text::Language) => "语言",
        (false, Text::Language) => "Language",
        (true, Text::DropHint) => "可拖放文件或文件夹；扫描在后台进行。",
        (false, Text::DropHint) => "Drop files or folders here; scanning runs in the background.",
        (true, Text::BackupTip) => {
            "rimage 会把原输入保留为“输入名@backup”硬链接（原路径不再保留原内容），转换结果写入输出位置；输出与输入同目录时就地替换输入路径。选择此项会自动禁用输出后缀以避免名称冲突。"
        }
        (false, Text::BackupTip) => {
            "rimage preserves the original as a stem@backup.ext hard link (the original path no longer holds it) and writes the converted file to the output location; in the input directory the input path is replaced in place. Selecting this automatically disables the output suffix to avoid name collisions."
        }
        (true, Text::DeleteTip) => {
            "仅当 metadata 明确命中、输出非空且与输入不同、任务未取消时删除。"
        }
        (false, Text::DeleteTip) => {
            "Deletes only after matching metadata, a non-empty distinct output, and no cancellation."
        }
        (true, Text::SizeTip) => {
            "保持宽高比。最小限制只放大、最大限制只缩小；两者只能选一个，不能同时设置。使用默认 Lanczos3。"
        }
        (false, Text::SizeTip) => {
            "Preserves aspect ratio. Minimum only enlarges and maximum only shrinks; set exactly one of them. Uses default Lanczos3."
        }
        (true, Text::FormatTip) => "选择输出编码格式；JPEG 使用 MozJPEG，PNG 使用 OxiPNG。",
        (false, Text::FormatTip) => {
            "Choose the output codec; JPEG uses MozJPEG and PNG uses OxiPNG."
        }
        (true, Text::QualityTip) => "编码质量 1–100；PNG 和 JPEG XL 不使用此参数。",
        (false, Text::QualityTip) => "Encoder quality from 1–100; PNG and JPEG XL do not use it.",
        (true, Text::QuantizationTip) => "减少颜色数量以缩小文件；可能产生色带。",
        (false, Text::QuantizationTip) => {
            "Reduces the color palette for smaller files and may introduce banding."
        }
        (true, Text::DitheringTip) => "在启用量化时缓解色带；数值范围 1–100。",
        (false, Text::DitheringTip) => {
            "Reduces quantization banding; available only with quantization, range 1–100."
        }
        (true, Text::SuffixTip) => {
            "自定义输出文件名后缀，默认 _new；输出名直接拼接在原名后（无分隔符），如 a.jpg → a_new.jpg。与“创建 @backup 备份”策略联动：选择备份会自动关闭后缀，勾选后缀会自动切换回“保留”策略。"
        }
        (false, Text::SuffixTip) => {
            "Custom output-name suffix, default _new; output is stemsuffix.ext with no separator, e.g. a.jpg → a_new.jpg. Linked with the @backup policy: selecting Backup disables it, and checking it switches the policy back to Keep."
        }
        (true, Text::SuffixBackupHint) => "备份策略已接管后缀，勾选将自动切换为“保留”。",
        (false, Text::SuffixBackupHint) => {
            "Backup owns the suffix; checking it switches the policy to Keep."
        }
        (true, Text::OutputModeTip) => "选择每个转换文件的输出目录。",
        (false, Text::OutputModeTip) => "Choose where each converted file is written.",
        (true, Text::OriginalDirTip) => "输出到每张输入图片所在目录。",
        (false, Text::OriginalDirTip) => "Write beside each input image.",
        (true, Text::SelectedDirTip) => "所有输出写入指定的统一目录；同名冲突会在启动前拒绝。",
        (false, Text::SelectedDirTip) => {
            "Write all outputs to one selected directory; name collisions are rejected before start."
        }
        (true, Text::KeepTip) => "保留输入文件，不创建额外备份。",
        (false, Text::KeepTip) => "Keep the input file without creating an extra backup.",
        (true, Text::AddFilesTip) => "选择一个或多个图片文件；新增项目默认选中。",
        (false, Text::AddFilesTip) => {
            "Choose one or more images; newly added items are selected by default."
        }
        (true, Text::AddFolderTip) => "后台递归扫描文件夹中的受支持图片。",
        (false, Text::AddFolderTip) => {
            "Recursively scan a folder for supported images in the background."
        }
        (true, Text::RemoveTip) => "一次移除列表中所有已选项目，不删除磁盘文件。",
        (false, Text::RemoveTip) => {
            "Remove every selected row from the list without deleting disk files."
        }
        (true, Text::ClearTip) => "清空整个图片列表，不删除磁盘文件。",
        (false, Text::ClearTip) => "Clear the list without deleting disk files.",
        (true, Text::SelectAllTip) => "选中列表中的全部图片。",
        (false, Text::SelectAllTip) => "Select every image in the list.",
        (true, Text::DeselectAllTip) => "取消列表中的全部选择。",
        (false, Text::DeselectAllTip) => "Deselect every image in the list.",
        (true, Text::StartTip) => "验证参数后，串行后台转换列表中的勾选项。",
        (false, Text::StartTip) => {
            "Validate the options, then convert the selected files serially in the background."
        }
        (true, Text::CancelTip) => "终止当前 rimage 进程并跳过未开始的文件；已完成输出会保留。",
        (false, Text::CancelTip) => {
            "Terminate the current rimage process and skip unstarted files; completed outputs remain."
        }
        (true, Text::Progress) => "进度",
        (false, Text::Progress) => "Progress",
        (true, Text::SuccessPrefix | Text::SummarySucceeded) => "成功",
        (false, Text::SuccessPrefix) => "Success",
        (true, Text::ErrorPrefix) => "错误",
        (false, Text::ErrorPrefix) => "Error",
        (true, Text::Summary) => "汇总",
        (false, Text::Summary) => "Summary",
        (false, Text::SummarySucceeded) => "succeeded",
        (false, Text::SummaryFailed) => "failed",
        (true, Text::SummarySkipped) => "未处理",
        (false, Text::SummarySkipped) => "skipped",
        (true, Text::ErrorNoFiles) => "请先添加输入文件。",
        (false, Text::ErrorNoFiles) => "Add at least one input file.",
        (true, Text::ErrorQuality) => "质量必须在 1 到 100 之间。",
        (false, Text::ErrorQuality) => "Quality must be between 1 and 100.",
        (true, Text::ErrorQuantization) => "量化必须在 1 到 100 之间。",
        (false, Text::ErrorQuantization) => "Quantization must be between 1 and 100.",
        (true, Text::ErrorDithering) => "抖动需要启用量化，且必须在 1 到 100 之间。",
        (false, Text::ErrorDithering) => {
            "Dithering requires quantization and must be between 1 and 100."
        }
        (true, Text::ErrorSuffix) => "输出后缀无效。",
        (false, Text::ErrorSuffix) => "The output suffix is invalid.",
        (true, Text::ErrorOutputDirectory) => "请选择输出目录。",
        (false, Text::ErrorOutputDirectory) => "Select an output directory.",
        (true, Text::ErrorSizeBounds) => "尺寸限制必须为正数，且只能设置最小或最大其中之一。",
        (false, Text::ErrorSizeBounds) => {
            "Size bounds must be positive and may only set one of minimum or maximum."
        }
        (true, Text::ErrorResize) => "缩放参数无效。",
        (false, Text::ErrorResize) => "The resize argument is invalid.",
        (true, Text::ErrorUnsafeDelete) => "删除模式不能让输出与输入为同一路径。",
        (false, Text::ErrorUnsafeDelete) => "Delete mode cannot use the input as its output.",
        (true, Text::ErrorDuplicateOutput) => "多个输入会写入同一输出",
        (false, Text::ErrorDuplicateOutput) => "Multiple inputs resolve to the same output",
        (true, Text::ErrorOutputOverwritesInput) => "输出会覆盖批次中的另一个输入",
        (false, Text::ErrorOutputOverwritesInput) => {
            "An output would overwrite another batch input"
        }
        (true, Text::ErrorBackupSuffixConflict) => {
            "“原件备份”策略不能与输出后缀同时使用，请先关闭后缀或改用其他原件策略。"
        }
        (false, Text::ErrorBackupSuffixConflict) => {
            "The original-file Backup policy cannot be combined with an output suffix; disable the suffix or choose another policy."
        }
        (true, Text::EncodingGroup) => "编码参数",
        (false, Text::EncodingGroup) => "Encoding",
        (false, Text::OriginalFilesGroup) => "Original files",
        (true, Text::SizeLimitsGroup) => "尺寸与缩放",
        (false, Text::SizeLimitsGroup) => "Size & resize",
        (true, Text::ExecutionGroup) => "执行",
        (false, Text::ExecutionGroup) => "Execution",
        (true, Text::HiddenExecute) => "隐藏 rimage 窗口",
        (false, Text::HiddenExecute) => "Hide rimage window",
        (true, Text::HiddenExecuteTip) => {
            "勾选时 rimage 在后台静默运行；取消勾选可观察其控制台输出。"
        }
        (false, Text::HiddenExecuteTip) => {
            "When checked, rimage runs hidden in the background; uncheck to watch its console output."
        }
        (true, Text::AutoThreads) => "自动线程数",
        (false, Text::AutoThreads) => "Auto threads",
        (true, Text::ThreadsTip) => {
            "勾选时按「系统逻辑 CPU 核数 - 1（最低 1）」自动决定 --threads；取消勾选可手动指定线程数。"
        }
        (false, Text::ThreadsTip) => {
            "When checked, --threads is derived from the logical CPU count minus one (minimum one); uncheck to set it manually."
        }
    }
}

#[must_use]
pub fn validation_message(
    language: Language,
    error: &crate::validation::ValidationError,
) -> String {
    use crate::validation::ValidationError;
    let (key, path) = match error {
        ValidationError::NoFiles => (Text::ErrorNoFiles, None),
        ValidationError::Quality => (Text::ErrorQuality, None),
        ValidationError::Quantization => (Text::ErrorQuantization, None),
        ValidationError::Dithering => (Text::ErrorDithering, None),
        ValidationError::Suffix => (Text::ErrorSuffix, None),
        ValidationError::OutputDirectory => (Text::ErrorOutputDirectory, None),
        ValidationError::SizeBounds => (Text::ErrorSizeBounds, None),
        ValidationError::Resize => (Text::ErrorResize, None),
        ValidationError::UnsafeDelete => (Text::ErrorUnsafeDelete, None),
        ValidationError::DuplicateOutput(path) => (Text::ErrorDuplicateOutput, Some(path)),
        ValidationError::OutputOverwritesInput(path) => {
            (Text::ErrorOutputOverwritesInput, Some(path))
        }
        ValidationError::BackupSuffixConflict => (Text::ErrorBackupSuffixConflict, None),
    };
    path.map_or_else(
        || tr(language, key).to_owned(),
        |path| {
            format!(
                "{}: {}",
                tr(language, key),
                crate::input::display_path(path)
            )
        },
    )
}
