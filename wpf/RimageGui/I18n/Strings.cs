using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimageGui.I18n
{
    public enum Language
    {
        System,
        Chinese,
        English
    }

    /// <summary>
    /// Compiled two-language string catalog. Satellite assemblies are avoided on
    /// purpose so the shipped product stays a single self-contained executable.
    /// </summary>
    public static class Strings
    {
        /// <summary>Resolves <see cref="Language.System"/> from the current UI culture.</summary>
        public static Language Effective(Language language)
        {
            if (language != Language.System)
            {
                return language;
            }

            var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase)
                ? Language.Chinese
                : Language.English;
        }

        public static string Get(Language language, string key)
        {
            if (key == null)
            {
                return string.Empty;
            }

            if (!Catalog.TryGetValue(key, out var pair))
            {
                // Surface the missing key instead of silently rendering blank text.
                return "!" + key;
            }

            return Effective(language) == Language.Chinese ? pair[0] : pair[1];
        }

        // [0] = Chinese, [1] = English
        private static readonly Dictionary<string, string[]> Catalog =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // ---- shell ----
            ["AppTitle"] = new[] { "Rimage 图像转换", "Rimage Image Converter" },
            ["Options"] = new[] { "转换选项", "Conversion options" },

            // ---- file toolbar ----
            ["AddFiles"] = new[] { "添加文件", "Add files" },
            ["AddFolder"] = new[] { "添加文件夹", "Add folder" },
            ["SelectAll"] = new[] { "全选", "Select all" },
            ["DeselectAll"] = new[] { "取消全选", "Deselect all" },
            ["Remove"] = new[] { "移除选中", "Remove selected" },
            ["Clear"] = new[] { "清空", "Clear" },
            ["SelectedCount"] = new[] { "已选", "Selected" },
            ["DropHint"] = new[]
            {
                "可拖放文件或文件夹；扫描在后台进行。",
                "Drop files or folders here; scanning runs in the background."
            },
            ["SkippedUnsupported"] = new[]
            {
                "（跳过 {0} 个不支持的文件）",
                "({0} unsupported file(s) skipped)"
            },
            ["AddFilesTip"] = new[]
            {
                "选择一个或多个图片文件；新增项目默认勾选。",
                "Choose one or more images; newly added items are checked by default."
            },
            ["AddFolderTip"] = new[]
            {
                "后台递归扫描文件夹中的受支持图片，不会阻塞界面。",
                "Recursively scan a folder for supported images in the background."
            },
            ["SelectAllTip"] = new[] { "勾选列表中的全部图片。", "Check every image in the list." },
            ["DeselectAllTip"] = new[] { "取消勾选列表中的全部图片。", "Uncheck every image in the list." },
            ["RemoveTip"] = new[]
            {
                "从列表移除所有已勾选项目，不会删除磁盘文件。",
                "Remove every checked row from the list without deleting disk files."
            },
            ["ClearTip"] = new[]
            {
                "清空整个图片列表，不会删除磁盘文件。",
                "Clear the whole list without deleting disk files."
            },

            // ---- file table ----
            ["ColumnName"] = new[] { "文件名", "File name" },
            ["ColumnStatus"] = new[] { "状态", "Status" },
            ["ColumnOutput"] = new[] { "输出路径", "Output path" },
            ["CtxOpenFolder"] = new[] { "打开所在文件夹", "Open containing folder" },
            ["CtxCopyPath"] = new[] { "复制完整路径", "Copy full path" },
            ["CtxRemoveRows"] = new[] { "从列表移除选中行", "Remove highlighted rows" },
            ["StatusPending"] = new[] { "待处理", "Pending" },
            ["StatusRunning"] = new[] { "处理中", "Working" },
            ["StatusDone"] = new[] { "成功", "Done" },
            ["StatusFailed"] = new[] { "失败", "Failed" },
            ["StatusSkipped"] = new[] { "未处理", "Skipped" },
            ["StatusUnchecked"] = new[] { "未勾选", "Unchecked" },

            // ---- progress + log ----
            ["Progress"] = new[] { "进度", "Progress" },
            ["Log"] = new[] { "日志", "Log" },
            ["Idle"] = new[] { "就绪", "Ready" },
            ["Scanning"] = new[] { "正在扫描…", "Scanning…" },
            ["Running"] = new[] { "正在转换…", "Converting…" },
            ["Finished"] = new[] { "完成", "Finished" },
            ["Cancelled"] = new[] { "已取消", "Cancelled" },
            ["Summary"] = new[] { "汇总", "Summary" },
            ["SummarySucceeded"] = new[] { "成功", "succeeded" },
            ["SummaryFailed"] = new[] { "失败", "failed" },
            ["SummarySkipped"] = new[] { "未处理", "skipped" },

            // ---- groups ----
            ["EncodingGroup"] = new[] { "编码参数", "Encoding" },
            ["OutputLocationGroup"] = new[] { "输出位置", "Output location" },
            ["OriginalFilesGroup"] = new[] { "原件处理", "Original files" },
            ["SizeGroup"] = new[] { "尺寸与缩放", "Size & resize" },
            ["ExecutionGroup"] = new[] { "执行", "Execution" },

            // ---- encoding ----
            ["Format"] = new[] { "输出格式", "Output format" },
            ["FormatTip"] = new[]
            {
                "选择输出编码格式。JPEG 使用 MozJPEG 编码器，PNG 使用 OxiPNG 编码器。\n默认：mozjpeg",
                "Choose the output codec. JPEG uses the MozJPEG encoder and PNG uses OxiPNG.\nDefault: mozjpeg"
            },
            ["Quality"] = new[] { "质量", "Quality" },
            ["QualityTip"] = new[]
            {
                "编码质量，范围 1–100，数值越高体积越大。\n无损编码（PNG、JPEG XL）不使用该参数，控件会自动禁用并清空。\nWebP 质量 100 且未启用量化时，自动改用无损模式。\n默认：85",
                "Encoder quality, 1–100; higher means larger files.\nLossless codecs (PNG, JPEG XL) ignore it; the box is disabled and cleared automatically.\nWebP at quality 100 with quantization off switches to the lossless mode automatically.\nDefault: 85"
            },
            ["QualityLosslessTag"] = new[] { "无损", "Lossless" },
            ["Quantization"] = new[] { "量化", "Quantization" },
            ["QuantizationTip"] = new[]
            {
                "减少调色板颜色数以缩小文件，可能产生色带。\n不是质量的替代品：建议同时降低质量才能真正减小体积。\n未勾选时完全不传给 rimage。默认：90",
                "Reduces the colour palette for smaller files; may introduce banding.\nNot a substitute for quality — lower the quality too for a real size win.\nNot passed to rimage at all when unchecked. Default: 90"
            },
            ["Dithering"] = new[] { "抖动", "Dithering" },
            ["DitheringTip"] = new[]
            {
                "缓解量化产生的色带，范围 1–100。\n必须先启用量化，否则该选项无效（控件会自动禁用）。\n默认：90",
                "Reduces quantization banding, range 1–100.\nRequires quantization to be enabled; the box is disabled otherwise.\nDefault: 90"
            },
            ["Suffix"] = new[] { "输出后缀", "Output suffix" },
            ["SuffixTip"] = new[]
            {
                "追加到输出文件名后的后缀，无分隔符，例如 a.jpg → a_new.jpg。\n与「创建 @backup 备份」互斥：选择备份会自动关闭后缀。\n默认：_new",
                "Suffix appended to the output name with no separator, e.g. a.jpg → a_new.jpg.\nMutually exclusive with the @backup policy: choosing Backup turns it off.\nDefault: _new"
            },
            ["SuffixBackupHint"] = new[]
            {
                "备份策略已接管命名，勾选后缀会自动切换回「保留」。",
                "The Backup policy owns naming; checking the suffix switches the policy back to Keep."
            },

            // ---- per-format notes (from the rimage codec table) ----
            ["FormatHintMozJpeg"] = new[]
            {
                "MozJPEG：推荐的有损 JPEG 编码器，速度与压缩率的均衡选择，质量 60–80 效果最佳。",
                "MozJPEG: the recommended lossy JPEG encoder; balanced speed and compression, best around quality 60–80."
            },
            ["FormatHintJpeg"] = new[]
            {
                "JPEG：标准 JPEG 编码器，质量 1–100，兼容性最好。",
                "JPEG: the standard JPEG encoder, quality 1–100, maximum compatibility."
            },
            ["FormatHintOxiPng"] = new[]
            {
                "OxiPNG：无损 PNG 优化器，压缩已有 PNG 最安全，速度取决于压缩等级。",
                "OxiPNG: lossless PNG optimizer; the safest way to shrink existing PNGs."
            },
            ["FormatHintPng"] = new[]
            {
                "PNG：无损编码，无质量参数。",
                "PNG: lossless; no quality parameter."
            },
            ["FormatHintWebP"] = new[]
            {
                "WebP：有损质量 1–100；质量设为 100 且未启用量化时自动改用无损模式。",
                "WebP: lossy quality 1–100; quality 100 with quantization off switches to the lossless mode automatically."
            },
            ["FormatHintAvif"] = new[]
            {
                "AVIF：仅静态图，压缩率高但编码较慢，默认质量 50。",
                "AVIF: still images only; high compression but slow encoding, default quality 50."
            },
            ["FormatHintJpegXl"] = new[]
            {
                "JPEG XL：仅无损（rimage 0.13 未暴露质量参数），仅静态图。",
                "JPEG XL: lossless only (rimage 0.13 exposes no quality knob), still images only."
            },
            // ---- output location ----
            ["OriginalDir"] = new[] { "原目录", "Original directory" },
            ["SelectedDir"] = new[] { "指定目录", "Selected directory" },
            ["OriginalDirTip"] = new[]
            {
                "每个输出写入其输入图片所在的目录。",
                "Write each output next to its own input image."
            },
            ["SelectedDirTip"] = new[]
            {
                "所有输出写入同一个目录；同名冲突会在任务开始前被拒绝。",
                "Write every output into one directory; name collisions are rejected before the job starts."
            },
            ["Browse"] = new[] { "浏览…", "Browse…" },
            ["OutputDirPlaceholder"] = new[] { "未选择目录", "No directory selected" },
            ["PreserveStructure"] = new[] { "保留原路径子目录结构", "Preserve source folder structure" },
            ["PreserveStructureTip"] = new[]
            {
                "在指定目录下重建输入文件的相对目录层级（rimage -r）。\n仅在「指定目录」模式下可用。",
                "Recreate the inputs' relative folder layout under the selected directory (rimage -r).\nAvailable only in Selected directory mode."
            },

            // ---- original files ----
            ["Keep"] = new[] { "保留", "Keep" },
            ["Backup"] = new[] { "创建 @backup 备份", "Create @backup copy" },
            ["Delete"] = new[] { "验证成功后删除", "Delete after verified success" },
            ["KeepTip"] = new[]
            {
                "保留输入文件，不创建额外备份。",
                "Keep the input file and create no extra copy."
            },
            ["BackupTip"] = new[]
            {
                "rimage 会把原文件保留为「原名@backup.扩展名」，转换结果写入输出位置。\n输出与输入同目录时会就地替换原路径。\n副作用：选择此项会自动关闭输出后缀以避免命名冲突。",
                "rimage keeps the original as \"stem@backup.ext\" and writes the converted file to the output location.\nWhen both land in the same directory the input path is replaced in place.\nSide effect: selecting this turns the output suffix off to avoid name collisions."
            },
            ["DeleteTip"] = new[]
            {
                "仅当 metadata 明确命中、输出文件非空、输出路径与输入不同且任务未被取消时，才删除原文件。\n副作用：该操作不可撤销，删除的文件不进回收站。",
                "Deletes the original only after metadata confirms it, the output is non-empty, the output path differs from the input, and the job was not cancelled.\nSide effect: irreversible; deleted files do not go to the Recycle Bin."
            },
            ["BackupSuffixLabel"] = new[] { "备份标记", "Backup marker" },
            ["BackupSuffixTip"] = new[]
            {
                "rimage 0.13 固定使用 @backup 作为备份标记，无法修改。",
                "rimage 0.13 hard-codes @backup as the marker; it cannot be changed."
            },

            // ---- size / resize ----
            ["ResizeMode"] = new[] { "缩放方式", "Resize mode" },
            ["ResizeNone"] = new[] { "不使用", "None" },
            ["ResizeClassic"] = new[] { "经典参数", "Classic args" },
            ["ResizeBounds"] = new[] { "尺寸限制", "Size limits" },
            ["ResizeModeTip"] = new[]
            {
                "「不使用」保持原尺寸；「经典参数」把原始值直接交给 rimage；「尺寸限制」使用 rimage 原生的单方向最长边/最短边约束。\n三者互斥。",
                "None keeps the original size; Classic args are handed to rimage verbatim; Size limits use rimage's native single-direction longest/shortest-edge constraint.\nThe three are mutually exclusive."
            },
            ["ResizeArgs"] = new[] { "参数", "Argument" },
            ["ResizeArgsTip"] = new[]
            {
                "传给 rimage 的原始缩放参数，用空格串联多个步骤（按顺序依次作用）：\n@1.5 倍数、150% 百分比、1920x1080 固定宽高、720w/720h 锁定一边、1000l/500s 锁定最长/最短边。\n兼容 720x_ 写法，会自动规范为 720w。",
                "Raw resize arguments passed to rimage; separate steps with spaces to chain them in order:\n@1.5 multiplier, 150% percentage, 1920x1080 fixed, 720w/720h anchor one side, 1000l/500s anchor longest/shortest side.\nThe 720x_ spelling is accepted and normalized to 720w."
            },
            ["Filter"] = new[] { "缩放算法", "Resample filter" },
            ["FilterTip"] = new[]
            {
                "缩放时使用的重采样算法。nearest 最快但锯齿明显，lanczos3 质量最好。\n仅在启用缩放时生效。默认：lanczos3",
                "Resampling filter used while resizing. nearest is fastest but aliased; lanczos3 gives the best quality.\nOnly applies when resizing is enabled. Default: lanczos3"
            },
            ["BoundDirection"] = new[] { "限制方向", "Limit direction" },
            ["BoundMax"] = new[] { "只缩小（上限）", "Shrink only (maximum)" },
            ["BoundMin"] = new[] { "只放大（下限）", "Enlarge only (minimum)" },
            ["BoundEdge"] = new[] { "参考边", "Reference edge" },
            ["LongestEdge"] = new[] { "最长边", "Longest edge" },
            ["ShortestEdge"] = new[] { "最短边", "Shortest edge" },
            ["BoundValue"] = new[] { "像素", "Pixels" },
            ["SizeTip"] = new[]
            {
                "始终保持宽高比。上限只缩小、下限只放大，二者只能选一个方向——rimage 无法在一次调用中同时表达。",
                "Aspect ratio is always preserved. A maximum only shrinks and a minimum only enlarges; exactly one direction can be set because rimage cannot express both in a single invocation."
            },

            // ---- execution ----
            ["HiddenExecute"] = new[] { "隐藏 rimage 窗口", "Hide rimage window" },
            ["HiddenExecuteTip"] = new[]
            {
                "勾选时 rimage 在后台静默运行（推荐）。\n取消勾选会显示其控制台窗口，便于排查后端问题。",
                "When checked, rimage runs silently in the background (recommended).\nUnchecking shows its console window, which helps when debugging the backend."
            },
            ["AutoThreads"] = new[] { "自动线程数", "Auto thread count" },
            ["Threads"] = new[] { "线程数", "Threads" },
            ["ThreadsTip"] = new[]
            {
                "勾选时按「逻辑 CPU 核数 − 1，最低 1」自动决定 rimage 的 --threads。\n取消勾选可手动指定。线程越多越快，但内存占用同比上升。\n默认：自动",
                "When checked, rimage's --threads is derived from the logical CPU count minus one (minimum one).\nUncheck to set it manually. More threads run faster but use proportionally more memory.\nDefault: automatic"
            },

            // ---- actions ----
            ["Start"] = new[] { "开始", "Start" },
            ["Cancel"] = new[] { "取消", "Cancel" },
            ["StartTip"] = new[]
            {
                "校验全部参数，然后在后台分批转换列表中已勾选的图片。\n运行期间按钮会变为「取消」。",
                "Validate every option, then convert the checked images in background batches.\nThe button turns into Cancel while the job runs."
            },
            ["CancelTip"] = new[]
            {
                "结束当前 rimage 进程并跳过尚未开始的文件；已经写出的输出会保留。",
                "Terminate the running rimage process and skip files that have not started; outputs already written are kept."
            },

            // ---- messages ----
            ["MsgTitleError"] = new[] { "无法开始", "Cannot start" },
            ["MsgTitleInfo"] = new[] { "提示", "Notice" },
            ["ErrorNoFiles"] = new[] { "请先勾选至少一个输入文件。", "Check at least one input file first." },
            ["ErrorQuality"] = new[] { "质量必须在 1 到 100 之间。", "Quality must be between 1 and 100." },
            ["ErrorQuantization"] = new[] { "量化必须在 1 到 100 之间。", "Quantization must be between 1 and 100." },
            ["ErrorDithering"] = new[]
            {
                "抖动需要先启用量化，且必须在 1 到 100 之间。",
                "Dithering requires quantization and must be between 1 and 100."
            },
            ["ErrorSuffix"] = new[]
            {
                "输出后缀无效：不能为空、不能包含 \\ / : * ? \" < > | 或控制字符，也不能是 Windows 保留设备名。",
                "Invalid output suffix: it cannot be empty, contain \\ / : * ? \" < > | or control characters, or be a reserved Windows device name."
            },
            ["ErrorBackupSuffixConflict"] = new[]
            {
                "「创建 @backup 备份」不能与输出后缀同时使用，请关闭其中之一。",
                "The @backup policy cannot be combined with an output suffix; turn one of them off."
            },
            ["ErrorOutputDirectory"] = new[] { "请选择输出目录。", "Select an output directory." },
            ["ErrorOutputDirectoryMissing"] = new[] { "输出目录不存在。", "The output directory does not exist." },
            ["ErrorResize"] = new[] { "缩放参数无效。", "The resize argument is invalid." },
            ["ErrorSizeBounds"] = new[]
            {
                "尺寸限制必须为正整数，且只能设置一个方向。",
                "Size limits must be positive integers and may only set one direction."
            },
            ["ErrorThreads"] = new[] { "线程数必须在 1 到 256 之间。", "Threads must be between 1 and 256." },
            ["ErrorUnsafeDelete"] = new[]
            {
                "删除模式下输出不能与输入是同一路径，否则会丢失原文件。",
                "In delete mode the output must differ from the input, otherwise the original would be lost."
            },
            ["ErrorDuplicateOutput"] = new[] { "多个输入会写入同一输出", "Multiple inputs resolve to the same output" },
            ["ErrorOutputOverwritesInput"] = new[]
            {
                "输出会覆盖本批次中的另一个输入",
                "An output would overwrite another input in this batch"
            },
            ["BackendFailed"] = new[] { "rimage 后端不可用", "The rimage backend is unavailable" },
            ["BackendReady"] = new[] { "rimage 后端就绪", "rimage backend ready" },
            ["ConfirmDeletePolicy"] = new[]
            {
                "「验证成功后删除」会在转换成功后永久删除原文件，且不进回收站。\n\n确定继续吗？",
                "\"Delete after verified success\" permanently removes the originals after a successful conversion; they do not go to the Recycle Bin.\n\nContinue?"
            },
            ["ConfirmTitle"] = new[] { "确认", "Confirm" }
        };
    }
}
