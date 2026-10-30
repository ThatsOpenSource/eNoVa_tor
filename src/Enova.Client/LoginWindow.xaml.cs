using System;
using System.Windows;
using Lragrz.Enova.Client.Services;

namespace Lragrz.Enova.Client;

public partial class LoginWindow : Window
{
    private readonly AuthSession _session;

    public LoginWindow(AuthSession session)
    {
        InitializeComponent();
        _session = session;
        UsernameBox.Focus();
    }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        var user = UsernameBox.Text.Trim();
        var pass = PasswordBox.Password;
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            MessageBox.Show("Bitte Benutzername und Passwort eingeben.", "Anmeldung",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsEnabled = false;
            await _session.LoginAsync(user, pass, RememberCheck.IsChecked == true);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(FriendlyError(ex.Message), "Anmeldung fehlgeschlagen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            IsEnabled = true;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private static string FriendlyError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Anmeldung fehlgeschlagen.";
        if (raw.Contains("401") || raw.Contains("Invalid username", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            return "Benutzername oder Passwort falsch.";
        if (raw.Contains("429") || raw.Contains("Too many", StringComparison.OrdinalIgnoreCase))
            return "Zu viele Anmeldeversuche. Bitte später erneut versuchen.";
        if (raw.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Verbindung", StringComparison.OrdinalIgnoreCase))
            return "Verbindung zum Server fehlgeschlagen. Ist der Server gestartet?";
        try
        {
            var i = raw.IndexOf('{');
            if (i >= 0)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw[i..]);
                if (doc.RootElement.TryGetProperty("error", out var err))
                    return FriendlyError(err.GetString() ?? raw);
            }
        }
        catch { /* not json */ }
        return raw;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

