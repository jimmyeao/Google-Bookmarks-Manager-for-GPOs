![CodeQL](https://github.com/jimmyeao/Google-Bookmarks-Manager-for-GPOs/actions/workflows/codeql.yml/badge.svg)
[![License](https://img.shields.io/badge/License-MIT-blue)](#license)

# Google Bookmarks Manager for GPOs
<img width="1195" height="723" alt="image" src="https://github.com/user-attachments/assets/7d87b122-0cd2-49c4-b61e-901964b08250" />

A simple, fast WPF app to edit and manage browser bookmarks for deployment via Intune or Group Policy (GPO) for Microsoft Edge and Google Chrome.

Features
- Profiles: create, rename, copy, delete and switch profiles. Profiles auto-save to a shared `profiles.json`.
- Top-level folder per profile: set the folder that policies use (e.g., Managed Bookmarks).
- Search: instant filter by name or URL without altering originals; edits during search update the underlying tree.
- Inline actions: right-justified + (add bookmark to a folder) and − (delete) shown on hover or selection.
- Drag and drop: reorder items or drop URLs directly from your browser/desktop.
- Import: paste JSON or PLIST for Edge (`ManagedFavorites`) or Chrome (`ManagedBookmarks`). Importer sanitizes common XML issues.
- Export:
  - Windows (Intune/GPO) JSON for Edge or Chrome
  - macOS (Intune) PLIST for Edge or Chrome
- Safety prompts: confirmation on Clear All and delete.
- Dark mode toggle.
- Built on .NET 9 + MaterialDesignInXaml.

Usage
1) Build and run with .NET 9 SDK.
2) Choose or create a Profile and set the Top-Level Folder Name.
3) Import existing policy data (JSON or PLIST) using Import Bookmarks.
4) Edit: use +/− actions, drag to reorder, or right-click folders for nested actions. Search to filter quickly.
5) Export: choose Edge/Chrome (JSON) for Windows or Edge/Chrome (PLIST) for macOS. The result is copied to the clipboard ready for Intune/GPO.

Notes
- Search rebuilds a filtered view; 
- PLIST export escapes special characters; PLIST import auto-fixes bare ampersands in string values.
- You can change the save location of `profiles.json` to share with a team.

License
MIT


