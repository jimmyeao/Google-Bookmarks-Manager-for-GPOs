using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<Bookmark> _bookmarks;
        private DateTime _lastClickTime;
        private Stack<(Bookmark parent, Bookmark bookmark)> _undoStack = new Stack<(Bookmark, Bookmark)>();

        private Bookmark _draggedBookmark;
        private string _topLevelBookmarkFolderName;

        public string TopLevelBookmarkFolderName
        {
            get => _topLevelBookmarkFolderName;
            set
            {
                _topLevelBookmarkFolderName = value;
                OnPropertyChanged(nameof(TopLevelBookmarkFolderName));
            }
        }

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

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();

            // Ensure initialization happens only once
            if (Bookmarks == null)
            {
                Bookmarks = new ObservableCollection<Bookmark>();
            }
            SwitchTheme(true);
            DataContext = this;
        }

        private void TreeView_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var treeView = sender as TreeView;
            var item = treeView.SelectedItem as Bookmark;
            if (item != null)
            {
                _draggedBookmark = item;
                DragDrop.DoDragDrop(treeView, item, DragDropEffects.Move);
            }
        }

        private void TextBlock_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var currentTime = DateTime.Now;
            if ((currentTime - _lastClickTime).TotalMilliseconds < 400)  // Detect double-click
            {
                if (sender is TextBlock textBlock && textBlock.DataContext is Bookmark bookmark)
                {
                    bookmark.IsEditing = true;
                }
            }
            _lastClickTime = currentTime;
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

        private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is Bookmark bookmark)
            {
                bookmark.IsEditing = false;
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

        private void SaveBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarksTreeView.SelectedItem is Bookmark selectedBookmark)
            {
                selectedBookmark.Name = bookmarkNameTextBox.Text;
                selectedBookmark.Url = bookmarkUrlTextBox.Text;
                MessageBox.Show("Bookmark updated!");
            }
        }

        private void TreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && BookmarksTreeView.SelectedItem is Bookmark selected)
            {
                DeleteBookmark(selected);
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Bookmark selected)
            {
                DeleteBookmark(selected);
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

        private TreeViewItem GetNearestContainer(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as TreeViewItem;
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

        public class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute) => _execute = execute;

            public event EventHandler CanExecuteChanged;

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _execute();
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

        private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox && textBox.DataContext is Bookmark bookmark)
            {
                bookmark.IsEditing = false;
            }
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

            OnPropertyChanged(nameof(Bookmarks));
            ExpandAndSelectNewItem(newFolder);
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

            OnPropertyChanged(nameof(Bookmarks));
            ExpandAndSelectNewItem(newBookmark);
        }

        private void AddTopLevelFolder_Click(object sender, RoutedEventArgs e)
        {
            Bookmarks.Add(new Bookmark
            {
                Name = "New Folder",
                IsFolder = true
            });
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

        private void AddNestedBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Bookmark parentFolder && parentFolder.IsFolder)
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

        private TreeViewItem GetTreeViewItem(object item)
        {
            return (TreeViewItem)BookmarksTreeView.ItemContainerGenerator.ContainerFromItem(item);
        }

        private void clearFormButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear the TreeView by resetting the Bookmarks collection
            Bookmarks.Clear();

            // Clear the text boxes
            bookmarkNameTextBox.Text = string.Empty;
            bookmarkUrlTextBox.Text = string.Empty;

            MessageBox.Show("Form cleared successfully!", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void exportBookmarksButton_Click_1(object sender, RoutedEventArgs e)
        {
            var jsonArray = new JArray(Bookmarks.Select(ConvertBookmarkToOriginalFormat));

            var json = jsonArray.ToString(Formatting.Indented);
            Clipboard.SetText(json);

            MessageBox.Show("Bookmarks exported to clipboard!", "Confirmation", MessageBoxButton.OK);
        }

        private JObject ConvertBookmarkToOriginalFormat(Bookmark bookmark)
        {
            var obj = new JObject();

            if (bookmark.IsFolder)
            {
                obj["name"] = bookmark.Name;

                if (bookmark.Children.Any())
                {
                    obj["children"] = new JArray(bookmark.Children.Select(ConvertBookmarkToOriginalFormat));
                }
            }
            else
            {
                obj["name"] = bookmark.Name;
                obj["url"] = bookmark.Url;
            }

            return obj;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                UndoDelete();
                e.Handled = true;
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

                CustomMessageBox.Show($"Undo successful: {bookmark.Name}", "Undo", MessageBoxButton.OK);
            }
            else
            {
                CustomMessageBox.Show("Nothing to undo!", "Undo", MessageBoxButton.OK);
            }
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
                CustomMessageBox.Show("Bookmark deleted successfully.", "Delete", MessageBoxButton.OK);
            }
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var json = JsonConvert.SerializeObject(Bookmarks, Formatting.Indented);
            Clipboard.SetText(json);
            MessageBox.Show("Bookmarks copied to clipboard!", "Confirmation", MessageBoxButton.OK);
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(true);
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
                MessageBox.Show("Please select a folder to add a nested folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(false);
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

        private void importBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            var importWindow = new ImportWindow();
            if (importWindow.ShowDialog() == true)
            {
                ParseBookmarks(importWindow.Json);
            }
        }

        private void ParseBookmarks(string json)
        {
            try
            {
                var parsedJson = JArray.Parse(json);

                // Clear existing bookmarks
                Bookmarks.Clear();

                foreach (var item in parsedJson)
                {
                    // If the item contains a top-level name, treat it as a folder
                    if (item["toplevel_name"] != null)
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
                                var bookmark = ParseBookmark(child);
                                topLevelFolder.Children.Add(bookmark);
                            }
                        }

                        Bookmarks.Add(topLevelFolder);
                    }
                    else
                    {
                        // Directly parse other bookmarks that do not have a "toplevel_name"
                        var bookmark = ParseBookmark(item);
                        Bookmarks.Add(bookmark);
                    }
                }

                // Force UI refresh
                OnPropertyChanged(nameof(Bookmarks));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error parsing bookmarks: " + ex.Message);
            }
        }

        private Bookmark ParseBookmark(JToken token)
        {
            var bookmark = new Bookmark
            {
                Name = token["name"]?.ToString() ?? "Unnamed Bookmark",
                IsFolder = token["children"] != null,
                Url = token["url"]?.ToString() ?? ""  // Ensure URL is set even if missing
            };

            if (bookmark.IsFolder)
            {
                foreach (var child in token["children"])
                {
                    bookmark.Children.Add(ParseBookmark(child));
                }
            }

            return bookmark;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}