//! Request signing for the device API (`contracts/device-v1/README.md`, "Signature").

use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine;
use hmac::{Hmac, Mac};
use sha2::{Digest, Sha256};

/// Computes the `X-Signature` header value:
/// `Base64(HMAC-SHA256(secret, METHOD \n path \n timestamp \n hex(SHA256(body))))`.
///
/// `path` is the URL path only (`/device/v1/records`) and `body` the exact bytes sent.
pub fn sign_request(
    secret: &[u8],
    method: &str,
    path: &str,
    timestamp: &str,
    body: &[u8],
) -> String {
    let body_hash: String = Sha256::digest(body)
        .iter()
        .map(|b| format!("{b:02x}"))
        .collect();
    let canonical = format!("{method}\n{path}\n{timestamp}\n{body_hash}");

    let mut mac = Hmac::<Sha256>::new_from_slice(secret).expect("HMAC accepts any key length");
    mac.update(canonical.as_bytes());
    BASE64.encode(mac.finalize().into_bytes())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn signature_is_base64_of_32_bytes() {
        let sig = sign_request(&[0u8; 32], "POST", "/device/v1/records", "x", b"{}");
        // 32 bytes of HMAC-SHA256 encode to 44 Base64 characters with one '=' of padding.
        assert_eq!(sig.len(), 44);
        assert!(sig.ends_with('='));
    }

    #[test]
    fn signature_covers_every_part() {
        let base = sign_request(b"secret", "POST", "/p", "t", b"body");
        assert_ne!(base, sign_request(b"Secret", "POST", "/p", "t", b"body"));
        assert_ne!(base, sign_request(b"secret", "GET", "/p", "t", b"body"));
        assert_ne!(base, sign_request(b"secret", "POST", "/q", "t", b"body"));
        assert_ne!(base, sign_request(b"secret", "POST", "/p", "u", b"body"));
        assert_ne!(base, sign_request(b"secret", "POST", "/p", "t", b"Body"));
    }
}
