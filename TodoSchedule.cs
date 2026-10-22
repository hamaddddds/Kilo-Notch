namespace Notch;

public static class TodoSchedule
{
    public static DateTime AtClock(DateTime now, int day, int hour, int minute)
    {
        if (day is < 0 or > 1 || hour is < 0 or > 23 || minute is < 0 or > 59) throw new ArgumentOutOfRangeException();
        var due = now.Date.AddDays(day).AddHours(hour).AddMinutes(minute);
        return due <= now ? due.AddDays(1) : due;
    }
}
