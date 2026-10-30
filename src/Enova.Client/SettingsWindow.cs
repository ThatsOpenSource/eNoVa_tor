using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lragrz.Enova.Client.Services;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client;

public class SettingsWindow : Window
{
    private readonly AuthSession _session;
    private readonly EnovaConfiguration _config;
    private readonly HttpClient _http;

    public SettingsWindow(AuthSession session, EnovaConfiguration config)
    {
        _session = session;
        _config = config;
        _http = EnovaHttp.Create(TimeSpan.FromSeconds(30));

        Title = "Einstellungen";
        Width = 980;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontSize = 16;

        var tabs = new TabControl { Margin = new Thickness(12) };

        var role = _session.Role?.ToLowerInvariant();

        if (role == "manager")
        {
            tabs.Items.Add(new TabItem { Header = "Behörden", Content = BuildDivisionsTab() });
            tabs.Items.Add(new TabItem { Header = "Server", Content = BuildServerTab() });
        }
        else if (role == "admin")
        {
            tabs.Items.Add(new TabItem { Header = "Konfiguration", Content = BuildDivisionConfigTab() });
            tabs.Items.Add(new TabItem { Header = "Dateidienst", Content = BuildDivisionFileServiceTab() });
            tabs.Items.Add(new TabItem { Header = "Benutzer", Content = new UserListControl(_session, _config, canEdit: true) });
        }
        else
        {
            tabs.Items.Add(new TabItem { Header = "Info", Content = new TextBlock { Text = "Keine Einstellungen für diese Rolle.", Margin = new Thickness(16), FontSize = 16 } });
        }

        Content = tabs;
    }

    private UIElement BuildDivisionsTab()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var refresh = new Button { Content = "Aktualisieren", Padding = new Thickness(10, 4, 10, 4), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "Neu", Padding = new Thickness(10, 4, 10, 4), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        var del = new Button { Content = "Löschen", Padding = new Thickness(10, 4, 10, 4), FontSize = 16 };
        toolbar.Children.Add(refresh);
        toolbar.Children.Add(add);
        toolbar.Children.Add(del);
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        var list = new ListView { FontSize = 15 };
        list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 180 },
                new GridViewColumn { Header = "Authority", DisplayMemberBinding = new System.Windows.Data.Binding("AuthorityIdentifier"), Width = 140 },
                new GridViewColumn { Header = "DMS", DisplayMemberBinding = new System.Windows.Data.Binding("DmsType"), Width = 100 }
            }
        };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);

        refresh.Click += async (_, _) => await LoadDivisionsAsync(list);
        add.Click += async (_, _) =>
        {
            var dlg = new Window
            {
                Title = Brand.Window("Neue Behörde"),
                Width = 480,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };
            var box = new TextBox { FontSize = 16, Margin = new Thickness(16), Padding = new Thickness(8, 6, 8, 6) };
            var ok = new Button { Content = "Anlegen", Padding = new Thickness(14, 8, 14, 8), FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(16) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "Name der Behörde", FontSize = 16, Margin = new Thickness(16, 16, 16, 0) });
            panel.Children.Add(box);
            panel.Children.Add(ok);
            dlg.Content = panel;
            string? name = null;
            ok.Click += (_, _) => { name = box.Text.Trim(); dlg.DialogResult = true; dlg.Close(); };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(name)) return;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies")
                {
                    Content = JsonContent.Create(new
                    {
                        name,
                        authorityIdentifier = "DE-LRAGRZ-01",
                        senderName = "LRA-GRZ",
                        cryptoProvider = "MOCK_FALLBACK",
                        dmsType = "Lokal",
                        localOutputDirectory = "%USERPROFILE%\\Documents\\eNoVa_tor",
                        outputAntworten = "antworten",
                        outputAblage = "ablage"
                    })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                await LoadDivisionsAsync(list);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        del.Click += async (_, _) =>
        {
            if (list.SelectedItem is not DivisionDto d) return;
            if (MessageBox.Show($"Behörde „{d.Name}“ löschen?", "Bestätigung", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Delete, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies/{d.Id}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                await LoadDivisionsAsync(list);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is DivisionDto d)
                OpenDivisionManagement(d);
        };

        Loaded += async (_, _) => await LoadDivisionsAsync(list);
        return grid;
    }

    private async Task LoadDivisionsAsync(ListView list)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var items = await resp.Content.ReadFromJsonAsync<List<DivisionDto>>() ?? new();
            list.ItemsSource = items;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private void OpenDivisionManagement(DivisionDto division)
    {
        var win = new DivisionManagementWindow(_session, _config, division);
        win.Owner = this;
        win.ShowDialog();
    }

    private UIElement BuildUsersTab(bool agencyScoped)
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "Neu", Padding = new Thickness(14, 8, 14, 8), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        var del = new Button { Content = "Löschen", Padding = new Thickness(14, 8, 14, 8), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        var refresh = new Button { Content = "Aktualisieren", Padding = new Thickness(14, 8, 14, 8), FontSize = 16 };
        toolbar.Children.Add(add);
        toolbar.Children.Add(del);
        toolbar.Children.Add(refresh);
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        var list = new ListView { FontSize = 16 };
        list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Benutzer", DisplayMemberBinding = new System.Windows.Data.Binding("Username"), Width = 150 },
                new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding("DisplayName"), Width = 200 },
                new GridViewColumn { Header = "Rolle", DisplayMemberBinding = new System.Windows.Data.Binding("RoleLabel"), Width = 140 },
                new GridViewColumn { Header = "Aktiv", DisplayMemberBinding = new System.Windows.Data.Binding("IsActive"), Width = 60 },
                new GridViewColumn { Header = "Sitzung", DisplayMemberBinding = new System.Windows.Data.Binding("IsSessionActive"), Width = 70 },
                new GridViewColumn { Header = "Letzter Login", DisplayMemberBinding = new System.Windows.Data.Binding("LastLoginAt"), Width = 140 },
                new GridViewColumn { Header = "Letzter Logout", DisplayMemberBinding = new System.Windows.Data.Binding("LastLogoutAt"), Width = 140 }
            }
        };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);

        add.Click += async (_, _) =>
        {
            var dlg = new UserEditDialog(null) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users")
                {
                    Content = JsonContent.Create(new { username = dlg.Username, password = dlg.Password, displayName = dlg.DisplayName, role = dlg.Role })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                await LoadUsersAsync(list, agencyScoped);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        list.MouseDoubleClick += async (_, _) =>
        {
            if (list.SelectedItem is not UserDto user) return;
            var dlg = new UserEditDialog(user) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}")
                {
                    Content = JsonContent.Create(new { displayName = dlg.DisplayName, role = dlg.Role, password = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                await LoadUsersAsync(list, agencyScoped);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        del.Click += async (_, _) =>
        {
            if (list.SelectedItem is not UserDto user) return;
            if (MessageBox.Show($"Benutzer „{user.Username}“ löschen?", "Bestätigung", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Delete, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                await LoadUsersAsync(list, agencyScoped);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        refresh.Click += async (_, _) => await LoadUsersAsync(list, agencyScoped);
        Loaded += async (_, _) => await LoadUsersAsync(list, agencyScoped);
        return grid;
    }

    private async Task LoadUsersAsync(ListView list, bool agencyScoped)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var users = await resp.Content.ReadFromJsonAsync<List<UserDto>>() ?? new();
            // Exclude organisation-level managers from division views
            if (agencyScoped)
                users = users.Where(u => !string.Equals(u.Role, "manager", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var u in users)
                u.RoleLabel = Strings.Role(u.Role);
            list.ItemsSource = users;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private UIElement BuildServerTab()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Organisations-Standards", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

        var authBox = new TextBox { Text = "DE-LRAGRZ-01", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var senderBox = new TextBox { Text = "LRA-GRZ", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var uploadBox = new CheckBox { Content = "Uploads erlaubt", FontSize = 16, IsChecked = true, Margin = new Thickness(0, 8, 0, 8) };

        panel.Children.Add(new TextBlock { Text = "Standard-Behördenkennung", FontSize = 16 });
        panel.Children.Add(authBox);
        panel.Children.Add(new TextBlock { Text = "Standard-Absender", FontSize = 16, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(senderBox);
        panel.Children.Add(uploadBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Gilt für Behörden, die noch die Server-Standards verwenden. Individuell gesetzte Werte bleiben unverändert.",
            FontSize = 13,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var save = new Button { Content = "Standards speichern", Padding = new Thickness(16, 8, 16, 8), FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        save.Click += async (_, _) =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/organisation")
                {
                    Content = JsonContent.Create(new
                    {
                        defaultAuthority = authBox.Text,
                        defaultSender = senderBox.Text,
                        defaultUploadEnabled = uploadBox.IsChecked == true
                    })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                MessageBox.Show("Standards gespeichert und auf passende Behörden angewendet.", "OK");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        panel.Children.Add(save);

        Loaded += async (_, _) =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/organisation");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return;
                var org = await resp.Content.ReadFromJsonAsync<OrgDto>();
                if (org is null) return;
                if (!string.IsNullOrEmpty(org.DefaultAuthority)) authBox.Text = org.DefaultAuthority;
                if (!string.IsNullOrEmpty(org.DefaultSender)) senderBox.Text = org.DefaultSender;
                uploadBox.IsChecked = org.DefaultUploadEnabled;
            }
            catch { }
        };
        return panel;
    }

    private UIElement BuildDivisionConfigTab()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Behörden-Konfiguration", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

        var authBox = new TextBox { Text = _config.AuthorityIdentifier, FontSize = 16, Margin = new Thickness(0, 0, 0, 8), IsReadOnly = true, Background = System.Windows.Media.Brushes.WhiteSmoke };
        var senderBox = new TextBox { Text = _config.SenderName, FontSize = 16, Margin = new Thickness(0, 0, 0, 8), IsReadOnly = true, Background = System.Windows.Media.Brushes.WhiteSmoke };
        var outBox = new TextBox { Text = _config.LocalOutputDirectory, FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var antBox = new TextBox { Text = _config.OutputAntworten, FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var ablBox = new TextBox { Text = _config.OutputAblage, FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };

        panel.Children.Add(new TextBlock { Text = "Behördenkennung (server-seitig)", FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(authBox);
        panel.Children.Add(new TextBlock { Text = "Absender (server-seitig)", FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(senderBox);
        panel.Children.Add(new TextBlock { Text = "Ausgabeordner", FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(outBox);
        panel.Children.Add(new TextBlock { Text = "Antworten", FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(antBox);
        panel.Children.Add(new TextBlock { Text = "Ablage", FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(ablBox);

        var save = new Button { Content = "Speichern", Padding = new Thickness(12, 6, 12, 6), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        save.Click += async (_, _) =>
        {
            MessageBox.Show("Einstellungen werden beim nächsten Login vom Server übernommen.\n(Server-API-Endpunkt bleibt lokal in enova.client.properties.)", "Hinweis");
        };
        panel.Children.Add(save);

        panel.Children.Add(new TextBlock
        {
            Text = "Der Server-Endpunkt (enova.server.baseurl) kann nur in der lokalen eNoVa_tor.client.properties geändert werden.",
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private UIElement BuildDivisionFileServiceTab()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Dateidienst", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var dmsBox = new ComboBox { FontSize = 15, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        dmsBox.Items.Add("Lokal");
        dmsBox.Items.Add("DocuWare");
        dmsBox.SelectedIndex = 0;
        panel.Children.Add(new TextBlock { Text = "Bevorzugter Dienst", FontSize = 14, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(dmsBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Kann vom Organisationsverwalter global überschrieben oder pro Behörde gesperrt werden.",
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }
}

public sealed class OrgDto
{
    public Guid Id { get; set; }
    public string? DefaultAuthority { get; set; }
    public string? DefaultSender { get; set; }
    public bool DefaultUploadEnabled { get; set; }
}

public sealed class DivisionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? AuthorityIdentifier { get; set; }
    public string? SenderName { get; set; }
    public string? DmsType { get; set; }
    public string? LocalOutputDirectory { get; set; }
    public string? OutputAntworten { get; set; }
    public string? OutputAblage { get; set; }
    public string? DmsConfigJson { get; set; }
}

public class DivisionManagementWindow : Window
{
    private readonly AuthSession _session;
    private readonly EnovaConfiguration _config;
    private readonly DivisionDto _division;
    private readonly HttpClient _http;

    public DivisionManagementWindow(AuthSession session, EnovaConfiguration config, DivisionDto division)
    {
        _session = session;
        _config = config;
        _division = division;
        _http = EnovaHttp.Create(TimeSpan.FromSeconds(30));

        Title = Brand.Window($"Behörde: {division.Name}");
        Width = 800;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        var tabs = new TabControl { Margin = new Thickness(12) };
        tabs.Items.Add(new TabItem { Header = "Konfiguration", Content = BuildConfigTab() });
        tabs.Items.Add(new TabItem { Header = "Dateidienst", Content = BuildFileServiceTab() });
        tabs.Items.Add(new TabItem { Header = "Benutzer", Content = new UserListControl(_session, _config, canEdit: true, agencyId: _division.Id) });
        Content = tabs;
    }

    private UIElement BuildConfigTab()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = _division.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

        var authBox = new TextBox { Text = _division.AuthorityIdentifier ?? "", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var senderBox = new TextBox { Text = "LRA-GRZ", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var outBox = new TextBox { Text = "%USERPROFILE%\\Documents\\eNoVa_tor", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var antBox = new TextBox { Text = "antworten", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };
        var ablBox = new TextBox { Text = "ablage", FontSize = 16, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 6, 8, 6) };

        // Load division values if available
        Loaded += async (_, _) =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                var agencies = await resp.Content.ReadFromJsonAsync<List<DivisionDto>>();
                var d = agencies?.FirstOrDefault(a => a.Id == _division.Id);
                if (d is not null)
                {
                    if (!string.IsNullOrEmpty(d.SenderName))
                        senderBox.Text = d.SenderName;
                    if (!string.IsNullOrEmpty(d.LocalOutputDirectory))
                        outBox.Text = d.LocalOutputDirectory;
                    if (!string.IsNullOrEmpty(d.OutputAntworten))
                        antBox.Text = d.OutputAntworten;
                    if (!string.IsNullOrEmpty(d.OutputAblage))
                        ablBox.Text = d.OutputAblage;
                }
            }
            catch { }
        };

        panel.Children.Add(new TextBlock { Text = "Behördenkennung", FontSize = 16 });
        panel.Children.Add(authBox);
        panel.Children.Add(new TextBlock { Text = "Absender", FontSize = 16, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(senderBox);
        panel.Children.Add(new TextBlock { Text = "Ausgabeordner", FontSize = 16, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(outBox);
        panel.Children.Add(new TextBlock { Text = "Antworten (unter Ausgabeordner)", FontSize = 16, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(antBox);
        panel.Children.Add(new TextBlock { Text = "Ablage (unter Ausgabeordner)", FontSize = 16, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(ablBox);

        var save = new Button { Content = "Speichern", Padding = new Thickness(16, 8, 16, 8), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        save.Click += async (_, _) =>
        {
            var ant = SanitizeRelativePath(antBox.Text);
            var abl = SanitizeRelativePath(ablBox.Text);
            if (ant is null || abl is null)
            {
                MessageBox.Show("Antworten/Ablage dürfen nur Buchstaben, Zahlen, Bindestrich und Unterstrich enthalten (kein '..').", "Hinweis");
                return;
            }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies/{_division.Id}")
                {
                    Content = JsonContent.Create(new
                    {
                        authorityIdentifier = authBox.Text,
                        senderName = senderBox.Text,
                        localOutputDirectory = outBox.Text,
                        outputAntworten = ant,
                        outputAblage = abl
                    })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                try { await _session.ApplyRemoteConfigAsync(); } catch { }
                MessageBox.Show("Gespeichert. Konfiguration aktualisiert.", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler");
            }
        };
        panel.Children.Add(save);
        scroll.Content = panel;
        return scroll;
    }

    private static string? SanitizeRelativePath(string input)
    {
        var s = (input ?? "").Trim().Replace('/', '\\');
        if (s.Contains("..") || s.Contains(':') || s.StartsWith('\\')) return null;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '\\') continue;
            return null;
        }
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private UIElement BuildFileServiceTab()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Dateidienst", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var dmsBox = new ComboBox { FontSize = 16, Width = 220, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };
        dmsBox.Items.Add("none");
        dmsBox.Items.Add("local");
        dmsBox.Items.Add("docuware");
        dmsBox.SelectedItem = _division.DmsType ?? "none";
        panel.Children.Add(dmsBox);

        var lockBox = new CheckBox { Content = "Dateidienst sperren (Behörde kann nicht ändern)", FontSize = 16, Margin = new Thickness(0, 12, 0, 8) };
        panel.Children.Add(lockBox);

        // Restore lock state from DmsConfigJson
        Loaded += async (_, _) =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                var agencies = await resp.Content.ReadFromJsonAsync<List<DivisionDto>>();
                var d = agencies?.FirstOrDefault(a => a.Id == _division.Id);
                if (d is not null && !string.IsNullOrEmpty(d.DmsConfigJson))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(d.DmsConfigJson);
                    if (doc.RootElement.TryGetProperty("locked", out var locked) && locked.GetBoolean())
                        lockBox.IsChecked = true;
                }
            }
            catch { }
        };

        var save = new Button { Content = "Speichern", Padding = new Thickness(16, 8, 16, 8), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        save.Click += async (_, _) =>
        {
            try
            {
                var dmsConfig = System.Text.Json.JsonSerializer.Serialize(new { locked = lockBox.IsChecked == true });
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/agencies/{_division.Id}")
                {
                    Content = JsonContent.Create(new { dmsType = dmsBox.SelectedItem?.ToString(), dmsConfigJson = dmsConfig })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                MessageBox.Show("Gespeichert.", "OK");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };
        panel.Children.Add(save);
        return panel;
    }

    private UIElement BuildUsersTab()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "Neu", Padding = new Thickness(14, 8, 14, 8), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        var del = new Button { Content = "Löschen", Padding = new Thickness(14, 8, 14, 8), FontSize = 16 };
        toolbar.Children.Add(add);
        toolbar.Children.Add(del);
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        var list = new ListView { FontSize = 16 };
        list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Benutzer", DisplayMemberBinding = new System.Windows.Data.Binding("Username"), Width = 150 },
                new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding("DisplayName"), Width = 180 },
                new GridViewColumn { Header = "Rolle", DisplayMemberBinding = new System.Windows.Data.Binding("RoleLabel"), Width = 140 }
            }
        };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);

        Func<Task> reload = async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                var users = await resp.Content.ReadFromJsonAsync<List<UserDto>>() ?? new();
                foreach (var u in users.Where(u => !string.Equals(u.Role, "manager", StringComparison.OrdinalIgnoreCase)))
                    u.RoleLabel = Strings.Role(u.Role);
                list.ItemsSource = users.Where(u => !string.Equals(u.Role, "manager", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch { }
        };

        add.Click += async (_, _) =>
        {
            var dlg = new UserEditDialog(null) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users")
                    {
                        Content = JsonContent.Create(new { username = dlg.Username, password = dlg.Password, displayName = dlg.DisplayName, role = dlg.Role })
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                    using var resp = await _http.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                    await reload();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
            }
        };
        list.MouseDoubleClick += async (_, _) =>
        {
            if (list.SelectedItem is not UserDto user) return;
            var dlg = new UserEditDialog(user) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Put, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}")
                    {
                        Content = JsonContent.Create(new { displayName = dlg.DisplayName, role = dlg.Role, password = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password })
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                    using var resp = await _http.SendAsync(req);
                    resp.EnsureSuccessStatusCode();
                    await reload();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
            }
        };
        del.Click += async (_, _) =>
        {
            if (list.SelectedItem is not UserDto user) return;
            if (MessageBox.Show($"Benutzer „{user.Username}“ löschen?", "Bestätigung", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Delete, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                await reload();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
        };

        Loaded += async (_, _) => await reload();
        return grid;
    }
}
