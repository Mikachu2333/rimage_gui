#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

use eframe::egui;
use rimage_gui::{
    app::{RimageApp, WINDOW_INNER_SIZE, WINDOW_MIN_INNER_SIZE},
    i18n::{Language, Text, tr},
};

fn main() -> eframe::Result {
    // Refresh the embedded backend before the window appears: whenever the
    // cached rimage no longer matches the bundled bytes it is re-extracted.
    // A failure here is non-fatal; the worker retries at job time and shows
    // the error in the UI.
    let _ = rimage_gui::backend::extract_backend();

    let mut viewport = egui::ViewportBuilder::default()
        .with_inner_size(WINDOW_INNER_SIZE)
        // Keep the floor useful but compact. The app performs a monitor-aware
        // clamp again on the first frame.
        .with_min_inner_size(WINDOW_MIN_INNER_SIZE)
        .with_clamp_size_to_monitor_size(true);
    if let Some(icon) = load_icon() {
        viewport = viewport.with_icon(icon);
    }
    let options = eframe::NativeOptions {
        viewport,
        ..Default::default()
    };
    eframe::run_native(
        tr(Language::System, Text::AppTitle),
        options,
        Box::new(|creation_context| {
            install_microsoft_yahei_ui(&creation_context.egui_ctx);
            configure_ui_style(&creation_context.egui_ctx);
            Ok(Box::<RimageApp>::default())
        }),
    )
}

fn install_microsoft_yahei_ui(context: &egui::Context) {
    // msyh.ttc is the Microsoft YaHei UI font collection shipped with
    // supported Windows versions. Keep egui's bundled fonts only as a final
    // glyph fallback, while making YaHei UI the primary face everywhere.
    let candidates = [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyh.ttf"];
    let Some(bytes) = candidates.iter().find_map(|path| std::fs::read(path).ok()) else {
        return;
    };
    let mut fonts = egui::FontDefinitions::default();
    fonts.font_data.insert(
        "microsoft-yahei-ui".into(),
        std::sync::Arc::new(egui::FontData::from_owned(bytes)),
    );
    for family in [egui::FontFamily::Proportional, egui::FontFamily::Monospace] {
        fonts
            .families
            .entry(family)
            .or_default()
            .insert(0, "microsoft-yahei-ui".into());
    }
    context.set_fonts(fonts);
}

fn configure_ui_style(context: &egui::Context) {
    use egui::{FontFamily::Proportional, FontId, TextStyle, ThemePreference};

    // Native egui receives Windows ThemeChanged events and updates this
    // preference automatically; no in-app theme switch is exposed.
    context.set_theme(ThemePreference::System);
    context.all_styles_mut(|style| {
        style
            .text_styles
            .insert(TextStyle::Heading, FontId::new(19.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Body, FontId::new(12.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Button, FontId::new(12.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Small, FontId::new(9.0, Proportional));
        style
            .text_styles
            .insert(TextStyle::Monospace, FontId::new(11.0, Proportional));
        style.spacing.item_spacing = egui::vec2(10.0, 8.0);
        style.spacing.button_padding = egui::vec2(12.0, 7.0);
        style.spacing.interact_size.y = 34.0;
        style.spacing.combo_width = 180.0;
    });
}

fn load_icon() -> Option<egui::IconData> {
    let (width, height) = (256, 256);
    let image = include_bytes!("../icon_raw");
    Some(egui::IconData {
        rgba: image.to_vec(),
        width,
        height,
    })
}
