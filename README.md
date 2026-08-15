# Rimage GUI

[ENG](/README_EN.md)

基于 `eframe`/`egui` 的 Windows 图形界面，封装 [rimage 0.13.0](https://github.com/SalOne22/rimage)，用于批量图像优化与格式转换。

## 功能特性

- 原生响应式、高 DPI 感知的 egui 界面。
- 界面语言跟随系统（简体中文或英文），不提供应用内切换。
- 支持拖放、原生文件对话框、后台递归扫描文件夹、去重、全选/反选，只转换被勾选的文件。
- 输出格式：JPEG（MozJPEG）、PNG（OxiPNG）、JPEG XL、WebP、AVIF。
- 支持质量、量化/抖动、输出位置、原件处理策略、自定义输出后缀。后缀默认 `_new`，直接拼接到原名后（无分隔符），例如 `a.jpg` → `a_new.jpg`。
- 经典 rimage 缩放参数（`@1.5`、`150%`、`1920x1080`、`720w`/`720h`、`1000l`/`500s`；Aardio 风格 `720x_` 会规范为 `720w`），可选滤镜（`nearest`、`box`、`bilinear`、`hamming`、`catmull-rom`、`mitchell`、`lanczos3`）；也可使用“尺寸限制”模式，按宽高比自动计算满足最小/最大尺寸的目标。经典参数可用空格串联多个步骤，每个值都会单独生成一个 `--resize` 参数，让 rimage 以上一步结果为基础依次缩放。两种缩放模式互斥。
- 备份策略与后缀联动：选择“备份”会关闭后缀，离开“备份”会恢复此前状态；备份激活时勾选后缀会自动切回“保留”。
- 可隐藏 rimage 控制台窗口。
- 串行转换：每次只给 rimage 一个输入文件，并固定 `--threads 1`。后台 worker 保持界面响应，取消会终止当前文件并跳过剩余文件。
- 逐文件元数据、有界日志、逐文件进度、取消，以及“验证成功后删除”的保守策略。

## 后端文件

构建时按目标架构嵌入对应后端：

- `i686-pc-windows-msvc`：`res/rimage_x86.exe`
- `x86_64-pc-windows-msvc`：`res/rimage_x64.exe`

两者都必须是 `rimage 0.13.0`。后端会解压到当前用户的缓存目录（按版本/架构分目录），通过 SHA-256 校验，并以临时文件重命名方式发布；不会写到 GUI 可执行文件旁边。

## 构建

必须使用 MSVC 工具链：rimage 的 MozJPEG 后端在 Windows GNU ABI 下 Release 模式不安全。

```pwsh
rustup run stable-x86_64-pc-windows-msvc cargo build --release --target x86_64-pc-windows-msvc
rustup run stable-x86_64-pc-windows-msvc cargo build --release --target i686-pc-windows-msvc
```

`build.ps1` 会自动化两个 release 构建，并用系统 UPX 以 6 级压缩各 GUI 可执行文件（`--compress-icons=0` 保留图标资源）。若未安装 UPX 则跳过压缩：

```pwsh
./build.ps1                  # 构建 x86_64 + i686 并用 UPX 压缩
./build.ps1 -Target x86_64-pc-windows-msvc
./build.ps1 -SkipUpx
```

检查命令：

```pwsh
rustup run stable-x86_64-pc-windows-msvc cargo fmt --all -- --check
rustup run stable-x86_64-pc-windows-msvc cargo clippy --all-targets --all-features --target x86_64-pc-windows-msvc -- -D warnings
rustup run stable-x86_64-pc-windows-msvc cargo test --workspace --all-features --target x86_64-pc-windows-msvc
```

## 测试图片

集成测试使用 `tests/fixtures/` 下固定的批量测试图片（JPEG、PNG、WebP）。这些图片由 Windows 壁纸 `C:\Windows\Web\Wallpaper\Windows\img0.jpg` 经 ffmpeg 缩放转换而来，测试运行时会自动复制到临时目录，不会修改原文件。

## 安全说明

“验证成功后删除”采用保守策略：rimage 元数据必须命中当前输入与真实输出；进程必须成功；输出必须存在、非空且与输入不同；且不得处于取消状态。否则保留输入不动。

备份策略会传入 rimage 的 `--backup`。在 rimage 0.13.0 下，原文件会以 `stem@backup.ext` 硬链接保留（原路径不再持有它），转换结果写入输出位置；当输出在输入目录内时，输入路径被就地替换。由于显式传入与输入目录相同的 `--directory` 会让 rimage 失败，这种情况会省略 `--directory`，让 rimage 保持原生的就地备份行为。

任务严格串行执行（`--threads 1`，每次一个输入进程），因此即使内存较小的机器行为也可预测，代价是比并行批处理慢。
