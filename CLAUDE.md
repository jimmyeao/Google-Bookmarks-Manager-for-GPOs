# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Google Bookmarks Manager for GPOs is a WPF desktop application for managing browser bookmarks (Chrome/Edge) that are deployed through Group Policy Objects (GPOs) or Intune. It allows administrators to edit, organize, and export bookmarks in various formats for enterprise deployment.

**Target Framework:** .NET 9.0 (Windows-only, WPF)

**Key Features:**
- Import bookmarks from clipboard (JSON or PLIST XML formats)
- Edit, save, and convert between formats
- Support for Chrome, Edge on Windows and macOS
- Multiple bookmark profiles (e.g., Company Bookmarks, Engineering Bookmarks, Sales Bookmarks)
- TreeView-based bookmark hierarchy editor with drag-and-drop
- Persistent storage between application runs
- Dark/Light theme support

**Project Goals:**
- Allow users to import bookmarks from clipboard
- Edit and organize bookmarks in a visual tree structure
- Maintain multiple named profiles with different bookmark sets
- Convert between Windows JSON and macOS PLIST formats
- Export to clipboard for easy deployment via GPO/Intune

## Build and Development Commands

### Build
```bash
dotnet build "Google Bookmarks Manager for GPOs.csproj"
```

### Run
```bash
dotnet run --project "Google Bookmarks Manager for GPOs.csproj"
```

### Clean
```bash
dotnet clean "Google Bookmarks Manager for GPOs.csproj"
```

### Restore Dependencies
```bash
dotnet restore "Google Bookmarks Manager for GPOs.csproj"
```

## Architecture

### Application Structure

The application is transitioning from a monolithic WPF code-behind pattern to a more modular service-based architecture:

**Core Components:**
- **MainWindow.xaml.cs** - Primary UI controller (legacy, contains most bookmark management logic)
- **Models/** - Data model classes
  - `Bookmark.cs` - Legacy model with INotifyPropertyChanged for UI binding (uses Newtonsoft.Json)
  - `BookmarkItem.cs` - New model using System.Text.Json attributes for serialization
- **Services/** - Business logic and format conversion services
  - `JsonBookmarkService` - JSON serialization/deserialization using System.Text.Json
  - `PlistBookmarkService` - PLIST XML format handling
  - `BookmarkConverterService` - Cross-format conversion (JSON ↔ PLIST)
- **Manager Classes** (Legacy) - Browser-specific handlers:
  - `ChromeManager` - Chrome native format import/export
  - `EdgeManager` - Edge native format import/export
  - `MacExportManager` - macOS PLIST XML generation

### Data Models

**BookmarkItem (New - Models/BookmarkItem.cs):**
Modern model for service layer using System.Text.Json:
- `TopLevelName` - Root folder name (first entry only)
- `Name` - Bookmark or folder name
- `Url` - Bookmark URL (null for folders)
- `TopLevel` - Edge-specific flag for top-level items
- `Children` - List of nested BookmarkItem objects

**Bookmark (Legacy - Bookmark.cs):**
UI-bound model for TreeView display:
- `Name` - Display name
- `Url` - Bookmark URL (empty for folders)
- `IsFolder` - Indicates if this is a container
- `IsRootFolder` - Special flag for top-level folders
- `Children` - ObservableCollection for nested bookmarks
- `IsEditing` - UI state for inline editing

### Key Formats

**See Examples.md for complete format specifications** including:
- Microsoft Edge on Windows (ManagedFavorites JSON)
- Google Chrome on Windows (ManagedBookmarks JSON)
- Microsoft Edge on macOS (ManagedFavorites PLIST XML)
- Google Chrome on macOS (ManagedBookmarks PLIST XML)

**JSON Format (Windows - GPO/Intune deployment):**
```json
[
  { "toplevel_name": "Company Bookmarks" },
  { "name": "Bookmark", "url": "https://example.com" },
  { "name": "Folder", "children": [...] }
]
```

**Key Differences:**
- **Edge on Windows**: Uses `toplevel: true` flag for top-level bookmarks
- **Chrome on Windows**: No `toplevel` flag needed
- **Both macOS formats**: Use PLIST XML with `<key>` and `<dict>` structure

**Chrome/Edge Native Format (for local import):**
- Includes metadata: `date_added`, `guid`, `id`, `meta_info`
- Uses Windows epoch timestamp (microseconds since 1601-01-01)
- Requires MD5 checksum for integrity
- Not typically used for GPO deployment

### Services Layer

**JsonBookmarkService:**
- Async JSON file operations using System.Text.Json
- Handles `List<BookmarkItem>` serialization/deserialization
- Configured to ignore null values and write indented JSON

**PlistBookmarkService:**
- Generates macOS-compatible PLIST XML fragments
- Parses PLIST XML back to `List<BookmarkItem>`
- Supports nested children and recursive structures

**BookmarkConverterService:**
- `ConvertJsonToPlistAsync()` - Windows → macOS conversion
- `ConvertPlistToJsonAsync()` - macOS → Windows conversion
- `AutoConvertAsync()` - Detects format by file extension and converts automatically
- Supports both ManagedBookmarks (Chrome) and ManagedFavorites (Edge)

### Persistence

Bookmarks are automatically saved to `%AppData%\GoogleBookmarksManager\bookmarks.json` on application exit and loaded on startup. Theme preference is stored in `Properties\Settings.settings`.

**Profile Management (Planned Feature):**
The application will support multiple named profiles, allowing users to maintain separate bookmark collections:
- Each profile has a user-defined name (e.g., "Company Bookmarks", "Engineering Bookmarks", "Sales Bookmarks")
- Profiles are stored as separate files or within a single configuration file
- Users can switch between profiles, create new profiles, and delete existing ones
- Each profile maintains its own bookmark hierarchy and top-level folder name

### UI Patterns

- **TreeView** (`BookmarksTreeView`) - Main hierarchy display with context menus
- **Drag-and-Drop** - Implemented via `PreviewMouseMove`, `DragOver`, and `Drop` events
- **Inline Editing** - Double-click on bookmarks toggles `IsEditing` mode
- **Material Design** - Uses MaterialDesignThemes for modern UI styling

## Important Implementation Details

### Timestamp Generation
Chrome and Edge use Windows file time (100-nanosecond intervals since January 1, 1601 UTC):
```csharp
DateTime epochStart = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
long timestamp = (DateTime.UtcNow - epochStart).Ticks / 10;
```

### Checksum Calculation
Bookmarks files require MD5 checksum of JSON (without the checksum field itself). This is critical for Chrome/Edge to accept the file.

### Top-Level Folder Handling
The `toplevel_name` field is special - it defines the root folder name in GPO deployments and must be the first element in the JSON array. The UI stores this separately in `TopLevelFolderName` property.

### Search and Filtering
Search functionality (`SearchQuery` property) creates a filtered view while preserving the original collection in `_originalBookmarks`. Matching preserves folder hierarchy - folders are included if they contain matching children.

## Format Reference

See **Examples.md** for comprehensive format documentation including:
- Complete JSON and PLIST XML examples for Windows and macOS
- Browser-specific policy keys (ManagedBookmarks vs ManagedFavorites)
- Edge-specific `toplevel` flag usage
- Nested folder structures
- Official documentation links

This file is the single source of truth for bookmark format validation.

## Dependencies

Key NuGet packages:
- **MaterialDesignThemes/MaterialDesignColors** - UI framework
- **Newtonsoft.Json** - JSON parsing (legacy code)
- **System.Text.Json** - JSON parsing (new services)
- **plist-cil** - PLIST format support for macOS
- **Serilog** - Logging framework (logs to `logs/bookmark-import-log.txt`)

## Architecture Migration Notes

The codebase is currently in a **dual-architecture state**:

**Legacy (MainWindow.xaml.cs):**
- Uses `Bookmark` class with Newtonsoft.Json
- Direct UI manipulation and code-behind logic
- Tightly coupled TreeView binding
- Uses older manager classes (ChromeManager, EdgeManager, MacExportManager)

**New (Models + Services):**
- Uses `BookmarkItem` class with System.Text.Json
- Service-oriented design with async operations
- Decoupled business logic from UI
- Simplified format conversion

**Migration Strategy:**
1. Keep legacy code functional for existing UI
2. New features should use Services layer where possible
3. Add conversion utilities between `Bookmark` ↔ `BookmarkItem` as needed
4. Profile management should be built on new architecture
5. Eventually refactor MainWindow to use Services layer

## Common Tasks

### Using the Services Layer

**Converting Between Formats:**
```csharp
// JSON to PLIST
await BookmarkConverterService.ConvertJsonToPlistAsync(
    "bookmarks.json",
    "bookmarks.plist",
    "ManagedBookmarks" // or "ManagedFavorites" for Edge
);

// PLIST to JSON
await BookmarkConverterService.ConvertPlistToJsonAsync(
    "bookmarks.plist",
    "bookmarks.json"
);

// Auto-detect format
await BookmarkConverterService.AutoConvertAsync(
    "input.json",
    "output.plist",
    "ManagedBookmarks"
);
```

**Loading and Saving JSON:**
```csharp
// Load
var bookmarks = await JsonBookmarkService.LoadFromJsonAsync("bookmarks.json");

// Save
await JsonBookmarkService.SaveToJsonAsync("bookmarks.json", bookmarks);
```

**Working with PLIST:**
```csharp
// Save
PlistBookmarkService.SaveToPlist("output.plist", "ManagedBookmarks", bookmarks);

// Load
var bookmarks = PlistBookmarkService.LoadFromPlist("input.plist");
```

### Implementing Profile Management

**Profile Model Structure:**
```csharp
public class BookmarkProfile
{
    public string Name { get; set; }  // e.g., "Company Bookmarks"
    public string TopLevelFolderName { get; set; }
    public List<BookmarkItem> Bookmarks { get; set; }
    public DateTime LastModified { get; set; }
}
```

**Profile Storage Options:**
1. **Separate files:** `%AppData%\GoogleBookmarksManager\Profiles\{ProfileName}.json`
2. **Single file:** `%AppData%\GoogleBookmarksManager\profiles.json` with array of profiles
3. **Hybrid:** Separate folder with an index file

**Considerations:**
- Profile switching should preserve unsaved changes (prompt user)
- Default profile on first launch
- Profile selector in UI (ComboBox or dedicated menu)
- Import/Export profiles for sharing between machines

### Adding New Export Formats
1. Add format-specific method to appropriate service (`JsonBookmarkService` or `PlistBookmarkService`)
2. Update `BookmarkConverterService` with new conversion methods
3. Add export button handler in UI layer
4. Ensure proper validation per Examples.md specifications

### Modifying Data Models

**When changing BookmarkItem (new model):**
- Update System.Text.Json attributes (`[JsonPropertyName]`, `[XmlElement]`)
- Update service layer serialization/deserialization
- Add migration logic if changing structure
- Test with Examples.md sample data

**When changing Bookmark (legacy model):**
- Update `ParseBookmark()` for deserialization
- Update `ConvertBookmarkToOriginalFormat()` for serialization
- Update `DeepCopyBookmarks()` for the backup/filter mechanism
- Consider adding conversion methods between `Bookmark` ↔ `BookmarkItem`

### Working with TreeView
TreeView item generation is lazy - use `Dispatcher.InvokeAsync()` with `DispatcherPriority.Background` when programmatically selecting/expanding items to ensure containers are generated first.

### Clipboard Import/Export
When implementing clipboard operations:
- Detect format (JSON starts with `[` or `{`, PLIST starts with `<?xml` or `<plist>`)
- Parse using appropriate service
- Validate structure matches Examples.md specifications
- Handle errors gracefully with user-friendly messages
