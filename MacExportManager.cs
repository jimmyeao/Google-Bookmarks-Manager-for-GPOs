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
                new XDeclaration("1.0", "UTF-8", null),
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

            using (var writer = new StringWriter())
            {
                writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                writer.WriteLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
                plist.Save(writer, SaveOptions.None);  // Save without additional indentation for plist compatibility
                return writer.ToString();
            }
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
