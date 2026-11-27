use crate::models::{AppSettings, BookmarkProfile};
use std::fs;
use std::path::PathBuf;

/// Resolves the app data folder, creating it if necessary.
/// Windows: %AppData%/GoogleBookmarksManager
/// macOS:   ~/Library/Application Support/GoogleBookmarksManager
/// Linux:   ~/.config/GoogleBookmarksManager (approx.)
pub fn app_data_folder() -> Result<PathBuf, String> {
    let base = dirs_config_folder().ok_or("Could not determine config folder")?;
    let folder = base.join("GoogleBookmarksManager");
    if !folder.exists() {
        fs::create_dir_all(&folder).map_err(|e| format!("Failed to create app data folder: {}", e))?;
    }
    Ok(folder)
}

fn dirs_config_folder() -> Option<PathBuf> {
    // Implement without extra dependency: use env vars per platform
    #[cfg(target_os = "windows")]
    {
        std::env::var("APPDATA").ok().map(PathBuf::from)
    }
    #[cfg(target_os = "macos")]
    {
        std::env::var("HOME")
            .ok()
            .map(|h| PathBuf::from(h).join("Library").join("Application Support"))
    }
    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    {
        std::env::var("XDG_CONFIG_HOME")
            .ok()
            .map(PathBuf::from)
            .or_else(|| std::env::var("HOME").ok().map(|h| PathBuf::from(h).join(".config")))
    }
}

fn settings_path() -> Result<PathBuf, String> {
    Ok(app_data_folder()?.join("settings.json"))
}

pub fn load_settings() -> AppSettings {
    let path = match settings_path() {
        Ok(p) => p,
        Err(_) => return AppSettings::default(),
    };
    if let Ok(json) = fs::read_to_string(&path) {
        serde_json::from_str(&json).unwrap_or_default()
    } else {
        AppSettings::default()
    }
}

pub fn save_settings(settings: &AppSettings) -> Result<(), String> {
    let path = settings_path()?;
    let json = serde_json::to_string_pretty(settings).map_err(|e| e.to_string())?;
    fs::write(&path, json).map_err(|e| format!("Failed to write settings: {}", e))
}

/// Resolves the profiles file path: user-configured path if set (and recorded
/// in the pointer file), otherwise the default in the app data folder.
pub fn profiles_file_path(settings: &AppSettings) -> Result<PathBuf, String> {
    if let Some(custom) = &settings.profiles_file_path {
        if !custom.is_empty() {
            return Ok(PathBuf::from(custom));
        }
    }
    // Fall back to pointer file (written when user picks a save location)
    let pointer = app_data_folder()?.join("profiles.path");
    if let Ok(custom) = fs::read_to_string(&pointer) {
        let custom = custom.trim();
        if !custom.is_empty() {
            return Ok(PathBuf::from(custom));
        }
    }
    Ok(app_data_folder()?.join("profiles.json"))
}

pub fn load_profiles(path: &PathBuf) -> Result<Vec<BookmarkProfile>, String> {
    if !path.exists() {
        return Ok(vec![BookmarkProfile::default()]);
    }
    let json = fs::read_to_string(path)
        .map_err(|e| format!("Failed to read profiles file: {}", e))?;
    let profiles: Vec<BookmarkProfile> = serde_json::from_str(&json)
        .map_err(|e| format!("Failed to parse profiles file: {}", e))?;
    if profiles.is_empty() {
        Ok(vec![BookmarkProfile::default()])
    } else {
        Ok(profiles)
    }
}

pub fn save_profiles(path: &PathBuf, profiles: &[BookmarkProfile]) -> Result<(), String> {
    if let Some(dir) = path.parent() {
        if !dir.exists() {
            fs::create_dir_all(dir).map_err(|e| format!("Failed to create directory: {}", e))?;
        }
    }
    let now = chrono::Local::now().to_rfc3339();
    let mut stamped = profiles.to_vec();
    for p in stamped.iter_mut() {
        p.last_modified = now.clone();
    }
    let json = serde_json::to_string_pretty(&stamped).map_err(|e| e.to_string())?;
    fs::write(path, json).map_err(|e| format!("Failed to write profiles: {}", e))?;
    Ok(())
}

/// Writes the pointer file recording the user's custom save location.
pub fn write_pointer_file(path: &str) -> Result<(), String> {
    let pointer = app_data_folder()?.join("profiles.path");
    fs::write(&pointer, path).map_err(|e| format!("Failed to write pointer file: {}", e))
}
