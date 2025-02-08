using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class MacExportManager
    {
        public string GenerateMacPlistXml(ObservableCollection<Bookmark> bookmarks)
        {
            var plist = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement("plist",
                    new XAttribute("version", "1.0"),
                    new XElement("dict",
                        new XElement("key", "FavoritesBarEnabled"),
                        new XElement("true"),
                        new XElement("key", "ManagedFavorites"),
                        new XElement("array", bookmarks.Select(b => ConvertBookmarkToPlistDict(b, isTopLevel: true)).Where(x => x != null))
                    )
                )
            );

            return plist.ToString(SaveOptions.DisableFormatting);
        }

        private XElement ConvertBookmarkToPlistDict(Bookmark bookmark, bool isTopLevel = false)
        {
            var dictElement = new XElement("dict");

            if (isTopLevel)  // Handle top-level bookmarks
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
                // Regular bookmark with a URL
                dictElement.Add(
                    new XElement("key", "url"),
                    new XElement("string", bookmark.Url)
                );
            }
            else if (bookmark.Children.Any())
            {
                // Folder with children
                dictElement.Add(
                    new XElement("key", "children"),
                    new XElement("array", bookmark.Children.Select(b => ConvertBookmarkToPlistDict(b)).Where(x => x != null))
                );
            }

            return dictElement;
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
