using System.Globalization;
using System.IO;
using System.Text;

namespace Ultrahand;

/// One setting of the game's own menu (F1 / Esc in TotK Recomp, runtime/ui/menu.cpp): its totk.ini key, the section the
/// key belongs to (totk.ini.example), the menu's English label and description (runtime/ui_text.c) and its kind.
sealed record Setting(string Key, string Section, string Label, string Description, string Default)
{
    /// Choices: value written to totk.ini and the name the menu shows
    public (string Value, string Name)[]? Choices { get; init; }
    public bool Toggle { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Step { get; init; }
    /// Slider value shown as text, e.g. "{0:0} %"
    public string Format { get; init; } = "{0:0}";
    public bool IsSlider => Choices == null && !Toggle;
}

sealed record SettingsGroup(string? Title, Setting[] Rows);

sealed record SettingsTab(string Name, SettingsGroup[] Groups);

/// The tabs of the game's menu that only write totk.ini. Left out: Advanced (testing and troubleshooting), Amiibo, Mods and
/// Cheats (they need the running game), the graphics card (needs the game's Vulkan device list) and the button remapping.
static class GameSettings
{
    static (string, string)[] Names(params string[] pairs)
    {
        var list = new (string, string)[pairs.Length / 2];
        for (var i = 0; i < list.Length; i++) list[i] = (pairs[2 * i], pairs[2 * i + 1]);
        return list;
    }

    static (string, string)[] Sizes(params string[] values) => values.Select(v => (v, v.Replace("x", " × "))).ToArray();

    static Setting Toggle(string key, string section, string label, string desc, bool on) =>
        new(key, section, label, desc, on ? "1" : "0") { Toggle = true };

    static Setting Slider(string key, string section, string label, string desc, double def, double min, double max, double step,
                          string format = "{0:0} %") =>
        new(key, section, label, desc, def.ToString(CultureInfo.InvariantCulture)) { Min = min, Max = max, Step = step, Format = format };

    static Setting Choice(string key, string section, string label, string desc, string def, (string, string)[] choices) =>
        new(key, section, label, desc, def) { Choices = choices };

    const string Display = "display", FrameRate = "frame rate", Language = "language", Audio = "audio", Input = "input",
                 Discord = "discord";

    public static readonly SettingsTab[] Tabs =
    {
        new("Screen", new[]
        {
            new SettingsGroup(null, new[]
            {
                Toggle("fullscreen", Display, "Fullscreen", "Fills the screen with the game; F11 toggles it.", false),
                Choice("window_size", Display, "Window size", "Size of the game window when not fullscreen.", "1920x1080",
                       Sizes("1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440", "3200x1800", "3840x2160")),
                Choice("vk_present", Display, "Presentation", "How finished frames reach the screen.", "mailbox",
                       Names("mailbox", "Mailbox", "fifo", "VSync", "immediate", "Immediate")),
                Choice("perf_overlay", Display, "Performance overlay",
                       "Frame rate in the top right corner. Full adds frame times with a graph, CPU, GPU and memory load, temperatures " +
                       "and the render thread's waits. F10 cycles the modes.", "off", Names("off", "Off", "fps", "FPS", "full", "Full")),
            }),
            new SettingsGroup("Picture", new[]
            {
                Choice("output_res", Display, "Game resolution",
                       "The size the game draws its picture and interface in. Above 1920 x 1080 the game needs more video memory and " +
                       "GPU time. A wider size (21:9) widens the camera's view and moves the HUD to the screen's corners; a taller one " +
                       "(16:10, 3:2) moves it to the top and bottom edges.", "1920x1080",
                       Sizes("1280x720", "1600x900", "1920x1080", "2560x1440", "3200x1800", "3840x2160", "2560x1080", "3440x1440",
                             "3840x1600", "5120x2160", "3840x1080", "5120x1440", "1280x800", "1680x1050", "1920x1200", "2560x1600",
                             "1920x1280", "2160x1440", "2256x1504", "2880x1920", "3000x2000")),
                Choice("render_scale", Display, "3D render scale",
                       "Size of the 3D scene relative to the game resolution; the game's FSR scales it to that size. Above 100 % " +
                       "smooths jagged edges (supersampling) at a high GPU cost.", "game",
                       Names("game", "Game (83 %)", "50", "50 %", "67", "67 %", "75", "75 %", "100", "100 %", "125", "125 %",
                             "150", "150 %", "200", "200 %")),
                Toggle("dynamic_res", Display, "Dynamic resolution",
                       "Lets the game lower the 3D scene to 90 or 80 %. It judges by an estimate made for the Switch that ignores " +
                       "your GPU, so wide scenes drop even on a fast PC. Off keeps the render scale.", true),
                Choice("shadow_res", Display, "Shadow map size",
                       "Size of the sun's shadow map. The game's own size changes with the view, down to about 400 near the ground, " +
                       "so leaf shadows look blocky and jump; a fixed size keeps them still and sharper. Above 1024 the shadows cost " +
                       "more GPU time and video memory; 256 is for integrated GPUs short of memory bandwidth.", "game",
                       Names("game", "Game (changes)", "256", "256", "512", "512", "768", "768", "1024", "1024", "1536", "1536",
                             "2048", "2048")),
                Choice("upscaler", Display, "Scaling to the window",
                       "How the picture is scaled when the window is larger than the game resolution. FSR keeps edges and detail " +
                       "sharper than bilinear.", "fsr", Names("fsr", "AMD FSR 1", "bilinear", "Bilinear")),
                Slider("sharpness", Display, "FSR sharpness", "Sharpening after FSR scaling; 0 turns it off.", 80, 0, 100, 5),
                Choice("aspect", Display, "Picture shape",
                       "When the window's shape differs from the game resolution's (a 16:9 picture on a 21:9 screen): keep the " +
                       "picture's shape with black bars, or stretch it over the window.", "fit",
                       Names("fit", "Black bars", "stretch", "Stretch")),
                Choice("movie_aspect", Display, "Movie shape",
                       "When the game resolution is not 16:9, keep the cutscene movies' own shape with black bars, or stretch them " +
                       "over the whole picture. Applies from the next movie.", "keep", Names("keep", "Black bars", "stretch", "Stretch")),
                Slider("fov", Display, "Field of view",
                       "How much of the world the camera shows, in percent of the game's own. Game version 1.4.3 only.", 100, 70, 140, 5),
                Choice("aniso", Display, "Texture filtering",
                       "Anisotropic filtering of surface textures: ground, roads and walls seen at a shallow angle stay sharp into " +
                       "the distance instead of blurring. Game: the level the game asks for, as on the Switch.", "game",
                       Names("game", "Game", "2", "2x", "4", "4x", "8", "8x", "16", "16x")),
                Choice("shadow_scale", Display, "Shadow resolution",
                       "Draws the sun's shadow maps this many times larger on each side than the game's 960x960: sharper shadow edges " +
                       "and leaf shadows. Takes effect at the next start.", "1",
                       Names("1", "960 (1x)", "2", "1920 (2x)", "3", "2880 (3x)", "4", "3840 (4x)")),
                Choice("shadow_range", Display, "Shadow cascade range",
                       "Moves the steps where shadows turn softer (the game's 20, 40, 80 and 160 m) further away. Spreads the shadow " +
                       "map over more ground: raise Shadow resolution with it. Takes effect at the next start.", "100",
                       Names("100", "160 m (1x)", "150", "240 m (1.5x)", "200", "320 m (2x)", "300", "480 m (3x)")),
                Slider("pcss", Display, "Soft shadows",
                       "Sun shadows sharp where an object meets the ground and softer the further the shadow falls (PCSS). 0 keeps " +
                       "the game's filter. Takes effect at the next start.", 0, 0, 200, 10),
                Slider("contact_shadows", Display, "Contact shadows",
                       "Small sun shadows the shadow maps miss: at feet, under stones, in grass and creases. Traced on the screen " +
                       "after lighting. Takes effect at the next start.", 0, 0, 100, 10),
                Toggle("dlaa", Display, "NVIDIA DLSS / DLAA",
                       "NVIDIA DLSS for the 3D scene, on NVIDIA RTX GPUs with nvngx_dlss.dll next to totk.exe. Takes effect at the " +
                       "next start.", false),
                Choice("dlss", Display, "DLSS mode",
                       "DLAA draws the 3D scene at full resolution. Quality, Balanced, Performance and Ultra Performance draw it at " +
                       "67, 58, 50 and 33 % and let DLSS scale it up, which saves GPU time; they replace the 3D render scale.", "dlaa",
                       Names("dlaa", "DLAA (native)", "quality", "Quality", "balanced", "Balanced", "performance", "Performance",
                             "ultra_performance", "Ultra Performance")),
                Choice("dlss_preset", Display, "DLSS model",
                       "Which DLSS model runs. Automatic follows NVIDIA's advice per mode: K for DLAA, Quality and Balanced, M for " +
                       "Performance, L for Ultra Performance. J: slightly less ghosting behind moving characters, a little more " +
                       "flicker. K: the sharpest model in every mode.", "default",
                       Names("default", "Automatic", "k", "K (sharpest)", "j", "J (less ghosting)")),
                Choice("dlss_char_mask", Display, "DLSS character trails",
                       "Reduces the faint trails behind moving characters, animals and what they hold: DLSS takes less of the " +
                       "previous frames there. Stronger removes more of the trails and leaves those edges a little less smooth.", "65",
                       Names("0", "Off", "35", "Light", "65", "Medium", "100", "Full")),
                Toggle("fxaa", Display, "Game anti-aliasing (FXAA)",
                       "The game's own FXAA edge smoothing. It blurs fine detail a little; with DLAA on it is not needed.", true),
            }),
            new SettingsGroup("Colour", new[]
            {
                Choice("color_preset", Display, "Colour profile",
                       "Sets the four values below. Vivid: more contrast and saturation, shadows opened a little (made for OLED " +
                       "screens). Clarity: a little more contrast, saturation and brightness. Moving a value makes the profile Custom.",
                       "default", Names("default", "Game's own", "vivid", "Vivid (OLED)", "clarity", "Clarity", "custom", "Custom")),
                Slider("color_brightness", Display, "Brightness", "Added to every colour; 0 leaves it.", 0, -30, 30, 1, "{0:+0;-0;0} %"),
                Slider("color_contrast", Display, "Contrast",
                       "Spreads colours away from mid-grey (above 100 %) or towards it; 100 % leaves them.", 100, 70, 150, 5),
                Slider("color_saturation", Display, "Saturation", "Colour strength against grey; 100 % leaves it.", 100, 50, 200, 5),
                Slider("color_gamma", Display, "Gamma",
                       "Below 1 brightens dark and middle tones, above 1 darkens them; 1 leaves them.", 1, 0.8, 1.2, 0.02, "{0:0.00}"),
            }),
            new SettingsGroup("HDR", new[]
            {
                Toggle("hdr", Display, "HDR output",
                       "Bright highlights on an HDR display. Needs HDR turned on in Windows (\"Use HDR\"); without it the picture " +
                       "stays SDR. Applies after a restart. The colour profile above is for SDR and is not used in HDR.", false),
                Slider("hdr_peak_nits", Display, "Peak brightness",
                       "Your display's peak in nits; the brightest highlights stop there.", 1000, 400, 10000, 50, "{0:0}"),
                Slider("hdr_game_nits", Display, "Game brightness",
                       "Nits of the picture's white (paper white). 203 is the HDR reference; raise it in a bright room.", 203, 80, 500, 5, "{0:0}"),
                Slider("hdr_ui_nits", Display, "HUD and menu brightness", "Nits of the HUD's and the menu's white.", 203, 80, 500, 5, "{0:0}"),
                Slider("hdr_hue", Display, "Highlight hue correction",
                       "Bright colours such as fire and sparks turn towards white as in the game's own SDR picture, matching the " +
                       "light they cast; 0 keeps them fully saturated.", 80, 0, 100, 5),
            }),
        }),
        new("Game", new[]
        {
            new SettingsGroup(null, new[]
            {
                Choice("fps_limit", FrameRate, "Frame rate limit",
                       "The most frames per second the game draws. Above 60 is experimental and needs the game speed to follow the clock " +
                       "(realtime = 1, the default).", "60",
                       Names("30", "30 FPS", "40", "40 FPS", "45", "45 FPS", "60", "60 FPS", "90", "90 FPS", "120", "120 FPS",
                             "144", "144 FPS", "display", "Monitor rate")),
                Toggle("frame_queue", FrameRate, "Frame queue",
                       "The game starts a frame while the previous one waits for its turn on screen: far fewer dropped frames at the " +
                       "limit, up to one frame more input delay.", false),
                Choice("lang", Language, "Game language", "Language of the game's text and voices.", "",
                       new[] { ("", "Automatic") }.Concat(GameLanguages.All.Select(l => (l.Code, l.Name))).ToArray()),
                Choice("operation_mode", Language, "Console mode", "Whether the game thinks it runs docked or handheld.", "docked",
                       Names("docked", "Docked", "handheld", "Handheld")),
                Toggle("assume_focus", Display, "Keep running out of focus",
                       "On (the default), the game keeps running while you are in another window, and the keyboard stays quiet until " +
                       "the window is in front again. Off, it pauses when its window loses focus, as on the Switch when Home is " +
                       "pressed; a load interrupted that way can stop for good.", true),
            }),
        }),
        new("Sound", new[]
        {
            new SettingsGroup(null, new[]
            {
                Slider("volume", Audio, "Volume", "Master volume of the game.", 100, 0, 100, 5),
                Choice("audio_channels", Audio, "Speakers",
                       "Stereo or 5.1 surround output. Automatic picks 5.1 when the Windows speaker setup has six or more channels. " +
                       "Applies after a restart.", "stereo", Names("auto", "Automatic", "stereo", "Stereo", "5.1", "5.1 surround")),
            }),
        }),
        new("Controls", new[]
        {
            new SettingsGroup(null, new[]
            {
                Choice("pad_layout", Input, "Button layout",
                       "Which button is A. Nintendo: the right one, as on a Switch. Xbox: the bottom one, as labelled on an Xbox " +
                       "controller (cross on a PlayStation controller). Automatic picks Nintendo for controllers with Nintendo labels " +
                       "and Xbox for the others.", "auto",
                       Names("auto", "Automatic", "nintendo", "Nintendo (A right)", "xbox", "Xbox (A bottom)")),
                Choice("prompts", Input, "Button prompts",
                       "Which controller's buttons the game's prompts show. Nintendo: the game's own. Xbox: LB, RB, LT, RT, Menu, " +
                       "View and a d-pad cross. PC: the keys and mouse buttons of the keyboard's default layout. The game reads its " +
                       "prompts at start, so they change after a restart.", "nintendo",
                       Names("nintendo", "Nintendo", "xbox", "Xbox", "pc", "PC")),
                Toggle("motion", Input, "Motion controls", "Aiming with the controller's gyroscope.", true),
                Slider("rumble_strength", Input, "Vibration strength", "How strong the controller vibrates.", 130, 0, 300, 10),
                Slider("rumble_curve", Input, "Vibration curve", "Lower values make weak vibration stronger.", 0.6, 0.2, 1.5, 0.05, "{0:0.00}"),
                Toggle("rumble_triggers", Input, "Trigger vibration (Xbox)", "Vibrates the triggers of an Xbox controller too.", false),
                Choice("mouse_mode", Input, "Mouse camera",
                       "Direct: the camera turns by how far the mouse moves, as in a PC game, with no stick dead zone or curve (games " +
                       "1.4.3 and 1.2.1; 1.1.0 uses stick emulation). Stick emulation: the mouse acts as the right stick.", "direct",
                       Names("stick", "Stick emulation", "direct", "Direct")),
                Slider("mouse_sens", Input, "Mouse camera sensitivity", "How fast the mouse turns the camera.", 1, 0, 10, 0.1, "{0:0.0}"),
                Toggle("menu_cursor", Input, "Mouse in the game's menus",
                       "In the game's own menus (title, inventory, shops, dialogue choices) the pointer shows: the item under it is " +
                       "selected, left click confirms, right click goes back (game 1.4.3).", true),
                Choice("walk_key", Input, "Walk key",
                       "While this key is held the keyboard's sticks shrink to the walk scale, so the keyboard can walk instead of run.",
                       "lshift", Names("none", "Off", "lshift", "Shift", "lalt", "Alt", "lctrl", "Ctrl", "tab", "Tab", "capslock", "Caps Lock")),
                Slider("walk_scale", Input, "Walk scale",
                       "The tilt the keyboard's sticks get while the walk key is held. Half is a walk, the full tilt a run.",
                       0.51, 0.2, 0.9, 0.01, "{0:0.00}"),
            }),
            new SettingsGroup("Analog sticks", new[]
            {
                Slider("lstick_deadzone", Input, "Left stick dead zone",
                       "How far a gamepad stick moves before the game sees it. Raise it if the character or the camera moves on its own.",
                       8, 0, 50, 1),
                Slider("rstick_deadzone", Input, "Right stick dead zone",
                       "How far a gamepad stick moves before the game sees it. Raise it if the character or the camera moves on its own.",
                       8, 0, 50, 1),
                Slider("stick_sens", Input, "Stick camera speed",
                       "Multiplies the game's own right-stick camera speed, top speed included, at each of the game's five camera " +
                       "sensitivities. The mouse keeps its own setting.", 1, 0.1, 3, 0.05, "{0:0.00}"),
            }),
        }),
        new("Menu", new[]
        {
            new SettingsGroup(null, new[]
            {
                Choice("menu_lang", Language, "Menu language", "Language of the game's settings menu and the overlay.", "",
                       Names("", "Automatic (from Windows)", "en", "English", "ru", "Русский", "pt-BR", "Português (Brasil)",
                             "zh-Hans", "简体中文")),
                Toggle("discord_rpc", Discord, "Discord Rich Presence",
                       "Shows \"Playing TotK Recomp\" with the play time in your Discord status while the game runs.", false),
            }),
        }),
    };

    /// The colour profiles of the menu (runtime/ui/menu.cpp rows_color): brightness, contrast, saturation, gamma
    public static readonly Dictionary<string, (string B, string C, string S, string G)> ColorPresets = new()
    {
        ["default"] = ("0", "100", "100", "1"), ["vivid"] = ("0", "115", "135", "0.95"), ["clarity"] = ("2", "110", "115", "1"),
    };
}

/// totk.ini as the game reads and writes it (runtime/cfg.c): "key = value" lines, '#' or ';' starts a comment line, keys are
/// case-insensitive and the last line of a key wins, [sections] only group. Set rewrites the key's last line, or adds the line
/// after its commented example or at the end of its section (a new section at the end when the file has none), and keeps
/// every other line as it is, as the game's cfg_save does.
sealed class TotkIni
{
    public readonly string Path;
    readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public TotkIni(string gameFolder)
    {
        Path = System.IO.Path.Combine(gameFolder, "totk.ini");
        if (!File.Exists(Path)) return;
        foreach (var raw in File.ReadAllLines(Path))
            if (Key(raw, commented: false) is { } k) values[k] = raw[(raw.IndexOf('=') + 1)..].Trim();
    }

    public string? Get(string key) => values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    /// The key of a "key = value" line, or null; commented: also "# key = value" (the examples of totk.ini.example)
    static string? Key(string line, bool commented)
    {
        var t = line.TrimStart('﻿').Trim();
        if (commented && (t.StartsWith('#') || t.StartsWith(';'))) t = t[1..].TrimStart();
        else if (t.StartsWith('#') || t.StartsWith(';')) return null;
        var eq = t.IndexOf('=');
        if (eq <= 0) return null;
        var key = t[..eq].Trim();
        return key.Length > 0 && key.All(c => char.IsLetterOrDigit(c) || c == '_') ? key.ToLowerInvariant() : null;
    }

    static string? Section(string line)
    {
        var t = line.Trim();
        return t.Length > 2 && t[0] == '[' && t[^1] == ']' ? t[1..^1].Trim().ToLowerInvariant() : null;
    }

    public void Set(string key, string section, string value)
    {
        values[key] = value;
        var text = File.Exists(Path) ? File.ReadAllText(Path) : "";
        var eol = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Length == 0 ? new List<string>() : text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        var last = lines.FindLastIndex(l => Key(l, false) == key);
        if (last >= 0)
        {
            var eq = lines[last].IndexOf('=');
            lines[last] = lines[last][..eq].TrimEnd() + " = " + value;
        }
        else
        {
            int example = -1, tail = -1;
            var inside = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (Section(lines[i]) is { } s)
                {
                    inside = s == section;
                    if (inside) tail = i;
                    continue;
                }
                if (!inside) continue;
                if (lines[i].Trim().Length > 0) tail = i;
                if (Key(lines[i], true) == key) example = i;
            }
            var line = $"{key} = {value}";
            if (example >= 0) lines.Insert(example + 1, line);
            else if (tail >= 0) lines.Insert(tail + 1, line);
            else
            {
                if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
                lines.Add($"[{section}]");
                lines.Add(line);
            }
        }
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, string.Join(eol, lines) + eol, new UTF8Encoding(false));
        File.Move(tmp, Path, overwrite: true);
    }
}
