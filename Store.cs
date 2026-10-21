using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Notch;

public class Settings
{
    public bool Light { get; set; }
    public double Hue { get; set; } = 265;   // purple
    public double Spread { get; set; } = 50; // hue distance to the second gradient color
    public double Sat { get; set; } = 0.9;
    public double Glow { get; set; } = 0.8;
    public bool Personality { get; set; } = true;
    public bool AlbumGlow { get; set; } = true;
    public bool ShowCity { get; set; } = true;
    public int FocusMin { get; set; } = 25;
    public int BreakMin { get; set; } = 5;
    public int WaterGoal { get; set; } = 1500;
    public int WaterCup { get; set; } = 250;
    public bool WaterRemind { get; set; } = true;
    public int WaterEvery { get; set; } = 60;
    public int WaterFrom { get; set; } = 9;
    public int WaterTo { get; set; } = 21;
    public int LastPage { get; set; }
    public int BoothFrame { get; set; }
}

public class TodoItem
{
    public string Text { get; set; } = "";
    public DateTime? Due { get; set; }
    public bool Done { get; set; }
    public DateTime? DoneAt { get; set; }
    public bool Notified { get; set; }
}

public class Sip
{
    public DateTime At { get; set; }
    public int Ml { get; set; }
}

public class Data
{
    public Settings Settings { get; set; } = new();
    public List<TodoItem> Todos { get; set; } = new();
    public List<Sip> Sips { get; set; } = new();
    public List<string> Shelf { get; set; } = new();
}

/// Everything lives in one JSON file in %APPDATA%\Notch. No accounts, nothing leaves the PC.
public static class Store
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch");
    static readonly string FilePath = Path.Combine(Dir, "data.json");

    public static Data D { get; } = Load();

    static Data Load()
    {
        if (!File.Exists(FilePath)) return new();
        try { return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new(); }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bad", true); // keep the broken file instead of overwriting it
            return new();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(D, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch (IOException) { } // file busy (sync client, antivirus); the next save catches up
    }

    /// Drop yesterday's finished tasks, old water logs and shelf items whose files are gone.
    public static void Prune()
    {
        D.Sips.RemoveAll(s => s.At < DateTime.Today.AddDays(-7));
        D.Todos.RemoveAll(t => t.Done && t.DoneAt < DateTime.Today);
        D.Shelf.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        Save();
    }
}

public static class AutoStart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get => Registry.CurrentUser.OpenSubKey(Key)?.GetValue("Notch") != null;
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue("Notch", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Notch", false);
        }
    }
}
