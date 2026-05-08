namespace HedgeModManager.Uwp.Models
{
    public sealed class ModUpdateEntry
    {
        public ModEntry Mod { get; set; }
        public string RemoteVersion { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool CanUpdate { get; set; }
    }
}
