using Google_Bookmarks_Manager_for_GPOs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Text.RegularExpressions;
using System.Security;

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

        public static void SaveToCompletePlist(string filePath, string keyName, List<BookmarkItem> bookmarks, bool enableBar)
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

            // XML declaration
            writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

            // DOCTYPE
            writer.WriteLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");

            // Root plist element
            writer.WriteLine("<plist version=\"1.0\">");
            writer.WriteLine("<dict>");

            // Policy control key based on browser (only write if enabled)
            if (enableBar)
            {
                if (keyName == "ManagedFavorites")
                {
                    WriteBooleanKey(writer, "FavoritesBarEnabled", enableBar);
                }
                else if (keyName == "ManagedBookmarks")
                {
                    WriteBooleanKey(writer, "BookmarkBarEnabled", enableBar);
                }
            }

            // Bookmarks array
            writer.WriteLine($"  <key>{keyName}</key>");
            writer.WriteLine("  <array>");
            foreach (var item in bookmarks)
            {
                writer.WriteLine("    <dict>");
                WriteDict(writer, item, 3);  // Indent level 3 (inside array, inside dict)
                writer.WriteLine("    </dict>");
            }
            writer.WriteLine("  </array>");

            // Close dict and plist
            writer.WriteLine("</dict>");
            writer.WriteLine("</plist>");
        }

        private static void WriteBooleanKey(StreamWriter writer, string key, bool value, int indent = 1)
        {
            string pad = new(' ', indent * 2);
            writer.WriteLine($"{pad}<key>{key}</key>");
            writer.WriteLine($"{pad}{(value ? "<true/>" : "<false/>")}");
        }

        private static void WriteDict(StreamWriter writer, BookmarkItem item, int indent)
        {
            string pad = new(' ', indent * 2);
            void WriteKeyVal(string key, string? val)
            {
                if (!string.IsNullOrEmpty(val))
                {
                    // Ensure XML special characters are escaped
                    var safe = SecurityElement.Escape(val);
                    writer.WriteLine($"{pad}<key>{key}</key>");
                    writer.WriteLine($"{pad}<string>{safe}</string>");
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
            XDocument doc;

            // Check if it's a complete PLIST document or a fragment
            bool isCompletePlist = xml.TrimStart().StartsWith("<?xml") || xml.Contains("<plist");

            try
            {
                if (isCompletePlist)
                {
                    // Parse as complete XML document
                    doc = XDocument.Parse(xml);
                }
                else
                {
                    // Parse as fragment (wrap in root element)
                    doc = XDocument.Parse($"<root>{xml}</root>");
                }
            }
            catch
            {
                // Attempt to sanitize invalid XML (common: unescaped & in URLs)
                var sanitized = SanitizeXmlFragment(xml);
                if (isCompletePlist)
                {
                    doc = XDocument.Parse(sanitized);
                }
                else
                {
                    doc = XDocument.Parse($"<root>{sanitized}</root>");
                }
            }

            var items = new List<BookmarkItem>();
            XElement rootArray = null;

            if (isCompletePlist)
            {
                // Navigate: <plist><dict><key>ManagedBookmarks/ManagedFavorites</key><array>...
                var plistElement = doc.Root?.Name.LocalName == "plist" ? doc.Root : doc.Descendants("plist").FirstOrDefault();
                if (plistElement != null)
                {
                    var dictElement = plistElement.Element("dict");
                    if (dictElement != null)
                    {
                        // Find the array that comes after a key element
                        // Look for keys like "ManagedBookmarks" or "ManagedFavorites"
                        var elements = dictElement.Elements().ToList();
                        for (int i = 0; i < elements.Count - 1; i++)
                        {
                            if (elements[i].Name == "key")
                            {
                                var keyValue = elements[i].Value;
                                // Check if this is a bookmark policy key (not a boolean setting key)
                                if ((keyValue == "ManagedBookmarks" || keyValue == "ManagedFavorites") &&
                                    elements[i + 1].Name == "array")
                                {
                                    rootArray = elements[i + 1];
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Fragment format: Find the first top-level array
                rootArray = doc.Root?.Elements("array").FirstOrDefault();
            }

            if (rootArray == null)
                return items;

            // Only iterate immediate dict children of the root array (not all descendants)
            foreach (var dict in rootArray.Elements("dict"))
            {
                items.Add(ParseDict(dict));
            }

            return items;
        }

        // Replaces bare '&' inside <string>...</string> with '&amp;' while preserving existing entities
        private static string SanitizeXmlFragment(string xml)
        {
            return Regex.Replace(
                xml,
                @"(<string>)(.*?)(</string>)",
                m =>
                {
                    var content = m.Groups[2].Value;
                    // Replace any & that is not the start of a valid entity
                    content = Regex.Replace(content, @"&(?![#a-zA-Z0-9]+;)", "&amp;");
                    return m.Groups[1].Value + content + m.Groups[3].Value;
                },
                RegexOptions.Singleline);
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
