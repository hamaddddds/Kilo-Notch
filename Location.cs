using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Windows.Devices.Geolocation;

namespace Notch;

/// The city the laptop is in (e.g. "Bekasi"), from Windows location services. Coordinates are rounded
/// to ~1 km before being sent to OpenStreetMap's Nominatim for the place name; refreshed every 15 min.
public static class Location
{
    static readonly HttpClient Http = MakeClient();
    static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMinutes(15) };

    public static string? City { get; private set; }
    public static event Action? Changed;

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KiloNotch/1.0 (+https://github.com/hamaddddds/Kilo-Notch)"); // Nominatim requires one
        return c;
    }

    public static void Start()
    {
        Timer.Tick += (_, _) => Refresh();
        Timer.Start();
        Refresh();
    }

    public static async void Refresh()
    {
        if (!Store.D.Settings.ShowCity)
        {
            if (City != null) { City = null; Changed?.Invoke(); }
            return;
        }
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return; // location is off for desktop apps
            var pos = await new Geolocator().GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15));
            var name = await Name(pos.Coordinate.Point.Position.Latitude, pos.Coordinate.Point.Position.Longitude);
            if (name != null && name != City) { City = name; Changed?.Invoke(); }
        }
        catch (Exception) { } // no fix, offline or service down: keep the last known city
    }

    /// Place name for a coordinate, rounded to ~1 km before it leaves the PC.
    public static async Task<string?> Name(double latitude, double longitude)
    {
        var lat = Math.Round(latitude, 2).ToString(CultureInfo.InvariantCulture);
        var lon = Math.Round(longitude, 2).ToString(CultureInfo.InvariantCulture);
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var json = await Http.GetStringAsync($"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&lat={lat}&lon={lon}&accept-language={lang}");
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("address", out var a)) return null;
        foreach (var key in new[] { "city", "town", "municipality", "county", "state_district", "state" })
        {
            if (!a.TryGetProperty(key, out var v) || v.GetString() is not string name || name == "") continue;
            foreach (var prefix in new[] { "Kota ", "Kabupaten ", "City of " }) // "Kota Bekasi" → "Bekasi"
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name[prefix.Length..];
            return name;
        }
        return null;
    }
}
