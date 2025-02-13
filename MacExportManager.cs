﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Serilog;
using System.Text;
using System.Threading.Tasks;
using Claunia.PropertyList;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Immutable;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class MacExportManager
    {

        public string GenerateMacPlistXml(ObservableCollection<Bookmark> bookmarks)
        {
            var plist = new XElement("plist", new XAttribute("version", "1.0"));
            var rootDict = new XElement("dict",
                new XElement("key", "FavoritesBarEnabled"),
                new XElement("true"),
                new XElement("key", "ManagedFavorites"),
                new XElement("array", bookmarks.Select((b, i) => ConvertBookmarkToXml(b, i == 0)))
            );

            plist.Add(rootDict);

            var xml = new XDocument(new XDeclaration("1.0", "utf-8", null), plist);

            // Convert the XDocument to string
            using (var memoryStream = new System.IO.MemoryStream())
            {
                xml.Save(memoryStream, System.Xml.Linq.SaveOptions.None);
                string xmlString = Encoding.UTF8.GetString(memoryStream.ToArray());

                // Insert the DOCTYPE declaration manually
                string doctype = "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n";
                int insertIndex = xmlString.IndexOf("<plist");
                return xmlString.Insert(insertIndex, doctype);
            }
        }


        private XElement ConvertBookmarkToXml(Bookmark bookmark, bool isTopLevel)
    {
        var dictElement = new XElement("dict");

        // Add "top_level_name" for the first bookmark, otherwise "name"
        dictElement.Add(new XElement("key", isTopLevel ? "top_level_name" : "name"));
        dictElement.Add(new XElement("string", bookmark.Name ?? ""));

        if (!string.IsNullOrEmpty(bookmark.Url))
        {
            dictElement.Add(new XElement("key", "url"), new XElement("string", bookmark.Url));
        }

        if (bookmark.Children.Any())
        {
            dictElement.Add(new XElement("key", "children"));
            var childrenArray = new XElement("array", bookmark.Children.Select(child => ConvertBookmarkToXml(child, false)));
            dictElement.Add(childrenArray);
        }

        return dictElement;
    }



    private NSDictionary CreateTopLevelDictionary(Bookmark bookmark, bool isFirst)
        {
            var dict = new NSDictionary();

            if (isFirst && bookmark.IsRootFolder)
            {
                dict.Add("top_level_name", bookmark.Name ?? "");
            }
            else
            {
                dict.Add("name", bookmark.Name ?? "");
            }

            if (!string.IsNullOrEmpty(bookmark.Url))
            {
                dict.Add("url", bookmark.Url);
            }
            else if (bookmark.Children.Any())
            {
                var childrenArray = new NSArray(bookmark.Children.Select(b => CreateTopLevelDictionary(b, false)).ToArray());
                dict.Add("children", childrenArray);
            }

            return dict;
        }


        private NSDictionary CreateTopLevelDictionary(Bookmark bookmark)
        {
            var dict = new NSDictionary();

            if (bookmark.IsRootFolder)
            {
                dict.Add("top_level_name", bookmark.Name ?? "");
            }
            else
            {
                dict.Add("name", bookmark.Name ?? "");
            }

            if (!string.IsNullOrEmpty(bookmark.Url))
            {
                dict.Add("url", bookmark.Url);
            }
            else if (bookmark.Children.Any())
            {
                var childrenArray = new NSArray(bookmark.Children.Select(CreateTopLevelDictionary).ToArray());
                dict.Add("children", childrenArray);
            }

            return dict;
        }



        private NSDictionary ConvertTopLevelBookmarkToPlistDict(Bookmark bookmark)
        {
            var dict = new NSDictionary();

            if (bookmark.IsRootFolder)
            {
                dict.Add("top_level_name", bookmark.Name ?? "");
            }
            else
            {
                dict.Add("name", bookmark.Name ?? "");
            }

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



        private NSDictionary ConvertBookmarkToPlistDict(Bookmark bookmark)
        {
            var dict = new NSDictionary();

            if (bookmark.IsRootFolder)
            {
                dict.Add("toplevel_name", bookmark.Name ?? "");
            }
            else
            {
                dict.Add("name", bookmark.Name ?? "");
            }

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
