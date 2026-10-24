using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexWingman.Core;

/// <summary>
/// T3's own app icon for Wingman's T3 Code (web) shortcut and windows, taken from the site's public web app
/// manifest (the same icons T3's installed PWA shows) and cached locally as an .ico. Nothing is patched:
/// Wingman only reads the manifest and icon files the site serves to every browser.
/// </summary>
public static class T3WebAppIcon
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);

    /// <summary>The sizes the cached .ico holds, so Windows never has to rescale for the taskbar, Start, Alt+Tab or Explorer.</summary>
    public static readonly IReadOnlyList<int> IcoSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>
    /// The downloaded PNG to draw a given icon size from: the smallest one at least that size (a downscale keeps
    /// it sharp), else the largest. Null when there are no images.
    /// </summary>
    public static byte[]? SourceFor(int size, IReadOnlyList<byte[]> pngs)
    {
        var sized = pngs.Select(png => (Png: png, Width: PngSize(png)?.Width ?? 0)).Where(item => item.Width > 0).ToList();
        if (sized.Count == 0) return null;
        return (sized.Where(item => item.Width >= size).OrderBy(item => item.Width).FirstOrDefault() is { Png: not null } larger
            ? larger : sized.OrderByDescending(item => item.Width).First()).Png;
    }

    /// <summary>The image widths an .ico declares (256 for a stored 0), or empty when the bytes are not an .ico.</summary>
    public static IReadOnlyList<int> IcoImageSizes(byte[] ico)
    {
        if (!IsIco(ico)) return [];
        var count = ico[4] | ico[5] << 8;
        if (ico.Length < 6 + 16 * count) return [];
        return Enumerable.Range(0, count).Select(i => ico[6 + 16 * i] == 0 ? 256 : ico[6 + 16 * i]).ToList();
    }

    /// <summary>True when a cached .ico already holds every size in <see cref="IcoSizes"/> (older caches are rebuilt).</summary>
    public static bool HasAllSizes(byte[] ico) => IcoImageSizes(ico).Order().SequenceEqual(IcoSizes);

    public static string IconPath(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "t3-web", "app-icon.ico");

    /// <summary>True when the cached icon is missing or older than <see cref="RefreshAfter"/>.</summary>
    public static bool NeedsRefresh(DateTime? cachedUtc, DateTime nowUtc) => cachedUtc is not { } written || nowUtc - written > RefreshAfter;

    /// <summary>The manifest named by the page's &lt;link rel="manifest"&gt;, else /manifest.webmanifest; same origin only.</summary>
    public static Uri ManifestUrl(string? html, Uri origin)
    {
        foreach (Match link in Regex.Matches(html ?? "", "<link\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(link.Value, "\\brel\\s*=\\s*[\"']?manifest\\b", RegexOptions.IgnoreCase)) continue;
            var href = Regex.Match(link.Value, "\\bhref\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (href.Success && Uri.TryCreate(origin, href.Groups[1].Value, out var url) && SameOrigin(url, origin)) return url;
        }
        return new Uri(origin, "/manifest.webmanifest");
    }

    /// <summary>PNG icons from the manifest usable as an app icon (purpose "any" or none), largest first; same origin only.</summary>
    public static IReadOnlyList<Uri> PngIcons(string manifestJson, Uri manifestUrl)
    {
        var found = new List<(Uri Url, int Size)>();
        using var document = JsonDocument.Parse(manifestJson);
        if (!document.RootElement.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Array) return [];
        foreach (var icon in icons.EnumerateArray())
        {
            if (!icon.TryGetProperty("src", out var src) || src.GetString() is not { Length: > 0 } source) continue;
            var purpose = icon.TryGetProperty("purpose", out var p) ? p.GetString() ?? "" : "";
            if (purpose.Length > 0 && !purpose.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("any")) continue;
            var type = icon.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (!Uri.TryCreate(manifestUrl, source, out var url) || !SameOrigin(url, manifestUrl)) continue;
            if (type != "image/png" && !url.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            var size = 0;
            if (icon.TryGetProperty("sizes", out var sizes) && sizes.GetString() is { } text)
                foreach (var entry in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (entry.Split('x', 'X') is [var w, _] && int.TryParse(w, out var width)) size = Math.Max(size, width);
            found.Add((url, size));
        }
        return found.OrderByDescending(item => item.Size).Select(item => item.Url).Distinct().ToList();
    }

    /// <summary>Width and height from a PNG's IHDR, or null when the bytes are not a PNG.</summary>
    public static (int Width, int Height)? PngSize(byte[] png)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature) || png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
            return null;
        static int BigEndian(byte[] b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];
        return (BigEndian(png, 16), BigEndian(png, 20));
    }

    /// <summary>
    /// An .ico holding the PNG images as they are (Windows Vista and later read PNG icon entries), smallest first;
    /// one entry per size.
    /// </summary>
    public static byte[] BuildIco(IReadOnlyList<byte[]> pngs)
    {
        var images = pngs.Select(png => (Png: png, Size: PngSize(png) ?? throw new InvalidDataException("Not a PNG icon.")))
            .Where(image => image.Size.Width is > 0 and <= 256 && image.Size.Height is > 0 and <= 256)
            .GroupBy(image => image.Size.Width).Select(group => group.First())
            .OrderBy(image => image.Size.Width).ToList();
        if (images.Count == 0) throw new InvalidDataException("No icon of 256 pixels or less.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var image in images)
        {
            writer.Write((byte)(image.Size.Width == 256 ? 0 : image.Size.Width));
            writer.Write((byte)(image.Size.Height == 256 ? 0 : image.Size.Height));
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(image.Png.Length); writer.Write(offset);
            offset += image.Png.Length;
        }
        foreach (var image in images) writer.Write(image.Png);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>True for an .ico file (reserved 0, type 1, at least one image).</summary>
    public static bool IsIco(byte[] bytes) => bytes.Length > 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0 && (bytes[4] | bytes[5]) != 0;

    private static bool SameOrigin(Uri url, Uri origin) =>
        url.Scheme == Uri.UriSchemeHttps && string.Equals(url.Host, origin.Host, StringComparison.OrdinalIgnoreCase) && url.Port == origin.Port;
}
