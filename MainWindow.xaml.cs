using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class Bookmark : INotifyPropertyChanged
    {
        private string _name;
        private string _url;
        private bool _isFolder;
        private ObservableCollection<Bookmark> _children;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Url
        {
            get => _url;
            set
            {
                _url = value;
                OnPropertyChanged(nameof(Url));
            }
        }

        public bool IsFolder
        {
            get => _isFolder;
            set
            {
                _isFolder = value;
                OnPropertyChanged(nameof(IsFolder));
            }
        }

        public ObservableCollection<Bookmark> Children
        {
            get => _children;
            set
            {
                _children = value;
                OnPropertyChanged(nameof(Children));
            }
        }

        public Bookmark()
        {
            Children = new ObservableCollection<Bookmark>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isFolder)
            {
                // If "IsFolder" is TRUE, hide the TextBox
                return isFolder ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Private Fields

        private ObservableCollection<Bookmark> _bookmarks;
        private Bookmark _draggedBookmark;
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

        #endregion Private Fields

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

        #region Private Methods

        private void clearFormButton_Click(object sender, RoutedEventArgs e)
        {
         
            bookmarkFolderNameTextBox.Clear();
            Bookmarks.Clear();
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
        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Bookmark parent)
            {
                parent.Children.Add(new Bookmark { Name = "New Folder", IsFolder = true });
            }
        }

        private void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Bookmark parent)
            {
                parent.Children.Add(new Bookmark { Name = "New Bookmark", Url = "http://", IsFolder = false });
            }
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is Bookmark bookmark)
            {
                RemoveBookmark(Bookmarks, bookmark);
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
            var jsonObject = new JObject
            {
                ["roots"] = new JObject
                {
                    ["bookmark_bar"] = new JObject
                    {
                        ["name"] = "Bookmarks Bar",
                        ["type"] = "folder",
                        ["children"] = new JArray(Bookmarks.Select(ConvertBookmarkToJson))
                    }
                }
            };

            var json = jsonObject.ToString(Formatting.Indented);

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Bookmark Files (*.json)|*.json"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveFileDialog.FileName, json);
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






        #endregion Private Methods

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
