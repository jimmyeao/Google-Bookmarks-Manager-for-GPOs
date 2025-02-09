using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Claunia.PropertyList;
using System.Xml;
using System.Xml.Linq;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class MacExportManager
    {
        public string GenerateMacPlistXml(ObservableCollection<Bookmark> bookmarks)
        {
            var rootDict = new NSDictionary
            {
                { "FavoritesBarEnabled", true }
            };

            // Convert each bookmark to NSDictionary and create NSArray
            var favoritesArray = new NSArray(bookmarks.Select(ConvertBookmarkToPlistDict).ToArray());
            rootDict.Add("ManagedFavorites", favoritesArray);

            // Serialize the dictionary to an XML plist format
            using (var memoryStream = new MemoryStream())
            {
                PropertyListParser.SaveAsXml(rootDict, memoryStream);
                return Encoding.UTF8.GetString(memoryStream.ToArray());
            }
        }

        private NSDictionary ConvertBookmarkToPlistDict(Bookmark bookmark)
        {
            var dict = new NSDictionary
            {
                { bookmark.IsRootFolder ? "toplevel_name" : "name", bookmark.Name ?? "" }
            };

            if (!string.IsNullOrEmpty(bookmark.Url))
            {
                dict.Add("url", bookmark.Url);
            }
            else if (bookmark.Children.Any())
            {
                var childrenArray = new NSArray(bookmark.Children.Select(ConvertBookmarkToPlistDict).ToArray());
                dict.Add("children", childrenArray);
            }

            return dict;
        }


        private XElement ConvertBookmarkToPlistDict(Bookmark bookmark, bool isTopLevel = false)
        {
            var dictElement = new XElement("dict");

            if (isTopLevel)
            {
                dictElement.Add(
                    new XElement("key", "toplevel_name"),
                    new XElement("string", bookmark.Name ?? "")
                );
            }
            else
            {
                dictElement.Add(
                    new XElement("key", "name"),
                    new XElement("string", bookmark.Name ?? "")
                );
            }

            if (!string.IsNullOrEmpty(bookmark.Url))
            {
                dictElement.Add(
                    new XElement("key", "url"),
                    new XElement("string", WrapInCDataIfNeeded(bookmark.Url))
                );
            }
            else if (bookmark.Children.Any())
            {
                dictElement.Add(
                    new XElement("key", "children"),
                    new XElement("array", bookmark.Children.Select(b => ConvertBookmarkToPlistDict(b)).Where(x => x != null))
                );
            }

            return dictElement;
        }

        private string WrapInCDataIfNeeded(string input)
        {
            if (input.Contains("&") || input.Contains("<") || input.Contains(">"))
            {
                return $"<![CDATA[{input}]]>";
            }
            return input;
        }






        private void WriteBookmarkToPlist(XmlWriter writer, Bookmark bookmark)
        {
            writer.WriteStartElement("dict");
            writer.WriteElementString("key", "name");
            writer.WriteElementString("string", bookmark.Name);

            if (bookmark.IsFolder)
            {
                writer.WriteElementString("key", "children");
                writer.WriteStartElement("array");
                foreach (var child in bookmark.Children)
                {
                    WriteBookmarkToPlist(writer, child);
                }
                writer.WriteEndElement(); // </array>
            }
            else
            {
                writer.WriteElementString("key", "url");
                writer.WriteElementString("string", bookmark.Url);
            }

            writer.WriteEndElement(); // </dict>
        }
    }
}
