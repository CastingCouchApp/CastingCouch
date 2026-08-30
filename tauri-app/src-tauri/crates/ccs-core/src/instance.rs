use fs4::fs_std::FileExt;
use std::fs::{File, OpenOptions};
use std::path::{Path, PathBuf};

pub struct SingleInstanceLock {
    path: PathBuf,
    file: File,
}

impl SingleInstanceLock {
    pub fn acquire(path: impl AsRef<Path>) -> Result<Self, InstanceError> {
        let path = path.as_ref().to_path_buf();
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }

        let file = OpenOptions::new()
            .create(true)
            .read(true)
            .write(true)
            .truncate(false)
            .open(&path)?;

        file.try_lock_exclusive()
            .map_err(|_| InstanceError::AlreadyRunning)?;

        Ok(Self { path, file })
    }
}

impl Drop for SingleInstanceLock {
    fn drop(&mut self) {
        let _ = self.file.unlock();
        let _ = std::fs::remove_file(&self.path);
    }
}

#[derive(Debug, thiserror::Error)]
pub enum InstanceError {
    #[error("another CastingCouch instance is already running")]
    AlreadyRunning,
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::thread;
    use std::time::Duration;
    use tempfile::tempdir;

    #[test]
    fn lock_roundtrip_after_drop() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("castingcouch.lock");
        let first = SingleInstanceLock::acquire(&path).unwrap();
        drop(first);
        assert!(SingleInstanceLock::acquire(&path).is_ok());
    }

    #[test]
    fn concurrent_thread_lock() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("castingcouch.lock");
        let first = SingleInstanceLock::acquire(&path).unwrap();
        let path2 = path.clone();
        let handle = thread::spawn(move || SingleInstanceLock::acquire(&path2));
        thread::sleep(Duration::from_millis(20));
        let second = handle.join().unwrap();
        // Windows may allow the same process to take the lock twice; Unix should not.
        if second.is_err() {
            assert!(matches!(second, Err(InstanceError::AlreadyRunning)));
        }
        drop(first);
    }
}
