use super::UpdateManifest;
use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine;
use rsa::pkcs1v15::{Signature, VerifyingKey};
use rsa::pkcs8::DecodePublicKey;
use rsa::signature::Verifier;
use rsa::RsaPublicKey;
use sha2::Sha256;

/// Same PEM as `src/CreatorControlSuite.App/Keys/update-public.pem`.
pub const UPDATE_PUBLIC_KEY_PEM: &str =
    include_str!("../../../../../../src/CreatorControlSuite.App/Keys/update-public.pem");

/// Canonical RSA-SHA256 payload, 1:1 with `UpdateManifestCanonical` / `New-UpdateArtifacts.ps1`.
pub fn canonical_payload(manifest: &UpdateManifest) -> String {
    let notes = manifest.release_notes.replace("\r\n", "\n");
    format!(
        "{}\n{}\n{}\n{}\n{}\n{}\n{}\n{}\n{}",
        manifest.product_id,
        manifest.version,
        manifest.channel,
        manifest.package_file_name,
        manifest.sha256,
        manifest.size,
        manifest.published_at,
        manifest.minimum_version,
        notes
    )
}

pub fn verify_manifest_signature(manifest: &UpdateManifest) -> bool {
    verify_manifest_signature_with_key(manifest, UPDATE_PUBLIC_KEY_PEM)
}

pub fn verify_manifest_signature_with_key(manifest: &UpdateManifest, public_pem: &str) -> bool {
    if manifest.signature.trim().is_empty() {
        return false;
    }
    let Ok(signature_bytes) = BASE64.decode(manifest.signature.trim()) else {
        return false;
    };
    let Ok(public_key) = RsaPublicKey::from_public_key_pem(public_pem.trim()) else {
        return false;
    };
    let Ok(signature) = Signature::try_from(signature_bytes.as_slice()) else {
        return false;
    };
    let verifying_key = VerifyingKey::<Sha256>::new(public_key);
    let payload = canonical_payload(manifest);
    verifying_key.verify(payload.as_bytes(), &signature).is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::updates::{UpdateManifest, PRODUCT_ID};
    use rsa::pkcs1v15::SigningKey;
    use rsa::pkcs8::EncodePublicKey;
    use rsa::signature::{SignatureEncoding, Signer};
    use rsa::RsaPrivateKey;

    fn sample(published: &str, notes: &str, signature: &str) -> UpdateManifest {
        UpdateManifest {
            product_id: PRODUCT_ID.into(),
            version: "8.0.0-alpha101".into(),
            channel: "Alpha".into(),
            package_file_name: "pkg.zip".into(),
            sha256: "DEADBEEF".into(),
            size: 100,
            published_at: published.into(),
            minimum_version: "0.0.0".into(),
            release_notes: notes.into(),
            signature: signature.into(),
        }
    }

    #[test]
    fn canonical_payload_matches_csharp_stable_form() {
        let manifest = sample(
            "2026-07-26T12:00:00.0000000Z",
            "line1\r\nline2",
            "sig",
        );
        assert_eq!(
            canonical_payload(&manifest),
            "CreatorControlSuite\n8.0.0-alpha101\nAlpha\npkg.zip\nDEADBEEF\n100\n2026-07-26T12:00:00.0000000Z\n0.0.0\nline1\nline2"
        );
    }

    #[test]
    fn embedded_public_key_is_spki_pem() {
        assert!(UPDATE_PUBLIC_KEY_PEM.contains("BEGIN PUBLIC KEY"));
        assert!(RsaPublicKey::from_public_key_pem(UPDATE_PUBLIC_KEY_PEM.trim()).is_ok());
    }

    #[test]
    fn signature_roundtrip_accepts_matching_payload() {
        let mut rng = rand::thread_rng();
        let private_key = RsaPrivateKey::new(&mut rng, 2048).expect("key");
        let public_pem = private_key
            .to_public_key()
            .to_public_key_pem(rsa::pkcs8::LineEnding::LF)
            .expect("pem");
        let signing_key = SigningKey::<Sha256>::new(private_key);
        let mut manifest = sample("2026-07-26T12:00:00.0000000Z", "Test notes", "");
        let signature = signing_key.sign(canonical_payload(&manifest).as_bytes());
        manifest.signature = BASE64.encode(signature.to_bytes());
        assert!(verify_manifest_signature_with_key(&manifest, &public_pem));
    }

    #[test]
    fn signature_roundtrip_rejects_tampered_version() {
        let mut rng = rand::thread_rng();
        let private_key = RsaPrivateKey::new(&mut rng, 2048).expect("key");
        let public_pem = private_key
            .to_public_key()
            .to_public_key_pem(rsa::pkcs8::LineEnding::LF)
            .expect("pem");
        let signing_key = SigningKey::<Sha256>::new(private_key);
        let mut manifest = sample("2026-07-26T12:00:00.0000000Z", "Test notes", "");
        let signature = signing_key.sign(canonical_payload(&manifest).as_bytes());
        manifest.signature = BASE64.encode(signature.to_bytes());
        manifest.version = "9.0.0".into();
        assert!(!verify_manifest_signature_with_key(&manifest, &public_pem));
    }

    #[test]
    fn empty_or_garbage_signature_is_rejected() {
        let manifest = sample("2026-07-26T12:00:00.0000000Z", "notes", "");
        assert!(!verify_manifest_signature(&manifest));
        let garbage = sample("2026-07-26T12:00:00.0000000Z", "notes", "dGVzdA==");
        assert!(!verify_manifest_signature(&garbage));
    }
}
