using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ultrahand;

/// The launcher's own updates. The game keeps coming from the original repository's releases (Releases.cs); the launcher comes from
/// this repository's releases tagged launcher-vX.Y.Z with UltrahandLauncher.exe attached (.github/workflows/release.yml publishes them).
static class LauncherUpdate
{
    public const string Repo = "paulosnp/ultrahand-launcher";
    const string TagPrefix = "launcher-v";
    const string AssetName = "UltrahandLauncher.exe";

    public sealed record Update(string Version, string Url, long Size, string? Sha256);

    /// This build's version: the csproj Version, which the release workflow sets from the tag
    public static string Current => typeof(LauncherUpdate).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// Only the published .exe running from App.InstallDir replaces itself; a bin\ build or a copy anywhere else does not
    public static bool CanApply =>
        Environment.ProcessPath is { } exe && string.Equals(Path.GetFullPath(exe), App.InstalledExe, StringComparison.OrdinalIgnoreCase);

    /// The newest launcher release when it is newer than this build, else null (also when GitHub cannot be reached)
    public static async Task<Update?> Check(CancellationToken token = default)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=30");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Releases.Http.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            Update? best = null;
            foreach (var release in json.RootElement.EnumerateArray())
            {
                if (Flag(release, "draft") || Flag(release, "prerelease")) continue;
                var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var version = Versions.Short(tag[TagPrefix.Length..]);
                if (!Versions.Newer(version, best?.Version ?? Current) || Asset(release) is not { } asset) continue;
                best = asset with { Version = version };
            }
            return best;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static Update? Asset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            if (!(a.TryGetProperty("name", out var n) && string.Equals(n.GetString(), AssetName, StringComparison.OrdinalIgnoreCase))) continue;
            if (!a.TryGetProperty("browser_download_url", out var url) || url.GetString() is not { } link) return null;
            var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            // (GitHub lists a digest for every uploaded asset as "sha256:<hex>")
            var digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
            var sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
            return new Update("", link, size, sha);
        }
        return null;
    }

    /// Downloads the new .exe next to the running one, checks it, swaps the two files and starts the new one with the same arguments;
    /// the caller then shuts this one down. Windows lets a running .exe be renamed but not overwritten, so this one becomes .old and
    /// the next start deletes it.
    public static async Task Apply(Update update, Action<double> progress, CancellationToken token)
    {
        var exe = App.InstalledExe;
        var fresh = exe + ".new";
        await Releases.Download(update.Url, update.Size, fresh, progress, _ => { }, token);
        Verify(fresh, update);
        var old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(fresh, exe);
        }
        catch (Exception)
        {
            File.Move(old, exe);   // (put the running version back so the launcher still opens)
            throw;
        }
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = App.InstallDir };
        foreach (var a in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(a);
        Process.Start(start);
    }

    static void Verify(string path, Update update)
    {
        using var file = File.OpenRead(path);
        if (update.Size > 0 && file.Length != update.Size)
            throw new InvalidDataException($"the download has {file.Length} bytes, the release lists {update.Size}");
        Span<byte> head = stackalloc byte[2];
        file.ReadExactly(head);
        if (head[0] != (byte)'M' || head[1] != (byte)'Z') throw new InvalidDataException("the download is not a Windows program");
        if (update.Sha256 == null) return;
        file.Position = 0;
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("the download does not match the SHA-256 the release lists");
    }

    /// Deletes what the previous update left: the .old (once that process has exited) and an interrupted .new
    public static void CleanUp()
    {
        if (!CanApply) return;
        Task.Run(async () =>
        {
            foreach (var leftover in new[] { App.InstalledExe + ".old", App.InstalledExe + ".new" })
                for (var attempt = 0; attempt < 10 && File.Exists(leftover); attempt++)
                {
                    try { File.Delete(leftover); }
                    catch (Exception) { await Task.Delay(1000); }
                }
        });
    }
}
