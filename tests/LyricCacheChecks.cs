using Notch;

static class LyricCacheChecks
{
    public static void Run(Action<bool, string> check)
    {
        static Lyric Text(int characters) => new()
        {
            Lines = new() { new Line { Words = new() { new Word { Text = new string('a', characters) } } } }
        };

        var cache = new LyricCache(2);
        var first = Text(3);
        cache.Put("first", first);
        cache.Put("second", Text(3));
        check(cache.TryGetValue("first", out var hit) && ReferenceEquals(hit, first), "lyric cache returns the original read-only lyric");
        cache.Put("third", Text(3));
        check(cache.Count == 2 && !cache.TryGetValue("second", out _) && cache.TryGetValue("first", out _), "lyric cache hits refresh least-recently-used eviction");

        var replacement = Text(4);
        cache.Put("third", replacement);
        check(cache.Count == 2 && cache.TryGetValue("third", out hit) && ReferenceEquals(hit, replacement), "lyric cache replaces an existing key without duplicate entries");
        cache.Put("missing", null);
        check(cache.TryGetValue("missing", out hit) && hit == null && !cache.TryGetValue("absent", out _), "lyric cache distinguishes cached null responses from absent songs");
        cache.Put("fourth", Text(3));
        check(cache.TryGetValue("missing", out _) && !cache.TryGetValue("third", out _), "cached null hits participate in recency");

        var budget = new LyricCache(10, 8); // one key character plus three lyric characters each
        budget.Put("a", Text(3)); budget.Put("b", Text(3)); budget.Put("c", Text(3));
        check(budget.Count == 2 && !budget.TryGetValue("a", out _) && budget.TryGetValue("b", out _) && budget.TryGetValue("c", out _), "lyric character budget evicts the oldest song");
        budget.Put("b", Text(6));
        check(budget.Count == 1 && budget.TryGetValue("b", out _) && !budget.TryGetValue("c", out _), "replacement size updates the lyric character budget");
        budget.Put("b", Text(1)); budget.Put("d", Text(5));
        check(budget.Count == 2, "smaller replacements release their old character budget");
        budget.Put("oversized-key", null);
        check(budget.Count == 2 && !budget.TryGetValue("oversized-key", out _), "oversized keys and cached misses obey the character budget");

        var oversized = new LyricCache();
        oversized.Put("valid", Text(32_000));
        oversized.Put("too-long", Text(32_001));
        oversized.Put("too-many-lines", new Lyric { Lines = Enumerable.Range(0, 1001).Select(_ => new Line()).ToList() });
        check(oversized.Count == 1 && oversized.TryGetValue("valid", out _) && !oversized.TryGetValue("too-long", out _) && !oversized.TryGetValue("too-many-lines", out _), "oversized lyric text and line counts bypass caching");
        oversized.Put("valid", Text(32_001));
        check(oversized.Count == 0 && !oversized.TryGetValue("valid", out _), "oversized replacement cannot return stale cached lyrics");
        oversized.Put("empty-words", new Lyric { Lines = new() { new Line { Words = Enumerable.Range(0, 32_001).Select(_ => new Word()).ToList() } } });
        check(oversized.Count == 0, "empty lyric tokens cannot bypass the memory budget");

        var defaults = new LyricCache();
        for (int i = 0; i < 25; i++) defaults.Put(i.ToString(), null);
        check(defaults.Count == 24 && !defaults.TryGetValue("0", out _) && defaults.TryGetValue("24", out _), "default lyric cache retains at most 24 songs");
        var total = new LyricCache();
        for (int i = 0; i < 6; i++) total.Put(i.ToString(), Text(32_000));
        check(total.Count == 5 && !total.TryGetValue("0", out _), "default lyric cache respects the 180000-character budget");

        var concurrent = new LyricCache();
        Parallel.For(0, 200, i => { var key = (i % 40).ToString(); concurrent.Put(key, Text(5)); concurrent.TryGetValue(key, out _); });
        check(concurrent.Count == 24, "parallel lyric cache reads and writes preserve capacity and integrity");
    }
}
