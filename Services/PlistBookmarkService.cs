using Google_Bookmarks_Manager_for_GPOs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Google_Bookmarks_Manager_for_GPOs.Services
{
    public static class PlistBookmarkService
    {
        public static void SaveToPlist(string filePath, string keyName, List<BookmarkItem> bookmarks)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine($"<key>{keyName}</key>");
            writer.WriteLine("<array>");
            foreach (var item in bookmarks)
            {
                writer.WriteLine("  <dict>");
                WriteDict(writer, item, 2);
                writer.WriteLine("  </dict>");
            }
            writer.WriteLine("</array>");
        }

        private static void WriteDict(StreamWriter writer, BookmarkItem item, int indent)
        {
            string pad = new(' ', indent * 2);
            void WriteKeyVal(string key, string? val)
            {
                if (!string.IsNullOrEmpty(val))
                {
                    writer.WriteLine($"{pad}<key>{key}</key>");
                    writer.WriteLine($"{pad}<string>{val}</string>");
                }
            }

            WriteKeyVal("toplevel_name", item.TopLevelName);
            WriteKeyVal("name", item.Name);
            WriteKeyVal("url", item.Url);

            if (item.Children?.Count > 0)
            {
                writer.WriteLine($"{pad}<key>children</key>");
                writer.WriteLine($"{pad}<array>");
                foreach (var child in item.Children)
                {
                    writer.WriteLine($"{pad}  <dict>");
                    WriteDict(writer, child, indent + 2);
                    writer.WriteLine($"{pad}  </dict>");
                }
                writer.WriteLine($"{pad}</array>");
            }
        }

        public static List<BookmarkItem> LoadFromPlist(string filePath)
        {
            if (!File.Exists(filePath))
                return [];

            var xml = File.ReadAllText(filePath);
            var doc = XDocument.Parse($"<root>{xml}</root>");
            var items = new List<BookmarkItem>();

            // Find the first top-level array which contains the bookmark dictionaries
            var rootArray = doc.Root?.Elements("array").FirstOrDefault();
            if (rootArray == null)
                return items;

            // Only iterate immediate dict children of the root array (not all descendants)
            foreach (var dict in rootArray.Elements("dict"))
            {
                items.Add(ParseDict(dict));
            }

            return items;
        }

        private static BookmarkItem ParseDict(XElement dict)
        {
            var item = new BookmarkItem();
            var elements = dict.Elements().ToList();
            for (int i = 0; i < elements.Count; i++)
            {
                var el = elements[i];
                if (el.Name != "key") continue;

                var key = el.Value;
                var valueElem = i + 1 < elements.Count ? elements[i + 1] : null;
                if (valueElem == null) continue;

                if (valueElem.Name == "string")
                {
                    var val = valueElem.Value;
                    if (key == "toplevel_name") item.TopLevelName = val;
                    else if (key == "name") item.Name = val;
                    else if (key == "url") item.Url = val;
                }
                else if (key == "children" && valueElem.Name == "array")
                {
                    item.Children = new();
                    foreach (var childDict in valueElem.Elements("dict"))
                    {
                        item.Children.Add(ParseDict(childDict));
                    }
                }

                // Skip over the value element we just processed
                i++;
            }

            return item;
        }
    }
}
