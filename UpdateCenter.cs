using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Notch;

public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("notes")] string Notes);

public static class UpdateCenter
{
    const string Feed = "https://hamaddddds.github.io/Kilo-Notch/updates/stable.json";
    const long MaximumDownload = 256L * 1024 * 1024;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(8) };
    static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(20) };
    static bool _checking, _downloading;
    static string _announced = "";
    public static UpdateManifest? Available { get; private set; }
    public static Version Current => typeof(App).Assembly.GetName().Version ?? new Version(1, 3, 0);
    public static event Action? Changed;

    public static bool Valid(UpdateManifest update)
    {
        if (update.Notes == null || update.Notes.Length > 4000) return false;
        if (!Version.TryParse(update.Version, out var version) || version.Major < 1 || version.Build < 0) return false;
        if (update.Sha256?.Length != 64 || !update.Sha256.All(Uri.IsHexDigit)) return false;
        return update.Url == $"https://github.com/hamaddddds/Kilo-Notch/releases/download/v{update.Version}/KiloSetup.exe";
    }
    public static void Start()
    {
        Timer.Tick += async (_, _) => { Timer.Interval = TimeSpan.FromHours(6); await Check(); };
        Timer.Start();
    }
    public static async Task Check(bool feedback = false)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await Http.GetAsync(Feed, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = new MemoryStream();
            var buffer = new byte[4096]; int read;
            while ((read = await body.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (json.Length + read > 32768) throw new InvalidDataException("Update feed is too large.");
                json.Write(buffer, 0, read);
            }
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json.ToArray());
            if (manifest == null || !Valid(manifest)) throw new InvalidDataException("Invalid update manifest.");
            Available = Version.Parse(manifest.Version) > Current ? manifest : null;
            Changed?.Invoke();
            if (Available != null && _announced != Available.Version)
            {
                _announced = Available.Version;
                MainWindow.I.React(Mood.Surprised, 8);
                MainWindow.I.Peek("", "AccentA", 1, "Kilo has something new", $"Version {Available.Version} · open Settings to update");
            }
            if (feedback) Show();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            if (feedback) KiloDialog.Notice("Can't check right now", "Your current Kilo is ready to use. Try checking again when you're connected.");
        }
        finally { _checking = false; }
    }
    public static void Show()
    {
        var update = Available;
        if (update == null) { KiloDialog.Notice("You're up to date", $"Kilo {Current.ToString(3)} is the latest version in the stable update feed."); return; }
        var dialog = new KiloDialog("A little something new", 560, 350);
        var title = new TextBlock { Text = $"Kilo {update.Version}", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        var note = new TextBlock { Text = update.Notes.Length > 400 ? update.Notes[..400] : update.Notes, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 16) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "FgDim");
        var status = new TextBlock { Text = "Your tasks, settings and photos stay with you.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) };
        var fill = new Border { Height = 4, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "Accent");
        var track = new Border { Height = 4, Margin = new Thickness(0, 0, 0, 16), Child = fill }; track.SetResourceReference(Border.BackgroundProperty, "SurfaceHi");
        var install = new Button { Content = _downloading ? "Download in progress…" : "Download update", IsEnabled = !_downloading, Style = (Style)Application.Current.Resources["AccentBtn"], HorizontalAlignment = HorizontalAlignment.Left };
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        dialog.Closed += (_, _) => cancellation.Cancel();
        install.Click += async (_, _) =>
        {
            if (_downloading) return;
            _downloading = true; install.IsEnabled = false;
            string? file = null;
            try
            {
                file = await Download(update, new Progress<double>(p => { fill.Width = Math.Max(0, track.ActualWidth * p); status.Text = $"Downloading · {p:P0}"; }), token);
                token.ThrowIfCancellationRequested();
                status.Text = "Verified. Opening Kilo's installer…";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
                MainWindow.I.React(Mood.Happy, 3);
                dialog.Close(); // installer asks the running app to exit only when installation begins
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { status.Text = "Couldn't install this download. Your current Kilo is unchanged; try again."; install.Content = "Try again"; }
            finally { _downloading = false; install.IsEnabled = true; }
        };
        dialog.Body.Children.Add(new StackPanel { Children = { title, note, status, track, install } });
        dialog.ShowDialog();
    }
    static async Task<string> Download(UpdateManifest update, IProgress<double> progress, CancellationToken cancellation)
    {
        if (!Valid(update)) throw new InvalidDataException("Invalid package location.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KiloNotch", "Updates");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"KiloSetup-{update.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            using var response = await Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length > MaximumDownload) throw new InvalidDataException("Package is too large.");
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            {
                var buffer = new byte[65536]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += read; if (total > MaximumDownload) throw new InvalidDataException("Package is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    progress.Report(length > 0 ? Math.Min(.99, (double)total / length.Value) : 0);
                }
            }
            await using (var verify = File.OpenRead(file))
            {
                var hash = await SHA256.HashDataAsync(verify, cancellation);
                if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(update.Sha256))) throw new InvalidDataException("The package checksum doesn't match.");
            }
            progress.Report(1); return file;
        }
        catch { if (File.Exists(file)) File.Delete(file); throw; }
    }
}
