# 📚 Bookmark Configuration Reference
This document describes the expected structures for **Microsoft Edge** and **Google Chrome** managed bookmark/favorite configurations  
on **Windows** and **macOS** platforms.

---

## 🪟 Microsoft Edge — Windows

**Policy Key:** `ManagedFavorites`  
**Format:** JSON Array

### Structure

```json
[
  {
    "toplevel_name": "<folder name>"
  },
  {
    "name": "<display name>",
    "url": "<https URL>",
    "toplevel": true
  },
  {
    "name": "<folder name>",
    "children": [
      {
        "name": "<sub-link>",
        "url": "<https URL>"
      }
    ]
  }
]
```

### Example

```json
[
  {
    "toplevel_name": "Managed favorites"
  },
  {
    "name": "Stack Overflow",
    "url": "https://stackoverflow.com",
    "toplevel": true
  },
  {
    "name": "Microsoft",
    "children": [
      {
        "name": "Microsoft Learn",
        "url": "https://learn.microsoft.com"
      },
      {
        "name": "Microsoft Support",
        "url": "https://support.microsoft.com"
      }
    ]
  }
]
```

### Notes
- The `toplevel_name` defines the root folder.
- Use `"toplevel": true` for items you want shown at the top level of that folder.
- This format is compatible with MDM/Intune JSON configuration for Edge.

---

## 🪟 Google Chrome — Windows

**Policy Key:** `ManagedBookmarks`  
**Format:** JSON Array

### Structure

```json
[
  {
    "toplevel_name": "<folder name>"
  },
  {
    "name": "<display name>",
    "url": "<https URL>"
  },
  {
    "name": "<folder name>",
    "children": [
      {
        "name": "<sub-link>",
        "url": "<https URL>"
      }
    ]
  }
]
```

### Example

```json
[
  {
    "toplevel_name": "Company Resources"
  },
  {
    "url": "https://intranet.example.com",
    "name": "Intranet"
  },
  {
    "name": "Developer Docs",
    "children": [
      {
        "url": "https://api.example.com",
        "name": "API Reference"
      },
      {
        "url": "https://docs.example.com",
        "name": "Internal Docs"
      }
    ]
  }
]
```

### Notes
- Chrome does **not** use `toplevel` or `ShowOnBookmarkBar`.
- The first entry’s `toplevel_name` defines the root managed folder.
- Follows the [Chrome Enterprise ManagedBookmarks policy](https://chromeenterprise.google/policies/?policy=ManagedBookmarks).

---

## 🍏 Microsoft Edge — macOS

**Policy Key:** `ManagedFavorites`  
**Format:** Apple plist XML fragment (inside a configuration profile)

### Structure

```xml
<key>ManagedFavorites</key>
<array>
  <dict>
    <key>toplevel_name</key>
    <string>My managed favorites folder</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Example Link</string>
    <key>url</key>
    <string>https://example.com</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Example Folder</string>
    <key>children</key>
    <array>
      <dict>
        <key>name</key>
        <string>Sub Link</string>
        <key>url</key>
        <string>https://sub.example.com</string>
      </dict>
    </array>
  </dict>
</array>
```

### Example

```xml
<key>ManagedFavorites</key>
<array>
  <dict>
    <key>toplevel_name</key>
    <string>My managed favorites folder</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Microsoft</string>
    <key>url</key>
    <string>https://microsoft.com</string>
  </dict>
  <dict>
    <key>children</key>
    <array>
      <dict>
        <key>name</key>
        <string>Microsoft Edge Insiders</string>
        <key>url</key>
        <string>https://www.microsoftedgeinsider.com</string>
      </dict>
      <dict>
        <key>name</key>
        <string>Microsoft Edge</string>
        <key>url</key>
        <string>https://www.microsoft.com/windows/microsoft-edge</string>
      </dict>
    </array>
    <key>name</key>
    <string>Microsoft Edge Links</string>
  </dict>
</array>
```

### Notes
- The first `<dict>` defines the top-level favorites folder.
- Each `<dict>` after that is either a link (`name` + `url`) or a folder (`name` + `<children>`).
- Nested arrays represent subfolders.

---

## 🍏 Google Chrome — macOS

**Policy Key:** `ManagedBookmarks`  
**Format:** Apple plist XML fragment (inside a configuration profile)

### Structure

```xml
<key>ManagedBookmarks</key>
<array>
  <dict>
    <key>toplevel_name</key>
    <string>My managed bookmarks folder</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Example Link</string>
    <key>url</key>
    <string>https://example.com</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Example Folder</string>
    <key>children</key>
    <array>
      <dict>
        <key>name</key>
        <string>Sub Link</string>
        <key>url</key>
        <string>https://sub.example.com</string>
      </dict>
    </array>
  </dict>
</array>
```

### Example

```xml
<key>ManagedBookmarks</key>
<array>
  <dict>
    <key>toplevel_name</key>
    <string>My managed bookmarks folder</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Google</string>
    <key>url</key>
    <string>https://google.com</string>
  </dict>
  <dict>
    <key>name</key>
    <string>YouTube</string>
    <key>url</key>
    <string>https://youtube.com</string>
  </dict>
  <dict>
    <key>name</key>
    <string>Chrome Links</string>
    <key>children</key>
    <array>
      <dict>
        <key>name</key>
        <string>Chromium</string>
        <key>url</key>
        <string>https://chromium.org</string>
      </dict>
      <dict>
        <key>name</key>
        <string>Chromium Developers</string>
        <key>url</key>
        <string>https://dev.chromium.org</string>
      </dict>
    </array>
  </dict>
</array>
```

### Notes
- The same structure applies as Edge on macOS.
- Key difference: the root key is `ManagedBookmarks`.
- Use `https://` URLs — macOS profiles require full valid URLs.

---

## 🧠 Summary Comparison Table

| Platform | Browser | Policy Key | Format | Root Folder Key | Folder Key | Link Keys | Child Array |
|-----------|----------|-------------|---------|------------------|-------------|------------|--------------|
| Windows | Edge | `ManagedFavorites` | JSON | `toplevel_name` | `name` + `children` | `name`, `url`, `toplevel` | `children` |
| Windows | Chrome | `ManagedBookmarks` | JSON | `toplevel_name` | `name` + `children` | `name`, `url` | `children` |
| macOS | Edge | `ManagedFavorites` | plist XML | `<key>toplevel_name</key>` | `<key>name</key>` | `<key>name</key>`, `<key>url</key>` | `<key>children</key>` |
| macOS | Chrome | `ManagedBookmarks` | plist XML | `<key>toplevel_name</key>` | `<key>name</key>` | `<key>name</key>`, `<key>url</key>` | `<key>children</key>` |

---

## 🧰 Reference
- [Microsoft Edge – Configure favorites using Group Policy or MDM](https://learn.microsoft.com/en-us/deployedge/configure-favorites)
- [Google Chrome Enterprise Policy – ManagedBookmarks](https://chromeenterprise.google/policies/?policy=ManagedBookmarks)
- [Apple Configuration Profile Reference](https://developer.apple.com/documentation/devicemanagement)

---
