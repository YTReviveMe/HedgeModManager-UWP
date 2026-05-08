using HedgeModManager.Uwp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace HedgeModManager.Uwp.Services
{
    public sealed class ModUpdateService
    {
        private static readonly HttpClient Http = new HttpClient();

        public async Task<ModUpdateEntry> CheckForUpdate(ModEntry mod)
        {
            if (mod == null || !mod.HasUpdateServer)
                return new ModUpdateEntry { Mod = mod, Status = "No update server", CanUpdate = false };

            var versionUrl = BuildUrl(mod.UpdateServer, "mod_version.ini");
            string versionText;
            try
            {
                versionText = await Http.GetStringAsync(versionUrl);
            }
            catch (Exception ex)
            {
                return new ModUpdateEntry { Mod = mod, Status = $"Check failed: {ex.Message}", CanUpdate = false };
            }

            var versionIni = HedgeIniFile.Parse(versionText);
            var remoteVersion = versionIni.Get("Main", "VersionString");
            if (string.IsNullOrWhiteSpace(remoteVersion))
                return new ModUpdateEntry { Mod = mod, Status = "No VersionString", CanUpdate = false };

            var changelog = await ReadChangelog(mod.UpdateServer, versionIni);
            var hasUpdate = !string.Equals(remoteVersion, mod.Version, StringComparison.OrdinalIgnoreCase);
            return new ModUpdateEntry
            {
                Mod = mod,
                RemoteVersion = remoteVersion,
                Changelog = string.IsNullOrWhiteSpace(changelog) ? "No changelog provided." : changelog,
                CanUpdate = hasUpdate,
                Status = hasUpdate ? "Update available" : "Up to date"
            };
        }

        public async Task<string> ApplyUpdate(ModUpdateEntry update)
        {
            if (update?.Mod == null || !update.CanUpdate)
                return "No update to apply.";

            var server = update.Mod.UpdateServer;
            var commandsUrl = BuildUrl(server, "mod_files.txt");
            string commandText;
            try
            {
                commandText = await Http.GetStringAsync(commandsUrl);
            }
            catch (Exception ex)
            {
                return $"Failed to fetch mod_files.txt: {ex.Message}";
            }

            var tempRoot = Path.Combine(update.Mod.Folder.Path, ".hmmtemp-uwp");
            Directory.CreateDirectory(tempRoot);

            var lines = commandText.Replace("\r\n", "\n").Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                var firstSpace = line.IndexOf(' ');
                if (firstSpace <= 0)
                    continue;

                var command = line.Substring(0, firstSpace).Trim().ToLowerInvariant();
                var arg = line.Substring(firstSpace + 1).Trim();
                var targetPath = Path.GetFullPath(Path.Combine(update.Mod.Folder.Path, arg));
                if (!targetPath.StartsWith(update.Mod.Folder.Path, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (command == "mkdir")
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                if (command == "delete")
                {
                    if (arg.EndsWith("/") || arg.EndsWith("\\"))
                    {
                        if (Directory.Exists(targetPath))
                            Directory.Delete(targetPath, true);
                    }
                    else if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    continue;
                }

                if (command == "add")
                {
                    var normalizedArg = arg.Replace('\\', '/');
                    var fileUrl = BuildUrl(server, Uri.EscapeDataString(normalizedArg).Replace("%2F", "/"));
                    var tempPath = Path.Combine(tempRoot, arg);
                    Directory.CreateDirectory(Path.GetDirectoryName(tempPath));
                    var bytes = await Http.GetByteArrayAsync(fileUrl);
                    File.WriteAllBytes(tempPath, bytes);
                }
            }

            foreach (var tempFile in Directory.GetFiles(tempRoot, "*", SearchOption.AllDirectories))
            {
                var rel = tempFile.Substring(tempRoot.Length).TrimStart('\\');
                var destination = Path.Combine(update.Mod.Folder.Path, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(tempFile, destination);
            }

            Directory.Delete(tempRoot, true);
            update.Mod.Version = update.RemoteVersion;
            update.Status = "Updated";
            update.CanUpdate = false;
            return "Update applied.";
        }

        private static async Task<string> ReadChangelog(string updateServer, HedgeIniFile versionIni)
        {
            var markdownPath = versionIni.Get("Main", "Markdown");
            if (!string.IsNullOrWhiteSpace(markdownPath))
            {
                try
                {
                    return await Http.GetStringAsync(BuildUrl(updateServer, markdownPath));
                }
                catch
                {
                }
            }

            var countText = versionIni.Get("Changelog", "StringCount");
            if (!int.TryParse(countText, out var count) || count <= 0)
                return string.Empty;

            var lines = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var line = versionIni.Get("Changelog", $"String{i}");
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add($"- {line}");
            }
            return string.Join("\n", lines);
        }

        private static string BuildUrl(string baseUrl, string relative)
        {
            baseUrl = (baseUrl ?? string.Empty).TrimEnd('/', '\\');
            relative = (relative ?? string.Empty).TrimStart('/', '\\').Replace('\\', '/');
            return $"{baseUrl}/{relative}";
        }
    }
}
