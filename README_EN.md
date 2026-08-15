# Rimage GUI

Windows-only `eframe`/`egui` GUI for [rimage 0.13.0](https://github.com/SalOne22/rimage).

## Features

- Native responsive, high-DPI-aware egui interface.
- UI language follows the system (简体中文 or English); no in-app language
  switcher.
- Drag/drop, native file dialogs, recursive background folder scanning, deduplication, and select-all/deselect-all batch list actions. Only the checked files are converted.
- JPEG (MozJPEG), PNG (OxiPNG), JPEG XL, WebP, and AVIF output.
- Quality, quantization/dithering, output location, original-file policy, and a custom output suffix. The suffix defaults to `_new` and is appended without a separator, so `a.jpg` becomes `a_new.jpg`.
- Classic rimage resize arguments (`@1.5`, `150%`, `1920x1080`, `720w`/`720h`, `1000l`/`500s`; Aardio-style `720x_` is normalized to `720w`) with a selectable filter (`nearest`, `box`, `bilinear`, `hamming`, `catmull-rom`, `mitchell`, `lanczos3`), or a longest/shortest-edge size limit (enlarge-only or reduce-only) as an alternative mode. Classic arguments may be chained by separating several values with spaces; each value is emitted as its own `--resize` flag so rimage maps it against the size the previous value produced. The two resize modes are mutually exclusive.
- Backup original-file policy is linked with the suffix: selecting Backup closes the suffix, leaving Backup restores its previous state, and re-checking the suffix while Backup is active switches the policy back to Keep.
- Hidden-execution toggle for the rimage console window.
- Batch conversion through rimage's native `file.list`: one rimage invocation processes the whole list, with `--threads` set to one less than the system's logical CPU count by default (minimum one), or manually overridden in the UI. A background worker keeps the GUI responsive, and cancellation terminates the running process.
- Per-file metadata, bounded logs, per-file progress, cancellation, and conservative delete-after-verified-success.

## Backend files

Builds embed exactly one backend according to the target architecture:

- `i686-pc-windows-msvc`: `res/rimage_x86.exe`
- `x86_64-pc-windows-msvc`: `res/rimage_x64.exe`

Both must report `rimage 0.13.0`. The backend is extracted to the current user's cache using a version/architecture directory, SHA-256 verification, and a temporary-file rename. It is never written beside the GUI executable.

## Build

Use MSVC because the rimage MozJPEG backend is not release-safe with the Windows GNU ABI.

```pwsh
rustup run stable-x86_64-pc-windows-msvc cargo build --release --target x86_64-pc-windows-msvc
rustup run stable-x86_64-pc-windows-msvc cargo build --release --target i686-pc-windows-msvc
```

`build.ps1` automates both release builds and then compresses each GUI
executable with the system's UPX at level 6 (`--compress-icons=0` so the icon
resource stays intact). If UPX is not installed the compression step is
skipped:

```pwsh
./build.ps1                  # build x86_64 + i686 and compress with UPX
./build.ps1 -Target x86_64-pc-windows-msvc
./build.ps1 -SkipUpx
```

Checks:

```pwsh
rustup run stable-x86_64-pc-windows-msvc cargo fmt --all -- --check
rustup run stable-x86_64-pc-windows-msvc cargo clippy --all-targets --all-features --target x86_64-pc-windows-msvc -- -D warnings
rustup run stable-x86_64-pc-windows-msvc cargo test --workspace --all-features --target x86_64-pc-windows-msvc
```

## Safety notes

`Delete after verified success` is conservative: rimage metadata must identify the current input and actual output; the process must succeed; the output must exist, be non-empty, and differ from the input; and cancellation must not have been requested. Otherwise the input remains untouched.

The Backup option passes rimage's `--backup`. Under rimage 0.13.0 the original
input is preserved as a `stem@backup.ext` hard link (the original path no
longer holds it) and the converted file is written to the output location; in
the input directory the input path is replaced in place. Because an explicit
`--directory` equal to the input directory makes rimage fail, that case runs
without `--directory` so rimage keeps its native in-place behavior.

Jobs run through a single `file.list` invocation with `--threads` derived from the system CPU count by default (cores minus one, at least one), or a manual override.
