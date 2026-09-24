using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Raseed;

/// <summary>
/// الخطوط المضمّنة داخل البرنامج (لا تحتاج تثبيتًا على الجهاز):
/// IBM Plex Sans Arabic للنصوص، وLucide للأيقونات. إن تعذر تحميلها يُستخدم Segoe UI.
/// </summary>
public static class FontKit
{
    [DllImport("gdi32.dll")]
    static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

    static readonly PrivateFontCollection pfc = new();
    static readonly Dictionary<(float, FontStyle, bool), Font> cache = new();
    static FontFamily text, semi, icons;
    static bool loaded;

    public const string Fallback = "Segoe UI";

    public static void Init()
    {
        if (loaded) return;
        loaded = true;
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var s = asm.GetManifestResourceStream(name);
                var data = new byte[s.Length];
                s.ReadExactly(data);
                // الذاكرة تبقى محجوزة طوال عمر البرنامج (يتطلبها GDI+ وGDI)
                var ptr = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, ptr, data.Length);
                pfc.AddMemoryFont(ptr, data.Length);          // GDI+ (الرسم المخصص)
                uint count = 0;
                AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, ref count);   // GDI (الأدوات القياسية)
            }
            catch { /* نكمل بالخط الاحتياطي */ }
        }
        text = pfc.Families.FirstOrDefault(f => f.Name == "IBM Plex Sans Arabic");
        semi = pfc.Families.FirstOrDefault(f => f.Name == "IBM Plex Sans Arabic SemiBold");
        icons = pfc.Families.FirstOrDefault(f => f.Name == "lucide");
    }

    public static bool HasIcons => icons != null;

    /// <summary>خط النصوص. semibold = وزن متوسط للعناوين والأزرار</summary>
    public static Font Get(float size, FontStyle style = FontStyle.Regular, bool semibold = false)
    {
        var key = (size, style, semibold);
        if (cache.TryGetValue(key, out var f)) return f;
        try
        {
            if (semibold && semi != null) f = new Font(semi, size, style & ~FontStyle.Bold);
            else if (text != null) f = new Font(text, size, style);
            else f = new Font(semibold ? "Segoe UI Semibold" : Fallback, size, style);
        }
        catch { f = new Font(Fallback, size, style); }
        cache[key] = f;
        return f;
    }

    /// <summary>خط جديد غير مخزّن (للطباعة: يُتخلص منه بعد الاستخدام)</summary>
    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        try { return text != null ? new Font(text, size, style) : new Font(Fallback, size, style); }
        catch { return new Font(Fallback, size, style); }
    }

    static readonly Dictionary<float, Font> iconCache = new();
    public static Font Icon(float px)
    {
        if (icons == null) return null;
        if (!iconCache.TryGetValue(px, out var f)) iconCache[px] = f = new Font(icons, px, FontStyle.Regular, GraphicsUnit.Pixel);
        return f;
    }
}

public static partial class Icons
{
    public static string Get(string name) => name != null && map.TryGetValue(name, out var g) ? g : null;

    static readonly StringFormat center = new(StringFormat.GenericTypographic)
    { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };

    /// <summary>رسم أيقونة داخل مستطيل بلون محدد</summary>
    public static void Draw(Graphics g, string name, RectangleF r, Color color, float px = 0)
    {
        var glyph = Get(name);
        if (glyph == null || !FontKit.HasIcons) return;
        if (px <= 0) px = Math.Min(r.Width, r.Height);
        var f = FontKit.Icon(px);
        var old = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var b = new SolidBrush(color);
        g.DrawString(glyph, f, b, new RectangleF(r.X, r.Y + px * 0.04f, r.Width, r.Height), center);
        g.TextRenderingHint = old;
    }

    /// <summary>صورة صغيرة للأيقونة (للقوائم والأزرار القياسية)</summary>
    public static Bitmap Image(string name, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Draw(g, name, new RectangleF(0, 0, size, size), color, size * 0.86f);
        return bmp;
    }
}
