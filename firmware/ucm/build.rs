use std::path::Path;

fn main() {
    const CONFIG: &str = "src/config.rs";

    println!("cargo:rerun-if-changed={CONFIG}");
    if !Path::new(CONFIG).exists() {
        panic!(
            "\n\n{CONFIG} not found.\n\
             Copy src/config.example.rs to {CONFIG} and enter your Wi-Fi details, the API URL\n\
             and the device ID and secret.\n\n"
        );
    }

    // Passes ESP-IDF build settings (linker args, cfgs) from esp-idf-sys to this crate.
    embuild::espidf::sysenv::output();
}
