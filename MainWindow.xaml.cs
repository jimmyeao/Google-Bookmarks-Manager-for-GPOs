using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class Bookmark
    {
        #region Public Properties

        public string FolderName { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }

        #endregion Public Properties
    }

    public partial class MainWindow : Window
    {
        #region Private Fields

        private List<Bookmark> bookmarks;
        private string topLevelName;

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion Private Fields

        #region Public Constructors

        private static bool _isDarkModeEnabled;

        public MainWindow()
        {
            InitializeComponent();

            // Set up the data context
            bookmarks = new List<Bookmark>();
            bookmarks.Add(new Bookmark());
            bookmarksDataGrid.ItemsSource = bookmarks;
            bookmarksDataGrid.CanUserAddRows = true;
            DataContext = this;
            // Clear the text boxes
            bookmarkUrlTextBox.Clear();
            bookmarkFolderNameTextBox.Clear();

            // Clear the bookmarks list and refresh the data grid
            bookmarks.Clear();
            bookmarksDataGrid.Items.Refresh();

            // Add an event handler for the PropertyChanged event
            PropertyChanged += MainWindow_PropertyChanged;
        }

        public static bool IsDarkModeEnabled
        {
            get => _isDarkModeEnabled;
            set
            {
                if (_isDarkModeEnabled != value)
                {
                    _isDarkModeEnabled = value;
                    UpdateTheme();
                }
            }
        }

        private static void UpdateTheme()
        {
            if (IsDarkModeEnabled)
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri("DarkTheme.xaml", UriKind.Relative) });
            }
            else
            {
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri("LightTheme.xaml", UriKind.Relative) });
            }
        }

        private void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IsDarkModeEnabled))
            {
                UpdateTheme();
            }
        }

        private void SwitchTheme(bool isDark)
        {
            var themeSource = isDark
                ? "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml"
                : "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml";

            var themeResource = Application.Current.Resources.MergedDictionaries
                .Where(d => d.Source != null && (d.Source.OriginalString.Contains("MaterialDesignTheme.Light.xaml") || d.Source.OriginalString.Contains("MaterialDesignTheme.Dark.xaml")))
                .FirstOrDefault();

            if (themeResource != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(themeResource);
            }

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themeSource) });
        }

        #endregion Public Constructors

        #region Private Methods

        private void addBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
        }

        private void bookmarksDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (bookmarks.Any())
            {
                bookmarks.RemoveAt(0);
                bookmarksDataGrid.Items.Refresh();
            }
        }

        private void clearFormButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear the text boxes
            bookmarkUrlTextBox.Clear();
            bookmarkFolderNameTextBox.Clear();

            // Clear the bookmarks list and refresh the data grid
            bookmarks.Clear();
            bookmarksDataGrid.Items.Refresh();
        }

        private void exportBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (bookmarks != null)
            {
                try
                {
                    string parentFolderName = bookmarkFolderNameTextBox.Text;
                    JArray bookmarksArray = BuildBookmarksArray(bookmarks, parentFolderName);
                    string json = bookmarksArray.ToString(Formatting.Indented);
                    var saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                    saveFileDialog.Filter = "Bookmark Files (*.json)|*.json";
                    if (saveFileDialog.ShowDialog() == true)
                    {
                        File.WriteAllText(saveFileDialog.FileName, json);
                    }

                    ShowCustomMessageBox("Bookmarks exported successfully", "Confirmation", MessageBoxButton.OK);
                }
                catch (Exception ex)
                {
                    ShowCustomMessageBox("An error occurred while exporting bookmarks.Error: " + ex.Message, "Error", MessageBoxButton.OK);
                }
            }
            else
            {
                ShowCustomMessageBox("No bookmarks to export.", "Warning", MessageBoxButton.OK);
            }
        }

        private void importBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            var importWindow = new ImportWindow();
            if (importWindow.ShowDialog() == true)
            {
                string jsonContent = importWindow.Json;
                try
                {
                    List<Bookmark> bookmarksList = ParseBookmarks(jsonContent);
                    bookmarks = bookmarksList;
                    bookmarksDataGrid.ItemsSource = bookmarks;

                    // Set the top-level folder name in the bookmarkFolderNameTextBox control
                    JArray jsonArray = JArray.Parse(jsonContent);
                    var topLevelFolder = jsonArray.FirstOrDefault(jo => jo["toplevel_name"] != null);
                    if (topLevelFolder != null)
                    {
                        bookmarkFolderNameTextBox.Text = topLevelFolder["toplevel_name"].ToString();
                    }
                    else
                    {
                        bookmarkFolderNameTextBox.Text = "Bookmarks";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("An error occurred while importing bookmarks. Please ensure the JSON is valid. Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private List<Bookmark> ParseBookmarks(string json)
        {
            JArray jsonArray = JArray.Parse(json);
            List<Bookmark> bookmarksList = new List<Bookmark>();

            foreach (JToken item in jsonArray)
            {
                if (item["toplevel_name"] != null)
                {
                    bookmarkFolderNameTextBox.Text = item["toplevel_name"].ToString();
                    continue;
                }

                if (item["children"] != null)
                {
                    string folderName = item["name"]?.ToString() ?? "Bookmarks";
                    if (folderName == bookmarkFolderNameTextBox.Text)
                    {
                        folderName = "Bookmarks";
                    }

                    foreach (JToken child in item["children"])
                    {
                        bookmarksList.Add(new Bookmark
                        {
                            Name = child["name"].ToString(),
                            Url = child["url"].ToString(),
                            FolderName = folderName
                        });
                    }
                }
                else
                {
                    bookmarksList.Add(new Bookmark
                    {
                        Name = item["name"].ToString(),
                        Url = item["url"].ToString(),
                        FolderName = "Bookmarks"
                    });
                }
            }

            return bookmarksList;
        }

        #endregion Private Methods

        #region Public Methods

        public static MessageBoxResult ShowCustomMessageBox(string message, string caption, MessageBoxButton buttons)
        {
            var customMessageBox = new CustomMessageBox(message, caption, buttons);
            customMessageBox.Owner = Application.Current.MainWindow;
            customMessageBox.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            customMessageBox.ShowDialog();
            return customMessageBox.Result;
        }

        #endregion Public Methods

        private JArray BuildBookmarksArray(IEnumerable<Bookmark> bookmarksList, string parentFolderName)
        {
            var bookmarksArray = new JArray();

            // Group bookmarks by folder name
            var groupedBookmarks = bookmarksList.GroupBy(x => x.FolderName);

            bool topLevelFolderAdded = false;
            foreach (var group in groupedBookmarks)
            {
                JArray childrenArray = new JArray();
                foreach (var bookmark in group)
                {
                    if (!string.IsNullOrEmpty(bookmark.Name) || !string.IsNullOrEmpty(bookmark.Url))
                    {
                        JObject bookmarkObject = new JObject
                        {
                            ["name"] = bookmark.Name,
                            ["url"] = bookmark.Url
                        };
                        childrenArray.Add(bookmarkObject);
                    }
                }

                if (childrenArray.Count > 0)
                {
                    // If the group key is equal to the parent folder name, add bookmarks directly
                    // to the bookmarksArray
                    if (group.Key == parentFolderName)
                    {
                        if (!topLevelFolderAdded)
                        {
                            JObject topLevelFolder = new JObject
                            {
                                ["toplevel_name"] = parentFolderName,
                               // ["children"] = childrenArray
                            };
                            bookmarksArray.Add(topLevelFolder);
                            topLevelFolderAdded = true;
                            foreach (var child in childrenArray)
                            {
                                bookmarksArray.Add(child);
                            }
                        }
                        else
                        {
                            foreach (var child in childrenArray)
                            {
                                bookmarksArray.Add(child);
                            }
                        }
                    }
                    else
                    {
                        // Create a new folder object and add the children array to it
                        JObject folderObject = new JObject
                        {
                            ["name"] = group.Key == "Bookmarks" ? parentFolderName : group.Key,
                            ["children"] = childrenArray
                        };
                        bookmarksArray.Add(folderObject);
                    }
                } // End of foreach loop iterating over groupedBookmarks
            }

            return bookmarksArray;
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var isDark = darkModeCheckBox.IsChecked.HasValue && darkModeCheckBox.IsChecked.Value;
            SwitchTheme(isDark);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var isDark = darkModeCheckBox.IsChecked.HasValue && darkModeCheckBox.IsChecked.Value;
            SwitchTheme(isDark);
        }

        private void ExportClipboardButton_Click(object sender, RoutedEventArgs e)
        {
            string parentFolderName = string.IsNullOrEmpty(bookmarkFolderNameTextBox.Text) ? "Bookmarks" : bookmarkFolderNameTextBox.Text;

            JArray bookmarksArray = BuildBookmarksArray(bookmarks, parentFolderName);

            // Convert the JSON object to a single line of text
            var json = bookmarksArray.ToString(Formatting.None);

            // Copy the JSON string to the clipboard
            Clipboard.SetText(json);

            // Show a confirmation message
            ShowCustomMessageBox("Bookmarks copied to clipboard!", "Confirmation", MessageBoxButton.OK);
        }
    }
}