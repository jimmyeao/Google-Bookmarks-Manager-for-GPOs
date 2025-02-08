using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class ChromeManager
    {
        public void ImportBookmarks(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("Chrome bookmarks file not found.");

                var json = File.ReadAllText(filePath);
                var parsedJson = JObject.Parse(json);
                var importedBookmarks = ParseChromeBookmarks(parsedJson["roots"]["bookmark_bar"]["children"]);
                foreach (var bookmark in importedBookmarks)
                {
                    bookmarks.Add(bookmark);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error importing Chrome bookmarks: {ex.Message}");
            }
        }

        public void ExportBookmarks(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            var rootObject = new JObject
            {
                ["roots"] = new JObject
                {
                    ["bookmark_bar"] = CreateFolderNode("Bookmarks bar", bookmarks.ToList()),
                    ["other"] = CreateFolderNode("Other bookmarks", new List<Bookmark>()),
                    ["synced"] = CreateFolderNode("Mobile bookmarks", new List<Bookmark>())
                },
                ["version"] = 1
            };

            File.WriteAllText(filePath, rootObject.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private List<Bookmark> ParseChromeBookmarks(JToken token)
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
                    bookmark.Children = new ObservableCollection<Bookmark>(ParseChromeBookmarks(child["children"]));

                result.Add(bookmark);
            }
            return result;
        }

        private JObject CreateFolderNode(string name, List<Bookmark> bookmarks)
        {
            return new JObject
            {
                ["children"] = new JArray(bookmarks.Select(ConvertToChromeFormat)),
                ["date_added"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                ["name"] = name,
                ["type"] = "folder"
            };
        }

        private JObject ConvertToChromeFormat(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["name"] = bookmark.Name,
                ["type"] = bookmark.IsFolder ? "folder" : "url"
            };

            if (!bookmark.IsFolder)
                obj["url"] = bookmark.Url;
            else
                obj["children"] = new JArray(bookmark.Children.Select(ConvertToChromeFormat));

            return obj;
        }
    }
}
