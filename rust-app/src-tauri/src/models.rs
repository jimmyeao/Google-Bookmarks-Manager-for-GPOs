use serde::{Deserialize, Serialize};

fn default_profile_name() -> String {
    "Default Profile".to_string()
}

fn default_folder_name() -> String {
    "Managed Bookmarks".to_string()
}

/// Bookmark item matching the GPO/Intune deployment format.
/// Mirrors the C# BookmarkItem model for file-format compatibility.
#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(default)]
pub struct BookmarkItem {
    #[serde(rename = "toplevel_name", skip_serializing_if = "Option::is_none")]
    pub toplevel_name: Option<String>,

    #[serde(rename = "name", skip_serializing_if = "Option::is_none")]
    pub name: Option<String>,

    #[serde(rename = "url", skip_serializing_if = "Option::is_none")]
    pub url: Option<String>,

    #[serde(rename = "toplevel", skip_serializing_if = "Option::is_none")]
    pub toplevel: Option<bool>,

    #[serde(rename = "children", skip_serializing_if = "Option::is_none")]
    pub children: Option<Vec<BookmarkItem>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BookmarkProfile {
    #[serde(rename = "name", default = "default_profile_name")]
    pub name: String,

    #[serde(rename = "topLevelFolderName", default = "default_folder_name")]
    pub top_level_folder_name: String,

    #[serde(rename = "bookmarks", default)]
    pub bookmarks: Vec<BookmarkItem>,

    // Kept as a string for tolerance: the C# app writes DateTime in several ISO variants.
    #[serde(rename = "lastModified", default)]
    pub last_modified: String,

    #[serde(rename = "id", default)]
    pub id: String,
}

impl Default for BookmarkProfile {
    fn default() -> Self {
        Self {
            name: default_profile_name(),
            top_level_folder_name: default_folder_name(),
            bookmarks: Vec::new(),
            last_modified: String::new(),
            id: uuid::Uuid::new_v4().to_string(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct AppSettings {
    #[serde(rename = "darkMode", default)]
    pub dark_mode: bool,

    #[serde(rename = "profilesFilePath", default)]
    pub profiles_file_path: Option<String>,
}

/// Result of parsing pasted JSON or PLIST content.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ImportResult {
    pub items: Vec<BookmarkItem>,
    #[serde(rename = "topLevelName")]
    pub top_level_name: Option<String>,
}
