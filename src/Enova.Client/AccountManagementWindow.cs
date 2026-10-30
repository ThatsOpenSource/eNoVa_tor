using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Lragrz.Enova.Client.Services;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client;

public partial class AccountManagementWindow : Window
{
    private readonly AuthSession _session;
    private readonly EnovaConfiguration _config;
    private readonly HttpClient _http;

    public AccountManagementWindow(AuthSession session, EnovaConfiguration config)
    {
        _session = session;
        _config = config;
        _http = EnovaHttp.Create(TimeSpan.FromSeconds(30));

        Title = "Kontenverwaltung";
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var addBtn = new Button { Content = "Neu", Padding = new Thickness(12, 6, 12, 6), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        addBtn.Click += async (_, _) => await AddUserAsync();
        var refreshBtn = new Button { Content = "Aktualisieren", Padding = new Thickness(12, 6, 12, 6), FontSize = 16 };
        refreshBtn.Click += async (_, _) => await LoadUsersAsync();
        toolbar.Children.Add(addBtn);
        toolbar.Children.Add(refreshBtn);
        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        var list = new ListView { FontSize = 16, Name = "UserList" };
        list.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Benutzer", DisplayMemberBinding = new System.Windows.Data.Binding("Username"), Width = 140 },
                new GridViewColumn { Header = "Name", DisplayMemberBinding = new System.Windows.Data.Binding("DisplayName"), Width = 180 },
                new GridViewColumn { Header = "Rolle", DisplayMemberBinding = new System.Windows.Data.Binding("Role"), Width = 100 },
                new GridViewColumn { Header = "Aktiv", DisplayMemberBinding = new System.Windows.Data.Binding("IsActive"), Width = 60 }
            }
        };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var editBtn = new Button { Content = "Bearbeiten", Padding = new Thickness(12, 6, 12, 6), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        editBtn.Click += async (_, _) => await EditSelectedAsync(list);
        var delBtn = new Button { Content = "Löschen", Padding = new Thickness(12, 6, 12, 6), FontSize = 16 };
        delBtn.Click += async (_, _) => await DeleteSelectedAsync(list);
        actions.Children.Add(editBtn);
        actions.Children.Add(delBtn);
        Grid.SetRow(actions, 2);
        grid.Children.Add(actions);

        Content = grid;

        Loaded += async (_, _) => await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var users = await resp.Content.ReadFromJsonAsync<List<UserDto>>() ?? new();
            if (Content is Grid g && g.Children[1] is ListView lv)
                lv.ItemsSource = users;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private async Task AddUserAsync()
    {
        var dlg = new UserEditDialog(null);
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users")
            {
                Content = JsonContent.Create(new
                {
                    username = dlg.Username,
                    password = dlg.Password,
                    displayName = dlg.DisplayName,
                    role = dlg.Role
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private async Task EditSelectedAsync(ListView list)
    {
        if (list.SelectedItem is not UserDto user) return;
        var dlg = new UserEditDialog(user);
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}")
            {
                Content = JsonContent.Create(new
                {
                    displayName = dlg.DisplayName,
                    role = dlg.Role,
                    password = string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password,
                    isActive = dlg.UserIsActive
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }

    private async Task DeleteSelectedAsync(ListView list)
    {
        if (list.SelectedItem is not UserDto user) return;
        if (MessageBox.Show($"Benutzer „{user.Username}“ wirklich löschen?", "Bestätigung",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{_config.ServerBaseUrl.TrimEnd('/')}/api/users/{user.Id}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                MessageBox.Show(body, "Löschen fehlgeschlagen");
                return;
            }
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fehler");
        }
    }
}

public sealed class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    public bool IsActive { get; set; }
    public Guid? AgencyId { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastLogoutAt { get; set; }
    public bool IsSessionActive { get; set; }
    public string LastLoginDisplay => LastLoginAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    public string LastLogoutDisplay => LastLogoutAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    public string SessionDisplay => IsSessionActive ? "Aktiv" : "—";
}

public class UserEditDialog : Window
{
    public string Username { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string Role { get; private set; } = "editor";
    public string Password { get; private set; } = "";
    public bool UserIsActive { get; private set; } = true;

    public UserEditDialog(UserDto? existing)
    {
        Title = existing is null ? "Neuer Benutzer" : "Benutzer bearbeiten";
        Width = 480;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.White;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var grid = new Grid { Margin = new Thickness(24) };
        for (int i = 0; i < 10; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var userBox = new TextBox { FontSize = 16, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(10, 8, 10, 8), IsEnabled = existing is null };
        if (existing is not null) userBox.Text = existing.Username;
        grid.Children.Add(new TextBlock { Text = "Benutzername", FontSize = 16, Margin = new Thickness(0, 0, 0, 6) });
        Grid.SetRow(userBox, 1); grid.Children.Add(userBox);

        var nameBox = new TextBox { FontSize = 16, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(10, 8, 10, 8), Text = existing?.DisplayName ?? "" };
        var nameLabel = new TextBlock { Text = "Anzeigename", FontSize = 16, Margin = new Thickness(0, 10, 0, 6) };
        Grid.SetRow(nameLabel, 2); grid.Children.Add(nameLabel);
        Grid.SetRow(nameBox, 3); grid.Children.Add(nameBox);

        var roleBox = new ComboBox { FontSize = 16, Margin = new Thickness(0, 0, 0, 12), Height = 40 };
        roleBox.Items.Add(new { Key = "editor", Label = Strings.Get("role.editor") });
        roleBox.Items.Add(new { Key = "admin", Label = Strings.Get("role.admin") });
        roleBox.Items.Add(new { Key = "manager", Label = Strings.Get("role.manager") });
        roleBox.DisplayMemberPath = "Label";
        roleBox.SelectedValuePath = "Key";
        roleBox.SelectedValue = existing?.Role ?? "editor";
        var roleLabel = new TextBlock { Text = "Rolle", FontSize = 16, Margin = new Thickness(0, 10, 0, 6) };
        Grid.SetRow(roleLabel, 4); grid.Children.Add(roleLabel);
        Grid.SetRow(roleBox, 5); grid.Children.Add(roleBox);

        var passBox = new PasswordBox { FontSize = 16, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(10, 8, 10, 8) };
        var passPlain = new TextBox { FontSize = 16, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(10, 8, 10, 8), Visibility = Visibility.Collapsed };
        var reveal = new CheckBox { Content = "Passwort anzeigen", FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
        reveal.Checked += (_, _) => { passPlain.Text = passBox.Password; passBox.Visibility = Visibility.Collapsed; passPlain.Visibility = Visibility.Visible; };
        reveal.Unchecked += (_, _) => { passBox.Password = passPlain.Text; passPlain.Visibility = Visibility.Collapsed; passBox.Visibility = Visibility.Visible; };

        var confirmBox = new PasswordBox { FontSize = 16, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(10, 8, 10, 8) };
        var confirmPlain = new TextBox { FontSize = 16, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(10, 8, 10, 8), Visibility = Visibility.Collapsed };
        var reveal2 = new CheckBox { Content = "Bestätigung anzeigen", FontSize = 14, Margin = new Thickness(0, 0, 0, 8) };
        reveal2.Checked += (_, _) => { confirmPlain.Text = confirmBox.Password; confirmBox.Visibility = Visibility.Collapsed; confirmPlain.Visibility = Visibility.Visible; };
        reveal2.Unchecked += (_, _) => { confirmBox.Password = confirmPlain.Text; confirmPlain.Visibility = Visibility.Collapsed; confirmBox.Visibility = Visibility.Visible; };

        var passLabel = new TextBlock { Text = existing is null ? "Passwort" : "Neues Passwort (leer = unverändert)", FontSize = 16, Margin = new Thickness(0, 10, 0, 2) };
        var hint = new TextBlock { Text = "Min. 8 Zeichen, Groß-/Kleinbuchstabe, Zahl, Sonderzeichen", FontSize = 12, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) };
        var confirmLabel = new TextBlock { Text = "Passwort bestätigen", FontSize = 16, Margin = new Thickness(0, 8, 0, 2) };

        Grid.SetRow(passLabel, 6); grid.Children.Add(passLabel);
        var passPanel = new StackPanel();
        passPanel.Children.Add(hint);
        passPanel.Children.Add(passBox);
        passPanel.Children.Add(passPlain);
        passPanel.Children.Add(reveal);
        Grid.SetRow(passPanel, 7); grid.Children.Add(passPanel);

        var confirmPanel = new StackPanel();
        confirmPanel.Children.Add(confirmLabel);
        confirmPanel.Children.Add(confirmBox);
        confirmPanel.Children.Add(confirmPlain);
        confirmPanel.Children.Add(reveal2);
        Grid.SetRow(confirmPanel, 8); grid.Children.Add(confirmPanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Abbrechen", Padding = new Thickness(18, 10, 18, 10), FontSize = 16, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
        var ok = new Button { Content = "Speichern", IsDefault = true, Padding = new Thickness(18, 10, 18, 10), FontSize = 16 };
        ok.Click += (_, _) =>
        {
            Username = userBox.Text.Trim();
            DisplayName = nameBox.Text.Trim();
            Role = roleBox.SelectedValue?.ToString() ?? "editor";
            Password = reveal.IsChecked == true ? passPlain.Text : passBox.Password;
            var confirm = reveal2.IsChecked == true ? confirmPlain.Text : confirmBox.Password;
            UserIsActive = true;

            if (string.IsNullOrEmpty(Username))
            {
                MessageBox.Show("Benutzername erforderlich.", "Hinweis");
                return;
            }
            if (existing is null || !string.IsNullOrEmpty(Password))
            {
                if (Password != confirm)
                {
                    MessageBox.Show("Passwörter stimmen nicht überein.", "Hinweis");
                    return;
                }
                if (!IsStrongPassword(Password))
                {
                    MessageBox.Show("Passwort muss mindestens 8 Zeichen haben und Groß-/Kleinbuchstaben, eine Zahl und ein Sonderzeichen enthalten.", "Hinweis");
                    return;
                }
            }
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 9); grid.Children.Add(buttons);

        scroll.Content = grid;
        Content = scroll;
    }

    private static bool IsStrongPassword(string p)
    {
        if (string.IsNullOrEmpty(p) || p.Length < 8) return false;
        return p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit) && p.Any(c => !char.IsLetterOrDigit(c));
    }
}
