use std::{
    fs,
    io::{Read, Write},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    thread,
};

use crossbeam_channel::{Receiver, Sender, bounded};
use directories::ProjectDirs;
use sha2::{Digest, Sha256};
use thiserror::Error;

use crate::{
    command::{build_args, format_command_line},
    metadata::load_output_map,
    model::{JobSpec, OriginalPolicy},
    validation::{prepare_job_files, safe_to_delete, validate_job},
};

#[cfg(target_arch = "x86")]
const BACKEND_BYTES: &[u8] = include_bytes!("../res/rimage_x86.exe");
#[cfg(target_arch = "x86_64")]
const BACKEND_BYTES: &[u8] = include_bytes!("../res/rimage_x64.exe");
#[cfg(not(any(target_arch = "x86", target_arch = "x86_64")))]
compile_error!("rimage-gui supports only Windows x86 and x86_64");

#[derive(Debug)]
pub enum WorkerEvent {
    Started {
        total: usize,
    },
    FileStarted {
        index: usize,
        input: PathBuf,
    },
    Log(String),
    ValidationFailed(crate::validation::ValidationError),
    FileSucceeded {
        input: PathBuf,
        output: PathBuf,
    },
    FileFailed {
        input: PathBuf,
        error: String,
    },
    /// `succeeded` and `failed` count files that produced a per-file event;
    /// `skipped` counts files that never started (for example after cancel).
    Finished {
        succeeded: usize,
        failed: usize,
        skipped: usize,
        cancelled: bool,
    },
}

pub struct WorkerHandle {
    pub events: Receiver<WorkerEvent>,
    cancel: Arc<AtomicBool>,
    join: Option<thread::JoinHandle<()>>,
}
impl WorkerHandle {
    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::Release);
    }
}

impl Drop for WorkerHandle {
    fn drop(&mut self) {
        self.cancel();
        if let Some(join) = self.join.take() {
            while !join.is_finished() {
                for _ in self.events.try_iter().take(128) {}
                thread::sleep(std::time::Duration::from_millis(10));
            }
            let _ = join.join();
        }
    }
}

#[derive(Debug, Error)]
pub enum BackendError {
    #[error("user cache directory is unavailable")]
    NoCache,
    #[error("backend I/O failed: {0}")]
    Io(#[from] std::io::Error),
    #[error("backend hash verification failed")]
    Hash,
    #[error("backend version is incompatible: {0}")]
    Version(String),
}

fn digest(bytes: &[u8]) -> [u8; 32] {
    Sha256::digest(bytes).into()
}
fn file_digest(path: &Path) -> std::io::Result<[u8; 32]> {
    let mut f = fs::File::open(path)?;
    let mut h = Sha256::new();
    let mut buf = vec![0; 64 * 1024];
    loop {
        let n = f.read(&mut buf)?;
        if n == 0 {
            break;
        }
        h.update(&buf[..n]);
    }
    Ok(h.finalize().into())
}

/// Extracts and verifies the architecture-specific embedded backend.
///
/// # Errors
/// Returns an error when the cache is unavailable, I/O fails, or hashes differ.
pub fn extract_backend() -> Result<PathBuf, BackendError> {
    let project =
        ProjectDirs::from("org", "Mikachu2333", "RimageGUI").ok_or(BackendError::NoCache)?;
    let arch = if cfg!(target_arch = "x86") {
        "x86"
    } else {
        "x64"
    };
    let dir = project
        .cache_dir()
        .join(env!("CARGO_PKG_VERSION"))
        .join(arch);
    fs::create_dir_all(&dir)?;
    let target = dir.join("rimage.exe");
    let expected = digest(BACKEND_BYTES);
    if target.is_file() && file_digest(&target)? == expected {
        return Ok(target);
    }
    let (temp, mut file) = create_unique_temp(&dir)?;
    let result = (|| -> Result<(), BackendError> {
        file.write_all(BACKEND_BYTES)?;
        file.sync_all()?;
        drop(file);
        if file_digest(&temp)? != expected {
            return Err(BackendError::Hash);
        }
        publish_backend(&temp, &target)?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temp);
    }
    result?;
    if file_digest(&target)? != expected {
        return Err(BackendError::Hash);
    }
    Ok(target)
}

fn create_unique_temp(dir: &Path) -> std::io::Result<(PathBuf, fs::File)> {
    for attempt in 0..100_u32 {
        let path = dir.join(format!("rimage-{}-{attempt}.tmp", std::process::id()));
        match fs::File::create_new(&path) {
            Ok(file) => return Ok((path, file)),
            Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {}
            Err(error) => return Err(error),
        }
    }
    Err(std::io::Error::new(
        std::io::ErrorKind::AlreadyExists,
        "could not allocate a unique backend temporary file",
    ))
}

#[cfg(windows)]
#[allow(unsafe_code)]
fn publish_backend(source: &Path, target: &Path) -> std::io::Result<()> {
    use std::{iter, os::windows::ffi::OsStrExt, ptr};

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn ReplaceFileW(
            replaced_file_name: *const u16,
            replacement_file_name: *const u16,
            backup_file_name: *const u16,
            replace_flags: u32,
            exclude: *mut std::ffi::c_void,
            reserved: *mut std::ffi::c_void,
        ) -> i32;
    }

    if !target.exists() {
        return match fs::rename(source, target) {
            Ok(()) => Ok(()),
            Err(error) if target.exists() => {
                if file_digest(target)? == digest(BACKEND_BYTES) {
                    fs::remove_file(source)
                } else {
                    Err(error)
                }
            }
            Err(error) => Err(error),
        };
    }
    let wide = |path: &Path| {
        path.as_os_str()
            .encode_wide()
            .chain(iter::once(0))
            .collect::<Vec<_>>()
    };
    let target_wide = wide(target);
    let source_wide = wide(source);
    // SAFETY: Both paths are NUL-terminated buffers alive for the call; optional
    // pointers are null and ReplaceFileW does not retain supplied pointers.
    let replaced = unsafe {
        ReplaceFileW(
            target_wide.as_ptr(),
            source_wide.as_ptr(),
            ptr::null(),
            0,
            ptr::null_mut(),
            ptr::null_mut(),
        )
    };
    if replaced == 0 {
        if file_digest(target)? == digest(BACKEND_BYTES) {
            fs::remove_file(source)
        } else {
            Err(std::io::Error::last_os_error())
        }
    } else {
        Ok(())
    }
}

#[cfg(not(windows))]
fn publish_backend(source: &Path, target: &Path) -> std::io::Result<()> {
    fs::rename(source, target)
}

#[cfg(windows)]
fn hidden(command: &mut Command) {
    use std::os::windows::process::CommandExt;
    command.creation_flags(0x0800_0000);
}
#[cfg(not(windows))]
fn hidden(_: &mut Command) {}

/// Confirms the extracted executable reports the supported rimage version.
///
/// # Errors
/// Returns an error when the process cannot run or reports another version.
pub fn verify_backend(path: &Path) -> Result<(), BackendError> {
    let mut command = Command::new(path);
    command.arg("--version");
    hidden(&mut command);
    let output = command.output()?;
    let text = String::from_utf8_lossy(&output.stdout).trim().to_owned();
    if output.status.success() && text.contains("rimage 0.12.5") {
        Ok(())
    } else {
        Err(BackendError::Version(text))
    }
}

#[must_use]
pub fn start_job(job: JobSpec) -> WorkerHandle {
    let (tx, rx) = bounded(1024);
    let cancel = Arc::new(AtomicBool::new(false));
    let worker_cancel = Arc::clone(&cancel);
    let join = thread::spawn(move || run_job(job, worker_cancel, tx));
    WorkerHandle {
        events: rx,
        cancel,
        join: Some(join),
    }
}

#[allow(clippy::too_many_lines, clippy::needless_pass_by_value)]
fn run_job(job: JobSpec, cancel: Arc<AtomicBool>, tx: Sender<WorkerEvent>) {
    let total = job.files.len();
    let _ = tx.send(WorkerEvent::Started { total });
    if let Err(error) = validate_job(&job) {
        let message = error.to_string();
        let _ = tx.send(WorkerEvent::ValidationFailed(error));
        abort_job(&job, &message, &tx);
        return;
    }
    let prepared_files = match prepare_job_files(&job) {
        Ok(files) => files,
        Err(error) => {
            let message = error.to_string();
            let _ = tx.send(WorkerEvent::ValidationFailed(error));
            abort_job(&job, &message, &tx);
            return;
        }
    };
    let backend = match extract_backend().and_then(|path| {
        verify_backend(&path)?;
        Ok(path)
    }) {
        Ok(path) => path,
        Err(error) => {
            let message = error.to_string();
            let _ = tx.send(WorkerEvent::Log(message.clone()));
            abort_job(&job, &message, &tx);
            return;
        }
    };
    let mut succeeded = 0;
    let mut failed = 0;
    let mut skipped = 0;
    for (index, file) in prepared_files.iter().enumerate() {
        if cancel.load(Ordering::Acquire) {
            skipped += prepared_files.len() - index;
            break;
        }
        let _ = tx.send(WorkerEvent::FileStarted {
            index,
            input: file.input.clone(),
        });
        if let Err(error) = fs::create_dir_all(&file.output_dir) {
            failed += 1;
            let _ = tx.send(WorkerEvent::FileFailed {
                input: file.input.clone(),
                error: error.to_string(),
            });
            continue;
        }
        let metadata_file = match tempfile::Builder::new()
            .prefix("rimage-gui-")
            .suffix(".json")
            .tempfile()
        {
            Ok(file) => file,
            Err(error) => {
                failed += 1;
                let _ = tx.send(WorkerEvent::FileFailed {
                    input: file.input.clone(),
                    error: error.to_string(),
                });
                continue;
            }
        };
        let metadata_path = metadata_file.path().to_path_buf();
        drop(metadata_file);
        let args = build_args(&job, file, &metadata_path);
        let command_line = format_command_line(&backend, &args);
        let mut command = Command::new(&backend);
        command
            .args(&args)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        if job.options.hidden {
            hidden(&mut command);
        }
        let mut child = match command.spawn() {
            Ok(child) => child,
            Err(error) => {
                failed += 1;
                let _ = tx.send(WorkerEvent::FileFailed {
                    input: file.input.clone(),
                    error: with_command(&error.to_string(), &command_line),
                });
                let _ = fs::remove_file(&metadata_path);
                continue;
            }
        };
        let mut readers = Vec::with_capacity(2);
        if let Some(stdout) = child.stdout.take() {
            readers.push(spawn_output_reader(stdout, tx.clone()));
        }
        if let Some(stderr) = child.stderr.take() {
            readers.push(spawn_output_reader(stderr, tx.clone()));
        }
        let status = loop {
            if cancel.load(Ordering::Acquire) {
                let _ = child.kill();
                break child.wait();
            }
            match child.try_wait() {
                Ok(Some(status)) => break Ok(status),
                Ok(None) => thread::sleep(std::time::Duration::from_millis(50)),
                Err(error) => break Err(error),
            }
        };
        let mut diagnostics = Vec::with_capacity(2);
        for reader in readers {
            diagnostics.push(reader.join().unwrap_or_default());
        }
        let stdout_tail = diagnostics.first().map_or("", String::as_str);
        let stderr_tail = diagnostics.get(1).map_or("", String::as_str);
        let cancelled = cancel.load(Ordering::Acquire);
        let status_success = status.as_ref().is_ok_and(std::process::ExitStatus::success);
        let outputs = if status_success {
            load_output_map(&metadata_path).ok()
        } else {
            None
        };
        let result = verify_result(
            status_success,
            outputs.as_ref(),
            &file.input,
            job.options.original_policy,
            cancelled,
            stdout_tail,
            stderr_tail,
        );
        match result {
            Ok(output) => {
                succeeded += 1;
                let _ = tx.send(WorkerEvent::FileSucceeded {
                    input: file.input.clone(),
                    output,
                });
            }
            Err(error) => {
                failed += 1;
                let _ = tx.send(WorkerEvent::FileFailed {
                    input: file.input.clone(),
                    error: with_command(&error, &command_line),
                });
            }
        }
        let _ = fs::remove_file(&metadata_path);
    }
    let _ = tx.send(WorkerEvent::Finished {
        succeeded,
        failed,
        skipped,
        cancelled: cancel.load(Ordering::Acquire),
    });
}

fn with_command(error: &str, command_line: &str) -> String {
    format!("{error}\nCommand: {command_line}")
}

fn send_all_failed(job: &JobSpec, message: &str, tx: &Sender<WorkerEvent>) {
    for input in &job.files {
        let _ = tx.send(WorkerEvent::FileFailed {
            input: input.clone(),
            error: message.to_owned(),
        });
    }
}

/// Terminates a job that failed before any file started: every input reports a
/// failure and the summary counts the whole batch as failed.
fn abort_job(job: &JobSpec, message: &str, tx: &Sender<WorkerEvent>) {
    send_all_failed(job, message, tx);
    let _ = tx.send(WorkerEvent::Finished {
        succeeded: 0,
        failed: job.files.len(),
        skipped: 0,
        cancelled: false,
    });
}

const MAX_DIAGNOSTIC_BYTES: usize = 16 * 1024;

fn spawn_output_reader<R: Read + Send + 'static>(
    mut reader: R,
    tx: Sender<WorkerEvent>,
) -> thread::JoinHandle<String> {
    thread::spawn(move || {
        const READ_CHUNK_BYTES: usize = 4 * 1024;
        let mut buffer = [0_u8; READ_CHUNK_BYTES];
        let mut tail = Vec::with_capacity(MAX_DIAGNOSTIC_BYTES);
        loop {
            let read = match reader.read(&mut buffer) {
                Ok(0) => break,
                Ok(read) => read,
                Err(error) => {
                    let _ = tx.try_send(WorkerEvent::Log(format!(
                        "failed to read rimage output: {error}"
                    )));
                    break;
                }
            };
            let chunk = &buffer[..read];
            let _ = tx.try_send(WorkerEvent::Log(
                String::from_utf8_lossy(chunk).into_owned(),
            ));
            if read >= MAX_DIAGNOSTIC_BYTES {
                tail.clear();
                tail.extend_from_slice(&chunk[read - MAX_DIAGNOSTIC_BYTES..]);
            } else {
                let excess = tail
                    .len()
                    .saturating_add(read)
                    .saturating_sub(MAX_DIAGNOSTIC_BYTES);
                if excess > 0 {
                    tail.drain(..excess);
                }
                tail.extend_from_slice(chunk);
            }
        }
        String::from_utf8_lossy(&tail).into_owned()
    })
}

fn diagnostic_failure(base: &str, stdout: &str, stderr: &str) -> String {
    let diagnostic = if stderr.trim().is_empty() {
        stdout.trim()
    } else {
        stderr.trim()
    };
    if diagnostic.is_empty() {
        base.to_owned()
    } else {
        format!("{base}; diagnostic: {diagnostic}")
    }
}

fn verify_result(
    status_success: bool,
    outputs: Option<&std::collections::HashMap<String, PathBuf>>,
    input: &Path,
    policy: OriginalPolicy,
    cancelled: bool,
    stdout_tail: &str,
    stderr_tail: &str,
) -> Result<PathBuf, String> {
    use crate::metadata::path_key;
    if cancelled {
        return Err("conversion was cancelled".into());
    }
    if !status_success {
        return Err("rimage exited with a failure".into());
    }
    let Some(outputs) = outputs else {
        return Err(diagnostic_failure(
            "rimage did not produce usable metadata",
            stdout_tail,
            stderr_tail,
        ));
    };
    let Some(output) = outputs.get(&path_key(input)) else {
        return Err(diagnostic_failure(
            "metadata does not contain the current input",
            stdout_tail,
            stderr_tail,
        ));
    };
    if !output.is_file() || !fs::metadata(output).is_ok_and(|metadata| metadata.len() > 0) {
        return Err("metadata output is missing or empty".into());
    }
    if policy == OriginalPolicy::DeleteAfterVerifiedSuccess {
        if !safe_to_delete(input, output, false) {
            return Err("refused to delete an input equal to the output".into());
        }
        fs::remove_file(input)
            .map_err(|error| format!("output created but input deletion failed: {error}"))?;
    }
    Ok(output.clone())
}

#[cfg(test)]
mod tests {
    use std::io::Cursor;

    use crossbeam_channel::bounded;

    use super::{MAX_DIAGNOSTIC_BYTES, WorkerEvent, diagnostic_failure, spawn_output_reader};

    #[test]
    fn output_reader_bounds_an_unbroken_stream() {
        let input = vec![b'x'; MAX_DIAGNOSTIC_BYTES * 8];
        let (tx, rx) = bounded(256);
        let tail = spawn_output_reader(Cursor::new(input), tx).join().unwrap();
        assert_eq!(tail.len(), MAX_DIAGNOSTIC_BYTES);
        assert!(
            rx.try_iter()
                .all(|event| { matches!(event, WorkerEvent::Log(line) if line.len() <= 4 * 1024) })
        );
    }

    #[test]
    fn metadata_failure_prefers_bounded_stderr_diagnostic() {
        assert_eq!(
            diagnostic_failure(
                "rimage did not produce usable metadata",
                "stdout detail",
                "decoder failed"
            ),
            "rimage did not produce usable metadata; diagnostic: decoder failed"
        );
        assert_eq!(
            diagnostic_failure(
                "rimage did not produce usable metadata",
                "stdout detail",
                ""
            ),
            "rimage did not produce usable metadata; diagnostic: stdout detail"
        );
    }
}
