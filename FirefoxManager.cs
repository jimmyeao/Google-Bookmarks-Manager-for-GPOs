using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class FirefoxManager
    {
        public void ImportBookmarks(string profilePath, ObservableCollection<Bookmark> bookmarks)
        {
            string placesDbPath = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(placesDbPath))
                throw new FileNotFoundException("places.sqlite file not found in Firefox profile.");

            try
            {
                using (var connection = new SqliteConnection($"Data Source={placesDbPath}"))
                {
                    connection.Open();
                    string query = @"SELECT b.title, p.url 
                                     FROM moz_bookmarks b 
                                     JOIN moz_places p ON b.fk = p.id 
                                     WHERE b.type = 1";  // Type 1 = Bookmark

                    using (var command = new SqliteCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string title = reader["title"]?.ToString() ?? "Unnamed";
                            string url = reader["url"]?.ToString();

                            if (!string.IsNullOrEmpty(url))
                            {
                                bookmarks.Add(new Bookmark
                                {
                                    Name = title,
                                    Url = url,
                                    IsFolder = false
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error importing Firefox bookmarks: {ex.Message}");
            }
        }

        public void ExportBookmarksAsJson(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            var rootObject = new JObject
            {
                ["title"] = "bookmarks",
                ["children"] = new JArray(bookmarks.Select(ConvertBookmarkToFirefoxJsonFormat))
            };

            File.WriteAllText(filePath, rootObject.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        public void ExportBookmarksToFirefox(ObservableCollection<Bookmark> bookmarks)
        {
            string profilePath = GetFirefoxProfilePath();
            if (string.IsNullOrEmpty(profilePath))
            {
                throw new Exception("Firefox profile not found.");
            }

            string placesDbPath = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(placesDbPath))
            {
                throw new Exception("places.sqlite file not found.");
            }

            using (var connection = new SqliteConnection($"Data Source={placesDbPath}"))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    ClearOldBookmarks(connection, transaction);

                    foreach (var bookmark in bookmarks)
                    {
                        InsertBookmark(connection, transaction, bookmark, 2); // 2 = Bookmarks Menu
                    }

                    transaction.Commit();
                }
            }
        }

        private void InsertBookmark(SqliteConnection connection, SqliteTransaction transaction, Bookmark bookmark, long parentId = 3) // Use 3 for the toolbar
        {
            long currentTimestamp = GetFirefoxTimestamp(DateTime.Now);
            string guid = Guid.NewGuid().ToString("N");

            // Insert into moz_places
            long placeId;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
        INSERT INTO moz_places (url, title, guid, hidden, typed, visit_count)
        VALUES (@url, @title, @guid, 0, 0, 1);
        SELECT last_insert_rowid();";

                command.Parameters.AddWithValue("@url", bookmark.Url ?? "");
                command.Parameters.AddWithValue("@title", bookmark.Name);
                command.Parameters.AddWithValue("@guid", guid);

                placeId = (long)command.ExecuteScalar();
            }

            // Insert into moz_bookmarks
            long newParentId = parentId;
            long bookmarkId;
            using (var bookmarkCommand = connection.CreateCommand())
            {
                bookmarkCommand.Transaction = transaction;
                bookmarkCommand.CommandText = @"
        INSERT INTO moz_bookmarks (type, fk, parent, position, title, dateAdded, lastModified, guid)
        VALUES (1, @fk, @parent, @position, @title, @dateAdded, @lastModified, @guid);
        SELECT last_insert_rowid();";

                bookmarkCommand.Parameters.AddWithValue("@fk", placeId);
                bookmarkCommand.Parameters.AddWithValue("@parent", parentId);  // Use 3 for Bookmarks Toolbar
                bookmarkCommand.Parameters.AddWithValue("@position", 0);       // Adjust position as needed
                bookmarkCommand.Parameters.AddWithValue("@title", bookmark.Name);
                bookmarkCommand.Parameters.AddWithValue("@dateAdded", currentTimestamp);
                bookmarkCommand.Parameters.AddWithValue("@lastModified", currentTimestamp);
                bookmarkCommand.Parameters.AddWithValue("@guid", Guid.NewGuid().ToString("N"));

                bookmarkId = (long)bookmarkCommand.ExecuteScalar();

                if (bookmark.IsFolder)
                {
                    newParentId = bookmarkId;  // Update the parent ID for the children if it's a folder
                }
            }

            // Recursively insert child bookmarks with the updated parentId for folders
            foreach (var child in bookmark.Children)
            {
                InsertBookmark(connection, transaction, child, newParentId);
            }
        }

        private void ClearOldBookmarks(SqliteConnection connection, SqliteTransaction transaction)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM moz_bookmarks WHERE parent = 2 OR parent = 3;";
                command.ExecuteNonQuery();
            }
        }

        private long GetFirefoxTimestamp(DateTime date)
        {
            DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(date.ToUniversalTime() - epochStart).TotalMilliseconds * 1000;
        }

       

        public void ExportBookmarksAsHtml(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            StringBuilder html = new StringBuilder();

            html.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            html.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            html.AppendLine("<TITLE>Bookmarks</TITLE>");
            html.AppendLine("<H1>Bookmarks</H1>");
            html.AppendLine("<DL><p>");

            foreach (var bookmark in bookmarks)
            {
                AppendBookmarkToHtml(bookmark, html, 1);
            }

            html.AppendLine("</DL><p>");
            File.WriteAllText(filePath, html.ToString());
        }

        private JObject ConvertBookmarkToFirefoxJsonFormat(Bookmark bookmark)
        {
            var obj = new JObject
            {
                ["title"] = bookmark.Name,
                ["type"] = bookmark.IsFolder ? "text/x-moz-place-container" : "text/x-moz-place",
                ["uri"] = bookmark.Url
            };

            if (bookmark.Children.Any())
            {
                obj["children"] = new JArray(bookmark.Children.Select(ConvertBookmarkToFirefoxJsonFormat));
            }

            return obj;
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

        public string GetFirefoxProfilePath()
        {
            string firefoxProfilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla\\Firefox\\Profiles");
            if (Directory.Exists(firefoxProfilesPath))
            {
                var profileDirs = Directory.GetDirectories(firefoxProfilesPath);
                if (profileDirs.Length > 0)
                {
                    return profileDirs[0];  // Return the first profile found
                }
            }
            return string.Empty;
        }
    }
}
