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
    public static ImageSource? Art { get; private set; }
    public static Color? ArtColor { get; private set; }
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
            ImageSource? art = null;
            Color? color = null;
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
                    color = Average(bmp);
                }
                catch (Exception) { } // odd thumbnail format: show the song without art
            }
            if (v != _version) return; // a newer song arrived while this one loaded
            Title = p?.Title ?? "";
            Artist = p?.Artist ?? "";
            Art = art;
            ArtColor = color;
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

    // Cover color, weighted toward colorful pixels so grey borders don't win, then brightened.
    static Color Average(BitmapSource src)
    {
        var small = new TransformedBitmap(new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0),
            new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight));
        var px = new byte[small.PixelWidth * small.PixelHeight * 4];
        small.CopyPixels(px, small.PixelWidth * 4, 0);
        double r = 0, g = 0, b = 0, w = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            int B = px[i], G = px[i + 1], R = px[i + 2];
            var sat = (Math.Max(R, Math.Max(G, B)) - Math.Min(R, Math.Min(G, B))) / 255.0 + 0.05;
            r += R * sat; g += G * sat; b += B * sat; w += sat;
        }
        r /= w; g /= w; b /= w;
        var k = 235 / Math.Max(1, Math.Max(r, Math.Max(g, b)));
        return Color.FromRgb((byte)Math.Min(255, r * k), (byte)Math.Min(255, g * k), (byte)Math.Min(255, b * k));
    }
}
