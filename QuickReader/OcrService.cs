using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace QuickReader;

internal sealed class OcrUnavailableException : Exception
{
    public OcrUnavailableException()
        : base("English text recognition isn't installed in Windows. Open Settings → Time & language → " +
               "Language & region, choose English → Language options, and add \"Optical character recognition\".")
    {
    }
}

/// <summary>Reads text from a picture with the text recognition built into Windows (works offline).</summary>
internal static class OcrService
{
    private static OcrEngine? _engine;

    public static async Task<string> RecognizeAsync(Bitmap picture)
    {
        OcrEngine engine = GetEngine() ?? throw new OcrUnavailableException();

        using Bitmap? resized = ResizeForOcr(picture);
        using SoftwareBitmap bitmap = ToSoftwareBitmap(resized ?? picture);
        OcrResult result = await engine.RecognizeAsync(bitmap);
        return JoinLines(result);
    }

    private static OcrEngine? GetEngine()
    {
        if (_engine is not null)
            return _engine;

        try
        {
            foreach (string tag in new[] { "en-GB", "en-US" })
            {
                var language = new Language(tag);
                if (OcrEngine.IsLanguageSupported(language))
                {
                    _engine = OcrEngine.TryCreateFromLanguage(language);
                    if (_engine is not null)
                        return _engine;
                }
            }

            foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
            {
                if (language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                {
                    _engine = OcrEngine.TryCreateFromLanguage(language);
                    if (_engine is not null)
                        return _engine;
                }
            }

            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return _engine;
    }

    /// <summary>
    /// Small screen text is recognised much better when enlarged 2×.
    /// Pictures bigger than Windows allows are shrunk. Returns null when no change is needed.
    /// </summary>
    private static Bitmap? ResizeForOcr(Bitmap source)
    {
        int maxSide = (int)OcrEngine.MaxImageDimension;
        int longest = Math.Max(source.Width, source.Height);

        double factor = 1;
        if (longest > maxSide)
            factor = (double)maxSide / longest;
        else if ((long)source.Width * source.Height <= 2_000_000 && longest * 2 <= maxSide)
            factor = 2;

        if (factor == 1)
            return null;

        int width = Math.Max(1, (int)(source.Width * factor));
        int height = Math.Max(1, (int)(source.Height * factor));
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(result);
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY); // avoids dark edges when enlarging
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, attributes);
        return result;
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap picture)
    {
        var area = new Rectangle(0, 0, picture.Width, picture.Height);
        BitmapData data = picture.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = picture.Width * 4;
            var pixels = new byte[rowBytes * picture.Height];
            for (int y = 0; y < picture.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);

            var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, picture.Width, picture.Height,
                BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            return bitmap;
        }
        finally
        {
            picture.UnlockBits(data);
        }
    }

    /// <summary>
    /// Puts the recognised lines back together: lines of one paragraph are joined with spaces,
    /// and a bigger gap between lines becomes a paragraph break (a pause when reading).
    /// </summary>
    private static string JoinLines(OcrResult result)
    {
        var text = new StringBuilder();
        double previousBottom = 0, previousHeight = 0;

        foreach (OcrLine line in result.Lines)
        {
            string lineText = line.Text.Trim();
            if (lineText.Length == 0)
                continue;

            double top = double.MaxValue, bottom = double.MinValue;
            foreach (OcrWord word in line.Words)
            {
                double wordTop = word.BoundingRect.Y;
                double wordBottom = wordTop + word.BoundingRect.Height;
                top = Math.Min(top, wordTop);
                bottom = Math.Max(bottom, wordBottom);
            }
            double height = Math.Max(1, bottom - top);

            if (text.Length > 0)
            {
                double gap = top - previousBottom;
                if (gap > Math.Max(height, previousHeight) * 0.9)
                    text.Append("\n\n");                     // new paragraph
                else if (text[^1] == '-' && char.IsLower(lineText[0]))
                    text.Length--;                           // "exam-" + "ple" -> "example"
                else
                    text.Append(' ');                        // same paragraph
            }

            text.Append(lineText);
            previousBottom = bottom;
            previousHeight = height;
        }

        return text.ToString();
    }
}
