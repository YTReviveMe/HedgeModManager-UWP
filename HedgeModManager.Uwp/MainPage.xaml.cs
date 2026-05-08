using HedgeModManager.Uwp.Models;
using HedgeModManager.Uwp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http;

namespace HedgeModManager.Uwp
{
    public sealed partial class MainPage : Page
    {
        private const string FixedModsPath = @"E:\Unleashed\mods";
        private const string FixedModsDbPath = @"E:\Unleashed\mods\ModsDB.ini";
        private const string FixedCpkredirPath = @"E:\Unleashed\cpkredir.ini";
        private const string GameBananaUrl = "https://gamebanana.com/mods/games/21975";
        private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        private readonly XboxModsDatabase _database = new XboxModsDatabase();
        private readonly ModUpdateService _updateService = new ModUpdateService();
        private readonly QuickInstallService _quickInstallService = new QuickInstallService();
        private StorageFolder _modsFolder;
        private TabPage _currentTab = TabPage.Main;

        public ObservableCollection<ModEntry> Mods { get; } = new ObservableCollection<ModEntry>();
        public ObservableCollection<ModEntry> FilteredMods { get; } = new ObservableCollection<ModEntry>();
        public ObservableCollection<ModUpdateEntry> Updates { get; } = new ObservableCollection<ModUpdateEntry>();
        public ObservableCollection<QuickInstallEntry> QuickInstallLinks { get; } = new ObservableCollection<QuickInstallEntry>();

        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
            GameBananaWebView.NavigationStarting += GameBananaWebView_NavigationStarting;
            GameBananaWebView.NavigationCompleted += GameBananaWebView_NavigationCompleted;
            NavigateGameBanana();
            await ResolveFixedModsFolder();
            await LoadMods();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
            GameBananaWebView.NavigationStarting -= GameBananaWebView_NavigationStarting;
            GameBananaWebView.NavigationCompleted -= GameBananaWebView_NavigationCompleted;
        }

        private async void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            await ResolveFixedModsFolder();
            await LoadMods();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadMods();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await SaveMods();
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckUpdates();
        }

        private void AddInstallLink_Click(object sender, RoutedEventArgs e)
        {
            var mmdlId = (MmdlIdBox.Text ?? string.Empty).Trim();
            var modId = (ModIdBox.Text ?? string.Empty).Trim();

            if (!IsExactDigits(mmdlId, 7) || !IsExactDigits(modId, 6))
            {
                GameBananaStatusText.Text = "Enter 7 digits for mmdl and 6 digits for mod id.";
                return;
            }

            var generated = $"https://gamebanana.com/mmdl/{mmdlId},Mod,{modId}";
            if (_quickInstallService.TryParse(generated, out var entry, out var error))
            {
                QuickInstallLinks.Add(entry);
                MmdlIdBox.Text = string.Empty;
                ModIdBox.Text = string.Empty;
                GameBananaStatusText.Text = $"Added {entry.DisplayName}";
            }
            else
            {
                GameBananaStatusText.Text = error;
            }
        }

        private async void InstallSelectedLink_Click(object sender, RoutedEventArgs e)
        {
            if (_modsFolder == null)
            {
                GameBananaStatusText.Text = "Mods folder is not available.";
                return;
            }

            if (!(InstallLinksList.SelectedItem is QuickInstallEntry selected))
            {
                GameBananaStatusText.Text = "Select a link first.";
                return;
            }

            selected.Status = "Installing...";
            GameBananaStatusText.Text = $"Installing {selected.DisplayName}...";
            var result = await _quickInstallService.InstallAsync(selected, _modsFolder);
            selected.Status = result.StartsWith("Installed ", StringComparison.OrdinalIgnoreCase) ? "Installed" : "Failed";
            GameBananaStatusText.Text = result;
            InstallLinksList.ItemsSource = null;
            InstallLinksList.ItemsSource = QuickInstallLinks;
            await LoadMods();
        }

        private async void UpdateSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!(UpdatesList.SelectedItem is ModUpdateEntry update))
                return;

            UpdatesStatusText.Text = $"Updating {update.Mod.Title}...";
            var message = await _updateService.ApplyUpdate(update);
            UpdatesStatusText.Text = $"{update.Mod.Title}: {message}";
            await LoadMods();
            await CheckUpdates();
        }

        private void UpdatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UpdatesList.SelectedItem is ModUpdateEntry update)
                ChangelogText.Text = update.Changelog;
            else
                ChangelogText.Text = string.Empty;
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void MmdlIdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            NormalizeDigitBox(MmdlIdBox, 7);
            if (MmdlIdBox.Text.Length == 7)
            {
                ModIdBox.Focus(FocusState.Programmatic);
                ModIdBox.Select(ModIdBox.Text.Length, 0);
            }
        }

        private void ModIdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            NormalizeDigitBox(ModIdBox, 6);
        }

        private async Task LoadMods()
        {
            if (_modsFolder == null)
            {
                SetStatus($"Cannot access fixed folder: {FixedModsPath}");
                return;
            }

            try
            {
                SetStatus("Reading mods...");
                var mods = await _database.Load(_modsFolder);

                Mods.Clear();
                foreach (var mod in mods.OrderByDescending(t => t.Enabled).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
                    Mods.Add(mod);

                await EnsureCpkredirIniAsync();
                FolderText.Text = _modsFolder.Path;
                ApplyFilter();
                await UpdateSanityText();
                SetStatus($"Loaded {Mods.Count} mod(s).");
            }
            catch (Exception ex)
            {
                SetStatus($"Load failed: {ex.Message}");
            }
        }

        private async Task SaveMods()
        {
            if (_modsFolder == null)
            {
                SetStatus($"Cannot access fixed folder: {FixedModsPath}");
                return;
            }

            try
            {
                SetStatus("Saving ModsDB.ini...");
                await _database.Save(_modsFolder, Mods);
                await EnsureCpkredirIniAsync();
                var validation = await _database.ValidateSavedDatabase(_modsFolder, Mods);
                await UpdateSanityText(validation.message);
                SetStatus(validation.ok ? "Saved ModsDB.ini." : $"Saved with warning: {validation.message}");
            }
            catch (Exception ex)
            {
                SetStatus($"Save failed: {ex.Message}");
            }
        }

        private void ApplyFilter()
        {
            var filter = FilterBox.Text?.Trim();
            FilteredMods.Clear();

            foreach (var mod in Mods)
            {
                if (string.IsNullOrEmpty(filter) ||
                    mod.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mod.Author.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mod.FolderName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FilteredMods.Add(mod);
                }
            }

            CountText.Text = $"{FilteredMods.Count}/{Mods.Count}";
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private async Task CheckUpdates()
        {
            UpdatesStatusText.Text = "Checking update servers...";
            Updates.Clear();

            var updateMods = Mods.Where(m => m.HasUpdateServer).ToList();
            foreach (var mod in updateMods)
            {
                var update = await _updateService.CheckForUpdate(mod);
                Updates.Add(update);
            }

            var available = Updates.Count(u => u.CanUpdate);
            UpdatesStatusText.Text = $"Checked {updateMods.Count} mod(s), {available} update(s) available.";
            if (Updates.Count > 0)
                UpdatesList.SelectedIndex = 0;
        }

        private void SetTab(TabPage tab)
        {
            _currentTab = tab;
            MainPanel.Visibility = tab == TabPage.Main ? Visibility.Visible : Visibility.Collapsed;
            UpdatesPanel.Visibility = tab == TabPage.Updates ? Visibility.Visible : Visibility.Collapsed;
            GameBananaPanel.Visibility = tab == TabPage.GameBanana ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            if (args.VirtualKey == VirtualKey.GamepadRightShoulder)
            {
                if (_currentTab == TabPage.Main)
                {
                    SetTab(TabPage.Updates);
                    if (Updates.Count == 0)
                        await CheckUpdates();
                }
                else if (_currentTab == TabPage.Updates)
                {
                    SetTab(TabPage.GameBanana);
                }
                else
                {
                    SetTab(TabPage.Main);
                }
            }
            else if (args.VirtualKey == VirtualKey.GamepadLeftShoulder)
            {
                if (_currentTab == TabPage.Main)
                {
                    SetTab(TabPage.GameBanana);
                }
                else if (_currentTab == TabPage.GameBanana)
                {
                    SetTab(TabPage.Updates);
                }
                else
                {
                    SetTab(TabPage.Main);
                }
            }
        }

        private void GameBananaWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            if (args.Uri != null && !IsAllowedGameBananaHost(args.Uri.Host))
            {
                args.Cancel = true;
                _ = Launcher.LaunchUriAsync(args.Uri);
                GameBananaStatusText.Text = $"Opened externally: {args.Uri.Host}";
                return;
            }

            GameBananaStatusText.Text = $"Loading: {args.Uri}";
        }

        private void GameBananaWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                GameBananaStatusText.Text = "GameBanana loaded.";
            }
            else
            {
                GameBananaStatusText.Text = $"Failed to load: {args.WebErrorStatus}";
            }
        }

        private async Task ResolveFixedModsFolder()
        {
            try
            {
                _modsFolder = await StorageFolder.GetFolderFromPathAsync(FixedModsPath);
                FolderText.Text = _modsFolder.Path;
            }
            catch (Exception ex)
            {
                _modsFolder = null;
                FolderText.Text = FixedModsPath;
                SetStatus($"Failed to open {FixedModsPath}: {ex.Message}");
            }
        }

        private async Task EnsureCpkredirIniAsync()
        {
            var gameRoot = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(FixedModsPath));
            var file = await gameRoot.CreateFileAsync("cpkredir.ini", CreationCollisionOption.OpenIfExists);
            var output = new StringBuilder();
            output.AppendLine("[CPKREDIR]");
            output.AppendLine("Enabled=1");
            output.AppendLine("PlaceTocAtEnd=1");
            output.AppendLine("HandleCpksWithoutExtFiles=0");
            output.AppendLine("LogFile=\"cpkredir.log\"");
            output.AppendLine("ReadBlockSizeKB=4096");
            output.AppendLine($"ModsDbIni=\"{FixedModsDbPath}\"");
            output.AppendLine("EnableSaveFileRedirection=1");
            output.AppendLine("SaveFileFallback=\"\"");
            output.AppendLine("SaveFileOverride=\"\"");
            output.AppendLine("LogType=\"\"");
            output.AppendLine();
            output.AppendLine("[HedgeModManager]");
            output.AppendLine("EnableFallbackSaveRedirection=1");
            output.AppendLine("UseLauncher=0");
            output.AppendLine("ModProfile=\"Default\"");

            await FileIO.WriteTextAsync(file, output.ToString());
        }

        private async Task UpdateSanityText(string extra = null)
        {
            var checks = new StringBuilder();
            checks.AppendLine($"ModsPath: {( _modsFolder != null ? "OK" : "FAIL")} ({FixedModsPath})");

            var modsDbOk = File.Exists(FixedModsDbPath);
            checks.AppendLine($"ModsDB: {(modsDbOk ? "OK" : "FAIL")} ({FixedModsDbPath})");

            var cpkOk = false;
            var cpkDetails = "missing";
            if (File.Exists(FixedCpkredirPath))
            {
                var cpkText = await FileIO.ReadTextAsync(await StorageFile.GetFileFromPathAsync(FixedCpkredirPath));
                cpkOk = cpkText.IndexOf("[CPKREDIR]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        cpkText.IndexOf("Enabled=1", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        cpkText.IndexOf($"ModsDbIni=\"{FixedModsDbPath}\"", StringComparison.OrdinalIgnoreCase) >= 0;
                cpkDetails = cpkOk ? "cpkredir+hedgemm format" : "found but invalid";
            }
            checks.AppendLine($"CPKREDIR: {(cpkOk ? "OK" : "FAIL")} ({cpkDetails})");
            checks.AppendLine($"ModFolders: {Mods.Count}");
            checks.AppendLine($"EnabledMods: {Mods.Count(t => t.Enabled)}");

            if (!string.IsNullOrWhiteSpace(extra))
                checks.AppendLine($"Validation: {extra}");

            SanityText.Text = checks.ToString().TrimEnd();
        }

        private enum TabPage
        {
            Main,
            Updates,
            GameBanana
        }

        private static bool IsAllowedGameBananaHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            host = host.ToLowerInvariant();
            return host == "gamebanana.com" || host.EndsWith(".gamebanana.com");
        }

        private static bool IsExactDigits(string value, int length)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != length)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                    return false;
            }

            return true;
        }

        private static void NormalizeDigitBox(TextBox textBox, int maxDigits)
        {
            var source = textBox.Text ?? string.Empty;
            var digits = new StringBuilder(maxDigits);
            for (var i = 0; i < source.Length && digits.Length < maxDigits; i++)
            {
                if (char.IsDigit(source[i]))
                    digits.Append(source[i]);
            }

            var normalized = digits.ToString();
            if (!string.Equals(source, normalized, StringComparison.Ordinal))
            {
                textBox.Text = normalized;
                textBox.Select(textBox.Text.Length, 0);
            }
        }

        private void NavigateGameBanana()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(GameBananaUrl));
            request.Headers.UserAgent.ParseAdd(DesktopUserAgent);
            GameBananaWebView.NavigateWithHttpRequestMessage(request);
        }

    }
}
