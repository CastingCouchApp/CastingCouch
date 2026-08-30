use std::path::Path;
use tracing_subscriber::EnvFilter;

pub fn init_logging(log_dir: &Path) -> Result<(), std::io::Error> {
    std::fs::create_dir_all(log_dir)?;
    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info"));
    tracing_subscriber::fmt()
        .with_env_filter(filter)
        .json()
        .with_writer(std::io::stderr)
        .try_init()
        .ok();
    Ok(())
}
