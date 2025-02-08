using Microsoft.Data.Sqlite;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class FirefoxManager
    {
        public void ImportBookmarks(string profilePath, ObservableCollection<Bookmark> bookmarks)
        {
            string placesDbPath = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(placesDbPath))
                throw new FileNotFoundException("Firefox places.sqlite file not found.");

            using (var connection = new SqliteConnection($"Data Source={placesDbPath}"))
            {
                connection.Open();
                string query = @"SELECT b.title, p.url FROM moz_bookmarks b JOIN moz_places p ON b.fk = p.id WHERE b.type = 1";
                using (var command = new SqliteCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string title = reader["title"]?.ToString() ?? "Unnamed";
                        string url = reader["url"]?.ToString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            bookmarks.Add(new Bookmark { Name = title, Url = url, IsFolder = false });
                        }
                    }
                }
            }
        }

        public void ExportBookmarksToHtml(string filePath, ObservableCollection<Bookmark> bookmarks)
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
                writer.WriteLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
                writer.WriteLine("<TITLE>Bookmarks</TITLE>");
                writer.WriteLine("<H1>Bookmarks</H1>");
                writer.WriteLine("<DL><p>");

                foreach (var bookmark in bookmarks)
                {
                    AppendBookmarkToHtml(bookmark, writer, 1);
                }

                writer.WriteLine("</DL><p>");
            }
        }

        private void AppendBookmarkToHtml(Bookmark bookmark, StreamWriter writer, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 4);
            if (bookmark.IsFolder)
            {
                writer.WriteLine($"{indent}<DT><H3>{bookmark.Name}</H3>");
                writer.WriteLine($"{indent}<DL><p>");
                foreach (var child in bookmark.Children)
                {
                    AppendBookmarkToHtml(child, writer, indentLevel + 1);
                }
                writer.WriteLine($"{indent}</DL><p>");
            }
            else
            {
                writer.WriteLine($"{indent}<DT><A HREF=\"{bookmark.Url}\">{bookmark.Name}</A>");
            }
        }
    }
}
