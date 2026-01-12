using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Google_Bookmarks_Manager_for_GPOs.Models
{
    public class BookmarkProfile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default Profile";

        [JsonPropertyName("topLevelFolderName")]
        public string TopLevelFolderName { get; set; } = "Managed Bookmarks";

        [JsonPropertyName("bookmarks")]
        public List<BookmarkItem> Bookmarks { get; set; } = new();

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; } = DateTime.Now;

        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("favoritesBarEnabled")]
        public bool FavoritesBarEnabled { get; set; } = true;

        [JsonPropertyName("bookmarkBarEnabled")]
        public bool BookmarkBarEnabled { get; set; } = true;
    }
}
