using Microsoft.Win32;
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
        public string FolderName { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
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

        private void exportBookmarksButton_Click_1(object sender, RoutedEventArgs e)
        {
            JArray bookmarksArray = BuildBookmarksArray();

            // Convert the JSON object to a single line of text
            var json = bookmarksArray.ToString(Formatting.None);

            // Save the JSON string to a file
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog();
            saveFileDialog.Filter = "Bookmark Files (*.json)|*.json";
            if (saveFileDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveFileDialog.FileName, json);
            }
        }


        private void importBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            var importWindow = new ImportWindow();
            if (importWindow.ShowDialog() == true)
            {
                string jsonContent = importWindow.Json;
                List<Bookmark> bookmarksList = ParseBookmarks(jsonContent);
                bookmarks = bookmarksList;
                bookmarksDataGrid.ItemsSource = bookmarks;
            }
        }


        public List<Bookmark> ParseBookmarks(string jsonContent)
        {
            var bookmarksData = JsonConvert.DeserializeObject<JArray>(jsonContent);
            List<Bookmark> bookmarksList = new List<Bookmark>();

            string folderName = "Bookmarks"; // Set default folder name

            foreach (var item in bookmarksData)
            {
                if (item["toplevel_name"] != null)
                {
                    folderName = item["toplevel_name"].ToString();
                }
                else if (item["name"] != null && item["url"] != null)
                {
                    Bookmark bookmark = new Bookmark
                    {
                        FolderName = folderName,
                        Name = item["name"].ToString(),
                        Url = item["url"].ToString()
                    };
                    bookmarksList.Add(bookmark);
                }
                else if (item["name"] != null && item["children"] != null)
                {
                    string subFolderName = item["name"].ToString();
                    JArray children = (JArray)item["children"];

                    foreach (var child in children)
                    {
                        if (child["name"] != null && child["url"] != null)
                        {
                            Bookmark childBookmark = new Bookmark
                            {
                                FolderName = subFolderName,
                                Name = child["name"].ToString(),
                                Url = child["url"].ToString()
                            };
                            bookmarksList.Add(childBookmark);
                        }
                    }
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            JArray bookmarksArray = BuildBookmarksArray();

            // Convert the JSON object to a single line of text
            var json = bookmarksArray.ToString(Formatting.None);

            // Copy the JSON string to the clipboard
            Clipboard.SetText(json);

            // Show a confirmation message
            ShowCustomMessageBox("Bookmarks copied to clipboard!", "Confirmation", MessageBoxButton.OK);
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
        private JArray BuildBookmarksArray()
        {
            var groupedBookmarks = bookmarks.GroupBy(b => b.FolderName);
            JArray bookmarksArray = new JArray();

            foreach (var group in groupedBookmarks)
            {
                if (group.Key == "Bookmarks")
                {
                    foreach (var bookmark in group)
                    {
                        JObject bookmarkObject = new JObject(new JProperty("name", bookmark.Name), new JProperty("url", bookmark.Url));
                        bookmarksArray.Add(bookmarkObject);
                    }
                }
                else
                {
                    JObject subFolder = new JObject(new JProperty("name", group.Key));
                    JArray children = new JArray();

                    foreach (var bookmark in group)
                    {
                        JObject bookmarkObject = new JObject(new JProperty("name", bookmark.Name), new JProperty("url", bookmark.Url));
                        children.Add(bookmarkObject);
                    }

                    subFolder.Add(new JProperty("children", children));
                    bookmarksArray.Add(subFolder);
                }
            }

            return bookmarksArray;
        }

    }
}