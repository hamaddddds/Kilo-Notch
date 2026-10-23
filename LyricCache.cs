namespace Notch;

/// A bounded least-recently-used cache, including songs with no available lyrics.
/// Published lyrics are read-only by convention; callers must not mutate cached lines/words.
public sealed class LyricCache
{
    const int MaximumLyricCharacters = 32_000;
    const int MaximumLines = 1_000;
    sealed record Entry(string Key, Lyric? Value, int Characters);
    readonly object _gate = new();
    readonly int _maximumSongs, _maximumCharacters;
    readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    readonly LinkedList<Entry> _recent = new();
    int _characters;

    public LyricCache(int maximumSongs = 24, int maximumCharacters = 180_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSongs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        _maximumSongs = maximumSongs;
        _maximumCharacters = maximumCharacters;
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    public bool TryGetValue(string key, out Lyric? lyric)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node)) { lyric = null; return false; }
            _recent.Remove(node);
            _recent.AddFirst(node);
            lyric = node.Value.Value;
            return true;
        }
    }

    public void Put(string key, Lyric? lyric)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            // Replacing with an oversized response must also discard the older cached value.
            if (_entries.TryGetValue(key, out var existing)) Remove(existing);
            int? size = Measure(key, lyric);
            if (size is not int characters || characters > _maximumCharacters) return;
            while (_entries.Count >= _maximumSongs || (long)_characters + characters > _maximumCharacters)
                Remove(_recent.Last!);
            var node = _recent.AddFirst(new Entry(key, lyric, characters));
            _entries.Add(key, node);
            _characters += characters;
        }
    }

    static int? Measure(string key, Lyric? lyric)
    {
        long characters = 0;
        if (lyric != null)
        {
            if (lyric.Lines.Count > MaximumLines) return null;
            characters = lyric.Source.Length;
            if (characters > MaximumLyricCharacters) return null;
            foreach (var line in lyric.Lines)
                foreach (var word in line.Words)
                {
                    // Empty tokens still occupy memory; do not allow unlimited empty words.
                    characters += Math.Max(1, word.Text.Length);
                    if (characters > MaximumLyricCharacters) return null;
                }
        }
        characters += key.Length; // account for retained keys, including cached misses
        return characters <= int.MaxValue ? (int)characters : null;
    }

    void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _recent.Remove(node);
        _characters -= node.Value.Characters;
    }
}
