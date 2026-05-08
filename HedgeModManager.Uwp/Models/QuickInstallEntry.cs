namespace HedgeModManager.Uwp.Models
{
    public sealed class QuickInstallEntry
    {
        public string RawLink { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = "Ready";
    }
}
