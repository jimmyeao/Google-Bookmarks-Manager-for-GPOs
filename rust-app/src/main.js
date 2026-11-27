// Bookmark Manager for Intune/GPO — Tauri frontend
// Mirrors the behavior of the original WPF application (see MainWindow.xaml.cs).

const { invoke } = window.__TAURI__.core;

/* ============================================================
   State
   ============================================================ */

let profiles = [];
let currentProfile = null;
let saveLocationPath = "";

// UI tree nodes: { id, name, url, isFolder, children, expanded }
// The canonical tree always reflects the full profile; the rendered tree is
// a filtered projection when a search is active.
let bookmarks = [];
let selectedId = null;
let searchQuery = "";
let copiedNode = null;       // single node deep copy for copy/paste
let copiedTree = null;       // full tree copy for Copy All / Paste Bookmarks
let copiedFromProfileId = null;
let undoStack = [];          // [{parentId|null, index, node}]
let loadingProfile = false;
let saveTimer = null;
let idCounter = 1;

function nextId() {
  return "n" + idCounter++ + "_" + Date.now().toString(36);
}

// crypto.randomUUID requires a secure context; fall back if unavailable.
function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID();
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}

function log(level, msg) {
  invoke("log_message", { level, message: msg }).catch(() => {});
}

/* ============================================================
   Conversion between UI tree and BookmarkItem (service model)
   Mirrors Services/BookmarkModelConverter.cs
   ============================================================ */

function nodeToItem(node) {
  if (node.isFolder) {
    return {
      name: node.name,
      children: node.children.map(nodeToItem),
    };
  }
  const item = { name: node.name };
  if (node.url) item.url = node.url;
  return item;
}

// Builds the export/persist list: toplevel_name entry first, then all nodes.
function treeToItems() {
  const items = [];
  const folderName = document.getElementById("topLevelFolderName").value.trim();
  if (folderName) {
    items.push({ toplevel_name: folderName });
  }
  for (const node of bookmarks) {
    items.push(nodeToItem(node));
  }
  return items;
}

function itemToNode(item) {
  const isFolder = Array.isArray(item.children);
  const node = {
    id: nextId(),
    name: item.name || item.toplevel_name || "Unnamed",
    url: item.url || "",
    isFolder,
    expanded: true,
    children: [],
  };
  if (isFolder) {
    node.children = item.children.map(itemToNode);
  }
  return node;
}

// Converts BookmarkItems to UI nodes, skipping pure toplevel_name metadata entries.
function itemsToTree(items) {
  const nodes = [];
  for (const item of items || []) {
    if (item.toplevel_name && !item.name && !item.url && !item.children) {
      continue; // metadata entry — the folder name lives in the profile field
    }
    nodes.push(itemToNode(item));
  }
  return nodes;
}

function stripForSave(nodes) {
  return nodes.map((n) => {
    if (n.isFolder) {
      return { name: n.name, children: stripForSave(n.children) };
    }
    const item = { name: n.name };
    if (n.url) item.url = n.url;
    return item;
  });
}

/* ============================================================
   Tree helpers — all operate on the canonical tree by id
   ============================================================ */

function findNode(id, nodes = bookmarks, parent = null) {
  for (const n of nodes) {
    if (n.id === id) return { node: n, parent, siblings: nodes };
    if (n.isFolder) {
      const found = findNode(id, n.children, n);
      if (found) return found;
    }
  }
  return null;
}

function deepCopy(node) {
  const copy = {
    id: nextId(),
    name: node.name,
    url: node.url,
    isFolder: node.isFolder,
    expanded: node.expanded !== false,
    children: (node.children || []).map(deepCopy),
  };
  return copy;
}

function deepCopyList(nodes) {
  return nodes.map(deepCopy);
}

function removeNode(id) {
  const found = findNode(id);
  if (!found) return false;
  const idx = found.siblings.indexOf(found.node);
  if (idx >= 0) {
    found.siblings.splice(idx, 1);
    return true;
  }
  return false;
}

function collectMatches(node, query) {
  const q = query.toLowerCase();
  const nameMatch = (node.name || "").toLowerCase().includes(q);
  const urlMatch = (node.url || "").toLowerCase().includes(q);
  const childMatches = (node.children || [])
    .map((c) => collectMatches(c, query))
    .filter(Boolean);
  if (nameMatch || urlMatch || childMatches.length > 0) {
    return { node, childMatches, selfMatch: nameMatch || urlMatch };
  }
  return null;
}

/* ============================================================
   Rendering
   ============================================================ */

const treeView = document.getElementById("treeView");

function visibleTree() {
  if (!searchQuery.trim()) {
    return bookmarks.map((n) => ({ node: n, childMatches: null }));
  }
  const results = [];
  for (const n of bookmarks) {
    const m = collectMatches(n, searchQuery);
    if (m) results.push(m);
  }
  return results;
}

function renderTree() {
  treeView.innerHTML = "";
  const entries = visibleTree();
  if (entries.length === 0) {
    const hint = document.createElement("div");
    hint.className = "empty-hint";
    hint.textContent = searchQuery
      ? "No bookmarks match your search."
      : "No bookmarks yet. Right-click to add folders or bookmarks, or import from clipboard.";
    treeView.appendChild(hint);
    return;
  }
  for (const entry of entries) {
    treeView.appendChild(renderNode(entry, 0));
  }
}

function renderNode(entry, depth) {
  const { node, childMatches } = entry;
  const container = document.createElement("div");
  container.className = "tree-node";

  const row = document.createElement("div");
  row.className = "tree-row";
  row.dataset.id = node.id;
  if (node.id === selectedId) row.classList.add("selected");

  // expand/collapse toggle
  const toggle = document.createElement("span");
  toggle.className = "tree-toggle";
  if (node.isFolder && node.children.length > 0) {
    toggle.textContent = node.expanded !== false ? "▼" : "▶";
    toggle.addEventListener("click", (e) => {
      e.stopPropagation();
      node.expanded = node.expanded === false;
      renderTree();
    });
  }
  row.appendChild(toggle);

  // icon
  const icon = document.createElement("span");
  icon.className = "tree-icon";
  icon.textContent = node.isFolder ? "📁" : "🔖";
  row.appendChild(icon);

  // label (or inline edit input)
  if (node.editing) {
    const input = document.createElement("input");
    input.className = "tree-label-input";
    input.value = node.name;
    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        node.name = input.value.trim() || node.name;
        node.editing = false;
        syncDetails();
        renderTree();
        autoSave();
      } else if (e.key === "Escape") {
        node.editing = false;
        renderTree();
      }
      e.stopPropagation();
    });
    input.addEventListener("blur", () => {
      if (node.editing) {
        node.name = input.value.trim() || node.name;
        node.editing = false;
        syncDetails();
        renderTree();
        autoSave();
      }
    });
    input.addEventListener("click", (e) => e.stopPropagation());
    row.appendChild(input);
    setTimeout(() => {
      input.focus();
      input.select();
    }, 0);
  } else {
    const label = document.createElement("span");
    label.className = "tree-label";
    label.textContent = node.name;
    label.title = node.url || node.name;
    row.appendChild(label);
  }

  // inline action buttons
  const actions = document.createElement("span");
  actions.className = "tree-actions";
  if (node.isFolder) {
    const addBtn = document.createElement("button");
    addBtn.className = "tree-action-btn";
    addBtn.title = "Add bookmark";
    addBtn.textContent = "+";
    addBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      addNestedBookmark(node.id);
    });
    actions.appendChild(addBtn);
  }
  const delBtn = document.createElement("button");
  delBtn.className = "tree-action-btn";
  delBtn.title = "Delete";
  delBtn.textContent = "−";
  delBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    deleteNodeById(node.id);
  });
  actions.appendChild(delBtn);
  row.appendChild(actions);

  // selection
  row.addEventListener("click", () => {
    selectNode(node.id);
  });

  // double-click inline rename
  row.addEventListener("dblclick", (e) => {
    if (e.target.closest(".tree-toggle") || e.target.closest(".tree-action-btn")) return;
    node.editing = true;
    renderTree();
  });

  // context menu
  row.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    e.stopPropagation();
    selectNode(node.id);
    showContextMenu(e.clientX, e.clientY, node.id);
  });

  // drag & drop
  row.draggable = true;
  row.addEventListener("dragstart", (e) => onDragStart(e, node.id));
  row.addEventListener("dragover", (e) => onDragOver(e, node.id, row));
  row.addEventListener("dragleave", (e) => onDragLeave(e, row));
  row.addEventListener("drop", (e) => onDrop(e, node.id, row));
  row.addEventListener("dragend", onDragEnd);

  container.appendChild(row);

  // children — when a search filter produced child matches, always render
  // them regardless of collapse state so matches are never hidden
  const filtering = childMatches !== null && childMatches !== undefined;
  if (node.isFolder && (node.expanded !== false || filtering)) {
    const childBox = document.createElement("div");
    childBox.className = "tree-children";
    let kids;
    if (filtering) {
      kids = childMatches;
    } else {
      kids = (node.children || []).map((c) => ({ node: c, childMatches: null }));
    }
    for (const childEntry of kids) {
      childBox.appendChild(renderNode(childEntry, depth + 1));
    }
    container.appendChild(childBox);
  }

  return container;
}

function selectNode(id) {
  selectedId = id;
  syncDetails();
  // Update selection styling in place — full re-render would flicker and
  // reset scroll position.
  treeView.querySelectorAll(".tree-row.selected").forEach((r) => r.classList.remove("selected"));
  const row = treeView.querySelector(`[data-id="${id}"]`);
  if (row) row.classList.add("selected");
}

function syncDetails() {
  const nameInput = document.getElementById("bookmarkName");
  const urlInput = document.getElementById("bookmarkUrl");
  const found = selectedId ? findNode(selectedId) : null;
  if (found) {
    if (document.activeElement !== nameInput) nameInput.value = found.node.name || "";
    if (document.activeElement !== urlInput) urlInput.value = found.node.url || "";
  } else {
    nameInput.value = "";
    urlInput.value = "";
  }
}

/* ============================================================
   Drag & drop — mirrors BookmarksTreeView_Drop:
   upper third = insert above, lower third = below, middle = inside folder
   ============================================================ */

let draggedId = null;
const insertionLine = document.getElementById("insertionLine");

function onDragStart(e, id) {
  draggedId = id;
  e.dataTransfer.effectAllowed = "move";
  e.dataTransfer.setData("text/plain", "internal:" + id);
  e.currentTarget.classList.add("dragging");
}

function dropPosition(e, row) {
  const rect = row.getBoundingClientRect();
  const third = rect.height / 3;
  const y = e.clientY - rect.top;
  if (y < third) return "above";
  if (y > rect.height - third) return "below";
  return "inside";
}

function onDragOver(e, targetId, row) {
  e.preventDefault();
  e.stopPropagation();
  // Required for the drop to be accepted — without an explicit dropEffect
  // some webviews show the no-entry cursor even when preventDefault is called
  e.dataTransfer.dropEffect = draggedId ? "move" : "copy";
  const target = findNode(targetId);
  if (!target) return;

  const types = Array.from(e.dataTransfer.types || []);
  const isInternal = draggedId && types.includes("text/plain");
  if (isInternal && draggedId === targetId) return;

  const pos = dropPosition(e, row);
  row.classList.remove("drop-inside");
  insertionLine.classList.add("hidden");

  if (pos === "inside" && target.node.isFolder) {
    row.classList.add("drop-inside");
  } else {
    const rect = row.getBoundingClientRect();
    insertionLine.style.left = rect.left + "px";
    insertionLine.style.width = rect.width + "px";
    insertionLine.style.top = (pos === "above" ? rect.top - 1 : rect.bottom - 1) + "px";
    insertionLine.classList.remove("hidden");
  }
}

function onDragLeave(e, row) {
  row.classList.remove("drop-inside");
}

function onDragEnd() {
  draggedId = null;
  insertionLine.classList.add("hidden");
  document.querySelectorAll(".tree-row.dragging").forEach((r) => r.classList.remove("dragging"));
  document.querySelectorAll(".tree-row.drop-inside").forEach((r) => r.classList.remove("drop-inside"));
}

// URL extraction helpers, mirroring TryExtractUrlAndTitle / NormalizeUrl
function extractFirstUrl(text) {
  const m = /https?:\/\/[^\s<>"]+/i.exec(text);
  return m ? m[0] : null;
}

function normalizeUrl(url) {
  if (!url) return url;
  if (/^view-source:/i.test(url)) url = url.substring("view-source:".length);
  try {
    return new URL(url).toString();
  } catch {
    try {
      return new URL("https://" + url).toString();
    } catch {
      return url;
    }
  }
}

function guessTitle(text, fallbackUrl) {
  const lines = text.split(/[\r\n]+/).filter((l) => l.trim());
  if (lines.length >= 2 && /https?:\/\//i.test(lines[1])) {
    return lines[0].trim();
  }
  try {
    return new URL(fallbackUrl).hostname;
  } catch {
    return fallbackUrl;
  }
}

function extractExternalUrl(dt) {
  // 1) uri-list (browsers)
  const uriList = dt.getData("text/uri-list");
  if (uriList) {
    const line = uriList.split(/[\r\n]+/).find((l) => l && !l.startsWith("#"));
    if (line) return { url: line.trim(), title: null };
  }
  // 2) HTML anchor
  const html = dt.getData("text/html");
  if (html) {
    const m = /<a\s[^>]*href=["']([^"']+)["'][^>]*>(.*?)<\/a>/is.exec(html);
    if (m) {
      const title = m[2].replace(/<[^>]+>/g, "").trim();
      return { url: m[1], title: title || null };
    }
  }
  // 3) plain text
  const text = dt.getData("text/plain");
  if (text && !text.startsWith("internal:")) {
    const u = extractFirstUrl(text.trim());
    if (u) return { url: u, title: guessTitle(text.trim(), u) };
  }
  return null;
}

function insertAt(id, parentId, index) {
  const found = findNode(id);
  if (!found) return false;
  const srcIdx = found.siblings.indexOf(found.node);
  if (srcIdx < 0) return false;

  // adjusting index when moving within same list
  let targetSiblings;
  if (parentId === null) {
    targetSiblings = bookmarks;
  } else {
    const p = findNode(parentId);
    if (!p) return false;
    targetSiblings = p.node.children;
  }

  const sameList = targetSiblings === found.siblings;
  found.siblings.splice(srcIdx, 1);
  if (sameList && srcIdx < index) index--;
  index = Math.max(0, Math.min(index, targetSiblings.length));
  targetSiblings.splice(index, 0, found.node);
  return true;
}

function insertNewAt(node, parentId, index) {
  let targetSiblings;
  if (parentId === null) {
    targetSiblings = bookmarks;
  } else {
    const p = findNode(parentId);
    if (!p) return;
    targetSiblings = p.node.children;
  }
  index = Math.max(0, Math.min(index, targetSiblings.length));
  targetSiblings.splice(index, 0, node);
}

function onDrop(e, targetId, row) {
  e.preventDefault();
  e.stopPropagation();
  row.classList.remove("drop-inside");
  insertionLine.classList.add("hidden");

  const target = findNode(targetId);
  if (!target) return;

  const pos = dropPosition(e, row);
  const dropInside = pos === "inside" && target.node.isFolder;

  // External URL drop?
  if (!draggedId || !e.dataTransfer.getData("text/plain").startsWith("internal:")) {
    const ext = extractExternalUrl(e.dataTransfer);
    if (ext) {
      const newNode = {
        id: nextId(),
        name: ext.title || ext.url,
        url: normalizeUrl(ext.url),
        isFolder: false,
        expanded: true,
        children: [],
      };
      placeDropped(newNode, target, pos, dropInside, true);
      finishDrop();
      return;
    }
  }

  if (!draggedId || draggedId === targetId) {
    onDragEnd();
    return;
  }

  // Prevent dropping a folder into its own descendant
  const dragged = findNode(draggedId);
  if (!dragged) return;
  if (isDescendant(dragged.node, targetId)) {
    showMessage("Cannot move a folder into itself.", "Operation Not Allowed");
    onDragEnd();
    return;
  }

  moveDragged(draggedId, target, pos, dropInside);
  finishDrop();
}

function isDescendant(node, id) {
  for (const child of node.children || []) {
    if (child.id === id || isDescendant(child, id)) return true;
  }
  return false;
}

function dropTargetLocation(target, pos, dropInside) {
  if (dropInside) {
    return { parentId: target.node.id, index: target.node.children.length };
  }
  // insert above/below target within its siblings
  const siblings = target.siblings;
  let index = siblings.indexOf(target.node);
  if (pos === "below") index++;
  const parentOfTarget = findParentOf(target.node.id);
  return { parentId: parentOfTarget, index };
}

function findParentOf(id) {
  function search(nodes, parentId) {
    for (const n of nodes) {
      if (n.id === id) return parentId;
      if (n.isFolder) {
        const r = search(n.children, n.id);
        if (r !== undefined) return r;
      }
    }
    return undefined;
  }
  const r = search(bookmarks, null);
  return r === undefined ? null : r;
}

function moveDragged(id, target, pos, dropInside) {
  const loc = dropTargetLocation(target, pos, dropInside);
  insertAt(id, loc.parentId, loc.index);
}

function placeDropped(node, target, pos, dropInside) {
  const loc = dropTargetLocation(target, pos, dropInside);
  insertNewAt(node, loc.parentId, loc.index);
}

function finishDrop() {
  draggedId = null;
  insertionLine.classList.add("hidden");
  renderTree();
  autoSave();
}

// Drop on empty tree background appends at root
treeView.addEventListener("dragover", (e) => {
  if (e.target === treeView) {
    e.preventDefault();
    e.dataTransfer.dropEffect = draggedId ? "move" : "copy";
    insertionLine.classList.add("hidden");
  }
});

treeView.addEventListener("drop", (e) => {
  if (e.target !== treeView) return;
  e.preventDefault();
  if (draggedId) {
    insertAt(draggedId, null, bookmarks.length);
  } else {
    const ext = extractExternalUrl(e.dataTransfer);
    if (ext) {
      bookmarks.push({
        id: nextId(),
        name: ext.title || ext.url,
        url: normalizeUrl(ext.url),
        isFolder: false,
        expanded: true,
        children: [],
      });
    }
  }
  finishDrop();
});

treeView.addEventListener("contextmenu", (e) => {
  if (e.target === treeView || e.target.classList.contains("empty-hint")) {
    e.preventDefault();
    showContextMenu(e.clientX, e.clientY, null);
  }
});

/* ============================================================
   Context menu
   ============================================================ */

const contextMenu = document.getElementById("contextMenu");
let ctxNodeId = null;

function showContextMenu(x, y, nodeId) {
  ctxNodeId = nodeId;
  const hasTarget = nodeId !== null;
  const node = hasTarget ? findNode(nodeId)?.node : null;

  setCtxEnabled("addNestedFolder", hasTarget && node?.isFolder);
  setCtxEnabled("addNestedBookmark", hasTarget && node?.isFolder);
  setCtxEnabled("sortAlpha", !hasTarget || node?.isFolder);
  setCtxEnabled("copy", hasTarget);
  setCtxEnabled("paste", copiedNode !== null);
  setCtxEnabled("delete", hasTarget);

  contextMenu.classList.remove("hidden");
  const rect = { w: 220, h: 260 };
  contextMenu.style.left = Math.min(x, window.innerWidth - rect.w - 8) + "px";
  contextMenu.style.top = Math.min(y, window.innerHeight - rect.h - 8) + "px";
}

function setCtxEnabled(action, enabled) {
  const el = contextMenu.querySelector(`[data-action="${action}"]`);
  if (el) el.classList.toggle("disabled", !enabled);
}

function hideContextMenu() {
  contextMenu.classList.add("hidden");
}

document.addEventListener("click", (e) => {
  if (!contextMenu.contains(e.target)) hideContextMenu();
  const dropdown = document.getElementById("fileMenuDropdown");
  if (!document.getElementById("fileMenuRoot").contains(e.target)) {
    dropdown.classList.add("hidden");
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") hideContextMenu();
});

contextMenu.addEventListener("click", (e) => {
  const item = e.target.closest(".ctx-item");
  if (!item || item.classList.contains("disabled")) return;
  const action = item.dataset.action;
  hideContextMenu();
  handleContextAction(action, ctxNodeId);
});

function handleContextAction(action, nodeId) {
  switch (action) {
    case "addTopFolder":
      addFolder(null);
      break;
    case "addNestedFolder":
      if (nodeId) addFolder(nodeId);
      break;
    case "addNestedBookmark":
      if (nodeId) addNestedBookmark(nodeId);
      break;
    case "sortAlpha":
      sortAlphabetically(nodeId);
      break;
    case "copy":
      if (nodeId) copyNode(nodeId);
      break;
    case "paste":
      pasteNode(nodeId);
      break;
    case "delete":
      if (nodeId) deleteNodeById(nodeId);
      break;
  }
}

/* ============================================================
   Bookmark operations
   ============================================================ */

function addFolder(parentId) {
  const node = {
    id: nextId(),
    name: "New Folder",
    url: "",
    isFolder: true,
    expanded: true,
    children: [],
  };
  if (parentId) {
    const p = findNode(parentId);
    if (!p || !p.node.isFolder) return;
    p.node.children.push(node);
    p.node.expanded = true;
  } else {
    bookmarks.push(node);
  }
  selectedId = node.id;
  syncDetails();
  renderTree();
  autoSave();
  // start inline rename
  node.editing = true;
  renderTree();
}

function addNestedBookmark(parentId) {
  const node = {
    id: nextId(),
    name: "New Bookmark",
    url: "",
    isFolder: false,
    expanded: true,
    children: [],
  };
  if (parentId) {
    const p = findNode(parentId);
    if (p && p.node.isFolder) {
      p.node.children.push(node);
      p.node.expanded = true;
    } else {
      bookmarks.push(node);
    }
  } else {
    bookmarks.push(node);
  }
  selectedId = node.id;
  syncDetails();
  renderTree();
  autoSave();
  node.editing = true;
  renderTree();
}

async function deleteNodeById(id) {
  const found = findNode(id);
  if (!found) return;
  const ok = await showConfirm(
    "Are you sure you want to delete this bookmark?",
    "Confirm Delete"
  );
  if (!ok) return;
  const idx = found.siblings.indexOf(found.node);
  undoStack.push({
    parentId: findParentOf(id),
    index: idx,
    node: found.node,
  });
  found.siblings.splice(idx, 1);
  if (selectedId === id) {
    selectedId = null;
    syncDetails();
  }
  log("Information", `Deleted '${found.node.name}'`);
  renderTree();
  autoSave();
}

function undoDelete() {
  const entry = undoStack.pop();
  if (!entry) return;
  // Reassign fresh ids so re-delete/undo cycles stay consistent
  if (entry.parentId) {
    const p = findNode(entry.parentId);
    if (p && p.node.isFolder) {
      const idx = Math.min(entry.index, p.node.children.length);
      p.node.children.splice(idx, 0, entry.node);
    } else {
      bookmarks.splice(Math.min(entry.index, bookmarks.length), 0, entry.node);
    }
  } else {
    bookmarks.splice(Math.min(entry.index, bookmarks.length), 0, entry.node);
  }
  renderTree();
  autoSave();
}

function copyNode(id) {
  const found = findNode(id);
  if (!found) return;
  copiedNode = deepCopy(found.node);
}

function pasteNode(targetId) {
  if (!copiedNode) {
    showMessage("Copy a bookmark or folder before pasting.", "Info");
    return;
  }
  const paste = deepCopy(copiedNode);
  if (!targetId) {
    bookmarks.push(paste);
  } else {
    const target = findNode(targetId);
    if (!target) {
      bookmarks.push(paste);
    } else if (target.node.isFolder) {
      target.node.children.push(paste);
      target.node.expanded = true;
    } else {
      // paste as sibling after the target, mirroring WPF PasteBookmarkAsync
      const siblings = target.siblings;
      siblings.splice(siblings.indexOf(target.node) + 1, 0, paste);
    }
  }
  renderTree();
  autoSave();
}

function sortList(list) {
  const folders = list.filter((n) => n.isFolder).sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
  const files = list.filter((n) => !n.isFolder).sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
  list.length = 0;
  list.push(...folders, ...files);
}

function sortAlphabetically(nodeId) {
  if (!nodeId) {
    sortList(bookmarks);
    showMessage("All bookmarks sorted alphabetically.", "Success");
  } else {
    const found = findNode(nodeId);
    if (!found) return;
    if (found.node.isFolder) {
      sortList(found.node.children);
      showMessage(`Folder '${found.node.name}' sorted alphabetically.`, "Success");
    } else {
      showMessage("Sorting is only available for folders.", "Info");
      return;
    }
  }
  renderTree();
  autoSave();
}

/* ============================================================
   Details panel
   ============================================================ */

document.getElementById("bookmarkName").addEventListener("input", (e) => {
  const found = selectedId ? findNode(selectedId) : null;
  if (found) {
    found.node.name = e.target.value;
    const row = treeView.querySelector(`[data-id="${selectedId}"] .tree-label`);
    if (row) row.textContent = e.target.value;
    autoSave();
  }
});

document.getElementById("bookmarkUrl").addEventListener("input", (e) => {
  const found = selectedId ? findNode(selectedId) : null;
  if (found) {
    found.node.url = e.target.value;
    autoSave();
  }
});

document.getElementById("saveBookmarkBtn").addEventListener("click", () => {
  renderTree();
  autoSave();
  showMessage("Bookmark saved.", "Success");
});

document.getElementById("deleteBookmarkBtn").addEventListener("click", () => {
  if (selectedId) deleteNodeById(selectedId);
});

/* ============================================================
   Search
   ============================================================ */

document.getElementById("searchBox").addEventListener("input", (e) => {
  searchQuery = e.target.value;
  renderTree();
});

/* ============================================================
   Keyboard shortcuts — mirrors TreeView_KeyDown / Window_PreviewKeyDown
   ============================================================ */

document.addEventListener("keydown", (e) => {
  const inInput = ["INPUT", "TEXTAREA", "SELECT"].includes(document.activeElement?.tagName);
  if (inInput) return;

  if (e.ctrlKey || e.metaKey) {
    if (e.key === "z" || e.key === "Z") {
      undoDelete();
      e.preventDefault();
    } else if (e.key === "c" || e.key === "C") {
      if (selectedId) {
        copyNode(selectedId);
        e.preventDefault();
      }
    } else if (e.key === "v" || e.key === "V") {
      pasteNode(selectedId);
      e.preventDefault();
    }
  } else if (e.key === "Delete" && selectedId) {
    deleteNodeById(selectedId);
    e.preventDefault();
  }
});

/* ============================================================
   Clear all
   ============================================================ */

document.getElementById("clearAllBtn").addEventListener("click", async () => {
  const ok = await showConfirm(
    "Are you sure you want to clear all bookmarks?",
    "Confirm Clear"
  );
  if (!ok) return;
  bookmarks = [];
  selectedId = null;
  document.getElementById("topLevelFolderName").value = "";
  document.getElementById("searchBox").value = "";
  searchQuery = "";
  syncDetails();
  renderTree();
  autoSave();
});

/* ============================================================
   Copy All / Paste Bookmarks (cross-profile)
   ============================================================ */

document.getElementById("copyAllBtn").addEventListener("click", () => {
  copiedTree = deepCopyList(bookmarks);
  copiedFromProfileId = currentProfile?.id || null;
  showMessage(
    `Copied ${copiedTree.length} top-level bookmark items from '${currentProfile?.name || "the current profile"}'. Switch profiles, then choose Paste Bookmarks.`,
    "Bookmarks Copied"
  );
});

document.getElementById("pasteAllBtn").addEventListener("click", async () => {
  if (!copiedTree) {
    showMessage("Copy bookmarks from a profile before pasting.", "Nothing to Paste");
    return;
  }
  const ok = await showConfirm(
    `Append ${copiedTree.length} top-level items to '${currentProfile?.name || "the current profile"}'? Existing bookmarks will be kept.`,
    "Confirm Paste"
  );
  if (!ok) return;
  bookmarks.push(...deepCopyList(copiedTree));
  renderTree();
  autoSave();
  showMessage("The copied bookmarks and folders were added to this profile.", "Paste Complete");
});

/* ============================================================
   Import / Export
   ============================================================ */

document.getElementById("importBtn").addEventListener("click", async () => {
  const content = await showInputModal(
    "Paste your bookmarks JSON or PLIST XML below, then click Import.",
    "Import Bookmarks",
    "",
    true
  );
  if (!content || !content.trim()) return;

  try {
    const result = await invoke("parse_import", { content: content.trim() });
    bookmarks = itemsToTree(result.items);
    if (result.topLevelName) {
      document.getElementById("topLevelFolderName").value = result.topLevelName;
    }
    selectedId = null;
    document.getElementById("searchBox").value = "";
    searchQuery = "";
    syncDetails();
    renderTree();
    autoSave();
    log("Information", "Bookmarks imported successfully");
    showMessage("Bookmarks imported successfully!", "Success");
  } catch (err) {
    log("Error", `Import failed: ${err}`);
    showMessage(`Error importing bookmarks: ${err}`, "Error");
  }
});

async function exportAsJson(edge) {
  try {
    const items = JSON.parse(JSON.stringify(treeToItems()));
    const json = await invoke("export_json", { items, edge });
    await invoke("set_clipboard", { text: json });
    const browser = edge ? "Microsoft Edge" : "Google Chrome";
    const key = edge ? "ManagedFavorites" : "ManagedBookmarks";
    showMessage(
      `Bookmarks exported to clipboard as JSON for ${browser} on Windows!\n\nPolicy Key: ${key}`,
      "Success"
    );
    log("Information", `Exported bookmarks as JSON for ${browser} on Windows`);
  } catch (err) {
    showMessage(`Error exporting: ${err}`, "Error");
  }
}

async function exportAsPlist(edge) {
  try {
    const items = JSON.parse(JSON.stringify(treeToItems()));
    const key = edge ? "ManagedFavorites" : "ManagedBookmarks";
    const plist = await invoke("export_plist", { items, keyName: key });
    await invoke("set_clipboard", { text: plist });
    const browser = edge ? "Microsoft Edge" : "Google Chrome";
    showMessage(
      `Bookmarks exported to clipboard as PLIST for ${browser} on macOS!\n\nPolicy Key: ${key}\n\nReady to paste into Intune Configuration Profile.`,
      "Success"
    );
    log("Information", `Exported bookmarks as PLIST for ${browser} on macOS`);
  } catch (err) {
    showMessage(`Error exporting: ${err}`, "Error");
  }
}

document.getElementById("exportEdgeJson").addEventListener("click", () => exportAsJson(true));
document.getElementById("exportChromeJson").addEventListener("click", () => exportAsJson(false));
document.getElementById("exportEdgePlist").addEventListener("click", () => exportAsPlist(true));
document.getElementById("exportChromePlist").addEventListener("click", () => exportAsPlist(false));

/* ============================================================
   Top-level folder name
   ============================================================ */

document.getElementById("topLevelFolderName").addEventListener("input", () => {
  autoSave();
});

/* ============================================================
   Profiles
   ============================================================ */

const profileCombo = document.getElementById("profileCombo");

function renderProfileCombo() {
  profileCombo.innerHTML = "";
  for (const p of profiles) {
    const opt = document.createElement("option");
    opt.value = p.id;
    opt.textContent = p.name;
    if (currentProfile && p.id === currentProfile.id) opt.selected = true;
    profileCombo.appendChild(opt);
  }
}

function serializeCurrentIntoProfile() {
  if (!currentProfile) return;
  currentProfile.bookmarks = stripForSave(bookmarks);
  currentProfile.topLevelFolderName =
    document.getElementById("topLevelFolderName").value.trim() || "Managed Bookmarks";
}

async function persistProfiles() {
  if (!currentProfile) return;
  serializeCurrentIntoProfile();
  try {
    await invoke("save_profiles", { profiles: JSON.parse(JSON.stringify(profiles)) });
  } catch (err) {
    log("Error", `Failed to save profiles: ${err}`);
  }
}

function autoSave() {
  if (loadingProfile) return;
  clearTimeout(saveTimer);
  saveTimer = setTimeout(persistProfiles, 400);
}

function loadProfileIntoUI(profile) {
  loadingProfile = true;
  currentProfile = profile;
  bookmarks = itemsToTree(profile.bookmarks);
  document.getElementById("topLevelFolderName").value = profile.topLevelFolderName || "Managed Bookmarks";
  selectedId = null;
  undoStack = [];
  document.getElementById("searchBox").value = "";
  searchQuery = "";
  syncDetails();
  renderTree();
  loadingProfile = false;
}

profileCombo.addEventListener("change", async () => {
  const id = profileCombo.value;
  const target = profiles.find((p) => p.id === id);
  if (!target || (currentProfile && currentProfile.id === target.id)) return;

  // save current before switching (mirrors ProfileComboBox_SelectionChanged)
  clearTimeout(saveTimer);
  await persistProfiles();
  loadProfileIntoUI(target);
  log("Information", `Switched to profile '${target.name}'`);
});

document.getElementById("newProfileBtn").addEventListener("click", async () => {
  const name = await showInputModal("Profile Name:", "New Profile", "New Profile");
  if (!name || !name.trim()) return;
  const folderName = await showInputModal("Folder Name:", "Top-Level Folder Name", "Managed Bookmarks");
  if (!folderName || !folderName.trim()) return;

  if (profiles.some((p) => p.name.toLowerCase() === name.trim().toLowerCase())) {
    showMessage(`Profile '${name.trim()}' already exists.`, "Error");
    return;
  }

  const profile = {
    name: name.trim(),
    topLevelFolderName: folderName.trim(),
    bookmarks: [],
    lastModified: new Date().toISOString(),
    id: uuid(),
  };
  clearTimeout(saveTimer);
  await persistProfiles();
  profiles.push(profile);
  await invoke("save_profiles", { profiles: JSON.parse(JSON.stringify(profiles)) }).catch((e) => log("Error", String(e)));
  renderProfileCombo();
  profileCombo.value = profile.id;
  loadProfileIntoUI(profile);
  showMessage(`Profile '${profile.name}' created successfully!`, "Success");
});

document.getElementById("renameProfileBtn").addEventListener("click", async () => {
  if (!currentProfile) {
    showMessage("No profile selected.", "Error");
    return;
  }
  const newName = await showInputModal("New Profile Name:", "Rename Profile", currentProfile.name);
  if (!newName || !newName.trim()) return;
  const trimmed = newName.trim();
  if (profiles.some((p) => p.id !== currentProfile.id && p.name.toLowerCase() === trimmed.toLowerCase())) {
    showMessage(`Profile '${trimmed}' already exists.`, "Error");
    return;
  }
  currentProfile.name = trimmed;
  await persistProfiles();
  renderProfileCombo();
  showMessage(`Profile renamed to '${trimmed}' successfully!`, "Success");
});

document.getElementById("deleteProfileBtn").addEventListener("click", async () => {
  if (!currentProfile) {
    showMessage("No profile selected.", "Error");
    return;
  }
  if (profiles.length <= 1) {
    showMessage("Cannot delete the last profile.", "Error");
    return;
  }
  const ok = await showConfirm(
    `Are you sure you want to delete profile '${currentProfile.name}'?`,
    "Confirm Delete"
  );
  if (!ok) return;
  profiles = profiles.filter((p) => p.id !== currentProfile.id);
  try {
    // persist the deletion itself: serialize without touching currentProfile (already removed)
    await invoke("save_profiles", { profiles: JSON.parse(JSON.stringify(profiles)) });
  } catch (err) {
    log("Error", `Failed to save profiles: ${err}`);
  }
  renderProfileCombo();
  if (profiles.length > 0) {
    profileCombo.value = profiles[0].id;
    loadProfileIntoUI(profiles[0]);
  }
  showMessage("Profile deleted successfully!", "Success");
});

document.getElementById("copyProfileBtn").addEventListener("click", async () => {
  if (!currentProfile) {
    showMessage("No profile selected.", "Error");
    return;
  }
  const newName = await showInputModal("New Profile Name:", "Copy Profile", currentProfile.name + " - Copy");
  if (!newName || !newName.trim()) return;
  if (profiles.some((p) => p.name.toLowerCase() === newName.trim().toLowerCase())) {
    showMessage(`Profile '${newName.trim()}' already exists.`, "Error");
    return;
  }
  serializeCurrentIntoProfile();
  const copy = {
    name: newName.trim(),
    topLevelFolderName: currentProfile.topLevelFolderName,
    bookmarks: JSON.parse(JSON.stringify(currentProfile.bookmarks)),
    lastModified: new Date().toISOString(),
    id: uuid(),
  };
  profiles.push(copy);
  await persistProfiles();
  renderProfileCombo();
  profileCombo.value = copy.id;
  loadProfileIntoUI(copy);
  showMessage(`Profile copied as '${copy.name}'!`, "Success");
});

/* ============================================================
   File menu: save location / open profile from
   ============================================================ */

document.getElementById("fileMenuBtn").addEventListener("click", (e) => {
  e.stopPropagation();
  document.getElementById("fileMenuDropdown").classList.toggle("hidden");
});

document.getElementById("changeSaveLocation").addEventListener("click", async () => {
  try {
    const path = await invoke("pick_save_location", { defaultName: "profiles.json" });
    if (!path) return;
    await persistProfiles(); // flush to old location first
    await invoke("register_save_location", { path });
    await invoke("save_profiles_to", {
      path,
      profiles: JSON.parse(JSON.stringify(profiles)),
    });
    saveLocationPath = path;
    document.getElementById("saveLocationDisplay").textContent = path;
    const settings = await invoke("get_settings");
    settings.profilesFilePath = path;
    await invoke("save_settings", { settings });
    showMessage(
      `All profiles will now be saved to:\n${path}\n\nThis location will be used for ALL profiles.\nShare this file with your team for collaboration!\n\nAuto-save is enabled - all changes save automatically.\n\nThis location will be remembered when you restart the app.`,
      "Save Location Changed"
    );
    log("Information", `Changed save location to: ${path}`);
  } catch (err) {
    showMessage(`Error changing save location: ${err}`, "Error");
  }
});

document.getElementById("openProfileFrom").addEventListener("click", async () => {
  try {
    const path = await invoke("pick_profile_file");
    if (!path) return;
    const loaded = await invoke("load_profiles_from", { path });
    if (!loaded || loaded.length === 0) {
      showMessage("No profiles found in that file.", "Error");
      return;
    }
    const profile = loaded[0];
    const existing = profiles.find((p) => p.id === profile.id);
    if (existing) {
      Object.assign(existing, profile);
      renderProfileCombo();
      showMessage(`Loaded profile: ${profile.name}`, "Success");
    } else {
      clearTimeout(saveTimer);
      await persistProfiles();
      profiles.push(profile);
      await invoke("save_profiles", { profiles: JSON.parse(JSON.stringify(profiles)) });
      renderProfileCombo();
      profileCombo.value = profile.id;
      loadProfileIntoUI(profile);
      showMessage(`Loaded and added profile: ${profile.name}`, "Success");
    }
    log("Information", `Loaded profile from: ${path}`);
  } catch (err) {
    showMessage(`Error loading profile: ${err}`, "Error");
  }
});

/* ============================================================
   Dark mode
   ============================================================ */

const darkModeCheckbox = document.getElementById("darkModeCheckbox");

function applyTheme(dark) {
  document.body.classList.toggle("dark", dark);
  darkModeCheckbox.checked = dark;
}

darkModeCheckbox.addEventListener("change", async () => {
  applyTheme(darkModeCheckbox.checked);
  try {
    const settings = await invoke("get_settings");
    settings.darkMode = darkModeCheckbox.checked;
    await invoke("save_settings", { settings });
  } catch (err) {
    log("Error", `Failed to save theme: ${err}`);
  }
});

/* ============================================================
   Modal dialogs (message / confirm / input)
   ============================================================ */

const modalOverlay = document.getElementById("modalOverlay");
const modalTitle = document.getElementById("modalTitle");
const modalBody = document.getElementById("modalBody");
const modalButtons = document.getElementById("modalButtons");

function openModal(title, bodyContent, buttons) {
  modalTitle.textContent = title;
  modalBody.innerHTML = "";
  if (typeof bodyContent === "string") {
    modalBody.textContent = bodyContent;
  } else {
    modalBody.appendChild(bodyContent);
  }
  modalButtons.innerHTML = "";
  for (const b of buttons) {
    const btn = document.createElement("button");
    btn.className = "btn";
    btn.textContent = b.label;
    btn.addEventListener("click", () => {
      closeModal();
      b.handler();
    });
    modalButtons.appendChild(btn);
  }
  modalOverlay.classList.remove("hidden");
}

function closeModal() {
  modalOverlay.classList.add("hidden");
}

function showMessage(text, title) {
  openModal(title || "Message", text, [{ label: "OK", handler: () => {} }]);
}

function showConfirm(text, title) {
  return new Promise((resolve) => {
    openModal(title || "Confirm", text, [
      { label: "Cancel", handler: () => resolve(false) },
      { label: "OK", handler: () => resolve(true) },
    ]);
  });
}

function showInputModal(label, title, initialValue, multiline = false) {
  return new Promise((resolve) => {
    const wrap = document.createElement("div");
    const lbl = document.createElement("div");
    lbl.textContent = label;
    lbl.style.marginBottom = "8px";
    wrap.appendChild(lbl);
    let input;
    if (multiline) {
      input = document.createElement("textarea");
      input.value = initialValue || "";
    } else {
      input = document.createElement("input");
      input.type = "text";
      input.className = "text-input";
      input.value = initialValue || "";
      input.style.width = "100%";
    }
    wrap.appendChild(input);
    openModal(title, wrap, [
      { label: "Cancel", handler: () => resolve(null) },
      {
        label: multiline ? "Import" : "OK",
        handler: () => resolve(input.value),
      },
    ]);
    setTimeout(() => input.focus(), 50);
    if (!multiline) {
      input.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
          closeModal();
          resolve(input.value);
        }
      });
    }
  });
}

/* ============================================================
   Initialization
   ============================================================ */

async function init() {
  try {
    const settings = await invoke("get_settings");
    applyTheme(!!settings.darkMode);

    const payload = await invoke("load_profiles");
    profiles = payload.profiles;
    saveLocationPath = payload.path;
    document.getElementById("saveLocationDisplay").textContent = payload.path;

    if (profiles.length === 0) {
      profiles = [{
        name: "Default Profile",
        topLevelFolderName: "Managed Bookmarks",
        bookmarks: [],
        lastModified: new Date().toISOString(),
        id: uuid(),
      }];
    }

    renderProfileCombo();
    profileCombo.value = profiles[0].id;
    loadProfileIntoUI(profiles[0]);
    log("Information", "Application started");
  } catch (err) {
    showMessage(`Failed to initialize: ${err}`, "Error");
    console.error(err);
  }
}

// Flush pending changes when window closes
window.addEventListener("beforeunload", () => {
  clearTimeout(saveTimer);
  persistProfiles();
});

init();
