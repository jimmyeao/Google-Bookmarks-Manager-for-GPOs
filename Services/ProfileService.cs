using Google_Bookmarks_Manager_for_GPOs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Google_Bookmarks_Manager_for_GPOs.Services
{
    public class ProfileService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GoogleBookmarksManager"
        );
        private static readonly string DefaultProfilesFilePath = Path.Combine(AppDataFolder, "profiles.json");

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string CurrentFilePath { get; set; } = DefaultProfilesFilePath;

        public async Task<List<BookmarkProfile>> LoadAllProfilesAsync(string? filePath = null)
        {
            var targetPath = filePath ?? CurrentFilePath;

            if (!File.Exists(targetPath))
            {
                // Create default profile if no profiles exist
                var defaultProfile = new BookmarkProfile
                {
                    Name = "Default Profile",
                    TopLevelFolderName = "Managed Bookmarks"
                };
                return new List<BookmarkProfile> { defaultProfile };
            }

            var json = await File.ReadAllTextAsync(targetPath);
            var profiles = JsonSerializer.Deserialize<List<BookmarkProfile>>(json, _options);

            // Update current file path if custom path was used
            if (filePath != null)
            {
                CurrentFilePath = filePath;
            }

            return profiles ?? new List<BookmarkProfile>();
        }

        public async Task SaveAllProfilesAsync(List<BookmarkProfile> profiles, string? filePath = null)
        {
            var targetPath = filePath ?? CurrentFilePath;
            var directory = Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Update LastModified for all profiles
            foreach (var profile in profiles)
            {
                profile.LastModified = DateTime.Now;
            }

            var json = JsonSerializer.Serialize(profiles, _options);
            await File.WriteAllTextAsync(targetPath, json);

            // Update current file path if custom path was used
            if (filePath != null)
            {
                CurrentFilePath = filePath;
            }
        }

        public async Task<BookmarkProfile?> GetProfileByNameAsync(string name, List<BookmarkProfile> profiles)
        {
            return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<BookmarkProfile> CreateProfileAsync(string name, string topLevelFolderName, List<BookmarkProfile> profiles)
        {
            // Check for duplicate names
            if (profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Profile '{name}' already exists.");
            }

            var newProfile = new BookmarkProfile
            {
                Name = name,
                TopLevelFolderName = topLevelFolderName,
                Bookmarks = new List<BookmarkItem>(),
                LastModified = DateTime.Now
            };

            profiles.Add(newProfile);
            await SaveAllProfilesAsync(profiles);
            return newProfile;
        }

        public async Task DeleteProfileAsync(string profileId, List<BookmarkProfile> profiles)
        {
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
            {
                profiles.Remove(profile);
                await SaveAllProfilesAsync(profiles);
            }
        }

        public async Task RenameProfileAsync(string profileId, string newName, List<BookmarkProfile> profiles)
        {
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                throw new InvalidOperationException("Profile not found.");
            }

            // Check for duplicate names
            if (profiles.Any(p => p.Id != profileId && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Profile '{newName}' already exists.");
            }

            profile.Name = newName;
            await SaveAllProfilesAsync(profiles);
        }

        public async Task UpdateProfileAsync(BookmarkProfile profile, List<BookmarkProfile> profiles, string? filePath = null)
        {
            var existingProfile = profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (existingProfile != null)
            {
                var index = profiles.IndexOf(existingProfile);
                profiles[index] = profile;
                await SaveAllProfilesAsync(profiles, filePath);
            }
        }

        public async Task SaveSingleProfileAsync(BookmarkProfile profile, string filePath)
        {
            var profileList = new List<BookmarkProfile> { profile };
            await SaveAllProfilesAsync(profileList, filePath);
        }

        public async Task<BookmarkProfile?> LoadSingleProfileAsync(string filePath)
        {
            var profiles = await LoadAllProfilesAsync(filePath);
            return profiles.FirstOrDefault();
        }
    }
}
