// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod models;
mod plist_service;
mod store;

use models::{AppSettings, BookmarkItem, BookmarkProfile, ImportResult};
use plist_service::{extract_top_level_name, load_from_plist_string, save_to_plist_string};
use serde::Serialize;
use std::path::PathBuf;
use std::sync::Mutex;

struct AppState {
    settings: Mutex<AppSettings>,
}

#[derive(Serialize)]
struct ProfilesPayload {
    profiles: Vec<BookmarkProfile>,
    path: String,
}

#[tauri::command]
fn get_settings(state: tauri::State<AppState>) -> AppSettings {
    state.settings.lock().unwrap().clone()
}

#[tauri::command]
fn save_settings(state: tauri::State<AppState>, settings: AppSettings) -> Result<(), String> {
    store::save_settings(&settings)?;
    *state.settings.lock().unwrap() = settings;
    Ok(())
}

#[tauri::command]
fn load_profiles(state: tauri::State<AppState>) -> Result<ProfilesPayload, String> {
    let settings = state.settings.lock().unwrap().clone();
    let path = store::profiles_file_path(&settings)?;
    let profiles = store::load_profiles(&path)?;
    Ok(ProfilesPayload {
        profiles,
        path: path.to_string_lossy().into_owned(),
    })
}

#[tauri::command]
fn save_profiles(
    state: tauri::State<AppState>,
    profiles: Vec<BookmarkProfile>,
) -> Result<(), String> {
    let settings = state.settings.lock().unwrap().clone();
    let path = store::profiles_file_path(&settings)?;
    store::save_profiles(&path, &profiles)
}

#[tauri::command]
fn save_profiles_to(path: String, profiles: Vec<BookmarkProfile>) -> Result<(), String> {
    store::save_profiles(&PathBuf::from(&path), &profiles)
}

#[tauri::command]
fn load_profiles_from(path: String) -> Result<Vec<BookmarkProfile>, String> {
    store::load_profiles(&PathBuf::from(&path))
}

#[tauri::command]
fn register_save_location(state: tauri::State<AppState>, path: String) -> Result<(), String> {
    store::write_pointer_file(&path)?;
    let mut settings = state.settings.lock().unwrap();
    settings.profiles_file_path = Some(path);
    let settings_copy = settings.clone();
    drop(settings);
    store::save_settings(&settings_copy)
}

#[tauri::command]
fn default_save_location() -> Result<String, String> {
    let folder = store::app_data_folder()?;
    Ok(folder
        .join("profiles.json")
        .to_string_lossy()
        .into_owned())
}

#[tauri::command]
fn get_log_path() -> Result<String, String> {
    let folder = store::app_data_folder()?;
    Ok(folder
        .join("logs")
        .join("bookmark-import-log.txt")
        .to_string_lossy()
        .into_owned())
}

#[tauri::command]
fn log_message(level: String, message: String) -> Result<(), String> {
    let folder = store::app_data_folder()?;
    let logs = folder.join("logs");
    if !logs.exists() {
        std::fs::create_dir_all(&logs).map_err(|e| e.to_string())?;
    }
    let line = format!(
        "{} [{}] {}\n",
        chrono::Local::now().format("%Y-%m-%d %H:%M:%S"),
        level,
        message
    );
    use std::io::Write;
    let mut file = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(logs.join("bookmark-import-log.txt"))
        .map_err(|e| e.to_string())?;
    file.write_all(line.as_bytes()).map_err(|e| e.to_string())
}

/// Serializes bookmark items to JSON in the exact GPO/Intune format
/// (indented, null fields omitted). If `edge` is true, root-level non-folder
/// items get `"toplevel": true` (Edge on Windows requirement).
#[tauri::command]
fn export_json(mut items: Vec<BookmarkItem>, edge: bool) -> Result<String, String> {
    if edge {
        // Skip the toplevel_name metadata entry only if it's actually first —
        // it's absent when the user left the folder name empty.
        let skip = if items
            .first()
            .map(|i| i.toplevel_name.is_some())
            .unwrap_or(false)
        {
            1
        } else {
            0
        };
        for item in items.iter_mut().skip(skip) {
            let is_folder = item.children.as_ref().map(|c| !c.is_empty()).unwrap_or(false);
            if !is_folder && item.toplevel_name.is_none() {
                item.toplevel = Some(true);
            }
        }
    }
    serde_json::to_string_pretty(&items).map_err(|e| e.to_string())
}

/// Serializes bookmark items to a macOS plist XML fragment.
#[tauri::command]
fn export_plist(items: Vec<BookmarkItem>, key_name: String) -> String {
    save_to_plist_string(&key_name, &items)
}

/// Parses pasted content, auto-detecting JSON vs plist XML.
#[tauri::command]
fn parse_import(content: String) -> Result<ImportResult, String> {
    let trimmed = content.trim();

    let items: Vec<BookmarkItem> = if trimmed.starts_with("<?xml")
        || trimmed.contains("<plist>")
        || trimmed.contains("<key>")
    {
        load_from_plist_string(trimmed)?
    } else if trimmed.starts_with('[') || trimmed.starts_with('{') {
        serde_json::from_str(trimmed).map_err(|e| format!("Invalid JSON: {}", e))?
    } else {
        return Err("Unrecognized format. Please paste valid JSON or PLIST XML.".to_string());
    };

    let top_level_name = extract_top_level_name(&items);
    Ok(ImportResult {
        items,
        top_level_name,
    })
}

/// Writes text to the system clipboard.
#[tauri::command]
fn set_clipboard(text: String) -> Result<(), String> {
    let mut clipboard = arboard::Clipboard::new().map_err(|e| format!("Clipboard error: {}", e))?;
    clipboard
        .set_text(text)
        .map_err(|e| format!("Clipboard error: {}", e))
}

/// Reads text from the system clipboard.
#[tauri::command]
fn get_clipboard() -> Result<String, String> {
    let mut clipboard = arboard::Clipboard::new().map_err(|e| format!("Clipboard error: {}", e))?;
    clipboard
        .get_text()
        .map_err(|e| format!("Clipboard error: {}", e))
}

/// Shows a native save-file dialog and returns the chosen path.
#[tauri::command]
fn pick_save_location(default_name: String) -> Option<String> {
    rfd::FileDialog::new()
        .set_file_name(&default_name)
        .add_filter("JSON files", &["json"])
        .save_file()
        .map(|p| p.to_string_lossy().into_owned())
}

/// Shows a native open-file dialog for profiles.
#[tauri::command]
fn pick_profile_file() -> Option<String> {
    rfd::FileDialog::new()
        .add_filter("JSON files", &["json"])
        .pick_file()
        .map(|p| p.to_string_lossy().into_owned())
}

fn main() {
    let settings = store::load_settings();
    tauri::Builder::default()
        .manage(AppState {
            settings: Mutex::new(settings),
        })
        .invoke_handler(tauri::generate_handler![
            get_settings,
            save_settings,
            load_profiles,
            save_profiles,
            save_profiles_to,
            load_profiles_from,
            register_save_location,
            default_save_location,
            get_log_path,
            log_message,
            export_json,
            export_plist,
            parse_import,
            set_clipboard,
            get_clipboard,
            pick_save_location,
            pick_profile_file,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
