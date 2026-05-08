using HedgeModManager.Uwp.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace HedgeModManager.Uwp.Services
{
    public sealed class XboxModsDatabase
    {
        private const string ModsDbFileName = "ModsDB.ini";

        public async Task<StorageFolder> ResolveModsFolder(StorageFolder selectedFolder)
        {
            if (await LooksLikeModsFolder(selectedFolder))
                return selectedFolder;

            var modsFolder = await TryGetFolder(selectedFolder, "mods") ?? await TryGetFolder(selectedFolder, "Mods");
            if (modsFolder != null)
                return modsFolder;

            return selectedFolder;
        }

        public async Task<IReadOnlyList<ModEntry>> Load(StorageFolder modsFolder)
        {
            var activeModIniPaths = await LoadActiveModIniPaths(modsFolder);
            var result = new List<ModEntry>();
            var modFolders = await FindModFolders(modsFolder);
            foreach (var folder in modFolders)
            {
                var modIni = await TryGetFile(folder, "mod.ini");
                if (modIni == null)
                    modIni = await TryGetFile(folder, "MOD.INI");
                if (modIni == null)
                    continue;

                var ini = HedgeIniFile.Parse(await FileIO.ReadTextAsync(modIni));
                var title = FirstNonEmpty(ini.Get("Desc", "Title"), ini.Get("Details", "Title"), folder.Name);
                var entry = new ModEntry
                {
                    Folder = folder,
                    Title = title,
                    Author = FirstNonEmpty(ini.Get("Desc", "Author"), ini.Get("Details", "Author"), "Unknown"),
                    Version = FirstNonEmpty(ini.Get("Desc", "Version"), ini.Get("Details", "Version"), "0.0"),
                    Id = FirstNonEmpty(ini.Get("Main", "ID"), DeterministicHash(title).ToString("X")),
                    UpdateServer = FirstNonEmpty(ini.Get("Main", "UpdateServer"), string.Empty)
                };
                entry.Enabled = activeModIniPaths.Contains(NormalizePath(entry.ModIniPath));
                result.Add(entry);
            }

            return result;
        }

        public async Task Save(StorageFolder modsFolder, ObservableCollection<ModEntry> mods)
        {
            var idsByMod = mods.ToDictionary(t => t, _ => Guid.NewGuid().ToString());
            var activeMods = mods.Where(t => t.Enabled).ToList();
            var builder = new StringBuilder();

            builder.AppendLine("[Main]");
            builder.AppendLine("ManifestVersion=1.1");
            builder.AppendLine("ReverseLoadOrder=0");
            builder.AppendLine($"ActiveModCount={activeMods.Count}");
            for (var i = 0; i < activeMods.Count; i++)
                builder.AppendLine($"ActiveMod{i}=\"{idsByMod[activeMods[i]]}\"");
            builder.AppendLine("FavoriteModCount=0");

            builder.AppendLine();
            builder.AppendLine("[Mods]");
            foreach (var mod in mods)
                builder.AppendLine($"{idsByMod[mod]}=\"{NormalizePath(mod.ModIniPath)}\"");

            builder.AppendLine();
            builder.AppendLine("[Codes]");
            builder.AppendLine("CodeCount=0");

            var file = await modsFolder.CreateFileAsync(ModsDbFileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, builder.ToString());
        }

        public async Task<(bool ok, string message)> ValidateSavedDatabase(StorageFolder modsFolder, IEnumerable<ModEntry> mods)
        {
            var file = await TryGetFile(modsFolder, ModsDbFileName);
            if (file == null)
                return (false, "ModsDB.ini missing after save");

            var ini = HedgeIniFile.Parse(await FileIO.ReadTextAsync(file));
            var enabledMods = mods.Where(t => t.Enabled).ToList();
            var countText = ini.Get("Main", "ActiveModCount");
            if (!int.TryParse(countText, out var count))
                return (false, "ActiveModCount is invalid");

            if (count != enabledMods.Count)
                return (false, $"ActiveModCount mismatch ({count} vs {enabledMods.Count})");

            var modsByGuid = ini.GetGroup("Mods")
                .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t.Key, t => NormalizePath(t.Last().Value), StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < count; i++)
            {
                var id = ini.Get("Main", $"ActiveMod{i}");
                if (string.IsNullOrWhiteSpace(id))
                    return (false, $"ActiveMod{i} missing");

                if (!modsByGuid.TryGetValue(id, out var mappedPath))
                    return (false, $"ActiveMod{i} id not found in [Mods]");

                if (!File.Exists(mappedPath))
                    return (false, $"Mapped mod.ini missing: {mappedPath}");
            }

            var codeCount = ini.Get("Codes", "CodeCount");
            if (string.IsNullOrWhiteSpace(codeCount))
                return (false, "Codes section missing CodeCount");

            return (true, "OK");
        }

        private async Task<HashSet<string>> LoadActiveModIniPaths(StorageFolder modsFolder)
        {
            var modsDb = await TryGetFile(modsFolder, ModsDbFileName);
            if (modsDb == null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ini = HedgeIniFile.Parse(await FileIO.ReadTextAsync(modsDb));
            var modsByGuid = ini.GetGroup("Mods")
                .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t.Key, t => NormalizePath(t.Last().Value), StringComparer.OrdinalIgnoreCase);

            var activeIds = ReadActiveModIds(ini);
            var activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in activeIds)
            {
                if (modsByGuid.TryGetValue(id, out var path))
                    activePaths.Add(path);
            }

            return activePaths;
        }

        private static async Task<bool> LooksLikeModsFolder(StorageFolder folder)
        {
            if (await TryGetFile(folder, ModsDbFileName) != null)
                return true;

            foreach (var child in await folder.GetFoldersAsync())
            {
                if (await TryGetFile(child, "mod.ini") != null)
                    return true;
            }

            return false;
        }

        private static async Task<StorageFile> TryGetFile(StorageFolder folder, string name)
        {
            try
            {
                return await folder.GetFileAsync(name);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<StorageFolder> TryGetFolder(StorageFolder folder, string name)
        {
            try
            {
                return await folder.GetFolderAsync(name);
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Trim('"').Replace('/', '\\');
        }

        private static List<string> ReadActiveModIds(HedgeIniFile ini)
        {
            var ids = new List<string>();
            var countText = ini.Get("Main", "ActiveModCount");
            if (int.TryParse(countText, out var count) && count >= 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var id = ini.Get("Main", $"ActiveMod{i}");
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }
            }
            else
            {
                ids.AddRange(ini.GetAll("Main", "ActiveMod"));
            }

            return ids;
        }

        private static async Task<List<StorageFolder>> FindModFolders(StorageFolder root)
        {
            var result = new List<StorageFolder>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<StorageFolder>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var key = NormalizePath(current.Path);
                if (!visited.Add(key))
                    continue;

                var modIni = await TryGetFile(current, "mod.ini") ?? await TryGetFile(current, "MOD.INI");
                if (modIni != null)
                {
                    result.Add(current);
                    continue;
                }

                IReadOnlyList<StorageFolder> children;
                try
                {
                    children = await current.GetFoldersAsync();
                }
                catch
                {
                    continue;
                }

                foreach (var child in children)
                    queue.Enqueue(child);
            }

            return result;
        }

        private static int DeterministicHash(string value)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;
                value = value ?? string.Empty;

                for (int i = 0; i < value.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ value[i];
                    if (i == value.Length - 1)
                        break;

                    hash2 = ((hash2 << 5) + hash2) ^ value[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }
    }
}
