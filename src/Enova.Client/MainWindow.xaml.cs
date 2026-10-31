using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Lragrz.Enova.Client.Services;
using Lragrz.Enova.Core.Config;
using Lragrz.Enova.Core.Crypto;
using Lragrz.Enova.Core.Models;
using Lragrz.Enova.Core.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lragrz.Enova.Client;

public partial class MainWindow : Window
{
    private sealed class OpenDoc
    {
        public string Path { get; init; } = "";
        public string FileName { get; init; } = "";
        public string? DetachedSignaturePath { get; set; }
        public XmlDocument Document { get; init; } = null!;
        public string? FileNumber { get; init; }
    }

    private readonly EnovaConfiguration _config;
    private readonly IQesProvider _qesProvider;
    private readonly EnovaXmlProcessor _processor;
    private readonly DocumentUploadClient _uploadClient;
    private readonly AuthSession _session;
    private readonly UserPreferences _prefs;
    private readonly ReferenceDataClient _refData;

    private readonly List<OpenDoc> _openDocs = new();
    private OpenDoc? _current;

    private ProcessingResult? _lastResult;
    private string? _lastSignedPath;
    private string? _lastPdfPath;
    private int _pendingUploads;
    private string? _pendingDetached;
    private readonly List<string> _sessionQueue = new();
    private DateTime _lastActivity = DateTime.Now;

    private Dictionary<string, List<string>> _decisionMappings = new();
    private List<CodeListEntryDto> _decisionCodes = new();

    private const int MaxOpen = 10;

    public MainWindow(AuthSession session, EnovaConfiguration config, DocumentUploadClient uploadClient)
    {
        InitializeComponent();

        _session = session;
        _config = config;
        _uploadClient = uploadClient;
        _prefs = UserPreferences.Load();
        _refData = new ReferenceDataClient(config);

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        var factory = new QesProviderFactory(_config, loggerFactory);
        _qesProvider = factory.Create();
        _processor = new EnovaXmlProcessor(_qesProvider, loggerFactory.CreateLogger<EnovaXmlProcessor>(), _config.SenderName);

        ProviderLabel.Text = _qesProvider.DisplayName;
        QuestPDF.Settings.License = LicenseType.Community;

        ApplyUserChrome();
        StatusText.Text = "Angemeldet.";

        // Always open on the primary monitor; restore last window state
        WindowStartupLocation = WindowStartupLocation.Manual;
        var primary = System.Windows.SystemParameters.WorkArea;
        Left = primary.Left + 40;
        Top = primary.Top + 40;
        var stateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eNoVa_tor", "window.state");
        if (File.Exists(stateFile) && File.ReadAllText(stateFile).Trim() == "Maximized")
            WindowState = WindowState.Maximized;

        Closing += (_, e) =>
        {
            if (_pendingUploads > 0)
            {
                MessageBox.Show("Uploads zum Server laufen noch. Bitte warten.", "Hinweis");
                e.Cancel = true;
                return;
            }
            _ = SendHeartbeatAsync(shutdown: true);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
                File.WriteAllText(stateFile, WindowState == WindowState.Maximized ? "Maximized" : "Normal");
            }
            catch { }
        };

        FileNumberBox.TextChanged += (_, _) => RefreshPreview();
        DecisionCombo.SelectionChanged += (_, _) => RefreshPreview();

        Loaded += async (_, _) =>
        {
            await LoadReferenceDataAsync();
            QueueList.ItemsSource = null;
            await SendHeartbeatAsync();
            // Periodic heartbeat every 60s
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            timer.Tick += async (_, _) => await SendHeartbeatAsync();
            timer.Start();

            // Inactivity timeout: 20 min, warn at 15
            _lastActivity = DateTime.Now;
            PreviewMouseMove += (_, _) => _lastActivity = DateTime.Now;
            KeyDown += (_, _) => _lastActivity = DateTime.Now;
            var idleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            idleTimer.Tick += (_, _) =>
            {
                var idle = DateTime.Now - _lastActivity;
                if (idle.TotalMinutes >= 20)
                {
                    MessageBox.Show("Sitzung wegen Inaktivität beendet.", "Abgemeldet");
                    _session.Logout();
                    Application.Current.Shutdown();
                }
                else if (idle.TotalMinutes >= 15)
                {
                    var left = TimeSpan.FromMinutes(20) - idle;
                    StatusText.Text = $"Sitzung endet in {left.Minutes}:{left.Seconds:00}";
                }
            };
            idleTimer.Start();
            // Ensure viewer starts white even with no document
            ViewerHost.Content = new TextBlock
            {
                Text = "Keine Datei geladen",
                FontSize = 16,
                Foreground = System.Windows.Media.Brushes.Gray,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        };
    }

    private void RefreshPreview()
    {
        if (_current is null) return;
        var decisionLabel = (DecisionCombo.SelectedItem as CodeListEntryDto)?.Label ?? "";
        ViewerHost.Content = NativePreviewBuilder.Build(_current.Document, _session.DisplayName, decisionLabel, FileNumberBox.Text);
    }

    private async Task SendHeartbeatAsync(bool shutdown = false)
    {
        try
        {
            using var http = EnovaHttp.Create(TimeSpan.FromSeconds(10));
            var url = shutdown
                ? $"{_config.ServerBaseUrl.TrimEnd('/')}/api/auth/logout"
                : $"{_config.ServerBaseUrl.TrimEnd('/')}/api/heartbeat";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
            await http.SendAsync(req);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Heartbeat fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task LoadReferenceDataAsync()
    {
        if (string.IsNullOrEmpty(_session.AccessToken)) return;
        try
        {
            _decisionMappings = await _refData.GetDecisionMappingsAsync(_session.AccessToken);
            _decisionCodes = await _refData.GetCodeListAsync("sachentscheidung", _session.AccessToken);

            DecisionCombo.ItemsSource = _decisionCodes;
            DecisionCombo.SelectedIndex = -1;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Referenzdaten nicht geladen: " + ex.Message;
        }
    }

    private void ApplyUserChrome()
    {
        var name = HtmlSanitizer.Plain(_session.DisplayName ?? _session.Username ?? "");
        var role = Strings.Role(_session.Role);
        UserLabel.Text = string.IsNullOrEmpty(role) ? name : $"{name} · {role}";
        CaseworkerLabel.Text = name;
        SettingsButton.Visibility = string.Equals(_session.Role, "admin", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(_session.Role, "manager", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_session, _config);
        win.Owner = this;
        win.ShowDialog();
    }

    private void OnAdmin(object sender, RoutedEventArgs e)
    {
        var win = new AccountManagementWindow(_session, _config);
        win.Owner = this;
        win.ShowDialog();
    }

    private void OnLogout(object sender, RoutedEventArgs e)
    {
        _session.Logout();
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var login = new LoginWindow(_session);
        Hide();
        if (login.ShowDialog() == true && _session.IsAuthenticated)
        {
            try { _session.ApplyRemoteConfigAsync().GetAwaiter().GetResult(); } catch { }
            ApplyUserChrome();
            StatusText.Text = "Angemeldet.";
            Show();
            Application.Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private void OnViewerDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnViewerDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnViewerDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnViewerDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files is null || files.Length == 0) return;

        var file = files[0];
        if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenXmlFromPathAsync(file);
        }
        else if (file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var result = PdfA3Service.VerifyAndExtract(file, _config.AuthorityIdentifier);
            MessageBox.Show(result.Summary, "PDF-Prüfung");
        }
        else
        {
            MessageBox.Show("Nur XML- oder PDF-Dateien werden unterstützt.", "Hinweis");
        }
        e.Handled = true;
    }

    private static string? PromptForDetachedSignature()
    {
        var dlg = new Window
        {
            Title = "Detached Signatur erforderlich",
            Width = 520,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            AllowDrop = true,
            Topmost = true
        };
        string? result = null;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Keine eingebettete Signatur gefunden.\nZiehen Sie die .p7s-Datei hierher oder wählen Sie sie aus.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var browse = new Button { Content = "Datei wählen", Padding = new Thickness(14, 8, 14, 8), FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        browse.Click += (_, _) =>
        {
            var ofd = new OpenFileDialog { Filter = "PKCS7 (*.p7s;*.p7b;*.pkcs7)|*.p7s;*.p7b;*.pkcs7" };
            if (ofd.ShowDialog() == true) { result = ofd.FileName; dlg.DialogResult = true; dlg.Close(); }
        };
        panel.Children.Add(browse);
        dlg.Content = panel;
        dlg.Drop += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0) { result = files[0]; dlg.DialogResult = true; dlg.Close(); }
            }
        };
        dlg.ShowDialog();
        return result;
    }

    private static string CaseFingerprint(XmlDocument doc, string? signaturePath)
    {
        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("tns", "http://www.xjustiz.de");
        string X(string xp) => doc.SelectSingleNode(xp, nsmgr)?.InnerText?.Trim() ?? "";

        var uvz = X("//tns:urNr");
        var datum = X("//tns:urkundsdatum");
        var flur = X("//tns:grundbuchblatt");
        var gemarkung = X("//tns:gemarkung");

        string sigHash = "";
        if (!string.IsNullOrEmpty(signaturePath) && File.Exists(signaturePath))
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            sigHash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(signaturePath)))[..16];
        }

        var raw = $"{uvz}|{datum}|{flur}|{gemarkung}|{sigHash}";
        using var sha2 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha2.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw)))[..32];
    }

    private async Task<(bool exists, string info)> CheckCaseAlreadyProcessedAsync(string caseId, string? fingerprint = null)
    {
        try
        {
            using var http = EnovaHttp.Create(TimeSpan.FromSeconds(15));
            var url = $"{_config.ServerBaseUrl.TrimEnd('/')}/api/cases/check?caseId={Uri.EscapeDataString(caseId)}";
            if (!string.IsNullOrEmpty(fingerprint))
                url += $"&sigHash={Uri.EscapeDataString(fingerprint)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (true, "Vorgangsprüfung fehlgeschlagen.");
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var exists = doc.RootElement.TryGetProperty("exists", out var ex) && ex.GetBoolean();
            if (!exists) return (false, "");
            var by = doc.RootElement.TryGetProperty("processedBy", out var pb) ? pb.GetString() : "?";
            var div = doc.RootElement.TryGetProperty("divisionLabel", out var dl) ? dl.GetString() : "";
            var at = doc.RootElement.TryGetProperty("processedAt", out var pa) ? pa.GetString() : "";
            var who = string.IsNullOrEmpty(div) ? by : $"{by}//{div}";
            return (true, $"Bearbeitet von {who} am {at}");
        }
        catch
        {
            return (true, "Vorgangsprüfung nicht erreichbar.");
        }
    }

    private async Task RecordCaseAsync(string caseId, string? fingerprint = null)
    {
        try
        {
            using var http = EnovaHttp.Create(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/cases/record")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { caseId, signatureHash = fingerprint, specificsHash = fingerprint })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                StatusText.Text = "Vorgang konnte nicht registriert werden.";
        }
        catch
        {
            StatusText.Text = "Vorgang konnte nicht registriert werden.";
        }
    }

    private async Task OpenXmlFromPathAsync(string path)
    {
        if (_openDocs.Count >= MaxOpen)
        {
            MessageBox.Show($"Maximal {MaxOpen} Dateien gleichzeitig geöffnet.", "Hinweis");
            return;
        }

        try
        {
            var doc = _processor.LoadFromFile(path);

            var sig = NotarySignatureVerifier.Verify(path);
            if (!sig.IsVerified)
            {
                if (sig.Mode == "Keine Signatur")
                {
                    MessageBox.Show("Keine Signatur gefunden. Bitte die zugehörige .p7s-Datei angeben.", "Signatur fehlt");
                    var p7s = PromptForDetachedSignature();
                    if (p7s is null)
                    {
                        MessageBox.Show("Keine Signatur angegeben. Datei wird nicht geöffnet.", "Abgelehnt");
                        return;
                    }
                    sig = NotarySignatureVerifier.VerifyWithDetached(path, p7s);
                }

                if (!sig.IsVerified)
                {
                    MessageBox.Show($"Signatur ungültig.\n\n{sig.Summary}", "Abgelehnt", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show($"{sig.Summary}\n\nModus: {sig.Mode}\nUnterzeichner: {sig.Signer ?? "—"}", Brand.Window("Signatur gültig"), MessageBoxButton.OK, MessageBoxImage.Information);
            SignatureInfo.Text = $"Signatur: {sig.Mode} · {sig.Signer ?? "—"}";

            ErsuchenSachentscheidungChecker.Check(doc);

            var az = doc.SelectSingleNode("//tns:nachrichtenkopf/tns:absender/tns:aktenzeichen", new XmlNamespaceManager(doc.NameTable) { })?.InnerText?.Trim();
            if (!string.IsNullOrEmpty(az))
            {
                var fp = CaseFingerprint(doc, _pendingDetached);
                var (blocked, info) = await CheckCaseAlreadyProcessedAsync(az, fp);
                if (blocked)
                {
                    MessageBox.Show($"Vorgang {az} wurde bereits bearbeitet.\n\n{info}", "Bereits bearbeitet", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("tns", "http://www.xjustiz.de");
            if (string.IsNullOrEmpty(az))
                az = doc.SelectSingleNode("//tns:nachrichtenkopf/tns:absender/tns:aktenzeichen", nsmgr)?.InnerText?.Trim();

            var open = new OpenDoc
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Document = doc,
                FileNumber = az,
                DetachedSignaturePath = _pendingDetached ?? FindDetached(path)
            };
            _pendingDetached = null;
            _openDocs.Add(open);
            _prefs.RememberDirectoryFromFile(path);
            SelectDocument(open);
            RebuildTabs();
        }
        catch (Exception ex)
        {
            MessageBox.Show(HtmlSanitizer.Plain(ex.Message), "Fehler beim Öffnen");
        }
    }

    private async void OnOpenXml(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "XJustiz eNoVA (*.xml)|*.xml|Alle Dateien (*.*)|*.*",
            Title = "eNoVA XML öffnen",
            InitialDirectory = _prefs.ResolveOpenStartDirectory(),
            Multiselect = true
        };

        if (dlg.ShowDialog() != true)
            return;

        var previous = _current;

        foreach (var path in dlg.FileNames)
        {
            if (_openDocs.Any(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Diese Datei ist bereits geöffnet.", "Hinweis");
                continue;
            }
            if (_openDocs.Count >= MaxOpen)
            {
                MessageBox.Show($"Maximal {MaxOpen} Dateien gleichzeitig geöffnet.", "Hinweis");
                break;
            }

            try
            {
                var doc = _processor.LoadFromFile(path);

                var sig = NotarySignatureVerifier.Verify(path);
                if (!sig.IsVerified)
                {
                    if (sig.Mode == "Keine Signatur")
                    {
                        MessageBox.Show("Keine Signatur gefunden. Bitte die zugehörige .p7s-Datei angeben.", "Signatur fehlt");
                        var p7s = PromptForDetachedSignature();
                        if (p7s is null)
                        {
                            MessageBox.Show("Keine Signatur angegeben. Datei wird nicht geöffnet.", "Abgelehnt");
                            continue;
                        }
                        sig = NotarySignatureVerifier.VerifyWithDetached(path, p7s);
                    }
                    if (!sig.IsVerified)
                    {
                        MessageBox.Show($"Signatur ungültig.\n\n{sig.Summary}", "Abgelehnt", MessageBoxButton.OK, MessageBoxImage.Error);
                        continue;
                    }
                }

                MessageBox.Show($"{sig.Summary}\n\nModus: {sig.Mode}\nUnterzeichner: {sig.Signer ?? "—"}", "Signatur gültig");

                ErsuchenSachentscheidungChecker.Check(doc);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("tns", "http://www.xjustiz.de");
                var az = doc.SelectSingleNode("//tns:nachrichtenkopf/tns:absender/tns:aktenzeichen", nsmgr)?.InnerText?.Trim();

                var open = new OpenDoc
                {
                    Path = path,
                    FileName = Path.GetFileName(path),
                    Document = doc,
                    FileNumber = az,
                    DetachedSignaturePath = _pendingDetached
                };
                _pendingDetached = null;
                _openDocs.Add(open);
                _prefs.RememberDirectoryFromFile(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(HtmlSanitizer.Plain(ex.Message), "Fehler beim Öffnen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                // Restore previous valid document if any
                if (previous is not null && _openDocs.Contains(previous))
                    SelectDocument(previous);
                else if (_openDocs.Count > 0)
                    SelectDocument(_openDocs[^1]);
                RebuildTabs();
                return;
            }
        }

        if (_openDocs.Count > 0)
            SelectDocument(_openDocs[^1]);
        RebuildTabs();
    }

    private static string? FindDetached(string xmlPath)
    {
        var dir = Path.GetDirectoryName(xmlPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(xmlPath);
        foreach (var ext in new[] { ".p7s", ".p7b", ".pkcs7" })
        {
            var p = Path.Combine(dir, baseName + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private void SelectDocument(OpenDoc doc)
    {
        _current = doc;
        LoadedFileLabel.Text = doc.FileName;

        if (!string.IsNullOrEmpty(doc.FileNumber))
        {
            FileNumberBox.Text = doc.FileNumber;
            FileNumberBox.IsReadOnly = true;
            FileNumberBox.Background = System.Windows.Media.Brushes.WhiteSmoke;
        }
        else
        {
            FileNumberBox.Text = "";
            FileNumberBox.IsReadOnly = false;
            FileNumberBox.Background = System.Windows.Media.Brushes.White;
        }

        var decisionLabel = (DecisionCombo.SelectedItem as CodeListEntryDto)?.Label ?? "";
        var preview = NativePreviewBuilder.Build(doc.Document, _session.DisplayName, decisionLabel, FileNumberBox.Text);
        ViewerHost.Content = preview;

        UpdateDecisionComboFromDocument();
        StatusText.Text = "Datei geladen: " + doc.FileName;
    }

    private void RebuildTabs()
    {
        TabBar.Children.Clear();
        foreach (var d in _openDocs)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 0) };

            var selectBtn = new Button
            {
                Content = d.FileName,
                Padding = new Thickness(8, 4, 4, 4),
                Tag = d,
                Background = d == _current ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1, 1, 0, 1)
            };
            selectBtn.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is OpenDoc od)
                {
                    SelectDocument(od);
                    RebuildTabs();
                }
            };

            var closeBtn = new Button
            {
                Content = "×",
                Padding = new Thickness(6, 4, 8, 4),
                Tag = d,
                Background = d == _current ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0, 1, 1, 1),
                FontWeight = FontWeights.Bold
            };
            closeBtn.Click += (s, e) =>
            {
                if (s is Button b && b.Tag is OpenDoc od)
                {
                    CloseDocument(od);
                    RebuildTabs();
                }
            };

            panel.Children.Add(selectBtn);
            panel.Children.Add(closeBtn);
            TabBar.Children.Add(panel);
        }
    }

    private void CloseDocument(OpenDoc doc)
    {
        var idx = _openDocs.IndexOf(doc);
        _openDocs.Remove(doc);
        if (_current == doc)
        {
            if (_openDocs.Count == 0)
            {
                _current = null;
                LoadedFileLabel.Text = "Keine Datei geladen";
                FileNumberBox.Text = "";
                FileNumberBox.IsReadOnly = false;
                FileNumberBox.Background = System.Windows.Media.Brushes.White;
                ViewerHost.Content = new TextBlock
                {
                    Text = "Keine Datei geladen",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                StatusText.Text = "Datei geschlossen.";
            }
            else
            {
                SelectDocument(_openDocs[Math.Max(0, idx - 1)]);
            }
        }
        RebuildTabs();
    }

    private void OnCloseXml(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
            CloseDocument(_current);
    }

    private void UpdateDecisionComboFromDocument()
    {
        if (_current?.Document is null || _decisionCodes.Count == 0) return;

        var nsmgr = new XmlNamespaceManager(_current.Document.NameTable);
        nsmgr.AddNamespace("tns", "http://www.xjustiz.de");
        var codes = _current.Document.SelectNodes("//tns:ersuchenSachentscheidung/tns:ersuchenSachentscheidung/code", nsmgr);
        var allowed = new HashSet<string>();
        if (codes is not null)
        {
            foreach (XmlNode c in codes)
            {
                var req = c.InnerText.Trim();
                if (_decisionMappings.TryGetValue(req, out var list))
                    foreach (var d in list) allowed.Add(d);
            }
        }

        var filtered = allowed.Count > 0
            ? _decisionCodes.Where(x => allowed.Contains(x.Code)).ToList()
            : new List<CodeListEntryDto>();

        DecisionCombo.ItemsSource = filtered;
        DecisionCombo.SelectedIndex = -1;
    }

    private async void OnGenerateAnswer(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            MessageBox.Show(Strings.Get("msg.openFirst"), Strings.Get("msg.hint"));
            return;
        }

        if (DecisionCombo.SelectedItem is not CodeListEntryDto)
        {
            MessageBox.Show("Bitte eine Entscheidung auswählen.", "Hinweis");
            return;
        }

        var caseId = FileNumberBox.Text.Trim();
        if (string.IsNullOrEmpty(caseId))
        {
            MessageBox.Show("Bitte ein Aktenzeichen eingeben oder eine Nachricht mit Aktenzeichen öffnen.", "Hinweis");
            return;
        }

        var fingerprint = CaseFingerprint(_current.Document, _current.DetachedSignaturePath);
        var (alreadyDone, info) = await CheckCaseAlreadyProcessedAsync(caseId, fingerprint);
        if (!string.IsNullOrEmpty(caseId) && alreadyDone)
        {
            MessageBox.Show($"Vorgang {caseId} wurde bereits bearbeitet.\n\n{info}", "Bereits bearbeitet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var caseworkerName = _session.DisplayName ?? _session.Username ?? "Sachbearbeiter";
            var selectedDecision = DecisionCombo.SelectedItem as CodeListEntryDto;
            var decisionCode = selectedDecision?.Code ?? "001";
            var decisionLabel = selectedDecision?.Label ?? decisionCode;

            var answer = new AnswerParameters
            {
                FileNumber = HtmlSanitizer.Plain(FileNumberBox.Text),
                MessageId = "msg-" + Guid.NewGuid().ToString("N"),
                CaseworkerName = HtmlSanitizer.Plain(caseworkerName),
                DecisionCodes = new[] { HtmlSanitizer.Plain(decisionCode) },
                ProductName = "Enova EGOV Editor",
                ManufacturerName = "EGOV",
                Version = "0.0.1"
            };

            StatusText.Text = Strings.Get("status.generating");
            _lastResult = await _processor.ProcessFileAsync(_current.Path, answer);

            var baseDir = _config.LocalOutputDirectory;
            var antwortenDir = Path.IsPathRooted(_config.OutputAntworten)
                ? _config.OutputAntworten
                : Path.Combine(baseDir, _config.OutputAntworten);
            var ablageDir = Path.IsPathRooted(_config.OutputAblage)
                ? _config.OutputAblage
                : Path.Combine(baseDir, _config.OutputAblage);

            Directory.CreateDirectory(antwortenDir);
            Directory.CreateDirectory(ablageDir);

            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var xmlName = $"eNoVa_tor-antwort-{stamp}.xml";
            var pdfName = $"eNoVa_tor-ablage-{stamp}.pdf";

            _lastSignedPath = Path.Combine(antwortenDir, xmlName);
            _lastResult.SaveToFile(_lastSignedPath);

            _lastPdfPath = Path.Combine(ablageDir, pdfName);
            GenerateDetailedPdf(_lastResult.SignedDocument, _lastPdfPath, decisionLabel, caseworkerName, _config.SenderName);

            // Embed the signed XML into a PDF/A-3 container
            var tempVisual = _lastPdfPath + ".visual.pdf";
            File.Move(_lastPdfPath, tempVisual, overwrite: true);
            PdfA3Service.EmbedXmlAsPdfA3(tempVisual, _lastSignedPath, _lastPdfPath, _config.AuthorityIdentifier, _config.SenderName, _current.DetachedSignaturePath);
            File.Delete(tempVisual);

            ViewerHost.Content = NativePreviewBuilder.Build(_lastResult.SignedDocument, caseworkerName);

            // Auto-queue the PDF if uploads are enabled
            try { await _session.ApplyRemoteConfigAsync(); } catch { }
            if (_config.UploadEnabled && !string.IsNullOrEmpty(_lastPdfPath))
            {
                try
                {
                    _pendingUploads++;
                    _uploadClient.SetToken(_session.AccessToken ?? "");
                    var fileName = Path.GetFileName(_lastPdfPath);
                    if (!_sessionQueue.Contains(fileName))
                        _sessionQueue.Add(fileName);
                    await _uploadClient.UploadFilesAsync(new[] { _lastPdfPath });
                    _sessionQueue.Remove(fileName);
                    StatusText.Text = "Auf Server gespeichert.";
                    QueueList.ItemsSource = _sessionQueue.ToList();
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Export gespeichert, Server-Upload fehlgeschlagen: " + ex.Message;
                }
                finally
                {
                    _pendingUploads = Math.Max(0, _pendingUploads - 1);
                }
            }
            else
            {
                StatusText.Text = Strings.Get("status.saved");
            }

            MessageBox.Show(
                $"XML: {_lastSignedPath}\nPDF: {_lastPdfPath}",
                "Export abgeschlossen");

            if (!string.IsNullOrEmpty(caseId))
                await RecordCaseAsync(caseId, fingerprint);

            // Close current file and open next or empty
            if (_current is not null)
                CloseDocument(_current);
        }
        catch (Exception ex)
        {
            MessageBox.Show(HtmlSanitizer.Plain(ex.Message), Strings.Get("msg.error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = Strings.Get("status.generateFailed");
        }
    }

    private void GenerateDetailedPdf(XmlDocument doc, string pdfPath, string decisionLabel, string caseworker, string senderName)
    {
        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("tns", "http://www.xjustiz.de");

        string X(string xp) => doc.SelectSingleNode(xp, nsmgr)?.InnerText?.Trim() ?? "";

        var gegenstand = X("//tns:gegenstand");
        var aktenzeichen = X("//tns:aktenzeichen");
        var urNr = X("//tns:urNr");
        var urDatum = X("//tns:urkundsdatum");
        var amtsgericht = X("//tns:amtsgericht");
        var blatt = X("//tns:grundbuchblatt");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.Header().Text(Strings.Get("pdf.header")).SemiBold().FontSize(18).FontColor("#1B4F72");
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"Absender: {senderName}").SemiBold();
                    col.Item().Text($"{Strings.Get("pdf.generated")}: {DateTime.Now:dd.MM.yyyy HH:mm}");
                    col.Item().Text($"{Strings.Get("pdf.bearbeiter")}: {caseworker}");
                    col.Item().Text($"{Strings.Get("pdf.aktenzeichen")}: {aktenzeichen}");
                    col.Item().Text($"{Strings.Get("pdf.entscheidung")}: {decisionLabel}");
                    col.Item().PaddingTop(10).Text(gegenstand).SemiBold().FontSize(14);

                    col.Item().PaddingTop(12).Text("Urkunde").SemiBold();
                    col.Item().Text($"UVZ-Nr.: {urNr}");
                    col.Item().Text($"Beurkundung: {urDatum}");

                    col.Item().PaddingTop(8).Text("Grundstück").SemiBold();
                    col.Item().Text($"Amtsgericht: {amtsgericht}");
                    col.Item().Text($"Grundbuchblatt: {blatt}");

                    col.Item().PaddingTop(16).Text(Strings.Get("pdf.note")).FontSize(11).Italic();
                    col.Item().Text($"Behörde: {senderName} | Authority: {_config.AuthorityIdentifier}").FontSize(10);
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                });
            });
        }).GeneratePdf(pdfPath);
    }

    private async void OnRefreshQueue(object sender, RoutedEventArgs e)
    {
        await RefreshQueueAsync();
    }

    private async Task RefreshQueueAsync()
    {
        try
        {
            using var http = EnovaHttp.Create(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/upload-queue");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var serverItems = new Dictionary<string, string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
                var status = item.TryGetProperty("status", out var st) ? st.GetString() : "?";
                var label = status switch
                {
                    "on-server" => "auf Server",
                    "dms-pending" => "DMS ausstehend",
                    "dms-done" => "DMS übertragen",
                    "duplicate" => "bereits vorhanden",
                    "failed" => "Fehler",
                    _ => status
                };
                if (!string.IsNullOrEmpty(name))
                    serverItems[name] = label ?? "?";
            }

            // Only show items from this session
            var lines = _sessionQueue
                .Select(name => serverItems.TryGetValue(name, out var s) ? $"{name} — {s}" : $"{name} — ausstehend")
                .ToList();
            QueueList.ItemsSource = lines;
        }
        catch { /* ignore */ }
    }

    private void OnClearQueue(object sender, RoutedEventArgs e)
    {
        _sessionQueue.Clear();
        QueueList.ItemsSource = null;
        StatusText.Text = "Warteschlange geleert.";
    }

    private async void OnShowQueue(object sender, RoutedEventArgs e)
    {
        try
        {
            using var http = EnovaHttp.Create(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/upload-queue");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                FontSize = 14,
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Datei", Binding = new System.Windows.Data.Binding("FileName"), Width = 220 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding("Status"), Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Versuche", Binding = new System.Windows.Data.Binding("Attempts"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Zeit", Binding = new System.Windows.Data.Binding("CreatedAt"), Width = 160 });

            var rows = new List<object>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                rows.Add(new
                {
                    FileName = item.TryGetProperty("fileName", out var fn) ? fn.GetString() : "",
                    Status = item.TryGetProperty("status", out var st) ? st.GetString() : "",
                    Attempts = item.TryGetProperty("attempts", out var at) ? at.GetInt32() : 0,
                    CreatedAt = item.TryGetProperty("createdAt", out var ca) ? ca.GetString() : ""
                });
            }
            grid.ItemsSource = rows;

            var win = new Window
            {
                Title = Brand.Window("Warteschlange"),
                Width = 640,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = grid
            };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private void OnVerifyPdf(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "PDF-Dateien (*.pdf)|*.pdf",
            Title = "PDF/A-3 zur Prüfung öffnen"
        };
        if (dlg.ShowDialog() != true) return;

        var result = PdfA3Service.VerifyAndExtract(dlg.FileName, _config.AuthorityIdentifier);

        var win = new Window
        {
            Title = "PDF-Prüfung",
            Width = 640,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Content = new System.Windows.Controls.TextBox
            {
                Text = result.Summary + (result.Error != null ? "\n\nFehler: " + result.Error : ""),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 14,
                Margin = new Thickness(12)
            }
        };
        win.ShowDialog();
    }

    private void OnGeneratePdf(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Bitte „Antwort generieren und exportieren“ verwenden.", "Hinweis");
    }
}
