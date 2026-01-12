using Newtonsoft.Json;
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
using Google_Bookmarks_Manager_for_GPOs.Services;
using Google_Bookmarks_Manager_for_GPOs.Models;
using System.Text.RegularExpressions;

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
        private InsertionAdorner _insertionAdorner; // visual drop feedback
        private DateTime _lastClickTime;
        private ObservableCollection<Bookmark> _originalBookmarks;
        private string _searchQuery;
        private string _topLevelBookmarkFolderName;
        // Plist Format (if needed)
        private string _topLevelFolderName;

        private Stack<(Bookmark parent, Bookmark bookmark)> _undoStack = new Stack<(Bookmark, Bookmark)>();
        private ChromeManager chromeManager = new ChromeManager();
        private EdgeManager edgeManager = new EdgeManager();

        // New Services
        private ProfileService _profileService;
        private List<BookmarkProfile> _profiles;
        private BookmarkProfile _currentProfile;
        private bool _isLoadingProfile = false; // Flag to prevent saves during profile load
        public string SaveLocationPath { get; set; }

        #endregion Fields

        #region Constructors

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _profileService = new ProfileService();

            // Load persisted profiles file path if it exists (after settings were upgraded in App.OnStartup)
            // Prefer path from user settings; if missing, fall back to a pointer file in AppData
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.ProfilesFilePath))
            {
                _profileService.CurrentFilePath = Properties.Settings.Default.ProfilesFilePath;
                Log.Information("Loaded saved profiles path from settings: {Path}", _profileService.CurrentFilePath);
            }
            else
            {
                var pointerPath = System.IO.Path.Combine(AppDataFolder, "profiles.path");
                try
                {
                    if (File.Exists(pointerPath))
                    {
                        var preferred = File.ReadAllText(pointerPath).Trim();
                        if (!string.IsNullOrWhiteSpace(preferred))
                        {
                            _profileService.CurrentFilePath = preferred;
                            Properties.Settings.Default.ProfilesFilePath = preferred;
                            Properties.Settings.Default.Save();
                            Log.Information("Loaded saved profiles path from pointer file: {Path}", preferred);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to read profiles pointer file: {Message}", ex.Message);
                }
            }
            SaveLocationPath = _profileService.CurrentFilePath;

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
            this.Title = $"Bookmark Manager for Intune/GPO - Version {version}";
            DataContext = this;

            // Ensure the preferred file exists (so subsequent loads use it)
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_profileService.CurrentFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(_profileService.CurrentFilePath))
                {
                    // Create with a single default profile so future loads hit this path
                    var defaultProfile = new List<BookmarkProfile>
                    {
                        new BookmarkProfile{ Name = "Default Profile", TopLevelFolderName = "Managed Bookmarks" }
                    };
                    File.WriteAllText(_profileService.CurrentFilePath, System.Text.Json.JsonSerializer.Serialize(defaultProfile, new System.Text.Json.JsonSerializerOptions{ WriteIndented = true }));
                }
                // Persist a pointer to the preferred file path for future runs
                var pointerPath = System.IO.Path.Combine(AppDataFolder, "profiles.path");
                File.WriteAllText(pointerPath, _profileService.CurrentFilePath ?? string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize profiles file path: {Message}", ex.Message);
            }

            // Load profiles and bookmarks from the preferred path
            _ = InitializeProfilesAsync();

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
                Url = "https://",
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
            AutoSaveCurrentProfile();
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
            AutoSaveCurrentProfile();
        }

        private void AddNestedBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.CommandParameter is Bookmark parentFolder &&
                parentFolder != null && // Explicit null check
                parentFolder.IsFolder)
            {
                var newBookmark = new Bookmark { Name = "New Bookmark", Url = "https://", IsFolder = false };

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

                UpdateOriginalBookmarks();
                OnPropertyChanged(nameof(Bookmarks));
                AutoSaveCurrentProfile();
            }
            e.Handled = true;
        }

        // Inline + button: adds a bookmark under the clicked folder
        private void AddBookmarkInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Bookmark parentFolder && parentFolder.IsFolder)
            {
                var newBookmark = new Bookmark { Name = "New Bookmark", Url = "https://", IsFolder = false };

                bool filtering = !string.IsNullOrWhiteSpace(SearchQuery);
                Bookmark targetFolder = parentFolder;
                if (filtering)
                {
                    // Map filtered folder to the original tree
                    targetFolder = FindEquivalentBookmark(_originalBookmarks, parentFolder) ?? parentFolder;
                }

                targetFolder.Children.Add(newBookmark);

                if (filtering)
                {
                    // Rebuild filtered view
                    FilterBookmarks();
                }
                else
                {
                    // Refresh UI, expand parent and select new bookmark
                    Dispatcher.Invoke(() =>
                    {
                        var treeViewItem = GetTreeViewItemForBookmark(targetFolder);
                        if (treeViewItem != null)
                        {
                            treeViewItem.IsExpanded = true;
                            treeViewItem.UpdateLayout();
                        }

                        var newBookmarkItem = GetTreeViewItemForBookmark(newBookmark);
                        if (newBookmarkItem != null)
                        {
                            newBookmarkItem.IsSelected = true;
                            newBookmarkItem.BringIntoView();
                        }
                    });

                    UpdateOriginalBookmarks();
                    OnPropertyChanged(nameof(Bookmarks));
                }

                AutoSaveCurrentProfile();
            }
            e.Handled = true;
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

                AutoSaveCurrentProfile();
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
            AutoSaveCurrentProfile();
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

            bool isExternalUrl = TryExtractUrlAndTitle(e.Data, out _, out _);

            // Show insertion feedback only for internal drags
            if (!isExternalUrl)
            {
                var targetItem = GetNearestContainer(e.OriginalSource as DependencyObject);
                RemoveInsertionAdorner();
                if (targetItem != null)
                {
                    var mousePos = e.GetPosition(targetItem);
                    var third = targetItem.ActualHeight / 3.0;
                    InsertionPosition pos = InsertionPosition.Inside;
                    if (mousePos.Y < third)
                        pos = InsertionPosition.Above;
                    else if (mousePos.Y > targetItem.ActualHeight - third)
                        pos = InsertionPosition.Below;
                    else
                        pos = InsertionPosition.Inside;

                    _insertionAdorner = new InsertionAdorner(targetItem, pos);
                    var layer = AdornerLayer.GetAdornerLayer(targetItem);
                    layer?.Add(_insertionAdorner);
                }
            }

            // If dragging in an external URL, show copy effect and don't show internal move cues
            if (isExternalUrl)
                e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void BookmarksTreeView_Drop(object sender, DragEventArgs e)
        {
            // Handle external URL drops
            if (TryExtractUrlAndTitle(e.Data, out var droppedUrl, out var droppedTitle))
            {
                var extTargetContainer = GetNearestContainer(e.OriginalSource as DependencyObject);
                var extTargetBookmark = extTargetContainer?.DataContext as Bookmark;

                var name = !string.IsNullOrWhiteSpace(droppedTitle) ? droppedTitle : droppedUrl;
                var newBookmark = new Bookmark
                {
                    Name = name,
                    Url = NormalizeUrl(droppedUrl),
                    IsFolder = false
                };

                if (extTargetBookmark != null)
                {
                    if (extTargetBookmark.IsFolder)
                    {
                        extTargetBookmark.Children.Add(newBookmark);
                    }
                    else
                    {
                        var parent = FindParentBookmark(Bookmarks, extTargetBookmark);
                        if (parent != null)
                        {
                            var index = parent.Children.IndexOf(extTargetBookmark);
                            var extMousePos = e.GetPosition(extTargetContainer);
                            var extThird = extTargetContainer.ActualHeight / 3.0;
                            bool extDropBelow = extMousePos.Y > (extTargetContainer.ActualHeight - extThird);
                            if (extDropBelow) index++;
                            parent.Children.Insert(index, newBookmark);
                        }
                        else
                        {
                            var index = Bookmarks.IndexOf(extTargetBookmark);
                            var extMousePos = e.GetPosition(extTargetContainer);
                            var extThird = extTargetContainer.ActualHeight / 3.0;
                            bool extDropBelow = extMousePos.Y > (extTargetContainer.ActualHeight - extThird);
                            if (extDropBelow) index++;
                            Bookmarks.Insert(index, newBookmark);
                        }
                    }
                }
                else
                {
                    Bookmarks.Add(newBookmark);
                }

                OnPropertyChanged(nameof(Bookmarks));
                AutoSaveCurrentProfile();
                RemoveInsertionAdorner();
                _draggedBookmark = null; // ensure internal drag state is cleared
                return;
            }

            if (_draggedBookmark == null) return;

            var targetContainer = GetNearestContainer(e.OriginalSource as DependencyObject);
            var targetBookmark = targetContainer?.DataContext as Bookmark;
            if (targetBookmark == null || targetBookmark == _draggedBookmark)
            {
                RemoveInsertionAdorner();
                return;
            }

            // Prevent moving root folders
            if (_draggedBookmark.IsRootFolder)
            {
                CustomMessageBox.Show("Root folders cannot be moved.", "Operation Not Allowed", MessageBoxButton.OK);
                _draggedBookmark = null;
                RemoveInsertionAdorner();
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

            // Determine intended drop position
            var mousePos = e.GetPosition(targetContainer);
            var third = targetContainer.ActualHeight / 3.0;
            bool dropAbove = mousePos.Y < third;
            bool dropBelow = mousePos.Y > (targetContainer.ActualHeight - third);

            if (!dropAbove && !dropBelow && targetBookmark.IsFolder)
            {
                // Drop inside the folder
                targetBookmark.Children.Add(_draggedBookmark);
            }
            else
            {
                // Reorder at same level above/below target
                var targetParent = FindParentBookmark(Bookmarks, targetBookmark);
                if (targetParent != null)
                {
                    int targetIndex = targetParent.Children.IndexOf(targetBookmark);
                    if (dropBelow) targetIndex++;
                    targetParent.Children.Insert(targetIndex, _draggedBookmark);
                }
                else
                {
                    int targetIndex = Bookmarks.IndexOf(targetBookmark);
                    if (dropBelow) targetIndex++;
                    Bookmarks.Insert(targetIndex, _draggedBookmark);
                }
            }

            _draggedBookmark = null;
            OnPropertyChanged(nameof(Bookmarks));
            AutoSaveCurrentProfile();
            RemoveInsertionAdorner();
        }

        private void BookmarksTreeView_DragLeave(object sender, DragEventArgs e)
        {
            RemoveInsertionAdorner();
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring("view-source:".Length);
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (Uri.TryCreate("https://" + url, UriKind.Absolute, out var uri2))
                    return uri2.ToString();
                return url;
            }
            return uri.ToString();
        }

        private static string ExtractFirstUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, "https?://[^\\s<>\\\"]+", RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;
            return null;
        }

        private static string GuessTitleFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length >= 2 && Regex.IsMatch(lines[1], @"https?://", RegexOptions.IgnoreCase))
                return lines[0].Trim();
            return null;
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
                RemoveInsertionAdorner();
            }
        }

        private void BookmarksTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem item = GetNearestContainer(e.OriginalSource as DependencyObject);

            if (item != null)
            {
                // Focus the TreeView and select the item so active selection styles are used
                BookmarksTreeView.Focus();
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

        private void FavoritesBarEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentProfile != null && !_isLoadingProfile)
            {
                _currentProfile.FavoritesBarEnabled = favoritesBarEnabledCheckBox.IsChecked ?? true;
                _ = _profileService.UpdateProfileAsync(_currentProfile, _profiles);
                Log.Information("Updated FavoritesBarEnabled to {Value} for profile '{Name}'",
                    _currentProfile.FavoritesBarEnabled, _currentProfile.Name);
            }
        }

        private void BookmarkBarEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentProfile != null && !_isLoadingProfile)
            {
                _currentProfile.BookmarkBarEnabled = bookmarkBarEnabledCheckBox.IsChecked ?? true;
                _ = _profileService.UpdateProfileAsync(_currentProfile, _profiles);
                Log.Information("Updated BookmarkBarEnabled to {Value} for profile '{Name}'",
                    _currentProfile.BookmarkBarEnabled, _currentProfile.Name);
            }
        }

        private void clearFormButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirm destructive action
            var result = CustomMessageBox.Show(
                "This will remove ALL bookmarks and folders in the current profile.\n\nAre you sure?",
                "ARE YOU SURE?",
                MessageBoxButton.OKCancel);

            if (result != MessageBoxResult.OK)
                return;

            // Clear all data in current profile
            Bookmarks.Clear();
            UpdateOriginalBookmarks();
            OnPropertyChanged(nameof(Bookmarks));

            // Reset fields
            TopLevelFolderName = string.Empty;
            OnPropertyChanged(nameof(TopLevelFolderName));
            TopLevelFolderNameTextBox.Text = string.Empty;
            bookmarkNameTextBox.Text = string.Empty;
            bookmarkUrlTextBox.Text = string.Empty;

            AutoSaveCurrentProfile();
        }

        // private string ConvertBookmarksToChromeJson()
        // {
        //     var rootObject = new JObject
        //     {
        //         ["roots"] = new JObject
        //         {
        //             ["bookmark_bar"] = new JObject
        //             {
        //                 ["children"] = new JArray(Bookmarks.Select(ConvertBookmarkToChromeFormat))
        //             }
        //         }
        //     };

        //     return rootObject.ToString(Formatting.Indented);
        // }

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
                AutoSaveCurrentProfile();
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Bookmark selected)
            {
                DeleteBookmark(selected);
            }
            else
            {
                // use the highlighted item in the treeview
                if (BookmarksTreeView.SelectedItem is Bookmark selectedBookmark)
                {
                    DeleteBookmark(selectedBookmark);
                }
            }
        }

        // Inline - button: deletes the clicked item (bookmark or folder)
        private void DeleteBookmarkInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Bookmark selected)
            {
                bool filtering = !string.IsNullOrWhiteSpace(SearchQuery);
                if (filtering)
                {
                    // Remove from original tree and rebuild filtered view
                    var original = FindEquivalentBookmark(_originalBookmarks, selected) ?? selected;
                    var parent = FindParentBookmark(_originalBookmarks, original);
                    if (parent != null)
                    {
                        parent.Children.Remove(original);
                    }
                    else
                    {
                        Bookmarks.Remove(original);
                    }
                    FilterBookmarks();
                }
                else
                {
                    DeleteBookmark(selected);
                    UpdateOriginalBookmarks();
                    OnPropertyChanged(nameof(Bookmarks));
                }
                Log.Information("Inline delete '{Name}'", selected?.Name);
            }
            e.Handled = true;
        }

        // Find an equivalent bookmark in the given roots by comparing basic fields
        private Bookmark FindEquivalentBookmark(IEnumerable<Bookmark> roots, Bookmark candidate)
        {
            foreach (var root in roots)
            {
                var found = FindEquivalentBookmarkRecursive(root, candidate);
                if (found != null) return found;
            }
            return null;
        }

        private Bookmark FindEquivalentBookmarkRecursive(Bookmark current, Bookmark candidate)
        {
            if (current == candidate) return current;
            // Basic equivalence check: name + url + folder flag
            if (string.Equals(current.Name, candidate.Name, StringComparison.Ordinal) &&
                string.Equals(current.Url ?? string.Empty, candidate.Url ?? string.Empty, StringComparison.Ordinal) &&
                current.IsFolder == candidate.IsFolder)
            {
                return current;
            }

            foreach (var child in current.Children)
            {
                var found = FindEquivalentBookmarkRecursive(child, candidate);
                if (found != null) return found;
            }
            return null;
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

        private async void importFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            await ImportFromClipboardAsync();
        }

        // Legacy handlers - keeping for compatibility
        private async void exportBookmarksButton_Click_1(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsJsonAsync("ManagedBookmarks");
        }

        private async void exportchromexml_Click(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsPlistAsync("ManagedBookmarks");
        }

        private async void exportxml_Click(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsPlistAsync("ManagedFavorites");
        }

        private void exportxml_Click_Legacy(object sender, RoutedEventArgs e)
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
            // Render from the original snapshot when filtering, but do not mutate the snapshot here
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                // Restore view to originals
                Bookmarks = new ObservableCollection<Bookmark>(_originalBookmarks.Select(CloneForView));
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

        // Create a shallow UI copy for the view so we never bind the original instances directly when rebuilding the view
        private Bookmark CloneForView(Bookmark src)
        {
            var b = new Bookmark
            {
                Name = src.Name,
                Url = src.Url,
                IsFolder = src.IsFolder,
                IsRootFolder = src.IsRootFolder,
            };
            foreach (var child in src.Children)
            {
                b.Children.Add(CloneForView(child));
            }
            return b;
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

        private void RemoveInsertionAdorner()
        {
            if (_insertionAdorner != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement);
                layer?.Remove(_insertionAdorner);
                _insertionAdorner = null;
            }
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

        private static bool TryExtractUrlAndTitle(IDataObject data, out string url, out string title)
        {
            url = null;
            title = null;

            // 1) Unicode/Text
            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                var txt = data.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    txt = txt.Trim();
                    // Some browsers copy "title\nurl" or add extra text. Extract first URL.
                    var u = ExtractFirstUrl(txt);
                    if (!string.IsNullOrEmpty(u) && Uri.TryCreate(u, UriKind.Absolute, out var uri))
                    {
                        url = uri.ToString();
                        title = GuessTitleFromText(txt) ?? uri.Host;
                        return true;
                    }
                }
            }

            if (data.GetDataPresent(DataFormats.Text))
            {
                var txt = data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    txt = txt.Trim();
                    var u = ExtractFirstUrl(txt);
                    if (!string.IsNullOrEmpty(u) && Uri.TryCreate(u, UriKind.Absolute, out var uri))
                    {
                        url = uri.ToString();
                        title = GuessTitleFromText(txt) ?? uri.Host;
                        return true;
                    }
                }
            }

            // 2) HTML format (anchor element)
            if (data.GetDataPresent(DataFormats.Html))
            {
                var html = data.GetData(DataFormats.Html) as string;
                if (!string.IsNullOrEmpty(html))
                {
                    try
                    {
                        // Check CF_HTML header for SourceURL
                        var srcIdx = html.IndexOf("SourceURL:", StringComparison.OrdinalIgnoreCase);
                        if (srcIdx >= 0)
                        {
                            var end = html.IndexOf('\n', srcIdx);
                            if (end > srcIdx)
                            {
                                var srcUrl = html.Substring(srcIdx + 10, end - (srcIdx + 10)).Trim();
                                if (Uri.TryCreate(srcUrl, UriKind.Absolute, out var srcUri))
                                {
                                    url = srcUri.ToString();
                                    // Attempt to pull title from inner text
                                }
                            }
                        }
                        // very simple extraction of href and inner text
                        var hrefIdx = html.IndexOf("href=\"", StringComparison.OrdinalIgnoreCase);
                        if (hrefIdx >= 0)
                        {
                            hrefIdx += 6;
                            var end = html.IndexOf('"', hrefIdx);
                            if (end > hrefIdx)
                            {
                                var href = html.Substring(hrefIdx, end - hrefIdx);
                                if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
                                {
                                    url = uri.ToString();
                                }
                            }
                        }

                        // get title between > and </a>
                        var gt = html.IndexOf('>');
                        var lt = html.IndexOf("</a>", StringComparison.OrdinalIgnoreCase);
                        if (gt >= 0 && lt > gt)
                        {
                            title = html.Substring(gt + 1, lt - gt - 1).Trim();
                        }

                        if (!string.IsNullOrEmpty(url))
                            return true;
                    }
                    catch { }
                }
            }

            // 3) UniformResourceLocatorW (Edge/Chrome address bar)
            try
            {
                if (data.GetDataPresent("UniformResourceLocatorW"))
                {
                    var raw = data.GetData("UniformResourceLocatorW");
                    if (raw is string s && Uri.TryCreate(s, UriKind.Absolute, out var uriS))
                    {
                        url = uriS.ToString();
                        title = uriS.Host;
                        return true;
                    }
                    else if (raw is System.IO.MemoryStream ms)
                    {
                        using var sr = new System.IO.StreamReader(ms, Encoding.Unicode, true, 1024, true);
                        var s2 = sr.ReadToEnd().TrimEnd('\0');
                        if (Uri.TryCreate(s2, UriKind.Absolute, out var uriMs))
                        {
                            url = uriMs.ToString();
                            title = uriMs.Host;
                            return true;
                        }
                    }
                }
                // ANSI variant
                if (data.GetDataPresent("UniformResourceLocator"))
                {
                    var raw = data.GetData("UniformResourceLocator");
                    if (raw is System.IO.MemoryStream msA)
                    {
                        using var sr = new System.IO.StreamReader(msA, Encoding.ASCII, true, 1024, true);
                        var s2 = sr.ReadToEnd().TrimEnd('\0');
                        if (Uri.TryCreate(s2, UriKind.Absolute, out var uriA))
                        {
                            url = uriA.ToString();
                            title = uriA.Host;
                            return true;
                        }
                    }
                }
            }
            catch { }

            // 4) FileDrop for .url files
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = data.GetData(DataFormats.FileDrop) as string[];
                var file = files?.FirstOrDefault();
                if (!string.IsNullOrEmpty(file) && System.IO.Path.GetExtension(file).Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var lines = System.IO.File.ReadAllLines(file);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                            {
                                var u = line.Substring(4).Trim();
                                if (Uri.TryCreate(u, UriKind.Absolute, out var uri))
                                {
                                    url = uri.ToString();
                                    title = System.IO.Path.GetFileNameWithoutExtension(file);
                                    return true;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // 5) Firefox format: text/x-moz-url -> "url\nTitle"
            try
            {
                if (data.GetDataPresent("text/x-moz-url"))
                {
                    var raw = data.GetData("text/x-moz-url") as string;
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var parts = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var u = parts.ElementAtOrDefault(0);
                        var t = parts.ElementAtOrDefault(1);
                        if (!string.IsNullOrEmpty(u) && Uri.TryCreate(u, UriKind.Absolute, out var uri))
                        {
                            url = uri.ToString();
                            title = string.IsNullOrWhiteSpace(t) ? uri.Host : t.Trim();
                            return true;
                        }
                    }
                }
            }
            catch { }

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

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save using new profile service
            await SaveCurrentProfileAsync();
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
                AutoSaveCurrentProfile();
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

        #region Profile Management Methods

        private async System.Threading.Tasks.Task InitializeProfilesAsync()
        {
            try
            {
                _isLoadingProfile = true; // Set flag during initialization
                _profiles = await _profileService.LoadAllProfilesAsync();
                SaveLocationPath = _profileService.CurrentFilePath;
                OnPropertyChanged(nameof(SaveLocationPath));

                // Load the first profile or create a default one
                if (_profiles.Count > 0)
                {
                    await LoadProfileAsync(_profiles[0]);
                }
                else
                {
                    _currentProfile = new BookmarkProfile
                    {
                        Name = "Default Profile",
                        TopLevelFolderName = "Managed Bookmarks"
                    };
                    _profiles.Add(_currentProfile);
                }

                // Populate the ComboBox AFTER loading profile data
                await RefreshProfileComboBox();

                _isLoadingProfile = false; // Clear flag after initialization
                Log.Information("Loaded {Count} profiles", _profiles.Count);
            }
            catch (Exception ex)
            {
                _isLoadingProfile = false; // Clear flag on error
                Log.Error("Error initializing profiles: {Message}", ex.Message);
                // Fallback to legacy load
                LoadBookmarksFromFile();
            }
        }

        private async System.Threading.Tasks.Task LoadProfileAsync(BookmarkProfile profile)
        {
            try
            {
                _isLoadingProfile = true; // Set flag to prevent auto-saves during load

                // Convert BookmarkItems to Bookmarks for UI
                var bookmarks = BookmarkModelConverter.ToBookmarkCollection(profile.Bookmarks);

                Bookmarks.Clear();
                foreach (var bookmark in bookmarks)
                {
                    Bookmarks.Add(bookmark);
                }

                TopLevelFolderName = profile.TopLevelFolderName;

                // Load checkbox states from profile
                favoritesBarEnabledCheckBox.IsChecked = profile.FavoritesBarEnabled;
                bookmarkBarEnabledCheckBox.IsChecked = profile.BookmarkBarEnabled;

                _currentProfile = profile; // Set current profile after loading data
                UpdateOriginalBookmarks();

                // Clear any active search to avoid mixing views with previous profile
                SearchQuery = string.Empty;

                Log.Information("Loaded profile: {Name} with {Count} bookmarks, FavoritesBar={FavBar}, BookmarkBar={BmBar}",
                    profile.Name, profile.Bookmarks?.Count ?? 0,
                    profile.FavoritesBarEnabled, profile.BookmarkBarEnabled);

                _isLoadingProfile = false; // Clear flag after load is complete
            }
            catch (Exception ex)
            {
                _isLoadingProfile = false; // Clear flag on error
                Log.Error("Error loading profile: {Message}", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task SaveCurrentProfileAsync()
        {
            try
            {
                if (_currentProfile == null || _profiles == null)
                    return;

                // Convert UI Bookmarks to BookmarkItems
                _currentProfile.Bookmarks = BookmarkModelConverter.ToBookmarkItemList(Bookmarks);
                _currentProfile.TopLevelFolderName = TopLevelFolderName ?? "Managed Bookmarks";

                await _profileService.UpdateProfileAsync(_currentProfile, _profiles);
                Log.Information("Saved profile: {Name}", _currentProfile.Name);
            }
            catch (Exception ex)
            {
                Log.Error("Error saving profile: {Message}", ex.Message);
            }
        }

        private async void AutoSaveCurrentProfile()
        {
            // Don't auto-save if we're in the middle of loading a profile
            if (_isLoadingProfile)
            {
                Log.Debug("Skipping auto-save during profile load");
                return;
            }

            await SaveCurrentProfileAsync();
        }

        // Export button handlers for specific browser/platform combinations

        private async void ExportEdgeWindows_Click(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsJsonAsync("ManagedFavorites");
        }

        private async void ExportChromeWindows_Click(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsJsonAsync("ManagedBookmarks");
        }

        private async void ExportEdgeMac_Click(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsPlistAsync("ManagedFavorites");
        }

        private async void ExportChromeMac_Click(object sender, RoutedEventArgs e)
        {
            await ExportToClipboardAsPlistAsync("ManagedBookmarks");
        }

        private async void OpenProfileFrom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".json",
                    Title = "Open Profile"
                };

                if (openDialog.ShowDialog() == true)
                {
                    var profile = await _profileService.LoadSingleProfileAsync(openDialog.FileName);
                    if (profile != null)
                    {
                        // Check if profile already exists in current profiles
                        var existing = _profiles.FirstOrDefault(p => p.Id == profile.Id);
                        if (existing != null)
                        {
                            // Update existing profile
                            await LoadProfileAsync(profile);
                            CustomMessageBox.Show($"Loaded profile: {profile.Name}", "Success", MessageBoxButton.OK);
                        }
                        else
                        {
                            // Add new profile to collection
                            _profiles.Add(profile);
                            await _profileService.SaveAllProfilesAsync(_profiles);
                            await RefreshProfileComboBox();
                            ProfileComboBox.SelectedItem = profile;
                            CustomMessageBox.Show($"Loaded and added profile: {profile.Name}", "Success", MessageBoxButton.OK);
                        }

                        Log.Information("Loaded profile from: {Path}", openDialog.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error loading profile: {Message}", ex.Message);
                CustomMessageBox.Show($"Error loading profile: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void ChangeSaveLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".json",
                    FileName = "profiles.json",
                    Title = "Choose Save Location for ALL Profiles",
                    InitialDirectory = !string.IsNullOrEmpty(_profileService.CurrentFilePath)
                        ? Path.GetDirectoryName(_profileService.CurrentFilePath)
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (saveDialog.ShowDialog() == true)
                {
                    _profileService.CurrentFilePath = saveDialog.FileName;

                    // Save the path to settings so it persists between sessions
                    Properties.Settings.Default.ProfilesFilePath = saveDialog.FileName;
                    Properties.Settings.Default.Save();

                    await _profileService.SaveAllProfilesAsync(_profiles);
                    SaveLocationPath = _profileService.CurrentFilePath;
                    OnPropertyChanged(nameof(SaveLocationPath));

                    // Write pointer file as an additional persistence mechanism
                    try
                    {
                        var pointerPath = System.IO.Path.Combine(AppDataFolder, "profiles.path");
                        File.WriteAllText(pointerPath, _profileService.CurrentFilePath ?? string.Empty);
                    }
                    catch (Exception ex2)
                    {
                        Log.Error("Failed to write profiles pointer file: {Message}", ex2.Message);
                    }
                    CustomMessageBox.Show($"All profiles will now be saved to:\n{saveDialog.FileName}\n\nThis location will be used for ALL profiles.\nShare this file with your team for collaboration!\n\nAuto-save is enabled - all changes save automatically.\n\nThis location will be remembered when you restart the app.", "Save Location Changed", MessageBoxButton.OK);
                    Log.Information("Changed save location to: {Path}", saveDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error changing save location: {Message}", ex.Message);
                CustomMessageBox.Show($"Error changing save location: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async System.Threading.Tasks.Task ImportFromClipboardAsync()
        {
            try
            {
                // Show the import window for user to paste bookmarks
                var importWindow = new ImportWindow();
                if (importWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(importWindow.Json))
                {
                    return;
                }

                string content = importWindow.Json.Trim();
                List<BookmarkItem>? bookmarkItems = null;

                // Detect format and parse
                if (content.StartsWith("<?xml") || content.Contains("<plist>") || content.Contains("<key>"))
                {
                    // PLIST XML format
                    Log.Information("Detected PLIST XML format");

                    // Save to temp file and use PlistBookmarkService
                    var tempFile = Path.GetTempFileName();
                    await File.WriteAllTextAsync(tempFile, content);

                    bookmarkItems = PlistBookmarkService.LoadFromPlist(tempFile);
                    File.Delete(tempFile);

                    // Extract top-level folder name
                    var topLevel = BookmarkModelConverter.ExtractTopLevelFolderName(bookmarkItems);
                    if (!string.IsNullOrEmpty(topLevel))
                    {
                        TopLevelFolderName = topLevel;
                    }
                }
                else if (content.StartsWith("[") || content.StartsWith("{"))
                {
                    // JSON format
                    Log.Information("Detected JSON format");

                    var tempFile = Path.GetTempFileName();
                    await File.WriteAllTextAsync(tempFile, content);

                    bookmarkItems = await JsonBookmarkService.LoadFromJsonAsync(tempFile);
                    File.Delete(tempFile);

                    // Extract top-level folder name
                    var topLevel = BookmarkModelConverter.ExtractTopLevelFolderName(bookmarkItems);
                    if (!string.IsNullOrEmpty(topLevel))
                    {
                        TopLevelFolderName = topLevel;
                    }
                }
                else
                {
                    CustomMessageBox.Show("Unrecognized format. Please paste valid JSON or PLIST XML.", "Error", MessageBoxButton.OK);
                    return;
                }

                if (bookmarkItems != null)
                {
                    // Convert to UI model
                    var bookmarks = BookmarkModelConverter.ToBookmarkCollection(bookmarkItems);
                    Bookmarks.Clear();
                    foreach (var bookmark in bookmarks)
                    {
                        Bookmarks.Add(bookmark);
                    }

                    UpdateOriginalBookmarks();
                    OnPropertyChanged(nameof(Bookmarks));
                    OnPropertyChanged(nameof(TopLevelFolderName));

                    AutoSaveCurrentProfile();
                    CustomMessageBox.Show("Bookmarks imported successfully!", "Success", MessageBoxButton.OK);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error importing bookmarks: {Message}", ex.Message);
                CustomMessageBox.Show($"Error importing bookmarks: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private void SetClipboardTextWithRetry(string text, int maxRetries = 5)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return; // Success
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x800401D0)) // CLIPBRD_E_CANT_OPEN
                {
                    if (i == maxRetries - 1)
                        throw; // Rethrow on final attempt

                    System.Threading.Thread.Sleep(50); // Wait 50ms before retry
                }
            }
        }

        private async System.Threading.Tasks.Task ExportToClipboardAsJsonAsync(string policyKey)
        {
            try
            {
                var bookmarkItems = BookmarkModelConverter.CreateBookmarkItemListWithTopLevel(
                    Bookmarks,
                    TopLevelFolderName ?? "Managed Bookmarks"
                );

                // Add toplevel flag for Edge on Windows
                if (policyKey == "ManagedFavorites")
                {
                    // For Edge, mark non-folder items at root level with toplevel: true
                    foreach (var item in bookmarkItems.Skip(1)) // Skip the toplevel_name entry
                    {
                        if (item.Children == null || item.Children.Count == 0)
                        {
                            item.TopLevel = true;
                        }
                    }
                }

                var tempFile = Path.GetTempFileName();
                await JsonBookmarkService.SaveToJsonAsync(tempFile, bookmarkItems);

                var json = await File.ReadAllTextAsync(tempFile);
                File.Delete(tempFile);

                SetClipboardTextWithRetry(json);

                string browserName = policyKey == "ManagedFavorites" ? "Microsoft Edge" : "Google Chrome";
                CustomMessageBox.Show($"Bookmarks exported to clipboard as JSON for {browserName} on Windows!\n\nPolicy Key: {policyKey}", "Success", MessageBoxButton.OK);
                Log.Information("Exported bookmarks as JSON for {Browser} on Windows", browserName);
            }
            catch (Exception ex)
            {
                Log.Error("Error exporting to clipboard: {Message}", ex.Message);
                CustomMessageBox.Show($"Error exporting: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async System.Threading.Tasks.Task ExportToClipboardAsPlistAsync(string keyName)
        {
            try
            {
                var bookmarkItems = BookmarkModelConverter.CreateBookmarkItemListWithTopLevel(
                    Bookmarks,
                    TopLevelFolderName ?? "Managed Bookmarks"
                );

                // Determine which boolean value to use based on keyName
                bool enableBar = keyName == "ManagedFavorites"
                    ? (_currentProfile?.FavoritesBarEnabled ?? true)
                    : (_currentProfile?.BookmarkBarEnabled ?? true);

                var tempFile = Path.GetTempFileName();

                // Use complete PLIST generator with boolean parameter
                PlistBookmarkService.SaveToCompletePlist(tempFile, keyName, bookmarkItems, enableBar);

                var plist = await File.ReadAllTextAsync(tempFile);
                File.Delete(tempFile);

                SetClipboardTextWithRetry(plist);

                string browserName = keyName == "ManagedFavorites" ? "Microsoft Edge" : "Google Chrome";
                string barStatus = enableBar ? "enabled" : "disabled";

                CustomMessageBox.Show(
                    $"Bookmarks exported to clipboard as PLIST for {browserName} on macOS!\n\n" +
                    $"Policy Key: {keyName}\n" +
                    $"Favorites/Bookmark Bar: {barStatus}\n\n" +
                    $"Ready to paste into Intune Configuration Profile.",
                    "Success",
                    MessageBoxButton.OK);

                Log.Information("Exported bookmarks as PLIST for {Browser} on macOS with bar {Status}",
                    browserName, barStatus);
            }
            catch (Exception ex)
            {
                Log.Error("Error exporting to clipboard: {Message}", ex.Message);
                CustomMessageBox.Show($"Error exporting: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore selection changes during initialization
            if (_isLoadingProfile || _profiles == null)
                return;

            if (ProfileComboBox.SelectedItem is BookmarkProfile selectedProfile)
            {
                // Don't reload if it's the same profile
                if (_currentProfile != null && _currentProfile.Id == selectedProfile.Id)
                {
                    Log.Debug("Same profile selected, skipping reload");
                    return;
                }

                // Save current profile before switching (with current bookmarks)
                if (_currentProfile != null)
                {
                    Log.Information("Saving profile '{CurrentName}' before switching to '{NewName}'",
                        _currentProfile.Name, selectedProfile.Name);

                    // Ensure we save the current state
                    _currentProfile.Bookmarks = BookmarkModelConverter.ToBookmarkItemList(Bookmarks);
                    _currentProfile.TopLevelFolderName = TopLevelFolderName ?? "Managed Bookmarks";
                    await _profileService.UpdateProfileAsync(_currentProfile, _profiles);

                    Log.Information("Saved {Count} bookmarks from profile '{Name}'",
                        _currentProfile.Bookmarks.Count, _currentProfile.Name);
                }

                // Now load the new profile
                await LoadProfileAsync(selectedProfile);
            }
        }

        private async void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create an input dialog
                var inputWindow = new InputDialog();
                inputWindow.Title = "New Profile";
                inputWindow.InputLabel = "Profile Name:";
                inputWindow.InputText = "New Profile";

                if (inputWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputWindow.InputText))
                {
                    string profileName = inputWindow.InputText.Trim();

                    // Get top-level folder name
                    var folderWindow = new InputDialog();
                    folderWindow.Title = "Top-Level Folder Name";
                    folderWindow.InputLabel = "Folder Name:";
                    folderWindow.InputText = "Managed Bookmarks";

                    if (folderWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(folderWindow.InputText))
                    {
                        string folderName = folderWindow.InputText.Trim();

                        var newProfile = await _profileService.CreateProfileAsync(profileName, folderName, _profiles);
                        await RefreshProfileComboBox();
                        ProfileComboBox.SelectedItem = newProfile;

                        CustomMessageBox.Show($"Profile '{profileName}' created successfully!", "Success", MessageBoxButton.OK);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error creating profile: {Message}", ex.Message);
                CustomMessageBox.Show($"Error creating profile: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProfile == null)
                {
                    CustomMessageBox.Show("No profile selected.", "Error", MessageBoxButton.OK);
                    return;
                }

                var inputWindow = new InputDialog();
                inputWindow.Title = "Rename Profile";
                inputWindow.InputLabel = "New Profile Name:";
                inputWindow.InputText = _currentProfile.Name;

                if (inputWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputWindow.InputText))
                {
                    string newName = inputWindow.InputText.Trim();
                    await _profileService.RenameProfileAsync(_currentProfile.Id, newName, _profiles);
                    await RefreshProfileComboBox();

                    CustomMessageBox.Show($"Profile renamed to '{newName}' successfully!", "Success", MessageBoxButton.OK);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error renaming profile: {Message}", ex.Message);
                CustomMessageBox.Show($"Error renaming profile: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProfile == null)
                {
                    CustomMessageBox.Show("No profile selected.", "Error", MessageBoxButton.OK);
                    return;
                }

                if (_profiles.Count <= 1)
                {
                    CustomMessageBox.Show("Cannot delete the last profile.", "Error", MessageBoxButton.OK);
                    return;
                }

                var result = CustomMessageBox.Show(
                    $"Are you sure you want to delete profile '{_currentProfile.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.OK)
                {
                    await _profileService.DeleteProfileAsync(_currentProfile.Id, _profiles);

                    // Load the first remaining profile
                    if (_profiles.Count > 0)
                    {
                        await RefreshProfileComboBox();
                        ProfileComboBox.SelectedIndex = 0;
                    }

                    CustomMessageBox.Show("Profile deleted successfully!", "Success", MessageBoxButton.OK);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error deleting profile: {Message}", ex.Message);
                CustomMessageBox.Show($"Error deleting profile: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async void CopyProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProfile == null)
                {
                    CustomMessageBox.Show("No profile selected.", "Error", MessageBoxButton.OK);
                    return;
                }

                // Ask for new profile name
                var inputWindow = new InputDialog();
                inputWindow.Title = "Copy Profile";
                inputWindow.InputLabel = "New Profile Name:";
                inputWindow.InputText = _currentProfile.Name + " - Copy";

                if (inputWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(inputWindow.InputText))
                    return;

                string newName = inputWindow.InputText.Trim();

                // Deep copy bookmarks
                var sourceBookmarks = BookmarkModelConverter.ToBookmarkCollection(_currentProfile.Bookmarks);
                var cloned = DeepCopyBookmarks(sourceBookmarks);
                var clonedItems = BookmarkModelConverter.ToBookmarkItemList(cloned);

                var newProfile = new BookmarkProfile
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = newName,
                    TopLevelFolderName = _currentProfile.TopLevelFolderName,
                    Bookmarks = clonedItems,
                    LastModified = DateTime.Now
                };

                _profiles.Add(newProfile);
                await _profileService.SaveAllProfilesAsync(_profiles);
                await RefreshProfileComboBox();
                ProfileComboBox.SelectedItem = newProfile;

                CustomMessageBox.Show($"Profile copied to '{newName}'.", "Success", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                Log.Error("Error copying profile: {Message}", ex.Message);
                CustomMessageBox.Show($"Error copying profile: {ex.Message}", "Error", MessageBoxButton.OK);
            }
        }

        private async System.Threading.Tasks.Task RefreshProfileComboBox()
        {
            var wasLoading = _isLoadingProfile;
            _isLoadingProfile = true; // Prevent SelectionChanged from firing during refresh

            ProfileComboBox.ItemsSource = null;
            ProfileComboBox.ItemsSource = _profiles;
            ProfileComboBox.DisplayMemberPath = "Name";
            ProfileComboBox.SelectedItem = _currentProfile;

            if (!wasLoading)
                _isLoadingProfile = false; // Restore flag if it wasn't already set
        }

        #endregion Profile Management Methods

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