fn main() {
    println!("cargo:rerun-if-changed=icon.ico");
    println!("cargo:rerun-if-changed=res/rimage_x86.exe");
    println!("cargo:rerun-if-changed=res/rimage_x64.exe");

    if std::env::var("CARGO_CFG_WINDOWS").is_ok() {
        let version = std::env::var("CARGO_PKG_VERSION").expect("package version is set by Cargo");
        let mut resource = winresource::WindowsResource::new();
        resource
            .set_icon("icon.ico")
            .set("ProductName", "Rimage GUI")
            .set("FileDescription", "Rimage image conversion GUI")
            .set("FileVersion", &version)
            .set("ProductVersion", &version)
            .set("LegalCopyright", "Copyright (c) 2026 Mikachu2333");
        resource
            .compile()
            .expect("failed to compile Windows resources");
    }
}
