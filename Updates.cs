using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Velopack;
using Velopack.Sources;

namespace OlivesApp;

public sealed record UpdateSettings(string RepositoryUrl);

public static class Updates
{
    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public static string? Repository()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "updates.json");
        var settings = JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(file));
        if (string.IsNullOrWhiteSpace(settings?.RepositoryUrl)) return null;
        if (!IsRepositoryUrl(settings.RepositoryUrl)) throw new InvalidDataException("Μη έγκυρη διεύθυνση ενημερώσεων.");
        return settings.RepositoryUrl.TrimEnd('/');
    }
    public static bool IsRepositoryUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$");
}

public sealed class UpdateWindow : Window
{
    private readonly TextBlock status = new() { Text = "Έλεγχος για νέα έκδοση…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
    private readonly ProgressBar progress = new() { Height = 12, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 18) };
    private readonly Button close = new() { Content = "Κλείσιμο", Padding = new Thickness(18, 8, 18, 8), HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = false };
    private bool busy = true;

    public UpdateWindow()
    {
        Title = "Ενημερώσεις OlivesApp"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(status); panel.Children.Add(progress); panel.Children.Add(close); Content = panel;
        close.Click += (_, _) => Close();
        Closing += (_, e) => e.Cancel = busy;
        Loaded += async (_, _) => await Check();
    }

    private async Task Check()
    {
        try
        {
            string? repository = Updates.Repository();
            if (repository == null) { status.Text = "Δεν έχει οριστεί ακόμη η διεύθυνση ενημερώσεων για αυτή την έκδοση."; return; }
            var manager = new UpdateManager(new GithubSource(repository, null, false));
            if (!manager.IsInstalled) { status.Text = "Για ενημερώσεις, εγκαταστήστε το OlivesApp από το αρχείο Setup."; return; }
            var update = await manager.CheckForUpdatesAsync();
            if (update == null) { status.Text = "Έχετε ήδη την πιο πρόσφατη έκδοση: " + Updates.CurrentVersion; return; }
            progress.IsIndeterminate = false;
            status.Text = "Διαθέσιμη έκδοση: " + update.TargetFullRelease.Version;
            if (MessageBox.Show(this, status.Text + "\nΘέλετε να γίνει λήψη και επανεκκίνηση για την εγκατάσταση;", "Νέα έκδοση", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            status.Text = "Λήψη ενημέρωσης…";
            await manager.DownloadUpdatesAsync(update, value => Dispatcher.Invoke(() => progress.Value = value));
            status.Text = "Επανεκκίνηση για την εγκατάσταση…";
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception)
        {
            status.Text = "Δεν ήταν δυνατή η ολοκλήρωση της ενημέρωσης. Ελέγξτε τη σύνδεση στο διαδίκτυο και δοκιμάστε ξανά αργότερα. Η υπάρχουσα έκδοση παραμένει διαθέσιμη.";
        }
        finally { busy = false; close.IsEnabled = true; progress.IsIndeterminate = false; }
    }
}
