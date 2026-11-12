using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Ultrahand;

/// Adds the game to the Steam library as a non-Steam game: an entry in userdata\&lt;user&gt;\config\shortcuts.vdf (binary VDF) that
/// starts totk.exe, plus its cover, banner, logo and icon from SteamGridDB in config\grid. Steam rewrites shortcuts.vdf from
/// memory when it exits, so it is closed first. An entry that already starts this totk.exe (added by hand or by an earlier
/// import) is updated in place, keeping its id, name and play time.
static class SteamImport
{
    const string AppName = "The Legend of Zelda: Tears of the Kingdom";

    // ponytail: art fixed to these SteamGridDB files; downloaded at import, not shipped with the launcher
    static readonly (string Url, string Suffix)[] Art =
    {
        ("https://cdn2.steamgriddb.com/grid/ae758fbcbd5bd841516c53b3c08ebc6f.png", "p.png"),        // cover, 600x900
        ("https://cdn2.steamgriddb.com/hero/6d6c5b7c73b3a57198402741c1e92df5.png", "_hero.png"),    // banner, 1920x620
        ("https://cdn2.steamgriddb.com/logo/19e524e7aa887f6311e8b6ae061bf5d7.png", "_logo.png"),
        ("https://cdn2.steamgriddb.com/icon/f340c2f9d9a79c6243ea596349704822.png", "_icon.png"),
    };

    public static string? SteamDir()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        path = path?.Replace('/', '\\');
        return path != null && File.Exists(Path.Combine(path, "steam.exe")) ? path : null;
    }

    /// The config folder of the Steam account used last (the one whose localconfig.vdf Steam wrote last)
    public static string? UserConfig(string steamDir)
    {
        var users = Path.Combine(steamDir, "userdata");
        if (!Directory.Exists(users)) return null;
        return Directory.GetDirectories(users)
            .Where(d => ulong.TryParse(Path.GetFileName(d), out var id) && id > 0)
            .Select(d => Path.Combine(d, "config"))
            .Where(Directory.Exists)
            .OrderByDescending(c => File.GetLastWriteTimeUtc(Path.Combine(c, "localconfig.vdf")))
            .FirstOrDefault();
    }

    static bool Running() => Process.GetProcessesByName("steam").Length > 0;

    /// Whether the last Steam account already has an entry starting this folder's totk.exe
    public static bool Has(string gameFolder)
    {
        try
        {
            var config = SteamDir() is { } steam ? UserConfig(steam) : null;
            var file = config != null ? Path.Combine(config, "shortcuts.vdf") : null;
            return file != null && File.Exists(file) && Find(Vdf.Read(File.ReadAllBytes(file)), Installed.GameExe(gameFolder)) != null;
        }
        catch (Exception) { return false; }
    }

    /// Writes the entry and the art; returns how many pictures could not be downloaded
    public static async Task<int> Add(string gameFolder, Action<string> note)
    {
        var steam = SteamDir() ?? throw new InvalidOperationException(Tr.T("Steam was not found on this PC."));
        var config = UserConfig(steam) ?? throw new InvalidOperationException(Tr.T("No Steam account was found. Open Steam and sign in once."));
        var wasRunning = Running();
        if (wasRunning)
        {
            note(Tr.T("Closing Steam…"));
            Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), "-shutdown") { UseShellExecute = false });
            for (var i = 0; i < 120 && Running(); i++) await Task.Delay(500);
            if (Running()) throw new InvalidOperationException(Tr.T("Steam did not close. Close it and try again."));
            await Task.Delay(1000);   // (its files are written by then; this is for the file handles)
        }

        note(Tr.T("Adding the game…"));
        var appId = await Task.Run(() => WriteShortcut(config, gameFolder));

        note(Tr.T("Downloading the pictures…"));
        var grid = Directory.CreateDirectory(Path.Combine(config, "grid")).FullName;
        var failed = 0;
        foreach (var (url, suffix) in Art)
        {
            try
            {
                var bytes = await Releases.Http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(Path.Combine(grid, appId + suffix), bytes);
            }
            catch (Exception) { failed++; }
        }

        if (wasRunning)
            try { Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe")) { UseShellExecute = true }); } catch (Exception) { }
        return failed;
    }

    /// Adds or updates the entry in shortcuts.vdf (the old file kept as shortcuts.vdf.bak); returns the entry's app id
    internal static uint WriteShortcut(string config, string gameFolder)
    {
        var file = Path.Combine(config, "shortcuts.vdf");
        var root = File.Exists(file) ? Vdf.Read(File.ReadAllBytes(file)) : new List<Vdf.Node>();
        var list = root.FirstOrDefault(n => n.Type == Vdf.Map && n.Key.Equals("shortcuts", StringComparison.OrdinalIgnoreCase))?.Children;
        if (list == null) root.Add(new Vdf.Node(Vdf.Map, "shortcuts") { Children = list = new() });

        var exe = Installed.GameExe(gameFolder.TrimEnd('\\', '/'));
        var quoted = $"\"{exe}\"";
        var entry = Find(root, exe);
        uint appId;
        if (entry == null)
        {
            appId = Crc32(Encoding.UTF8.GetBytes(quoted + AppName)) | 0x80000000;
            var index = list.Select(n => int.TryParse(n.Key, out var k) ? k + 1 : 0).DefaultIfEmpty(0).Max();
            entry = new Vdf.Node(Vdf.Map, index.ToString()) { Children = new() };
            list.Add(entry);
            Set(entry, Vdf.Int, "appid", BitConverter.GetBytes(appId));
            Set(entry, Vdf.String, "AppName", AppName);
        }
        else
        {
            var id = entry.Children!.FirstOrDefault(n => n.Type == Vdf.Int && n.Key.Equals("appid", StringComparison.OrdinalIgnoreCase));
            appId = id != null ? BitConverter.ToUInt32(id.Raw!) : Crc32(Encoding.UTF8.GetBytes(quoted + AppName)) | 0x80000000;
            if (id == null) Set(entry, Vdf.Int, "appid", BitConverter.GetBytes(appId));
        }
        Set(entry, Vdf.String, "Exe", quoted);
        Set(entry, Vdf.String, "StartDir", $"\"{Path.GetDirectoryName(exe)}\\\"");
        Set(entry, Vdf.String, "icon", Path.Combine(config, "grid", appId + "_icon.png"));
        foreach (var (key, value) in new[] { ("AllowDesktopConfig", 1), ("AllowOverlay", 1) })
            if (!entry.Children!.Any(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                Set(entry, Vdf.Int, key, BitConverter.GetBytes(value));
        if (!entry.Children!.Any(n => n.Type == Vdf.Map && n.Key.Equals("tags", StringComparison.OrdinalIgnoreCase)))
            entry.Children!.Add(new Vdf.Node(Vdf.Map, "tags") { Children = new() });

        if (File.Exists(file)) File.Copy(file, file + ".bak", overwrite: true);
        File.WriteAllBytes(file + ".tmp", Vdf.Write(root));
        File.Move(file + ".tmp", file, overwrite: true);
        return appId;
    }

    static Vdf.Node? Find(List<Vdf.Node> root, string exe) =>
        root.FirstOrDefault(n => n.Type == Vdf.Map && n.Key.Equals("shortcuts", StringComparison.OrdinalIgnoreCase))?.Children!
            .Where(n => n.Type == Vdf.Map)
            .FirstOrDefault(n => n.Children!.Any(c => c.Type == Vdf.String && c.Key.Equals("Exe", StringComparison.OrdinalIgnoreCase) &&
                                 SamePath(c.Text!.Trim().Trim('"'), exe)));

    static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    static void Set(Vdf.Node entry, byte type, string key, object value)
    {
        var node = entry.Children!.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (node == null) entry.Children!.Add(node = new Vdf.Node(type, key));
        node.Type = type;
        if (value is string s) node.Text = s; else node.Raw = (byte[])value;
    }

    static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}

/// Valve's binary KeyValues, as in shortcuts.vdf: type byte, null-terminated UTF-8 key, value; 0x08 closes a map. Fixed-size
/// values are kept as raw bytes so a read and a write give the same file back.
static class Vdf
{
    public const byte Map = 0, String = 1, Int = 2, End = 8;

    public sealed class Node(byte type, string key)
    {
        public byte Type = type;
        public string Key = key;
        public string? Text;
        public byte[]? Raw;
        public List<Node>? Children;
    }

    static int Size(byte type) => type switch
    {
        2 or 3 or 4 or 6 => 4,   // int32, float, pointer, colour
        7 or 10 => 8,            // uint64, int64
        _ => throw new InvalidDataException($"shortcuts.vdf: unknown value type {type}"),
    };

    public static List<Node> Read(byte[] data)
    {
        var at = 0;
        return ReadMap(data, ref at);
    }

    static List<Node> ReadMap(byte[] data, ref int at)
    {
        var nodes = new List<Node>();
        while (at < data.Length)
        {
            var type = data[at++];
            if (type == End) return nodes;
            var node = new Node(type, ReadString(data, ref at));
            if (type == Map) node.Children = ReadMap(data, ref at);
            else if (type == String) node.Text = ReadString(data, ref at);
            else
            {
                var n = Size(type);
                if (at + n > data.Length) throw new InvalidDataException("shortcuts.vdf: truncated");
                node.Raw = data[at..(at + n)];
                at += n;
            }
            nodes.Add(node);
        }
        return nodes;
    }

    static string ReadString(byte[] data, ref int at)
    {
        var end = Array.IndexOf(data, (byte)0, at);
        if (end < 0) throw new InvalidDataException("shortcuts.vdf: unterminated string");
        var s = Encoding.UTF8.GetString(data, at, end - at);
        at = end + 1;
        return s;
    }

    public static byte[] Write(List<Node> root)
    {
        using var stream = new MemoryStream();
        WriteNodes(stream, root);
        stream.WriteByte(End);
        return stream.ToArray();
    }

    static void WriteNodes(Stream s, List<Node> nodes)
    {
        foreach (var n in nodes)
        {
            s.WriteByte(n.Type);
            WriteString(s, n.Key);
            if (n.Type == Map) { WriteNodes(s, n.Children!); s.WriteByte(End); }
            else if (n.Type == String) WriteString(s, n.Text ?? "");
            else s.Write(n.Raw!);
        }
    }

    static void WriteString(Stream s, string text)
    {
        s.Write(Encoding.UTF8.GetBytes(text));
        s.WriteByte(0);
    }
}
