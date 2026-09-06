use fs4::fs_std::FileExt;
use std::fs::{File, OpenOptions};
use std::path::Path;

pub struct SingleInstanceLock {
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

        if !file.try_lock_exclusive()? {
            return Err(InstanceError::AlreadyRunning);
        }

        Ok(Self { file })
    }
}

impl Drop for SingleInstanceLock {
    fn drop(&mut self) {
        let _ = self.file.unlock();
        // Keep the file node: unlinking permits concurrent locks on different inodes.
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
    fn lock_file_is_not_unlinked_between_owners() {
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let first = SingleInstanceLock::acquire(&path).unwrap();
        drop(first);
        assert!(
            path.exists(),
            "unlinking a lock creates an inode race between owners"
        );
    }
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
        assert!(matches!(second, Err(InstanceError::AlreadyRunning)));
        drop(first);
    }
    #[test]
    fn rejects_a_second_process() {
        const CHILD_PATH: &str = "CCS_INSTANCE_CONTRACT_PATH";
        if let Some(path) = std::env::var_os(CHILD_PATH) {
            assert!(matches!(
                SingleInstanceLock::acquire(path),
                Err(InstanceError::AlreadyRunning)
            ));
            return;
        }
        let dir = tempdir().unwrap();
        let path = dir.path().join("lock");
        let _first = SingleInstanceLock::acquire(&path).unwrap();
        let status = std::process::Command::new(std::env::current_exe().unwrap())
            .args(["--exact", "instance::tests::rejects_a_second_process"])
            .env(CHILD_PATH, &path)
            .status()
            .unwrap();
        assert!(status.success());
    }
}
