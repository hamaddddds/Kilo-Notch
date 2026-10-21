using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace Notch;

/// Whatever is playing on the PC, through the same media session Windows shows in its volume flyout.
public static class Player
{
    static GlobalSystemMediaTransportControlsSessionManager? _mgr;
    static Session? _s;
    static TimeSpan _pos, _end;
    static DateTimeOffset _at, _stamp;
    static int _version;

    public static string Title { get; private set; } = "";
    public static string Artist { get; private set; } = "";
    public static BitmapSource? Art { get; private set; }

    /// The song's two colors: the cover's palette when album colors are on, otherwise the theme.
    /// Worked out on demand, since light/dark changes the lightness.
    public static (Color A, Color B) Vibe => Store.D.Settings.AlbumGlow && Art != null ? Palette(Art) : (Theme.A, Theme.B);
    public static bool Playing { get; private set; }
    public static bool HasSession => _s != null && Title != "";
    public static TimeSpan Duration => _end;

    // Players only report position on change, so extrapolate while playing.
    public static TimeSpan Position
    {
        get
        {
            var p = _pos + (Playing ? DateTimeOffset.Now - _at : TimeSpan.Zero);
            return p < TimeSpan.Zero ? TimeSpan.Zero : _end > TimeSpan.Zero && p > _end ? _end : p;
        }
    }

    public static event Action? PropsChanged, StateChanged;

    public static async Task Init()
    {
        _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _mgr.CurrentSessionChanged += (_, _) => Ui(Hook);
        Hook();
    }

    static void Ui(Action a) => Application.Current.Dispatcher.InvokeAsync(a);

    static void Hook()
    {
        if (_s != null)
        {
            _s.MediaPropertiesChanged -= OnProps;
            _s.PlaybackInfoChanged -= OnState;
            _s.TimelinePropertiesChanged -= OnState;
        }
        _s = _mgr?.GetCurrentSession();
        if (_s != null)
        {
            _s.MediaPropertiesChanged += OnProps;
            _s.PlaybackInfoChanged += OnState;
            _s.TimelinePropertiesChanged += OnState;
        }
        LoadProps();
        LoadState();
    }

    static void OnProps(Session s, object e) => Ui(LoadProps);
    static void OnState(Session s, object e) => Ui(LoadState);

    static async void LoadProps()
    {
        var v = ++_version;
        try
        {
            var p = _s == null ? null : await _s.TryGetMediaPropertiesAsync();
            BitmapSource? art = null;
            if (p?.Thumbnail != null)
            {
                try
                {
                    using var stream = (await p.Thumbnail.OpenReadAsync()).AsStreamForRead();
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = stream;
                    bmp.EndInit();
                    bmp.Freeze();
                    art = bmp;
                }
                catch (Exception) { } // odd thumbnail format: show the song without art
            }
            if (v != _version) return; // a newer song arrived while this one loaded
            Title = p?.Title ?? "";
            Artist = p?.Artist ?? "";
            Art = art;
            PropsChanged?.Invoke();
        }
        catch (Exception) { } // session closed mid-read; the next event refreshes
    }

    static void LoadState()
    {
        var before = Position;
        var wasPlaying = Playing;
        try
        {
            Playing = _s?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var t = _s?.GetTimelineProperties();
            if (t == null) { _pos = _end = TimeSpan.Zero; _at = DateTimeOffset.Now; }
            else if (t.LastUpdatedTime == _stamp && Playing != wasPlaying)
            {
                // play/pause without a fresh timeline: carry on from where we were
                _pos = before;
                _at = DateTimeOffset.Now;
            }
            else
            {
                _pos = t.Position;
                _end = t.EndTime;
                _at = t.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : t.LastUpdatedTime;
                _stamp = t.LastUpdatedTime;
            }
        }
        catch (Exception) { Playing = false; } // session closed mid-read
        StateChanged?.Invoke();
    }

    static void Do(Func<Session, IAsyncOperation<bool>> f)
    {
        try { if (_s != null) _ = f(_s); }
        catch (Exception) { } // session already gone: nothing to control
    }

    public static void Toggle() => Do(s => s.TryTogglePlayPauseAsync());
    public static void Next() => Do(s => s.TrySkipNextAsync());
    public static void Prev() => Do(s => s.TrySkipPreviousAsync());

    public static void Seek(TimeSpan t)
    {
        Do(s => s.TryChangePlaybackPositionAsync(t.Ticks));
        _pos = t;
        _at = DateTimeOffset.Now;
        StateChanged?.Invoke();
    }

    // The cover's two strongest hues. Averaging would turn a rainbow cover grey, so vivid pixels vote into
    // hue buckets instead; the runner-up must be a different hue or the gradient would be flat.
    public static (Color, Color) Palette(BitmapSource src)
    {
        var small = new TransformedBitmap(new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0),
            new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
        var px = new byte[small.PixelWidth * small.PixelHeight * 4];
        small.CopyPixels(px, small.PixelWidth * 4, 0);
        const int bins = 24;
        var weight = new double[bins];
        var sum = new (double x, double y, double s)[bins]; // hue as a vector, so 359° and 1° average to 0°
        double total = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            var (h, s, v) = Hsv(px[i + 2], px[i + 1], px[i]);
            var w = s * s * v; // vivid and bright pixels count, grey and dark ones barely
            total += v;
            var k = (int)(h / 360 * bins) % bins;
            weight[k] += w;
            sum[k].x += Math.Cos(h * Math.PI / 180) * w;
            sum[k].y += Math.Sin(h * Math.PI / 180) * w;
            sum[k].s += s * w;
        }
        // neighbouring buckets count together, so a hue sitting on a boundary isn't split in two
        double Score(int k) => weight[k] + 0.5 * (weight[(k + 1) % bins] + weight[(k + bins - 1) % bins]);
        var first = Enumerable.Range(0, bins).MaxBy(Score);
        var second = Enumerable.Range(0, bins).Where(k => Math.Min((k - first + bins) % bins, (first - k + bins) % bins) >= 3).MaxBy(Score);
        var light = Store.D.Settings.Light;
        Color Pick(int k)
        {
            var hue = Math.Atan2(sum[k].y, sum[k].x) * 180 / Math.PI;
            var sat = weight[k] > 0 ? sum[k].s / weight[k] : 0;
            return Theme.Hsl(hue, Math.Clamp(sat * 1.15, 0.45, 0.95), light ? 0.5 : 0.64);
        }
        if (weight[first] < total * 0.01) // black-and-white cover: a silver gradient
            return light ? (Color.FromRgb(70, 70, 78), Color.FromRgb(130, 130, 140)) : (Color.FromRgb(235, 235, 240), Color.FromRgb(150, 150, 160));
        var a = Pick(first);
        if (Score(second) < Score(first) * 0.12) // one-color cover: lean the second stop along the hue wheel
        {
            var hue = Math.Atan2(sum[first].y, sum[first].x) * 180 / Math.PI;
            return (a, Theme.Hsl(hue + 35, 0.85, light ? 0.46 : 0.58));
        }
        return (a, Pick(second));
    }

    static (double h, double s, double v) Hsv(byte r, byte g, byte b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (max == 0) return (0, 0, 0);
        var h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, d / max, max / 255);
    }
}
