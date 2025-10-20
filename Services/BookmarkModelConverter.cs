using Google_Bookmarks_Manager_for_GPOs.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Google_Bookmarks_Manager_for_GPOs.Services
{
    /// <summary>
    /// Converts between legacy Bookmark (UI model) and BookmarkItem (service model)
    /// </summary>
    public static class BookmarkModelConverter
    {
        /// <summary>
        /// Converts a BookmarkItem to a Bookmark for UI binding
        /// </summary>
        public static Bookmark ToBookmark(BookmarkItem item)
        {
            var bookmark = new Bookmark
            {
                Name = item.Name ?? item.TopLevelName ?? "Unnamed",
                Url = item.Url ?? string.Empty,
                IsFolder = item.Children != null && item.Children.Count > 0,
                IsRootFolder = !string.IsNullOrEmpty(item.TopLevelName),
                Children = new ObservableCollection<Bookmark>()
            };

            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    bookmark.Children.Add(ToBookmark(child));
                }
            }

            return bookmark;
        }

        /// <summary>
        /// Converts a Bookmark to a BookmarkItem for service operations
        /// </summary>
        public static BookmarkItem ToBookmarkItem(Bookmark bookmark, bool isTopLevel = false)
        {
            var item = new BookmarkItem
            {
                Name = bookmark.Name,
                Url = string.IsNullOrEmpty(bookmark.Url) ? null : bookmark.Url
            };

            // Handle top-level folder name
            if (bookmark.IsRootFolder || isTopLevel)
            {
                item.TopLevelName = bookmark.Name;
                item.Name = null; // TopLevelName takes precedence
            }

            // Handle children
            if (bookmark.Children != null && bookmark.Children.Count > 0)
            {
                item.Children = bookmark.Children.Select(c => ToBookmarkItem(c)).ToList();
            }

            return item;
        }

        /// <summary>
        /// Converts a list of BookmarkItems to ObservableCollection of Bookmarks
        /// </summary>
        public static ObservableCollection<Bookmark> ToBookmarkCollection(List<BookmarkItem> items)
        {
            var collection = new ObservableCollection<Bookmark>();

            if (items == null)
                return collection;

            foreach (var item in items)
            {
                // Skip the toplevel_name entry (it's metadata, not a bookmark)
                if (!string.IsNullOrEmpty(item.TopLevelName) && string.IsNullOrEmpty(item.Name) && string.IsNullOrEmpty(item.Url))
                    continue;

                collection.Add(ToBookmark(item));
            }

            return collection;
        }

        /// <summary>
        /// Converts an ObservableCollection of Bookmarks to List of BookmarkItems
        /// </summary>
        public static List<BookmarkItem> ToBookmarkItemList(ObservableCollection<Bookmark> bookmarks)
        {
            var list = new List<BookmarkItem>();

            if (bookmarks == null)
                return list;

            foreach (var bookmark in bookmarks)
            {
                list.Add(ToBookmarkItem(bookmark));
            }

            return list;
        }

        /// <summary>
        /// Extracts the top-level folder name from a list of BookmarkItems
        /// </summary>
        public static string? ExtractTopLevelFolderName(List<BookmarkItem> items)
        {
            if (items == null || items.Count == 0)
                return null;

            // First item might contain toplevel_name
            var firstItem = items.FirstOrDefault();
            if (firstItem != null && !string.IsNullOrEmpty(firstItem.TopLevelName))
            {
                return firstItem.TopLevelName;
            }

            return null;
        }

        /// <summary>
        /// Creates a complete BookmarkItem list with top-level folder name as first entry
        /// </summary>
        public static List<BookmarkItem> CreateBookmarkItemListWithTopLevel(
            ObservableCollection<Bookmark> bookmarks,
            string topLevelFolderName)
        {
            var list = new List<BookmarkItem>();

            // Add top-level name as first entry
            if (!string.IsNullOrEmpty(topLevelFolderName))
            {
                list.Add(new BookmarkItem
                {
                    TopLevelName = topLevelFolderName
                });
            }

            // Add all bookmarks
            list.AddRange(ToBookmarkItemList(bookmarks));

            return list;
        }
    }
}
