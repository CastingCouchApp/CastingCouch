//! Optional .NET sidecar client for modules not yet in Rust.

#[derive(Debug, Clone)]
pub struct SidecarConfig {
    pub base_url: String,
}

impl Default for SidecarConfig {
    fn default() -> Self {
        Self {
            base_url: "http://127.0.0.1:18765".into(),
        }
    }
}

impl SidecarConfig {
    pub fn health_url(&self) -> String {
        format!("{}/sidecar/health", self.base_url.trim_end_matches('/'))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn health_url() {
        assert_eq!(
            SidecarConfig::default().health_url(),
            "http://127.0.0.1:18765/sidecar/health"
        );
    }
}
