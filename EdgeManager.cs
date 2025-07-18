using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class EdgeManager
    {
        #region Methods

        public void ExportBookmarks(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            var rootObject = new JObject
            {
                ["roots"] = new JObject
                {
                    ["bookmark_bar"] = CreateEdgeFolderNode("Favourites bar", bookmarks.ToList()),
                    ["other"] = CreateEdgeFolderNode("Other favourites", new List<Bookmark>()),
                    ["synced"] = CreateEdgeFolderNode("Mobile favourites", new List<Bookmark>())
                },
                ["version"] = 1
            };

            // Generate checksum
            string jsonWithoutChecksum = rootObject.ToString(Newtonsoft.Json.Formatting.None);
            string checksum = GenerateChecksum(jsonWithoutChecksum);

            // Add checksum to JSON
            rootObject["checksum"] = checksum;

            File.WriteAllText(filePath, rootObject.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void ImportBookmarks(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Microsoft Edge bookmarks file not found.");

            var json = File.ReadAllText(filePath);
            var parsedJson = JObject.Parse(json);
            var bookmarkBar = parsedJson["roots"]?["bookmark_bar"]?["children"];
            if (bookmarkBar == null)
                throw new Exception("Invalid Edge bookmarks structure.");

            var importedBookmarks = ParseEdgeBookmarks(bookmarkBar);
            foreach (var bookmark in importedBookmarks)
            {
                bookmarks.Add(bookmark);
            }
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

        private JObject CreateEdgeFolderNode(string name, List<Bookmark> bookmarks)
        {
            return new JObject
            {
                ["children"] = new JArray(bookmarks.Select(ConvertBookmarkToEdgeFormat)),
                ["date_added"] = GetCurrentTimestamp(),
                ["date_last_used"] = "0",
                ["date_modified"] = GetCurrentTimestamp(),
                ["guid"] = Guid.NewGuid().ToString(),
                ["id"] = GenerateId(),
                ["name"] = name,
                ["type"] = "folder"
            };
        }

        private string GenerateChecksum(string json)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string GenerateId()
        {
            return new Random().Next(1, 1000000).ToString(); // Simple random ID generation, can be replaced with a more robust method if needed
        }

        private string GetCurrentTimestamp()
        {
            DateTime epochStart = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long timestamp = (DateTime.UtcNow - epochStart).Ticks / 10; // Convert ticks to microseconds
            return timestamp.ToString();
        }

        private List<Bookmark> ParseEdgeBookmarks(JToken token)
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
                    bookmark.Children = new ObservableCollection<Bookmark>(ParseEdgeBookmarks(child["children"]));
                }

                result.Add(bookmark);
            }

            return result;
        }

        #endregion Methods
    }
}
