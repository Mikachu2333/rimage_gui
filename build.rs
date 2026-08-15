fn main() {
    #[cfg(windows)]
    {
        use winresource::VersionInfo;
        const VERSION_PRE: u16 = 0;

        println!("cargo:rerun-if-changed=icon.ico");
        println!("cargo:rerun-if-changed=res/rimage_x86.exe");
        println!("cargo:rerun-if-changed=res/rimage_x64.exe");

        let env_u64 =
            |name: &str| -> u64 { std::env::var(name).unwrap_or_default().parse().unwrap_or(0) };
        if std::env::var("CARGO_CFG_WINDOWS").is_ok() {
            let pack = |pre: u16| -> u64 {
                (env_u64("CARGO_PKG_VERSION_MAJOR") << 48)
                    | (env_u64("CARGO_PKG_VERSION_MINOR") << 32)
                    | (env_u64("CARGO_PKG_VERSION_PATCH") << 16)
                    | u64::from(pre)
            };
            let mut resource = winresource::WindowsResource::new();
            resource
                .set_icon("icon.ico")
                .set("ProductName", "Rimage GUI")
                .set("FileDescription", "Rimage image conversion GUI")
                .set_version_info(VersionInfo::FILEVERSION, pack(VERSION_PRE))
                .set_version_info(VersionInfo::PRODUCTVERSION, pack(VERSION_PRE))
                .set("LegalCopyright", "Copyright (c) 2026 Mikachu2333");
            resource
                .compile()
                .expect("failed to compile Windows resources");
        }
    }
}
