﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Claunia.PropertyList;
using System.Windows.Input;
using System.Windows.Media;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Xml.Linq;
using Serilog;
using Windows.UI.WebUI;
using System.Reflection;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Fields

        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GoogleBookmarksManager");
        private static readonly string BookmarksFilePath = Path.Combine(AppDataFolder, "bookmarks.json");
        // JSON Format
        private static readonly string BookmarksPlistPath = Path.Combine(AppDataFolder, "bookmarks.plist");

        private static int _idCounter = 1;
        private AdornerLayer _adornerLayer;
        private ObservableCollection<Bookmark> _bookmarks;
        private DragAdorner _dragAdorner;
        private Bookmark _draggedBookmark;
        private TreeViewItem _draggedItemContainer;
        private bool _isDragging = false;
        private DateTime _lastClickTime;
        private ObservableCollection<Bookmark> _originalBookmarks;
        private string _searchQuery;
        private string _topLevelBookmarkFolderName;
        // Plist Format (if needed)
        private string _topLevelFolderName;

        private Stack<(Bookmark parent, Bookmark bookmark)> _undoStack = new Stack<(Bookmark, Bookmark)>();
        private ChromeManager chromeManager = new ChromeManager();
        private EdgeManager edgeManager = new EdgeManager();

        #endregion Fields

        #region Constructors

        public MainWindow()
        {
            InitializeComponent();
            // Upgrade settings if required (only once after an update)
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade(); // Migrate old settings
                Properties.Settings.Default.UpgradeRequired = false; // Prevent further upgrades
                Properties.Settings.Default.Save(); // Persist setting
            }

            // Restore the saved theme preference
            bool isDarkMode = Properties.Settings.Default.IsDarkMode;
            darkModeCheckBox.IsChecked = isDarkMode;
            SwitchTheme(isDarkMode);
            // Ensure initialization happens only once
            if (Bookmarks == null)
            {
                Bookmarks = new ObservableCollection<Bookmark>();
            }
            _originalBookmarks = new ObservableCollection<Bookmark>(Bookmarks);  // Backup the original list
            string version = GetAppVersion();
            this.Title = $"Bookmark Mangager for Intune/GPO - Version {version}";
            DataContext = this;
            LoadBookmarksFromFile();
            this.Closing += MainWindow_Closing;
        }

        #endregion Constructors

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion Events

        #region Properties

        public ObservableCollection<Bookmark> Bookmarks
        {
            get => _bookmarks;
            set
            {
                if (_bookmarks != value)
                {
                    _bookmarks = value;
                    OnPropertyChanged(nameof(Bookmarks));
                }
            }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                _searchQuery = value;
                OnPropertyChanged(nameof(SearchQuery));
                FilterBookmarks();
            }
        }

        public string TopLevelBookmarkFolderName
        {
            get => _topLevelBookmarkFolderName;
            set
            {
                _topLevelBookmarkFolderName = value;
                OnPropertyChanged(nameof(TopLevelBookmarkFolderName));
            }
        }

        public string TopLevelFolderName
        {
            get => _topLevelFolderName;
            set
            {
                _topLevelFolderName = value;
                OnPropertyChanged(nameof(TopLevelFolderName));
            }
        }

        #endregion Properties

        #region Methods

        public JObject ConvertBookmarkToOriginalFormat(Bookmark bookmark, bool isTopLevel = false)
        {
            var obj = new JObject();

            if (isTopLevel && !string.IsNullOrEmpty(TopLevelFolderName))
            {
                obj["toplevel_name"] = TopLevelFolderName; // Use the UI-entered name
            }
            else
            {
                obj["name"] = bookmark.Name;
            }

            if (!bookmark.IsFolder && !string.IsNullOrWhiteSpace(bookmark.Url))
            {
                obj["url"] = bookmark.Url;
            }

            if (bookmark.Children != null && bookmark.Children.Any())
            {
                obj["children"] = new JArray(bookmark.Children.Select(child => ConvertBookmarkToOriginalFormat(child)));
            }

            return obj;
        }

        public ObservableCollection<Bookmark> ParsePlistWithClaunia(string plistContent)
        {
            var bookmarks = new ObservableCollection<Bookmark>();

            try
            {
                NSDictionary rootDict = (NSDictionary)PropertyListParser.Parse(Encoding.UTF8.GetBytes(plistContent));

                // Ensure either ManagedBookmarks (Chrome) or ManagedFavorites (Edge) key exists
                NSArray managedItems = null;
                if (rootDict.ContainsKey("ManagedBookmarks"))
                {
                    managedItems = (NSArray)rootDict["ManagedBookmarks"];
                    Log.Information("Parsing Chrome ManagedBookmarks");
                }
                else if (rootDict.ContainsKey("ManagedFavorites"))
                {
                    managedItems = (NSArray)rootDict["ManagedFavorites"];
                    Log.Information("Parsing Edge ManagedFavorites");
                }
                else
                {
                    Log.Warning("No recognized bookmark key found in plist");
                    return bookmarks;
                }

                // Extract the toplevel_name from the first entry in managedItems if available
                if (managedItems.Count > 0 && managedItems[0] is NSDictionary firstItem && firstItem.ContainsKey("toplevel_name"))
                {
                    TopLevelFolderName = firstItem["toplevel_name"].ToString();
                    Log.Information("Plist Top-Level Folder Name: {TopLevelFolderName}", TopLevelFolderName);
                }

                foreach (var item in managedItems)
                {
                    if (item is NSDictionary dict && dict.ContainsKey("name"))
                    {
                        Bookmark bookmark = ParsePlistBookmark(dict);
                        bookmarks.Add(bookmark);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error parsing plist: {Message}", ex.Message);
            }

            return bookmarks;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            var parentFolder = BookmarksTreeView.SelectedItem as Bookmark;

            var newBookmark = new Bookmark
            {
                Name = "New Bookmark",
                Url = "http://",
                IsFolder = false
            };

            if (parentFolder != null && parentFolder.IsFolder)
            {
                parentFolder.Children.Add(newBookmark);
            }
            else
            {
                // Add at top-level if no valid parent is selected
                Bookmarks.Add(newBookmark);
            }
            UpdateOriginalBookmarks();
            OnPropertyChanged(nameof(Bookmarks));
            ExpandAndSelectNewItem(newBookmark);
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var parentFolder = BookmarksTreeView.SelectedItem as Bookmark;

            var newFolder = new Bookmark
            {
                Name = "New Folder",
                IsFolder = true
            };

            if (parentFolder != null && parentFolder.IsFolder)
            {
                parentFolder.Children.Add(newFolder);
            }
            else
            {
                // Add at top-level if no valid parent is selected
                Bookmarks.Add(newFolder);
            }
            UpdateOriginalBookmarks();
            OnPropertyChanged(nameof(Bookmarks));
            ExpandAndSelectNewItem(newFolder);
        }

        private void AddNestedBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.CommandParameter is Bookmark parentFolder &&
                parentFolder != null && // Explicit null check
                parentFolder.IsFolder)
            {
                var newBookmark = new Bookmark { Name = "New Bookmark", Url = "http://", IsFolder = false };

                // Add the new bookmark to the folder
                parentFolder.Children.Add(newBookmark);

                // Force the TreeView to refresh and expand the folder
                Dispatcher.Invoke(() =>
                {
                    var treeViewItem = GetTreeViewItemForBookmark(parentFolder);
                    if (treeViewItem != null)
                    {
                        treeViewItem.IsExpanded = true;  // Keep the folder expanded
                        treeViewItem.UpdateLayout();     // Ensure it updates before selection
                    }

                    // Scroll to and select the new bookmark
                    var newBookmarkItem = GetTreeViewItemForBookmark(newBookmark);
                    if (newBookmarkItem != null)
                    {
                        newBookmarkItem.IsSelected = true;
                        newBookmarkItem.BringIntoView();
                    }
                });
            }
        }

        private void AddNestedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarksTreeView.SelectedItem is Bookmark selectedBookmark)
            {
                var newFolder = new Bookmark
                {
                    Name = "New Folder",
                    IsFolder = true
                };

                selectedBookmark.Children.Add(newFolder);

                // Force the UI to refresh
                OnPropertyChanged(nameof(Bookmarks));

                // Use the same expand and select logic as nested bookmarks
                Dispatcher.InvokeAsync(() =>
                {
                    if (FindTreeViewItem(BookmarksTreeView, selectedBookmark) is TreeViewItem parentItem)
                    {
                        parentItem.IsExpanded = true;

                        Dispatcher.InvokeAsync(() =>
                        {
                            if (FindTreeViewItem(BookmarksTreeView, newFolder) is TreeViewItem newItem)
                            {
                                newItem.IsSelected = true;
                                newItem.Focus();
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                CustomMessageBox.Show("Please select a folder to add a nested folder.", "Confirmation", MessageBoxButton.OK);
            }
        }

        private void AddTopLevelFolder_Click(object sender, RoutedEventArgs e)
        {
            Bookmarks.Add(new Bookmark
            {
                Name = "New Folder",
                IsFolder = true
            });
        }

        private void AppendBookmarkToHtml(Bookmark bookmark, StringBuilder html, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 4);
            if (bookmark.IsFolder)
            {
                html.AppendLine($"{indent}<DT><H3>{bookmark.Name}</H3>");
                html.AppendLine($"{indent}<DL><p>");
                foreach (var child in bookmark.Children)
                {
                    AppendBookmarkToHtml(child, html, indentLevel + 1);
                }
                html.AppendLine($"{indent}</DL><p>");
            }
            else
            {
                html.AppendLine($"{indent}<DT><A HREF=\"{bookmark.Url}\">{bookmark.Name}</A>");
            }
        }

        private void BookmarksTreeView_DragEnter(object sender, DragEventArgs e)
        {
            if (_adornerLayer == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(BookmarksTreeView);
                _dragAdorner = new DragAdorner(BookmarksTreeView, _draggedBookmark.Name);
                _adornerLayer.Add(_dragAdorner);
            }
        }

        private void BookmarksTreeView_DragOver(object sender, DragEventArgs e)
        {
            if (_dragAdorner != null)
            {
                var position = e.GetPosition(BookmarksTreeView);
                _dragAdorner.UpdatePosition(position.X, position.Y);
            }
            e.Handled = true;
        }

        private void BookmarksTreeView_Drop(object sender, DragEventArgs e)
        {
            if (_draggedBookmark == null) return;

            var targetBookmark = (e.OriginalSource as FrameworkElement)?.DataContext as Bookmark;
            if (targetBookmark == null || targetBookmark == _draggedBookmark) return;

            // Prevent moving root folders
            if (_draggedBookmark.IsRootFolder)
            {
                CustomMessageBox.Show("Root folders cannot be moved.", "Operation Not Allowed", MessageBoxButton.OK);
                _draggedBookmark = null;
                return;
            }

            var sourceParent = FindParentBookmark(Bookmarks, _draggedBookmark);

            // Remove from old location
            if (sourceParent != null)
            {
                sourceParent.Children.Remove(_draggedBookmark);
            }
            else
            {
                Bookmarks.Remove(_draggedBookmark);
            }

            // Handle dropping into a folder or reordering at the same level
            if (targetBookmark.IsFolder)
            {
                targetBookmark.Children.Add(_draggedBookmark);
            }
            else
            {
                var targetParent = FindParentBookmark(Bookmarks, targetBookmark);
                if (targetParent != null)
                {
                    int targetIndex = targetParent.Children.IndexOf(targetBookmark);
                    targetParent.Children.Insert(targetIndex, _draggedBookmark);
                }
                else
                {
                    int targetIndex = Bookmarks.IndexOf(targetBookmark);
                    Bookmarks.Insert(targetIndex, _draggedBookmark);
                }
            }

            _draggedBookmark = null;
            OnPropertyChanged(nameof(Bookmarks));
        }

        private void BookmarksTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = GetNearestContainer(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                _draggedBookmark = item.DataContext as Bookmark;
                _isDragging = false;
            }
            else
            {
                _draggedBookmark = null;
            }
        }

        private void BookmarksTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedBookmark != null && !_isDragging)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(BookmarksTreeView, _draggedBookmark, DragDropEffects.Move);
                _isDragging = false;
            }
        }

        private void BookmarksTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem item = GetNearestContainer(e.OriginalSource as DependencyObject);

            if (item != null)
            {
                item.IsSelected = true;  // Select the item under right-click
            }
            else
            {
                // Handle right-click on empty space
                ShowEmptySpaceContextMenu();
                e.Handled = true;
            }
        }

        private void BookmarksTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is Bookmark selectedBookmark)
            {
                // Populate the text boxes with the selected bookmark's details
                bookmarkNameTextBox.Text = selectedBookmark.Name;
                bookmarkUrlTextBox.Text = selectedBookmark.Url;
            }
            else
            {
                // Clear the text boxes if no valid bookmark is selected
                bookmarkNameTextBox.Text = string.Empty;
                bookmarkUrlTextBox.Text = string.Empty;
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(true);
            SaveThemePreference(true);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(false);
            SaveThemePreference(false);
        }

        private void clearFormButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear the TreeView by resetting the Bookmarks collection
            Bookmarks.Clear();
            // clear the textboxes
            TopLevelFolderNameTextBox.Text = string.Empty;
            // Clear the text boxes
            bookmarkNameTextBox.Text = string.Empty;
            bookmarkUrlTextBox.Text = string.Empty;
        }

        private string ConvertBookmarksToChromeJson()
        {
            var rootObject = new JObject
            {
                ["roots"] = new JObject
                {
                    ["bookmark_bar"] = new JObject
                    {
                        ["children"] = new JArray(Bookmarks.Select(ConvertBookmarkToChromeFormat))
                    }
                }
            };

            return rootObject.ToString(Formatting.Indented);
        }

        private JObject ConvertBookmarkToChromeFormat(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["name"] = bookmark.Name,
                ["type"] = bookmark.IsFolder ? "folder" : "url",
                ["date_added"] = GetCurrentTimestamp(),
                ["guid"] = GenerateGuid(),
                ["id"] = GenerateId()
            };

            if (!bookmark.IsFolder)
            {
                obj["url"] = bookmark.Url;
            }
            else if (bookmark.Children.Any())
            {
                obj["children"] = new JArray(bookmark.Children.Select(ConvertBookmarkToChromeFormat));
            }

            // Optional: Add meta_info for Chrome-specific metadata
            obj["meta_info"] = new JObject
            {
                ["power_bookmark_meta"] = "" // Leave empty for now
            };

            return obj;
        }

        private JObject ConvertBookmarkToEdgeFormat(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["name"] = bookmark.Name,
                ["type"] = bookmark.IsFolder ? "folder" : "url",
                ["date_added"] = GetCurrentTimestamp(),
                ["guid"] = Guid.NewGuid().ToString()
            };

            if (!bookmark.IsFolder)
            {
                obj["url"] = bookmark.Url;
            }
            else
            {
                obj["children"] = new JArray(bookmark.Children.Select(ConvertBookmarkToEdgeFormat));
            }

            return obj;
        }

        private JObject ConvertBookmarkToFirefoxFormat(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["title"] = bookmark.Name,
                ["type"] = bookmark.IsFolder ? "text/x-moz-place-container" : "text/x-moz-place",
                ["uri"] = bookmark.Url
            };

            if (bookmark.Children.Any())
            {
                obj["children"] = new JArray(bookmark.Children.Select(ConvertBookmarkToFirefoxFormat));
            }

            return obj;
        }

        private JObject ConvertBookmarkToJson(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["name"] = bookmark.Name,
                ["type"] = bookmark.IsFolder ? "folder" : "url"
            };

            if (!bookmark.IsFolder)
            {
                obj["url"] = bookmark.Url;
            }
            else
            {
                obj["children"] = new JArray(bookmark.Children.Select(ConvertBookmarkToJson));
            }

            return obj;
        }

        private Bookmark ConvertPlistDictToBookmark(NSDictionary dict)
        {
            var bookmark = new Bookmark
            {
                Name = dict.ContainsKey("toplevel_name") ? dict["toplevel_name"].ToString() : dict["name"]?.ToString(),
                Url = dict.ContainsKey("url") ? dict["url"].ToString() : null,
                IsFolder = dict.ContainsKey("children")
            };

            if (dict.ContainsKey("children"))
            {
                var childrenArray = dict["children"] as NSArray;
                if (childrenArray != null)
                {
                    bookmark.Children = new ObservableCollection<Bookmark>(
                        childrenArray.Cast<NSDictionary>().Select(ConvertPlistDictToBookmark).Where(b => b != null)
                    );
                }
            }

            return bookmark;
        }

        private Bookmark ConvertPlistDictToBookmarkClaunia(NSDictionary dict)
        {
            var bookmark = new Bookmark
            {
                Name = dict.ContainsKey("toplevel_name") ? dict["toplevel_name"].ToString() : dict["name"]?.ToString(),
                Url = dict.ContainsKey("url") ? dict["url"].ToString() : null,
                IsFolder = dict.ContainsKey("children")
            };

            if (dict.ContainsKey("children") && dict["children"] is NSArray childrenArray)
            {
                bookmark.Children = new ObservableCollection<Bookmark>();
                foreach (var child in childrenArray)
                {
                    if (child is NSDictionary childDict)
                    {
                        var childBookmark = ConvertPlistDictToBookmarkClaunia(childDict);
                        if (childBookmark != null)
                        {
                            bookmark.Children.Add(childBookmark);
                        }
                    }
                }
            }

            return bookmark;
        }

        private JObject ConvertToChromeFormat(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["name"] = bookmark.Name,
                ["url"] = bookmark.IsFolder ? null : bookmark.Url,
                ["type"] = bookmark.IsFolder ? "folder" : "url"
            };

            if (bookmark.Children.Any())
            {
                obj["children"] = new JArray(bookmark.Children.Select(ConvertToChromeFormat));
            }

            return obj;
        }

        private JObject ConvertToSimpleJsonObject(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["Name"] = bookmark.Name,
                ["Url"] = bookmark.IsFolder ? null : bookmark.Url
            };

            if (bookmark.Children.Any())
            {
                obj["Children"] = new JArray(bookmark.Children.Select(ConvertToSimpleJsonObject));
            }

            return obj;
        }

        private JObject CreateChromeFolderNode(string name, List<Bookmark> bookmarks)
        {
            var folderNode = new JObject
            {
                ["children"] = new JArray(bookmarks.Select(ConvertBookmarkToChromeFormat)),
                ["date_added"] = GetCurrentTimestamp(),
                ["date_last_used"] = "0",
                ["date_modified"] = GetCurrentTimestamp(),
                ["guid"] = GenerateGuid(),
                ["id"] = GenerateId(),
                ["name"] = name,
                ["type"] = "folder"
            };

            return folderNode;
        }

        private JObject CreateEdgeFolderNode(string name, List<Bookmark> bookmarks)
        {
            var folderNode = new JObject
            {
                ["children"] = new JArray(bookmarks.Select(ConvertBookmarkToEdgeFormat)),
                ["date_added"] = GetCurrentTimestamp(),
                ["date_last_used"] = "0",
                ["date_modified"] = GetCurrentTimestamp(),
                ["guid"] = Guid.NewGuid().ToString(),
                ["id"] = Guid.NewGuid().ToString(),
                ["name"] = name,
                ["type"] = "folder"
            };

            return folderNode;
        }

        private object CreateExportableObject(Bookmark bookmark)
        {
            return new
            {
                Name = bookmark.Name,
                Url = bookmark.Url,
                Children = bookmark.Children.Select(CreateExportableObject).ToList()
            };
        }

        private ObservableCollection<Bookmark> DeepCopyBookmarks(ObservableCollection<Bookmark> source)
        {
            var copy = new ObservableCollection<Bookmark>();
            foreach (var bookmark in source)
            {
                var bookmarkCopy = new Bookmark
                {
                    Name = bookmark.Name,
                    Url = bookmark.Url,
                    IsFolder = bookmark.IsFolder,
                    IsRootFolder = bookmark.IsRootFolder,
                    Children = DeepCopyBookmarks(bookmark.Children) // Recursively copy children
                };
                copy.Add(bookmarkCopy);
            }
            return copy;
        }

        private void DeleteBookmark(Bookmark selected)
        {
            var result = CustomMessageBox.Show("Are you sure you want to delete this bookmark?", "Confirm Delete", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                var parent = FindParentBookmark(Bookmarks, selected);
                if (parent != null)
                {
                    parent.Children.Remove(selected);
                    _undoStack.Push((parent, selected));
                }
                else
                {
                    Bookmarks.Remove(selected);
                    _undoStack.Push((null, selected));
                }

                OnPropertyChanged(nameof(Bookmarks));
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Bookmark selected)
            {
                DeleteBookmark(selected);
            }
        }

        private void ExpandAndSelectNewItem(Bookmark newItem)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (FindTreeViewItem(BookmarksTreeView, newItem) is TreeViewItem treeViewItem)
                {
                    treeViewItem.IsExpanded = true;
                    treeViewItem.IsSelected = true;
                    treeViewItem.Focus();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void exportBookmarksButton_Click_1(object sender, RoutedEventArgs e)
        {
            try
            {
                var exportList = new JArray();

                // Ensure the top-level name is the first item
                exportList.Add(new JObject
                {
                    ["toplevel_name"] = TopLevelFolderName
                });

                // Add all bookmarks as separate items (not nested under "bookmarks")
                foreach (var bookmark in Bookmarks)
                {
                    exportList.Add(ConvertBookmarkToOriginalFormat(bookmark));
                }

                var json = exportList.ToString(Formatting.Indented);
                Clipboard.SetText(json);
                CustomMessageBox.Show("Bookmarks exported to clipboard in the desired format!", "Confirmation", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Error during export: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private void exportchromexml_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MacExportManager macExportManager = new MacExportManager();
                string plistXml = macExportManager.GenerateMacChromePlistXml(Bookmarks, TopLevelFolderName); // Pass the UI value
                Clipboard.SetText(plistXml);
                CustomMessageBox.Show("Bookmarks successfully exported to macOS plist format!", "Success", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Error exporting to plist: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private void exportxml_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MacExportManager macExportManager = new MacExportManager();
                string plistXml = macExportManager.GenerateMacPlistXml(Bookmarks, TopLevelFolderName); // Pass the UI value
                Clipboard.SetText(plistXml);
                CustomMessageBox.Show("Bookmarks successfully exported to macOS plist format!", "Success", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"Error exporting to plist: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private string ExtractPlistTopLevelName(string plistContent)
        {
            try
            {
                byte[] plistBytes = Encoding.UTF8.GetBytes(plistContent);
                var plistRoot = PropertyListParser.Parse(plistBytes);

                if (plistRoot is NSDictionary rootDict && rootDict.ContainsKey("ManagedFavorites"))
                {
                    NSArray favoritesArray = (NSArray)rootDict["ManagedFavorites"];
                    if (favoritesArray.Count > 0 && favoritesArray.ElementAt(0) is NSDictionary firstEntry)
                    {
                        if (firstEntry.ContainsKey("toplevel_name"))
                        {
                            return firstEntry["toplevel_name"].ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error extracting Plist toplevel_name: {Message}", ex.Message);
            }

            return string.Empty; // Return empty string if not found
        }

        private void FilterBookmarks()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                Bookmarks = new ObservableCollection<Bookmark>(_originalBookmarks);  // Restore original bookmarks
            }
            else
            {
                var filteredBookmarks = new ObservableCollection<Bookmark>();

                foreach (var bookmark in _originalBookmarks)
                {
                    var matchedBookmark = FindMatchingBookmarks(bookmark, SearchQuery);
                    if (matchedBookmark != null)
                    {
                        filteredBookmarks.Add(matchedBookmark);
                    }
                }

                Bookmarks = filteredBookmarks;
            }

            OnPropertyChanged(nameof(Bookmarks));
        }

        private Bookmark FindMatchingBookmarks(Bookmark bookmark, string query)
        {
            bool isMatch = (bookmark.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                           (bookmark.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

            var matchedBookmark = new Bookmark
            {
                Name = bookmark.Name,
                Url = bookmark.Url,
                IsFolder = bookmark.IsFolder,
                IsRootFolder = bookmark.IsRootFolder,
                Children = new ObservableCollection<Bookmark>()
            };

            foreach (var child in bookmark.Children)
            {
                var matchedChild = FindMatchingBookmarks(child, query);
                if (matchedChild != null)
                {
                    matchedBookmark.Children.Add(matchedChild);
                }
            }

            // Return the bookmark if it matches the search or has matching children
            return (isMatch || matchedBookmark.Children.Any()) ? matchedBookmark : null;
        }

        private Bookmark FindParentBookmark(ObservableCollection<Bookmark> bookmarks, Bookmark target)
        {
            foreach (var bookmark in bookmarks)
            {
                if (bookmark.Children.Contains(target))
                    return bookmark;

                var result = FindParentBookmark(bookmark.Children, target);
                if (result != null)
                    return result;
            }
            return null;
        }

        private TreeViewItem FindTreeViewItem(ItemsControl parent, object item)
        {
            if (parent == null) return null;

            for (int i = 0; i < parent.Items.Count; i++)
            {
                var child = parent.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (child == null) continue;

                if (child.DataContext == item)
                    return child;

                var result = FindTreeViewItem(child, item);
                if (result != null)
                    return result;
            }

            return null;
        }

        private string GenerateChecksum(string json)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string GenerateCRC32Checksum(string json)
        {
            using (var crc32 = new Crc32())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                byte[] hash = crc32.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }
        private string GenerateGuid()
        {
            return Guid.NewGuid().ToString();
        }

        private string GenerateId()
        {
            return new Random().Next(1, 1000).ToString(); // Replace with your own ID generation logic if needed
        }

        private string GetAppVersion()
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "1.0.0.0";

            // Remove any build metadata after '+'
            int plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }

            return version;
        }

        private string GetChromeTimestamp()
        {
            DateTime epochStart = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long timestamp = (DateTime.UtcNow - epochStart).Ticks / 10; // Convert ticks to microseconds
            return timestamp.ToString();
        }

        private string GetCurrentTimestamp()
        {
            DateTime epochStart = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long timestamp = (DateTime.UtcNow - epochStart).Ticks / 10; // Convert ticks to microseconds
            return timestamp.ToString();
        }

        private TreeViewItem GetNearestContainer(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as TreeViewItem;
        }

        private bool GetSelectedBookmark(object sender, out Bookmark selectedBookmark)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Bookmark bookmark)
            {
                selectedBookmark = bookmark;
                return true;
            }
            selectedBookmark = null;
            return false;
        }

        private TreeViewItem GetTreeViewItem(object item)
        {
            return (TreeViewItem)BookmarksTreeView.ItemContainerGenerator.ContainerFromItem(item);
        }

        private TreeViewItem GetTreeViewItemForBookmark(Bookmark bookmark)
        {
            return GetTreeViewItemForObject(BookmarksTreeView, bookmark);
        }

        private TreeViewItem GetTreeViewItemForObject(ItemsControl container, object item)
        {
            if (container == null) return null;

            foreach (object child in container.Items)
            {
                TreeViewItem childItem = (TreeViewItem)container.ItemContainerGenerator.ContainerFromItem(child);
                if (childItem == null) continue;

                if (child == item)
                {
                    return childItem;
                }

                TreeViewItem descendant = GetTreeViewItemForObject(childItem, item);
                if (descendant != null) return descendant;
            }

            return null;
        }

        private void importBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            var importWindow = new ImportWindow();
            if (importWindow.ShowDialog() == true)
            {
                ParseBookmarks(importWindow.Json);
                UpdateOriginalBookmarks();
            }
        }

        private bool IsJson(string content)
        {
            return content.StartsWith("{") || content.StartsWith("[");
        }

        private bool IsPlistXml(string content)
        {
            return content.StartsWith("<?xml") || content.Contains("<plist>");
        }

        private void LoadBookmarksFromFile()
        {
            try
            {
                if (File.Exists(BookmarksFilePath))
                {
                    string jsonContent = File.ReadAllText(BookmarksFilePath);
                    var parsedJson = JArray.Parse(jsonContent);

                    Bookmarks.Clear();

                    foreach (var item in parsedJson)
                    {
                        if (item["toplevel_name"] != null)
                        {
                            TopLevelFolderName = item["toplevel_name"].ToString();
                            Log.Information("Loaded Top-Level Folder Name: {TopLevelFolderName}", TopLevelFolderName);
                        }
                        else if (item["name"] != null) // Ensure we only process valid bookmark objects
                        {
                            var bookmark = ParseBookmark(item);
                            MarkFolderStatus(bookmark);
                            Bookmarks.Add(bookmark);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error loading bookmarks: {Message}", ex.Message);
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveBookmarksToFile();
        }

        private void MarkFolderStatus(Bookmark bookmark)
        {
            if (bookmark.Children != null && bookmark.Children.Any())
            {
                bookmark.IsFolder = true;
                foreach (var child in bookmark.Children)
                {
                    MarkFolderStatus(child);
                }
            }
            else
            {
                bookmark.IsFolder = false;
            }
        }

        private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox && textBox.DataContext is Bookmark bookmark)
            {
                bookmark.IsEditing = false;
            }
        }

        private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is Bookmark bookmark)
            {
                bookmark.IsEditing = false;
            }
        }

        private Bookmark ParseBookmark(JToken token)
        {
            var bookmark = new Bookmark
            {
                Name = token["name"]?.ToString() ?? "Unnamed Bookmark",
                Url = token["url"]?.ToString() ?? "",
                IsFolder = token["isFolder"]?.ToObject<bool>() ?? false,
                IsRootFolder = token["isRootFolder"]?.ToObject<bool>() ?? false  // Import the new property
            };

            if (token["children"] != null)
            {
                foreach (var child in token["children"])
                {
                    bookmark.Children.Add(ParseBookmark(child));
                }
            }

            return bookmark;
        }

        private void ParseBookmarks(string content)
        {
            try
            {
                content = content.Trim();
                Log.Information("Bookmark Content Preview (first 300 chars): {Content}", content.Substring(0, Math.Min(300, content.Length)));

                if (IsPlistXml(content))
                {
                    Log.Information("Parsing plist as XML...");
                    var bookmarks = ParsePlistWithClaunia(content);
                    Bookmarks.Clear();

                    // Extract Top-Level Name from plist correctly
                    var plistRoot = ExtractPlistTopLevelName(content);
                    if (!string.IsNullOrEmpty(plistRoot))
                    {
                        TopLevelFolderName = plistRoot;
                        Log.Information("Plist Top-Level Folder Name: {TopLevelFolderName}", TopLevelFolderName);
                    }

                    foreach (var bookmark in bookmarks)
                    {
                        Bookmarks.Add(bookmark);
                        Log.Information("Parsed bookmark: {Name}", bookmark.Name);
                    }
                }
                else if (IsJson(content))
                {
                    content = ReplaceTopLevelName(content); // Ensure correct renaming of keys
                    var parsedJson = JArray.Parse(content);
                    Bookmarks.Clear();

                    // Extract Top-Level Folder Name from JSON
                    if (parsedJson.Count > 0 && parsedJson[0]["toplevel_name"] != null)
                    {
                        TopLevelFolderName = parsedJson[0]["toplevel_name"].ToString();
                        parsedJson.RemoveAt(0); // Remove this object so only bookmarks remain
                        Log.Information("JSON Top-Level Folder Name: {TopLevelFolderName}", TopLevelFolderName);
                    }

                    foreach (var item in parsedJson)
                    {
                        if (item["name"] != null)
                        {
                            var bookmark = ParseBookmark(item);
                            MarkFolderStatus(bookmark);
                            Bookmarks.Add(bookmark);
                        }
                        else
                        {
                            Log.Warning("Bookmark without a 'name' key found.");
                        }
                    }
                }
                else
                {
                    throw new FormatException("Unrecognized content format.");
                }

                OnPropertyChanged(nameof(Bookmarks));
                OnPropertyChanged(nameof(TopLevelFolderName)); // Ensure UI updates
            }
            catch (Exception ex)
            {
                Log.Error("Error parsing bookmarks: {Message}", ex.Message);
                CustomMessageBox.Show("Error parsing bookmarks: " + ex.Message, "Error", MessageBoxButton.OK);
            }
        }

        private List<Bookmark> ParseChromeOrEdgeBookmarks(JToken token)
        {
            var result = new List<Bookmark>();

            foreach (var child in token)
            {
                var bookmark = new Bookmark
                {
                    Name = child["name"]?.ToString(),
                    Url = child["url"]?.ToString(),
                    IsFolder = child["type"]?.ToString() == "folder"
                };

                if (bookmark.IsFolder && child["children"] != null)
                {
                    bookmark.Children = new ObservableCollection<Bookmark>(ParseChromeOrEdgeBookmarks(child["children"]));
                }

                result.Add(bookmark);
            }

            return result;
        }

        private Bookmark ParsePlistBookmark(NSDictionary dict)
        {
            var bookmark = new Bookmark
            {
                Name = dict.ContainsKey("name") ? dict["name"].ToString() : "Unnamed Folder",
                Url = dict.ContainsKey("url") ? dict["url"].ToString() : null,
                IsFolder = dict.ContainsKey("children") && dict["children"] is NSArray // Ensure IsFolder is correctly set
            };

            // Recursively process children
            if (dict.ContainsKey("children") && dict["children"] is NSArray childrenArray)
            {
                bookmark.Children = new ObservableCollection<Bookmark>();
                foreach (var child in childrenArray)
                {
                    if (child is NSDictionary childDict)
                    {
                        bookmark.Children.Add(ParsePlistBookmark(childDict));
                    }
                }
            }

            return bookmark;
        }

        private ObservableCollection<Bookmark> ParsePlistXml(string xmlContent)
        {
            var bookmarks = new ObservableCollection<Bookmark>();

            try
            {
                // Convert the XML string to a byte array
                byte[] plistBytes = Encoding.UTF8.GetBytes(xmlContent);

                // Parse the plist from the byte array
                var plistObject = (NSDictionary)PropertyListParser.Parse(plistBytes);

                if (plistObject.ContainsKey("ManagedFavorites"))
                {
                    var managedFavorites = plistObject["ManagedFavorites"] as NSArray;
                    if (managedFavorites != null)
                    {
                        foreach (var item in managedFavorites)
                        {
                            var dict = item as NSDictionary;
                            if (dict != null)
                            {
                                var bookmark = ConvertPlistDictToBookmark(dict);
                                if (bookmark != null)
                                {
                                    bookmarks.Add(bookmark);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error parsing Plist: {Message}", ex.Message);
            }

            return bookmarks;
        }

        private Bookmark ParseTopLevelBookmark(JToken item)
        {
            var topLevelFolder = new Bookmark
            {
                Name = item["toplevel_name"].ToString(),
                IsFolder = true
            };

            if (item["children"] != null)
            {
                foreach (var child in item["children"])
                {
                    var childBookmark = ParseBookmark(child);
                    if (childBookmark != null)
                    {
                        topLevelFolder.Children.Add(childBookmark);
                    }
                }
            }

            return topLevelFolder;
        }

        private void RefreshTreeView()
        {
            BookmarksTreeView.Items.Refresh();
        }

        private void RefreshTreeViewAndSelect(Bookmark bookmark)
        {
            BookmarksTreeView.Items.Refresh();

            Dispatcher.InvokeAsync(() =>
            {
                var container = GetTreeViewItem(bookmark);
                if (container != null)
                {
                    container.IsExpanded = true;
                    container.IsSelected = true;
                    container.BringIntoView();
                }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private bool RemoveBookmark(ObservableCollection<Bookmark> bookmarks, Bookmark bookmark)
        {
            foreach (var item in bookmarks)
            {
                if (item.Children.Contains(bookmark))
                {
                    item.Children.Remove(bookmark);
                    return true;
                }
                if (RemoveBookmark(item.Children, bookmark)) return true;
            }
            return false;
        }

        private string ReplaceTopLevelName(string content)
        {
            Log.Information("Replacing 'toplevel_name' with 'name' in plist content.");
            return content.Replace("<key>toplevel_name</key>", "<key>name</key>");
        }

        private void SaveBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarksTreeView.SelectedItem is Bookmark selectedBookmark)
            {
                selectedBookmark.Name = bookmarkNameTextBox.Text;
                selectedBookmark.Url = bookmarkUrlTextBox.Text;

                CustomMessageBox.Show("Bookmark updated!", "Confirmation", MessageBoxButton.OK);
            }
        }

        private void SaveBookmarksToChrome()
        {
            string chromeBookmarksPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data\Default\Bookmarks"
            );

            if (!File.Exists(chromeBookmarksPath))
                throw new FileNotFoundException("Google Chrome bookmarks file not found.");

            var rootObject = new JObject
            {
                ["roots"] = new JObject
                {
                    ["bookmark_bar"] = CreateChromeFolderNode("Bookmarks bar", Bookmarks.ToList()),
                    ["other"] = CreateChromeFolderNode("Other bookmarks", new List<Bookmark>()),
                    ["synced"] = CreateChromeFolderNode("Mobile bookmarks", new List<Bookmark>())
                },
                ["version"] = 1
            };

            // Generate the checksum
            string jsonWithoutChecksum = rootObject.ToString(Formatting.None);
            string checksum = GenerateChecksum(jsonWithoutChecksum);

            // Add the checksum to the JSON
            rootObject["checksum"] = checksum;

            File.WriteAllText(chromeBookmarksPath, rootObject.ToString(Formatting.Indented));
            CustomMessageBox.Show("Bookmarks successfully exported to Chrome!", "Success", MessageBoxButton.OK);
        }

        private void SaveBookmarksToEdge()
        {
            string edgeFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data\Default\Bookmarks"
            );

            if (!File.Exists(edgeFilePath))
                throw new FileNotFoundException("Microsoft Edge bookmarks file not found.");

            edgeManager.ExportBookmarks(edgeFilePath, Bookmarks);
        }

        private void SaveBookmarksToFile()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                // Reuse JSON export function for consistency
                var exportList = new JArray(Bookmarks.Select(bookmark => ConvertBookmarkToOriginalFormat(bookmark)));

                var json = new JArray(
                    new JObject { ["toplevel_name"] = TopLevelFolderName ?? "Default Folder" }
                );
                json.Merge(exportList); // Append the bookmarks to match the correct format

                File.WriteAllText(BookmarksFilePath, json.ToString(Formatting.Indented));
                Log.Information("Bookmarks successfully saved on exit.");
            }
            catch (Exception ex)
            {
                Log.Error("Error saving bookmarks on exit: {Message}", ex.Message);
            }
        }
        private void SaveThemePreference(bool isDarkMode)
        {
            Properties.Settings.Default.IsDarkMode = isDarkMode;
            Properties.Settings.Default.UpgradeRequired = false; // Mark settings as upgraded
            Properties.Settings.Default.Save(); // Persist setting
        }

        private void SetClipboardTextWithRetry(string text)
        {
            int retryCount = 5;
            while (retryCount > 0)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
                {
                    retryCount--;
                    System.Threading.Thread.Sleep(100); // Wait 100 ms before retrying
                }
            }

            throw new Exception("Failed to set clipboard text after multiple attempts.");
        }

        private void ShowEmptySpaceContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();
            contextMenu.Items.Add(new MenuItem
            {
                Header = "Add Top-Level Folder",
                Command = new RelayCommand(() => AddTopLevelFolder_Click(null, null))
            });

            contextMenu.IsOpen = true;
        }

        private void SortAlphabetically_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Bookmark selectedBookmark)
            {
                // Check if it's the root item (i.e., no parent folder)
                var parent = FindParentBookmark(Bookmarks, selectedBookmark);

                if (parent == null) // Root item selected
                {
                    SortBookmarks(Bookmarks);
                    CustomMessageBox.Show("All bookmarks sorted alphabetically.", "Success", MessageBoxButton.OK);
                }
                else if (selectedBookmark.IsFolder)
                {
                    SortBookmarks(selectedBookmark.Children);
                    CustomMessageBox.Show($"Folder '{selectedBookmark.Name}' sorted alphabetically.", "Success", MessageBoxButton.OK);
                }
                else
                {
                    CustomMessageBox.Show("Sorting is only available for folders.", "Info", MessageBoxButton.OK);
                }

                RefreshTreeView();
            }
        }

        private void SortBookmarks(ObservableCollection<Bookmark> bookmarks, bool recursive = false)
        {
            var folders = bookmarks.Where(b => b.IsFolder).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var files = bookmarks.Where(b => !b.IsFolder).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

            bookmarks.Clear();
            foreach (var folder in folders)
            {
                bookmarks.Add(folder);
                if (recursive && folder.Children.Any())
                {
                    SortBookmarks(folder.Children, true); // Sort children recursively if recursive flag is true
                }
            }
            foreach (var file in files)
            {
                bookmarks.Add(file);
            }
            UpdateOriginalBookmarks();
        }

        private void SwitchTheme(bool isDark)
        {
            var themeSource = isDark
                ? "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml"
                : "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml";

            var themeResource = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null &&
                    (d.Source.OriginalString.Contains("MaterialDesignTheme.Light.xaml") ||
                     d.Source.OriginalString.Contains("MaterialDesignTheme.Dark.xaml")));

            if (themeResource != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(themeResource);
            }

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themeSource) });
        }

        private void TextBlock_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is Bookmark bookmark)
            {
                bookmark.IsEditing = true;
            }
        }

        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock textBlock && textBlock.DataContext is Bookmark bookmark)
            {
                bookmark.IsEditing = true;
            }
        }

        private void TreeView_Drop(object sender, DragEventArgs e)
        {
            if (_draggedBookmark == null) return;

            var target = (e.OriginalSource as FrameworkElement)?.DataContext as Bookmark;
            if (target == null || target == _draggedBookmark) return;

            // Remove from old location
            RemoveBookmark(Bookmarks, _draggedBookmark);

            // Only allow folders to receive children
            if (!target.IsFolder) return;

            // Add to new location
            target.Children.Add(_draggedBookmark);

            _draggedBookmark = null;
        }

        private void TreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && BookmarksTreeView.SelectedItem is Bookmark selected)
            {
                DeleteBookmark(selected);
            }
        }

        private void UndoDelete()
        {
            if (_undoStack.Count > 0)
            {
                var (parent, bookmark) = _undoStack.Pop();
                if (parent != null)
                {
                    parent.Children.Add(bookmark);
                }
                else
                {
                    Bookmarks.Add(bookmark);
                }

                OnPropertyChanged(nameof(Bookmarks));
            }
            else
            {
            }
        }

        private void UpdateOriginalBookmarks()
        {
            _originalBookmarks = DeepCopyBookmarks(Bookmarks);
        }
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                UndoDelete();
                e.Handled = true;
            }
        }

        #endregion Methods

        #region Classes

        public class RelayCommand : ICommand
        {
            #region Fields

            private readonly Action _execute;

            #endregion Fields

            #region Constructors

            public RelayCommand(Action execute) => _execute = execute;

            #endregion Constructors

            #region Events

            public event EventHandler CanExecuteChanged;

            #endregion Events

            #region Methods

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _execute();

            #endregion Methods
        }

        #endregion Classes
    }
}