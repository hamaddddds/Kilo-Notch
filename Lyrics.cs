using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Notch;

public class Word { public double Start, End; public string Text = ""; public bool Space = true; public bool Bg; }
public class Line { public double Start; public List<Word> Words = new(); }
public class Lyric { public List<Line> Lines = new(); public bool Synced; public string Source = ""; }

/// Better Lyrics first (Apple-style TTML with per-syllable timing, from its public cache), then LRCLIB.
/// Better Lyrics answers 401 for songs it hasn't cached; filling the cache needs its extension's
/// sign-in, so those songs fall through to LRCLIB.
public static class Lyrics
{
    static readonly HttpClient Http = MakeClient();
    static readonly Dictionary<string, Lyric?> Cache = new();
    static readonly Regex TimeTag = new(@"\[(\d+):(\d+(?:[.:]\d+)?)\]");
    static readonly Regex WordTag = new(@"<(\d+):(\d+(?:[.:]\d+)?)>");
    static readonly Regex Junk = new(@"\s*[\(\[][^\)\]]*\b(official|video|lyrics?|audio|mv|hd|4k|visuali[sz]er|remaster(ed)?|feat\.?|ft\.?)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase);

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KiloNotch/1.0 (+https://github.com/hamaddddds/Kilo-Notch)");
        return c;
    }

    /// null = nothing found (cached) or offline (not cached, so the next try asks again).
    public static async Task<Lyric?> Get(string title, string artist, double duration)
    {
        var key = $"{title}|{artist}";
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var t = Junk.Replace(title, "").Trim();
        var a = Regex.Replace(artist, @"\s*(- Topic|VEVO)$", "", RegexOptions.IgnoreCase).Trim();
        try
        {
            var lyric = await BetterLyrics(t, a, duration) ?? await Lrclib(t, a, duration);
            Cache[key] = lyric;
            return lyric;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    static string E(string s) => Uri.EscapeDataString(s);

    // ---------- Better Lyrics ----------

    static async Task<Lyric?> BetterLyrics(string title, string artist, double duration)
    {
        var url = $"https://api.betterlyrics.org/getLyrics?s={E(title)}&a={E(artist)}" + (duration > 0 ? $"&d={(int)Math.Round(duration)}" : "");
        try
        {
            using var r = await Http.GetAsync(url);
            if (r.StatusCode != HttpStatusCode.OK) return null; // 401 = not in its public cache
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number && score.GetDouble() < 0.7) return null; // weak match
            if (!root.TryGetProperty("ttml", out var ttml) || ttml.ValueKind != JsonValueKind.String) return null;
            var lines = ParseTtml(ttml.GetString()!);
            return lines.Count == 0 ? null : new Lyric { Lines = lines, Synced = true, Source = "Better Lyrics" };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException) { return null; }
    }

    /// Apple-style TTML: <p begin end> lines holding <span begin end> syllables. A whitespace text node
    /// between spans ends a word; spans with ttm:role="x-bg" are background vocals.
    public static List<Line> ParseTtml(string ttml)
    {
        XNamespace tt = "http://www.w3.org/ns/ttml", ttm = "http://www.w3.org/ns/ttml#metadata";
        var doc = XDocument.Parse(ttml, LoadOptions.PreserveWhitespace); // the spaces between spans are the word breaks
        var lines = new List<Line>();
        var ps = doc.Descendants(tt + "p").ToList();
        for (int i = 0; i < ps.Count; i++)
        {
            var p = ps[i];
            var line = new Line { Start = Clock((string?)p.Attribute("begin")) };
            void Walk(XElement e, bool bg)
            {
                foreach (var node in e.Nodes())
                {
                    if (node is XText text)
                    {
                        if (string.IsNullOrWhiteSpace(text.Value)) { if (line.Words.Count > 0) line.Words[^1].Space = true; }
                        else if (e != p && line.Words.Count > 0) line.Words[^1].Text += text.Value.Trim(); // stray text inside a span
                    }
                    else if (node is XElement span && span.Name == tt + "span")
                    {
                        var isBg = bg || (string?)span.Attribute(ttm + "role") == "x-bg";
                        if (span.Elements(tt + "span").Any()) { Walk(span, isBg); continue; }
                        var w = span.Value.Trim();
                        if (w == "") continue;
                        line.Words.Add(new Word
                        {
                            Start = Clock((string?)span.Attribute("begin")), End = Clock((string?)span.Attribute("end")), Text = w,
                            Space = char.IsWhiteSpace(span.Value[^1]), Bg = isBg,
                        });
                    }
                }
            }
            Walk(p, false);
            if (line.Words.Count == 0) // line-timed TTML: plain text in <p>
            {
                var end = p.Attribute("end") != null ? Clock((string?)p.Attribute("end")) : line.Start + 4;
                Estimate(line, p.Value.Trim(), end);
            }
            else foreach (var w in line.Words) if (w.End <= w.Start) w.End = w.Start + 0.3;
            lines.Add(line);
        }
        return lines;
    }

    // "12.5", "1:02.345", "1:02:03.4", "12.5s"
    static double Clock(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        double total = 0;
        foreach (var part in s.TrimEnd('s').Split(':')) total = total * 60 + double.Parse(part, CultureInfo.InvariantCulture);
        return total;
    }

    // ---------- LRCLIB ----------

    static async Task<Lyric?> Lrclib(string t, string a, double duration)
    {
        var found = await Search($"https://lrclib.net/api/search?track_name={E(t)}&artist_name={E(a)}", t, a, duration);
        // YouTube puts "Artist - Song" in the title and the channel name in artist
        if (found == null && t.Contains(" - "))
        {
            var parts = t.Split(" - ", 2);
            found = await Search($"https://lrclib.net/api/search?track_name={E(parts[1])}&artist_name={E(parts[0])}", parts[1], parts[0], duration);
        }
        found ??= await Search($"https://lrclib.net/api/search?q={E(t + " " + a)}", t, a, duration);
        if (found == null) return null;
        var e = found.Value;
        if (Has(e, "syncedLyrics")) return new Lyric { Lines = ParseLrc(e.GetProperty("syncedLyrics").GetString()!), Synced = true, Source = "LRCLIB" };
        var plain = e.GetProperty("plainLyrics").GetString()!.Split('\n').Select(s => s.Trim())
            .Select(s => new Line { Words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new Word { Text = w }).ToList() })
            .ToList();
        return new Lyric { Lines = plain, Synced = false, Source = "LRCLIB" };
    }

    // Same song first (title, artist, and lyrics in the title's script, so a Japanese cover can't win
    // just by being a second closer in length), then synced over plain, then the closest length.
    static async Task<JsonElement?> Search(string url, string title, string artist, double duration)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var wantLatin = Latin(title + artist) > 0.6;
        JsonElement? best = null;
        var bestScore = double.MaxValue;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var synced = Has(e, "syncedLyrics");
            if (!synced && !Has(e, "plainLyrics")) continue;
            var lyrics = Str(e, synced ? "syncedLyrics" : "plainLyrics");
            var diff = duration > 0 && e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? Math.Abs(d.GetDouble() - duration) : 0;
            string candTitle = Norm(Str(e, "trackName")), candArtist = Norm(Str(e, "artistName")), t = Norm(title), a = Norm(artist);
            var score = Math.Min(diff, 30)
                + (synced ? 0 : 30)
                + (candTitle == t ? 0 : candTitle.Contains(t) || t.Contains(candTitle) ? 25 : 50)
                + (a != "" && (candArtist.Contains(a) || a.Contains(candArtist)) ? 0 : 20)
                + (Latin(TimeTag.Replace(lyrics, "")) > 0.6 == wantLatin ? 0 : 100);
            if (score < bestScore) { bestScore = score; best = e.Clone(); }
        }
        return best;
    }

    static bool Has(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() != "";
    static string Str(JsonElement e, string prop) => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    static string Norm(string s) => Regex.Replace(Junk.Replace(s, "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    // Share of letters that are Latin script (incl. accented), to tell a song from its translation.
    static double Latin(string s)
    {
        int letters = 0, latin = 0;
        foreach (var c in s)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c <= 'ɏ') latin++;
        }
        return letters == 0 ? 1 : (double)latin / letters;
    }

    public static List<Line> ParseLrc(string lrc)
    {
        var lines = new List<(Line Line, string Text)>();
        foreach (var raw in lrc.Split('\n'))
        {
            var tags = TimeTag.Matches(raw);
            if (tags.Count == 0) continue;
            var body = TimeTag.Replace(raw, "").Trim();
            var marks = WordTag.Matches(body);
            var text = Regex.Replace(WordTag.Replace(body, ""), @"\s+", " ").Trim();
            foreach (Match tag in tags)
            {
                var line = new Line { Start = Sec(tag) };
                // enhanced LRC carries real word timings: <mm:ss.xx>word
                for (int i = 0; i < marks.Count; i++)
                {
                    var from = marks[i].Index + marks[i].Length;
                    var word = body[from..(i + 1 < marks.Count ? marks[i + 1].Index : body.Length)].Trim();
                    if (word != "") line.Words.Add(new Word { Start = Sec(marks[i]), Text = word });
                }
                lines.Add((line, text == "" ? "♪" : text));
            }
        }
        lines.Sort((a, b) => a.Line.Start.CompareTo(b.Line.Start));

        for (int i = 0; i < lines.Count; i++)
        {
            var (l, text) = lines[i];
            var next = i + 1 < lines.Count ? lines[i + 1].Line.Start : l.Start + 5;
            if (l.Words.Count > 0)
            {
                for (int k = 0; k < l.Words.Count; k++)
                    l.Words[k].End = k + 1 < l.Words.Count ? l.Words[k + 1].Start : Math.Min(next, l.Words[k].Start + 1);
                continue;
            }
            Estimate(l, text, next);
        }
        return lines.Select(x => x.Line).ToList();
    }

    // ponytail: line-synced only, so each word's timing is estimated from its length; real karaoke needs word timings
    static void Estimate(Line l, string text, double next)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sing = Math.Min(next - l.Start, Math.Max(0.8, text.Length * 0.075));
        var total = tokens.Sum(t => t.Length + 1.0);
        var at = l.Start;
        foreach (var t in tokens)
        {
            var d = sing * (t.Length + 1) / total;
            l.Words.Add(new Word { Start = at, End = at + d, Text = t });
            at += d;
        }
    }

    static double Sec(Match m) => int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
}
