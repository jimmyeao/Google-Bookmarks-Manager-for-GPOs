using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google_Bookmarks_Manager_for_GPOs.Services
{
    public static class BookmarkConverterService
    {
        /// <summary>
        /// Converts Windows JSON (Edge or Chrome) to macOS plist format.
        /// </summary>
        public static async Task ConvertJsonToPlistAsync(string jsonFile, string outputFile, string keyName)
        {
            var bookmarks = await JsonBookmarkService.LoadFromJsonAsync(jsonFile);
            if (bookmarks == null)
                throw new FileNotFoundException($"Could not load JSON file: {jsonFile}");

            PlistBookmarkService.SaveToPlist(outputFile, keyName, bookmarks);
        }

        /// <summary>
        /// Converts macOS plist (Edge or Chrome) to Windows JSON format.
        /// </summary>
        public static async Task ConvertPlistToJsonAsync(string plistFile, string outputFile)
        {
            var bookmarks = PlistBookmarkService.LoadFromPlist(plistFile);
            await JsonBookmarkService.SaveToJsonAsync(outputFile, bookmarks);
        }

        /// <summary>
        /// Detects format based on extension and converts automatically.
        /// </summary>
        public static async Task AutoConvertAsync(string inputFile, string outputFile, string? keyName = null)
        {
            var ext = Path.GetExtension(inputFile).ToLowerInvariant();

            if (ext == ".json")
            {
                await ConvertJsonToPlistAsync(inputFile, outputFile, keyName ?? "ManagedBookmarks");
            }
            else
            {
                await ConvertPlistToJsonAsync(inputFile, outputFile);
            }
        }
    }
}
