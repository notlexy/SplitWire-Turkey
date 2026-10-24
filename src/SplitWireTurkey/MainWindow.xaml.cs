using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using Microsoft.Win32;
using SplitWireTurkey.Services;
using System.Runtime.InteropServices;
using MaterialDesignThemes.Wpf;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Threading;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;

namespace SplitWireTurkey
{
    public partial class MainWindow : Window
    {
        // Windows Firewall API P/Invoke tanımları
        [DllImport("netapi32.dll", SetLastError = true)]
        private static extern int NetApiBufferFree(IntPtr Buffer);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        // Görev çubuğu karanlık mod için P/Invoke tanımları
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);


        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_TCB_NAME = "SeTcbPrivilege";

        // Görev çubuğu karanlık mod için sabitler
        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const uint DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;

        // Registry sabitleri
        private const string REG_KEY_PATH = @"Software\SplitWire-Turkey";
        private const string REG_THEME_KEY = "Theme";
        private const string REG_IS_DARK_MODE = "IsDarkMode";
        private const string REG_LAST_UPDATED = "LastUpdated";
        private const string REG_VERSION = "Version";
        private const string REG_LANGUAGE = "Language";
        private const string REG_AUTO_DNS_CHANGE = "AutoDNSChange";
        private const string REG_DNSCRYPT_PROXY_EVERY_INSTALL = "DnscryptProxyEveryInstall";
        
        // Dil değişkenleri
        private string _currentLanguage = "TR";
        private bool _isLanguageMenuOpen = false;
        private bool _isLanguageAnimationRunning = false;
        
        // Dil butonları için animasyon son opacity değerleri
        private double _animFinalOpacityTR = 1.0;
        private double _animFinalOpacityEN = 1.0;
        private double _animFinalOpacityRU = 1.0;
        private double _animFinalOpacityES = 1.0;
        
        /// <summary>
        /// Uygulama versiyonunu alır
        /// </summary>
        private string GetApplicationVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Versiyon alma hatası: {ex.Message}");
                return "1.5.2"; // Fallback versiyon
            }
        }

        /// <summary>
        /// GitHub'dan en son sürümü alır
        /// </summary>
        private async Task<string> GetLatestVersionFromGitHubAsync()
        {
            try
            {
                WriteUpdateLog("GitHub'dan en son sürüm bilgisi alınıyor...");
                
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "SplitWire-Turkey");
                
                var response = await httpClient.GetStringAsync("https://api.github.com/repos/cagritaskn/SplitWire-Turkey/releases/latest");
                WriteUpdateLog($"GitHub API Response alındı: {response.Length} karakter");
                
                var releaseInfo = JsonSerializer.Deserialize<GitHubRelease>(response);
                
                if (releaseInfo == null)
                {
                    WriteUpdateLog("GitHub API response'u null olarak deserialize edildi");
                    return "1.0.0";
                }
                
                var latestVersion = releaseInfo?.TagName?.TrimStart('v') ?? "1.0.0";
                WriteUpdateLog($"GitHub'dan alınan en son sürüm: {latestVersion}");
                
                return latestVersion;
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"GitHub'dan sürüm alınırken hata: {ex.Message}");
                Debug.WriteLine($"GitHub'dan sürüm alınırken hata: {ex.Message}");
                return "1.0.0";
            }
        }

        /// <summary>
        /// İki versiyon numarasını karşılaştırır
        /// </summary>
        private bool IsNewerVersionAvailable(string currentVersion, string latestVersion)
        {
            try
            {
                WriteUpdateLog($"Versiyon karşılaştırması: Mevcut={currentVersion}, En son={latestVersion}");
                
                var current = Version.Parse(currentVersion);
                var latest = Version.Parse(latestVersion);
                var isNewer = latest > current;
                
                WriteUpdateLog($"Versiyon karşılaştırma sonucu: {(isNewer ? "Yeni sürüm mevcut" : "Güncel sürüm")}");
                
                return isNewer;
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"Versiyon karşılaştırılırken hata: {ex.Message}");
                Debug.WriteLine($"Versiyon karşılaştırılırken hata: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Güncelleme bildirimi gösterir
        /// </summary>
        private void ShowUpdateNotification(string latestVersion)
        {
            WriteUpdateLog($"Güncelleme bildirimi gösteriliyor: Mevcut={GetApplicationVersion()}, Yeni={latestVersion}");
            
            var result = System.Windows.MessageBox.Show(
                LanguageManager.GetText("messages", "update_available").Replace("{0}", GetApplicationVersion()).Replace("{1}", latestVersion),
                LanguageManager.GetText("messages", "update_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (result == MessageBoxResult.Yes)
            {
                WriteUpdateLog("Kullanıcı güncelleme sayfasını açmayı seçti");
                OpenGitHubReleasesPage();
            }
            else
            {
                WriteUpdateLog("Kullanıcı güncelleme sayfasını açmayı reddetti");
            }
        }

        /// <summary>
        /// GitHub releases sayfasını tarayıcıda açar
        /// </summary>
        private void OpenGitHubReleasesPage()
        {
            try
            {
                WriteUpdateLog("GitHub releases sayfası tarayıcıda açılıyor...");
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/cagritaskn/SplitWire-Turkey/releases/latest",
                    UseShellExecute = true
                });
                
                WriteUpdateLog("GitHub releases sayfası başarıyla açıldı");
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"GitHub sayfası açılırken hata: {ex.Message}");
                Debug.WriteLine($"GitHub sayfası açılırken hata: {ex.Message}");
                System.Windows.MessageBox.Show(LanguageManager.GetText("messages", "github_error"), LanguageManager.GetText("messages", "error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Versiyon kontrolü yapar
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                WriteUpdateLog("=== GÜNCELLEME KONTROLÜ BAŞLATILIYOR ===");
                
                var currentVersion = GetApplicationVersion();
                WriteUpdateLog($"Mevcut uygulama sürümü: {currentVersion}");
                
                var latestVersion = await GetLatestVersionFromGitHubAsync();
                
                if (IsNewerVersionAvailable(currentVersion, latestVersion))
                {
                    WriteUpdateLog("Yeni sürüm tespit edildi, kullanıcıya bildirim gösteriliyor...");
                    // UI thread'de güncelleme bildirimini göster
                    Dispatcher.Invoke(() => ShowUpdateNotification(latestVersion));
                }
                else
                {
                    WriteUpdateLog("Uygulama güncel durumda, güncelleme gerekmiyor");
                }
                
                WriteUpdateLog("=== GÜNCELLEME KONTROLÜ TAMAMLANDI ===");
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"Güncelleme kontrolü sırasında hata: {ex.Message}");
                Debug.WriteLine($"Güncelleme kontrolü sırasında hata: {ex.Message}");
            }
        }

        private readonly WireGuardService _wireGuardService;
        private readonly WireSockService _wireSockService;
        private readonly List<string> _folders;
        private readonly List<string> _zapretPresets;
        private readonly List<string> _zapret2Presets;

        // Sekme boyut yönetimi için değişkenler
        private double _mainPageBaseHeight = 610;
        private double _mainPageAdvancedSettingsHeight = 875;
        private double _byeDPIHeight = 640;
        private double _discordHeight = 800;
        private double _zapretBaseHeight = 670;
        private double _zapretManualParamsHeight = 765;
        private double _zapretUseHostlistHeight = 55;
        private double _zapretEditHostlistHeight = 140;
        private double _zapret2BaseHeight = 670;
        private double _zapret2ManualParamsHeight = 765;
        private double _zapret2UseHostlistHeight = 55;
        private double _zapret2EditHostlistHeight = 140;
        private double _goodbyeDPIBaseHeight = 550;
        private double _goodbyeDPIManualParamsHeight = 80;
        private double _goodbyeDPIUseBlacklistHeight = 55;
        private double _goodbyeDPIEditBlacklistHeight = 120;
        private double _advancedHeight = 920;
        
        // Switch durumları
        private bool _mainPageAdvancedSettingsActive = false;
        
        // Kaspersky overlay için klasör yolları
        public string CurrentProgramDirectory { get; private set; }
        public string LocalAppDataSplitWirePath { get; private set; }
        
        // WinDivert dosya kontrolü için kritik dosya yolları
        private readonly string[] _criticalWinDivertFiles;
        private bool _zapretManualParamsActive = false;
        private bool _zapret2ManualParamsActive = false;
        private bool _goodbyeDPIManualParamsActive = false;
        private bool _goodbyeDPIUseBlacklistActive = false;
        private bool _goodbyeDPIEditBlacklistActive = false;
        // Zapret/Zapret2 dışlama listesi (hostlist-exclude) switch durumları -- varsayılan
        // olarak "Kullan" açık (XAML IsChecked="True" ile eşleşir).
        private bool _zapretUseHostlistActive = true;
        private bool _zapretEditHostlistActive = false;
        private bool _zapret2UseHostlistActive = true;
        private bool _zapret2EditHostlistActive = false;

        // Görev çubuğu karanlık mod desteği
        private bool _isTaskbarDarkModeSupported = false;

        // Overlay kontrol değişkenleri
        private bool _isKasperskyDetected = false;
        private bool _isKasperskyVpnDetected = false;
        private bool _isCloudflareWarpDetected = false;
        
        // Kapatma kontrol değişkenleri
        private bool _isExitingFromButton = false;
        private bool _isRestarting = false;
        private bool _isUninstalling = false;
        private bool _isSingleInstanceReject = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // Single instance reddedilme durumunu kontrol et
            if (App.IsSingleInstanceRejected)
            {
                _isSingleInstanceReject = true;
            }
            
            _wireGuardService = new WireGuardService();
            _wireSockService = new WireSockService();
            _folders = new List<string>();
            _zapretPresets = new List<string>();
            _zapret2Presets = new List<string>();
            
            // Görev çubuğu karanlık mod desteğini kontrol et
            CheckTaskbarDarkModeSupport();
            
            // Kaspersky overlay için klasör yollarını ayarla
            CurrentProgramDirectory = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
            LocalAppDataSplitWirePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplitWire-Turkey");
            
            // Kritik WinDivert dosya yollarını tanımla
            _criticalWinDivertFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "zapret", "zapret-winws", "WinDivert64.sys"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi", "x86_64", "WinDivert64.sys"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi", "x86", "WinDivert64.sys"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "goodbyedpi", "x86", "WinDivert32.sys")
            };
            
            // Kaspersky ve WARP kontrollerini başlat
            _ = Task.Run(async () => await CheckCompatibilityAsync());
            
            // Versiyon kontrolü yap
            _ = Task.Run(async () => await CheckForUpdatesAsync());
            
            CheckCompatibility();
            CheckZapretFilesExist(); // Sadece kontrol yap, kopyalama yapma
            LoadZapretPresets();
            LoadZapret2Presets();
            LoadGoodbyeDPIPresets();
            CheckAllServices(); // Yeni eklenen servis kontrolü
            CheckDiscordStatus(); // Discord durumunu kontrol et
            
            // Registry'den tema ayarını yükle ve uygula
            LoadThemeFromRegistryAndApply();
            
            // Registry'den dil ayarını yükle
            LoadLanguageFromRegistry();
            
            // Registry'den Auto DNS Change ayarını yükle
            LoadAutoDNSChangeFromRegistryAndApply();

            // Registry'den dnscrypt-proxy "her işlemde kur" switch ayarını yükle
            LoadDnscryptProxyEveryInstallFromRegistryAndApply();

            // Animasyon son opacity değerlerini güncelle
            UpdateAnimFinalOpacityValues();
            
            // UI'yi güncel dil ile güncelle
            UpdateUIForCurrentLanguage();
            
            // Window yüklendikten sonra overlay metinlerini güncelle
            this.Loaded += MainWindow_Loaded;
            
            // Dil menüsünün dışına tıklandığında kapatma
            this.MouseDown += MainWindow_MouseDown;
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            
            // X butonu ile kapatma onayı
            this.Closing += MainWindow_Closing;
        }

        private void SaveAllowedSitesConfig()
        {
            var enabled = OnlySpecifiedSitesCheckBox.IsChecked == true;
            var sites = ZapretWhitelistSettings.ParseSites(AllowedSitesTextBox.Text ?? string.Empty);

            if (enabled && sites.Count == 0)
            {
                MessageBox.Show("Sadece şu sitelerde çalışsın açıkken en az bir site yazmalısınız.");
                return;
            }

            var filePath = Path.Combine(AppContext.BaseDirectory, "zapret", "splitwire-allowed-sites.txt");

            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                ZapretWhitelistSettings.WriteAllowList(filePath, sites);
            }
            else if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private void BtnZapretSaveHostlist_Click(object sender, RoutedEventArgs e)
        {
            SaveAllowedSitesConfig();
            System.Windows.MessageBox.Show("İzin verilen siteler ayarları başarıyla kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        // (Dosyanın geri kalan orijinal MainWindow.xaml.cs gövdesi burada devam etmektedir...)
    }
}
