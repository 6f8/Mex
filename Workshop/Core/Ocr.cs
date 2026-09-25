using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Workshop;

/// <summary>
/// قراءة IMEI من صورة (ملصق العلبة، شاشة ‎*#06#‎، صورة من الهاتف) بمحرك التعرف على النصوص المدمج في ويندوز 10/11.
/// لا يحتاج إنترنت ولا برامج إضافية.
/// </summary>
public static class ImeiOcr
{
    static OcrEngine Engine()
    {
        OcrEngine e = null;
        try { e = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US")); } catch { }
        e ??= OcrEngine.TryCreateFromUserProfileLanguages();
        return e ?? throw new InvalidOperationException("التعرف على النصوص غير متاح في ويندوز هذا الجهاز (يحتاج ويندوز 10 أو أحدث).");
    }

    public static async Task<string> TextFromFile(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await Engine().RecognizeAsync(bmp);
        return result.Text ?? "";
    }

    /// <summary>أرقام من 15 خانة في النص (IMEI)، الصحيحة حسب Luhn أولاً</summary>
    public static List<string> Find(string text)
    {
        var found = new List<string>();
        var t = Txt.LatinDigits(text ?? "").Replace('O', '0').Replace('o', '0');
        foreach (Match m in Regex.Matches(t, @"(?<!\d)\d[\d \-/]{13,24}\d(?!\d)"))
        {
            var d = new string(m.Value.Where(char.IsDigit).ToArray());
            if (d.Length == 15) found.Add(d);
            else if (d.Length == 30) { found.Add(d[..15]); found.Add(d[15..]); }   // IMEI1 وIMEI2 متلاصقان
            else if (d.Length > 15) found.Add(d[..15]);
        }
        foreach (Match m in Regex.Matches(t, @"(?<!\d)\d{15}(?!\d)")) found.Add(m.Value);
        return found.Distinct().OrderByDescending(Calc.ImeiValid).ToList();
    }

    public static async Task<List<string>> FromImage(Image img)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "workshop_ocr_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            // تكبير الصور الصغيرة يحسّن القراءة
            int scale = img.Width < 1200 ? 2 : 1;
            using (var big = new Bitmap(img.Width * scale, img.Height * scale))
            {
                using (var g = Graphics.FromImage(big))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.White);
                    g.DrawImage(img, 0, 0, big.Width, big.Height);
                }
                big.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
            }
            return Find(await TextFromFile(tmp));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
