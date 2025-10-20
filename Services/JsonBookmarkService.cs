using Google_Bookmarks_Manager_for_GPOs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Google_Bookmarks_Manager_for_GPOs.Services
{
    public static class JsonBookmarkService
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static async Task SaveToJsonAsync(string filePath, List<BookmarkItem> bookmarks)
        {
            var json = JsonSerializer.Serialize(bookmarks, _options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public static async Task<List<BookmarkItem>?> LoadFromJsonAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<List<BookmarkItem>>(json, _options);
        }
    }
}
