using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Workshop;

/// <summary>أدوات النصوص والأرقام والتواريخ (مطابقة لسلوك النسخة السابقة من البرنامج)</summary>
public static class Txt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string ArDigits = "٠١٢٣٤٥٦٧٨٩", FaDigits = "۰۱۲۳۴۵۶۷۸۹";

    /// <summary>الأرقام العربية والفارسية ← أرقام لاتينية (والفاصلة العشرية العربية)</summary>
    public static string LatinDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            int i = ArDigits.IndexOf(ch);
            if (i < 0) i = FaDigits.IndexOf(ch);
            sb.Append(i >= 0 ? (char)('0' + i) : ch == '٫' ? '.' : ch == '٬' ? ',' : ch);
        }
        return sb.ToString();
    }

    static readonly Regex Diacritics = new("[ً-ْـ]", RegexOptions.Compiled);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>مطابقة مرنة للعربية: تتجاهل الهمزات والتاء المربوطة والألف المقصورة والتشكيل وحالة الأحرف</summary>
    public static string Fold(string s)
    {
        var t = Diacritics.Replace(LatinDigits(s ?? "").ToLowerInvariant(), "");
        t = t.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ٱ', 'ا')
             .Replace('ة', 'ه').Replace('ى', 'ي').Replace('ؤ', 'و').Replace('ئ', 'ي');
        return Spaces.Replace(t, " ").Trim();
    }

    /// <summary>كل كلمات البحث موجودة في النص</summary>
    public static bool Matches(string haystack, string foldedQuery)
    {
        if (string.IsNullOrEmpty(foldedQuery)) return true;
        var hay = Fold(haystack);
        return foldedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(hay.Contains);
    }

    public static string Str(object v) => (v == null ? "" : Convert.ToString(v, Inv) ?? "").Trim();

    /// <summary>قراءة مبلغ من نص مكتوب بأي شكل (فواصل، أرقام عربية، رمز عملة)</summary>
    public static double ParseMoney(object v)
    {
        switch (v)
        {
            case null: return 0;
            case double d: return double.IsFinite(d) ? d : 0;
            case int i: return i;
            case long l: return l;
            case decimal m: return (double)m;
        }
        var clean = new string(LatinDigits(Convert.ToString(v, Inv)).Where(c => char.IsAsciiDigit(c) || c == '.' || c == '-').ToArray());
        return double.TryParse(clean, NumberStyles.Float, Inv, out var n) && double.IsFinite(n) ? n : 0;
    }

    /// <summary>عدد صحيح أو null (فارغ = الكمية غير متابَعة). الكمية السالبة مسموحة</summary>
    public static int? OptInt(object v)
    {
        if (v == null) return null;
        var t = new string(LatinDigits(Convert.ToString(v, Inv)).Where(c => char.IsAsciiDigit(c) || c == '-').ToArray());
        if (t == "" || t == "-") return null;
        return int.TryParse(t, NumberStyles.Integer, Inv, out var n) ? n : null;
    }

    public static string Num(double n) => (double.IsFinite(n) && Math.Abs(n) >= 0.005 ? n : 0).ToString("#,0.##", Inv);
    public static string Money(double n) => Num(n) + " " + Store.Currency;

    // ---------------- التواريخ (نصوص yyyy-MM-dd كما في البيانات) ----------------
    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", Inv);
    public static string Today => Iso(DateTime.Today);
    /// <summary>وقت الآن بصيغة ISO (يُفرز نصيًا)</summary>
    public static string Now => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv);

    public static DateTime? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 10) return null;
        return DateTime.TryParseExact(s[..10], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>وقت كامل (ISO بتوقيت محلي أو UTC)</summary>
    public static DateTime? ParseTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal, out var d) ? d.ToLocalTime() : null;
    }

    public static int DaysBetween(string a, string b)
    {
        var A = ParseDate(a); var B = ParseDate(b);
        return A != null && B != null ? (int)Math.Round((B.Value - A.Value).TotalDays) : 0;
    }

    public static string Cut10(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 10 ? s[..10] : s;

    static CultureInfo ar;
    static CultureInfo Ar
    {
        get
        {
            if (ar != null) return ar;
            try
            {
                var c = (CultureInfo)CultureInfo.GetCultureInfo("ar-IQ").Clone();
                c.DateTimeFormat.Calendar = new GregorianCalendar();
                ar = c;
            }
            catch { ar = Inv; }
            return ar;
        }
    }

    /// <summary>«25 أيلول 2026»</summary>
    public static string FmtDate(string s) => ParseDate(s) is DateTime d ? d.ToString("d MMMM yyyy", Ar) : "—";
    /// <summary>«25 أيلول»</summary>
    public static string FmtShortDate(string s) => ParseDate(s) is DateTime d ? d.ToString("d MMM", Ar) : "—";
    public static string FmtLong(DateTime d) => d.ToString("dddd، d MMMM yyyy", Ar);
    public static string FmtDayMonth(DateTime d) => d.ToString("d/M", Inv);
    public static string MonthName(DateTime d) => d.ToString("MMM", Ar);
    public static string FmtClock(DateTime d) => d.ToString("dddd، d MMMM — hh:mm tt", Ar);

    // ---------------- المعرّفات ----------------
    static readonly Random rnd = new();
    static string B36(long v)
    {
        const string A = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (v <= 0) return "0";
        var sb = new StringBuilder();
        while (v > 0) { sb.Insert(0, A[(int)(v % 36)]); v /= 36; }
        return sb.ToString();
    }
    static string RndB36(int n) { var sb = new StringBuilder(); lock (rnd) for (int i = 0; i < n; i++) sb.Append("0123456789abcdefghijklmnopqrstuvwxyz"[rnd.Next(36)]); return sb.ToString(); }

    public static string Uid(string prefix = "o") => prefix + "_" + B36(DateTimeOffset.Now.ToUnixTimeMilliseconds()) + "_" + RndB36(5);

    /// <summary>رقم مرجعي قصير للطلب مثل R-8K2QF1 (يُطبع ملصقًا وباركودًا)</summary>
    public static string GenRef(ICollection<string> used)
    {
        string r;
        do r = "R-" + (B36(DateTimeOffset.Now.ToUnixTimeMilliseconds()) + RndB36(2)).ToUpperInvariant()[^6..];
        while (used.Contains(r));
        return r;
    }

    // ---------------- الهاتف ----------------
    static string Digits(string p) => new(LatinDigits(p ?? "").Where(char.IsAsciiDigit).ToArray());

    /// <summary>مفتاح موحّد للهاتف لتجميع طلبات نفس الزبون (07xxxxxxxxx)</summary>
    public static string NormPhoneKey(string p)
    {
        var d = Digits(p);
        if (d.StartsWith("00")) d = d[2..];
        var cc = Store.CountryCode;
        if (cc != "" && d.StartsWith(cc)) d = "0" + d[cc.Length..];
        if (d.Length == 10 && !d.StartsWith("0")) d = "0" + d;
        return d;
    }

    /// <summary>الرقم بالصيغة الدولية لواتساب</summary>
    public static string WaPhone(string p)
    {
        var d = Digits(p);
        if (d == "") return "";
        var cc = Store.CountryCode;
        if (d.StartsWith("00")) d = d[2..];
        if (cc != "" && d.StartsWith(cc)) return d;
        if (d.StartsWith("0")) return cc + d[1..];
        return d.Length <= 10 ? cc + d : d;
    }

    /// <summary>ترميز HTML للطباعة</summary>
    public static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
