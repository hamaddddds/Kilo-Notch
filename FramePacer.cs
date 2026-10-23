namespace Notch;

/// Caps render work without tying motion speed to the monitor's refresh rate.
/// Deadlines retain their remainder, so 144 Hz does not turn a 60 Hz cap into 48 Hz.
public sealed class FramePacer
{
    bool _started;
    long _sample, _accepted;
    double _next;
    int _rate;

    public void Reset() => _started = false;

    public bool TryStep(TimeSpan timestamp, int framesPerSecond, out double seconds)
    {
        if (framesPerSecond is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        seconds = 0;
        var now = timestamp.Ticks;
        var interval = (double)TimeSpan.TicksPerSecond / framesPerSecond;
        if (!_started || now < _sample)
        {
            _started = true;
            _sample = _accepted = now;
            _rate = framesPerSecond;
            _next = now + interval;
            seconds = 1.0 / framesPerSecond;
            return true;
        }
        if (now == _sample) return false;
        _sample = now;
        if (_rate != framesPerSecond)
        {
            _rate = framesPerSecond;
            _next = _accepted + interval;
        }
        if (now + 1 < _next) return false; // tolerate TimeSpan's rounding to one tick
        seconds = Math.Min((double)(now - _accepted) / TimeSpan.TicksPerSecond, .05);
        _accepted = now;
        // Skip missed slots after a stall instead of scheduling a catch-up burst.
        _next += (Math.Floor(Math.Max(0, now + 1 - _next) / interval) + 1) * interval;
        return true;
    }
}
