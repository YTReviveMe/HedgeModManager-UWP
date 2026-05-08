using HedgeModManager.Uwp.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;

namespace HedgeModManager.Uwp.Services
{
    public sealed class QuickInstallService
    {
        private static readonly HttpClient Http = new HttpClient();
        private const string MmdlPrefix = "https://gamebanana.com/mmdl/";

        public bool TryParse(string input, out QuickInstallEntry entry, out string error)
        {
            entry = null;
            error = null;
            var text = (input ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "Link is empty.";
                return false;
            }

            if (text.StartsWith("hedgemmswa:", StringComparison.OrdinalIgnoreCase))
                text = text.Substring("hedgemmswa:".Length);

            var parts = text.Split(',');
            if (parts.Length < 1)
            {
                error = "Invalid install URL.";
                return false;
            }

            var urlPart = NormalizeMmdlInput(parts[0].Trim());
            if (!Uri.TryCreate(urlPart, UriKind.Absolute, out var uri))
            {
                error = "Invalid install URL.";
                return false;
            }

            entry = new QuickInstallEntry
            {
                RawLink = input,
                DownloadUrl = uri.ToString(),
                ItemType = parts.Length > 1 ? parts[1].Trim() : "Mod",
                ItemId = parts.Length > 2 ? parts[2].Trim() : string.Empty,
                DisplayName = $"GB { (parts.Length > 1 ? parts[1].Trim() : "Mod")} { (parts.Length > 2 ? parts[2].Trim() : string.Empty)}",
                Status = "Ready"
            };
            return true;
        }

        public async Task<string> InstallAsync(QuickInstallEntry entry, StorageFolder modsFolder)
        {
            if (entry == null || modsFolder == null)
                return "Invalid install request.";

            byte[] bytes;
            try
            {
                bytes = await Http.GetByteArrayAsync(entry.DownloadUrl);
            }
            catch (Exception ex)
            {
                return $"Download failed: {ex.Message}";
            }

            var tempRoot = Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "hmm-quickinstall");
            Directory.CreateDirectory(tempRoot);
            var archivePath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.archive");
            File.WriteAllBytes(archivePath, bytes);

            string extractedRoot = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractedRoot);
            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    foreach (var item in archive.Entries.Where(e => !e.IsDirectory))
                        item.WriteToDirectory(extractedRoot, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }
            }
            catch (Exception ex)
            {
                return $"Unsupported archive (extract failed): {ex.Message}";
            }

            var modIniPath = Directory.GetFiles(extractedRoot, "mod.ini", SearchOption.AllDirectories).FirstOrDefault();
            if (modIniPath == null)
                return "No mod.ini found in archive.";

            var modRoot = Path.GetDirectoryName(modIniPath);
            var installName = new DirectoryInfo(modRoot).Name;
            var destPath = Path.Combine(modsFolder.Path, installName);
            if (Directory.Exists(destPath))
                Directory.Delete(destPath, true);

            CopyDirectory(modRoot, destPath);
            return $"Installed to {destPath}";
        }

        private static string NormalizeMmdlInput(string value)
        {
            value = (value ?? string.Empty).Trim();

            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return value;

            if (value.StartsWith("gamebanana.com/", StringComparison.OrdinalIgnoreCase))
                return $"https://{value}";

            if (value.StartsWith("/mmdl/", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("/mmdl/".Length);
            else if (value.StartsWith("mmdl/", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("mmdl/".Length);

            return MmdlPrefix + value.TrimStart('/');
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                var dest = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var dest = Path.Combine(destination, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
            }
        }
    }
}
