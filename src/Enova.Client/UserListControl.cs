using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lragrz.Enova.Client.Services;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client;

/// <summary>
/// Reusable user list with session info, used by both division admin and Organisationsverwalter.
/// </summary>
public class UserListControl : UserControl
{
    private readonly AuthSession _session;
    private readonly EnovaConfiguration _config;
    private readonly HttpClient _http;
    private readonly ListView _list;
    private readonly bool _canEdit;
    private readonly Guid? _agencyId;

    public UserListControl(AuthSession session, EnovaConfiguration config, bool canEdit = true, Guid? agencyId = null)
    {
        _session = session;
        _config = config;
        _canEdit = canEdit;
        _agencyId = agencyId;
        _http = EnovaHttp.Create(TimeSpan.FromSeconds(30));

        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (canEdit)
        {
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var add = StyleButton("Neu");
            var del = StyleButton("Löschen");
            var refresh = StyleButton("Aktualisieren");
            toolbar.Children.Add(add);
            toolbar.Children.Add(del);
            toolbar.Children.Add(refresh);
            Grid.SetRow(toolbar, 0);
            grid.Children.Add(toolbar);

            add.Click += async (_, _) => await AddUserAsync();
            del.Click += async (_, _) => await DeleteSelectedAsync();
            refresh.Click += async (_, _) => await ReloadAsync();
        }

        _list = new ListView { FontSize = 16 };
        _list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Benutzer", DisplayMemberBinding = new System.Windows.Data.Binding("Username"), Width = 140 },
                new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding("DisplayName"), Width = 160 },
                new GridViewColumn { Header = "Rolle", DisplayMemberBinding = new System.Windows.Data.Binding("RoleLabel"), Width = 140 },
                new GridViewColumn { Header = "Aktiv", DisplayMemberBinding = new System.Windows.Data.Binding("IsActive"), Width = 60 },
                new GridViewColumn { Header = "Sitzung", DisplayMemberBinding = new System.Windows.Data.Binding("SessionDisplay"), Width = 70 },
                new GridViewColumn { Header = "Letzter Login", DisplayMemberBinding = new System.Windows.Data.Binding("LastLoginDisplay"), Width = 140 },
                new GridViewColumn { Header = "Letzter Logout", DisplayMemberBinding = new System.Windows.Data.Binding("LastLogoutDisplay"), Width = 140 }
            }
        };
        _list.MouseDoubleClick += async (_, _) => await EditSelectedAsync();
        Grid.SetRow(_list, 1);
        grid.Children.Add(_list);

        Content = grid;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private static Button StyleButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(14, 8, 14, 8),
        FontSize = 16,
        Margin = new Thickness(0, 0, 8, 0),
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xDB)),
        BorderThickness = new Thickness(1)
    };

    private async Task ReloadAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var users = await resp.Content.ReadFromJsonAsync<List<UserDto>>() ?? new();
            foreach (var u in users)
                u.RoleLabel = Strings.Role(u.Role);

            // Scope: exclude managers from division views; filter by agency if given
            var filtered = users.Where(u => !string.Equals(u.Role, "manager", StringComparison.OrdinalIgnoreCase));
            if (_agencyId.HasValue)
                filtered = filtered.Where(u => u.AgencyId == _agencyId.Value);

            _list.ItemsSource = filtered.ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private async Task AddUserAsync()
    {
        var dlg = new UserEditDialog(null) { Owner = Window.GetWindow(this) };
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
            await ReloadAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
    }

    private async Task EditSelectedAsync()
    {
        if (!_canEdit || _list.SelectedItem is not UserDto user) return;
        var dlg = new UserEditDialog(user) { Owner = Window.GetWindow(this) };
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
            await ReloadAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_list.SelectedItem is not UserDto user) return;
        if (MessageBox.Show($"Benutzer „{user.Username}“ löschen?", "Bestätigung", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            await ReloadAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
    }
}
