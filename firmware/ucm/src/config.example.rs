//! Device configuration template.
//!
//! Copy this file to `src/config.rs` and fill in your details. `config.rs` is listed in
//! `.gitignore` so your password and device secret are never committed. These values are
//! compiled in for milestone 1; provisioning into NVS replaces them later.

/// Wi-Fi network name. The ESP32-C6 only supports 2.4 GHz networks.
pub const WIFI_SSID: &str = "your-network-name";
/// Wi-Fi password. For an open network, leave it empty.
pub const WIFI_PASSWORD: &str = "your-password";

/// Where the device API runs, without a trailing slash: the PC running `func start` on the
/// bench (`http://192.168.1.50:7071`) or the Function App (`https://<app>.azurewebsites.net`).
pub const API_BASE_URL: &str = "http://192.168.1.50:7071";

/// This device's ID, as registered in the `Devices` table.
pub const DEVICE_ID: &str = "WH001";
/// This device's 32-byte secret, Base64 (printed by `infra/provision-device.ps1`).
/// The placeholder is all zeros, which the server will reject.
pub const DEVICE_SECRET_B64: &str = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
