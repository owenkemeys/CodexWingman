using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CodexWingman.Core;

namespace CodexWingman;

public static class TrayIconPresentation
{
    public static string TextFor(TrayIconState state) => state == TrayIconState.Attention
        ? "Codex Wingman - Needs attention"
        : "Codex Wingman";
}

public sealed class TrayIconSet : IDisposable
{
    private static readonly Color AttentionYellow = Color.FromArgb(255, 246, 174, 45);
    private readonly Icon normal;
    private readonly Icon attention;

    public TrayIconSet(Icon source)
    {
        normal = new Icon(source, 32, 32);
        attention = CreateAttentionIcon(normal);
    }

    public Icon For(TrayIconState state) => state == TrayIconState.Attention ? attention : normal;

    private static Icon CreateAttentionIcon(Icon source)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.DrawIcon(source, new Rectangle(0, 0, 32, 32));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var outline = new SolidBrush(Color.FromArgb(255, 32, 32, 32));
        using var fill = new SolidBrush(AttentionYellow);
        graphics.FillEllipse(outline, 20, 20, 12, 12);
        graphics.FillEllipse(fill, 22, 22, 8, 8);

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        attention.Dispose();
        normal.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
