# Rimage GUI

Windows-only `eframe`/`egui` GUI for [rimage 0.12.5](https://github.com/SalOne22/rimage).

## Features

- Native responsive, high-DPI-aware egui interface.
- System language, 简体中文, and English UI.
- Drag/drop, native file dialogs, recursive background folder scanning, deduplication, and select-all/deselect-all batch list actions. Only the checked files are converted.
- JPEG (MozJPEG), PNG (OxiPNG), JPEG XL, WebP, and AVIF output.
- Quality, quantization/dithering, output location, original-file policy, and a custom output suffix. The suffix defaults to `backup` and produces `stem@backup.ext`.
- Classic rimage resize arguments (`@1.5`, `150%`, `1920x1080`, `720w`/`720h`; Aardio-style `720x_` is normalized to `720w`) with a selectable filter (`nearest`, `box`, `bilinear`, `hamming`, `catmull-rom`, `mitchell`, `lanczos3`), or aspect-ratio-preserving minimum/maximum size constraints as an alternative mode. The two resize modes are mutually exclusive.
- Backup original-file policy is linked with the suffix: selecting Backup closes the suffix, leaving Backup restores its previous state, and re-checking the suffix while Backup is active switches the policy back to Keep.
- Hidden-execution toggle for the rimage console window.
- Serial conversion: exactly one input file per rimage process, always with `--threads 1`. A background worker keeps the GUI responsive, and cancellation stops the current file and skips the rest.
- Per-file metadata, bounded logs, per-file progress, cancellation, and conservative delete-after-verified-success.

## Backend files

Builds embed exactly one backend according to the target architecture:

- `i686-pc-windows-msvc`: `res/rimage_x86.exe`
- `x86_64-pc-windows-msvc`: `res/rimage_x64.exe`

Both must report `rimage 0.12.5`. The backend is extracted to the current user's cache using a version/architecture directory, SHA-256 verification, and a temporary-file rename. It is never written beside the GUI executable.

## Build

Use MSVC because the rimage MozJPEG backend is not release-safe with the Windows GNU ABI.

```pwsh
rustup run stable-x86_64-pc-windows-msvc cargo build --release --target x86_64-pc-windows-msvc
rustup run stable-x86_64-pc-windows-msvc cargo build --release --target i686-pc-windows-msvc
```

Checks:

```pwsh
rustup run stable-x86_64-pc-windows-msvc cargo fmt --all -- --check
rustup run stable-x86_64-pc-windows-msvc cargo clippy --all-targets --all-features --target x86_64-pc-windows-msvc -- -D warnings
rustup run stable-x86_64-pc-windows-msvc cargo test --workspace --all-features --target x86_64-pc-windows-msvc
```

## Safety notes

`Delete after verified success` is conservative: rimage metadata must identify the current input and actual output; the process must succeed; the output must exist, be non-empty, and differ from the input; and cancellation must not have been requested. Otherwise the input remains untouched.

The Backup option passes rimage's `--backup`. Under rimage 0.12.5 this creates an `@backup` hard link beside the input; when the output path differs, rimage deletes the original input after publishing. It is therefore not an ordinary extra copy that also keeps the original input.

Jobs run strictly serially (`--threads 1`, one input per process) so behavior stays predictable on low-memory machines even though it is slower than parallel batching.
