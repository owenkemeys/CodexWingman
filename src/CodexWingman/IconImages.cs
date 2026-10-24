using CodexWingman.Core;

namespace CodexWingman;

/// <summary>Icon image scaling for the T3 Code (web) app icon (Windows drawing; kept apart so the Windows tests compile it alone).</summary>
internal static class IconImages
{
    /// <summary>The PNG redrawn at size x size (high-quality bicubic, transparent edges kept); returned as is when already that size.</summary>
    public static byte[] ScaledPng(byte[] png, int size)
    {
        if (T3WebAppIcon.PngSize(png) is (var width, var height) && width == size && height == size) return png;
        try
        {
            using var input = new MemoryStream(png);
            using var source = new System.Drawing.Bitmap(input);
            using var target = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(target))
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                attributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                graphics.DrawImage(source, new System.Drawing.Rectangle(0, 0, size, size), 0, 0, source.Width, source.Height,
                    System.Drawing.GraphicsUnit.Pixel, attributes);
            }
            using var output = new MemoryStream();
            target.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            throw new InvalidDataException("Could not scale the T3 icon.", error);
        }
    }
}
