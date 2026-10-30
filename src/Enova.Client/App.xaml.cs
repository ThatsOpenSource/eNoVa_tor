using System;
using System.Windows;
using System.Windows.Threading;
using Lragrz.Enova.Client.Services;
using Lragrz.Enova.Core.Config;

namespace Lragrz.Enova.Client;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var config = ConfigurationLoader.Load(fileName: "eNoVa_tor.client.properties");
            var uploadClient = new DocumentUploadClient(config);
            var session = new AuthSession(config, uploadClient);

            // Silent restore
            if (session.TryRestore())
            {
                try { session.ApplyRemoteConfigAsync().GetAwaiter().GetResult(); } catch { /* offline ok */ }
                var main = new MainWindow(session, config, uploadClient);
                MainWindow = main;
                main.Show();
                return;
            }

            // Login first – main UI stays hidden until success
            // Prevent app shutdown when the modal login dialog closes (default is OnLastWindowClose)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var login = new LoginWindow(session);
            var ok = login.ShowDialog() == true && session.IsAuthenticated;
            if (!ok)
            {
                Shutdown(0);
                return;
            }

            try { session.ApplyRemoteConfigAsync().GetAwaiter().GetResult(); } catch { /* offline ok */ }
            var window = new MainWindow(session, config, uploadClient);
            MainWindow = window;
            window.Show();

            // Now that a main window is open, restore normal shutdown behavior
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Startfehler:\n{ex.Message}\n\n{ex.StackTrace}",
                "eNoVA EGOV – Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unerwarteter Fehler:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "eNoVA EGOV – Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                $"Kritischer Fehler:\n{ex.Message}",
                "eNoVA EGOV – Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
