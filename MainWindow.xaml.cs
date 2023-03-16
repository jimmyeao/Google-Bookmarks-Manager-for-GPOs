using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Google_Bookmarks_Manager_for_GPOs
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<Bookmark> bookmarks;
        private string topLevelName;
        public MainWindow()
        {
            InitializeComponent();
            bookmarks = new List<Bookmark>();
            bookmarks.Add(new Bookmark());
            bookmarksDataGrid.ItemsSource = bookmarks;
            bookmarksDataGrid.CanUserAddRows = true;
        }

        private void bookmarksDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (bookmarks.Any())
            {
                bookmarks.RemoveAt(0);
                bookmarksDataGrid.Items.Refresh();
            }
        }
        private void ParseBookmarks(string json)
        {
            try
            {
                var parsedJson = JArray.Parse(json);

                topLevelName = parsedJson.First["toplevel_name"].ToString();
                bookmarkFolderNameTextBox.Text = topLevelName;

                bookmarks = JsonConvert.DeserializeObject<List<Bookmark>>(parsedJson.ToString());

                // Remove the first element from the bookmarks list
                bookmarks.RemoveAt(0);

                bookmarksDataGrid.ItemsSource = bookmarks;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error parsing bookmarks: " + ex.Message);
            }
        }



        private void importBookmarksButton_Click(object sender, RoutedEventArgs e)
        {
            var importWindow = new ImportWindow();
            if (importWindow.ShowDialog() == true)
            {
                ParseBookmarks(importWindow.Json);
            }
        }


        private void exportBookmarksButton_Click_1(object sender, RoutedEventArgs e)
        {
            // Create a new JSON object with an array of bookmarks
            JArray bookmarksArray = new JArray();
            string topLevelName = bookmarkFolderNameTextBox.Text;
            bookmarksArray.Add(new JObject(new JProperty("toplevel_name", topLevelName)));
            foreach (Bookmark bookmark in bookmarks)
            {
                if (bookmark.Name != null || bookmark.Url != null) // check if the bookmark is not null
                {
                    JObject bookmarkObject = new JObject(new JProperty("name", bookmark.Name ?? ""), new JProperty("url", bookmark.Url ?? ""));
                    bookmarksArray.Add(bookmarkObject);
                }
            }

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



        private void addBookmarkButton_Click(object sender, RoutedEventArgs e)
        {

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
    }


    public class Bookmark
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }
}

