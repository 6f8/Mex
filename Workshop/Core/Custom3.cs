using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workshop;

// ============================== تفاصيل الطلب الإضافية ==============================
/// <summary>
/// تفاصيل الطلب الإضافية في كائن واحد يُحفظ تحت المفتاح "x" (فلا تتغير صيغة الطلب الأساسية المتوافقة مع نسخة المتصفح):
/// فحص الجودة، توقيت العمل، رسم الخدوش، بنود الإصلاح، سقف السعر، ملاحظات الموظفين، رقم الملصق، مصدر الزبون،
/// نوع الخدمة والشحن، خطة التقسيط، والفرع.
/// </summary>
public class OrderExtra
{
    public Dictionary<string, string> QC = new();
    public string QcAt, QcBy;
    public List<WorkSpan> Work = new();
    public List<Mark> Marks = new();
    public List<JobItem> Items = new();
    public double? MaxBudget;
    public List<Note> Chat = new();
    public string SealNo = "", Source = "", Area = "";
    /// <summary>shop: في المحل، pickup: استلام من المنزل، onsite: زيارة ميدانية، ship: شحن من/إلى محافظة</summary>
    public string Service = "shop";
    public string Address = "", VisitAt = "";
    public double ServiceFee;
    public string ShipCompany = "", ShipTracking = "", ShipStatus = "";
    public double ShipFee;
    public List<Inst> Plan = new();
    public string Branch = "";
}

public class WorkSpan { public string Start, End, Tech = ""; }
/// <summary>علامة على رسم الجهاز: الوجه (أمام/خلف) والموضع (0..1) والوصف</summary>
public class Mark { public string Side = "front", Note = ""; public double X, Y; }
public class JobItem { public string Desc = ""; public double Price; public bool Approved = true; }
public class Note { public string At, Author = "", Text = ""; }
public class Inst { public string Date; public double Amount; }

public static class Extra
{
    static readonly JsonSerializerOptions opts = new() { IncludeFields = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static JsonNode ToJson(OrderExtra x) => JsonSerializer.SerializeToNode(x, opts);

    public static OrderExtra From(JsonNode n)
    {
        if (n is not JsonObject) return new OrderExtra();
        try
        {
            var x = n.Deserialize<OrderExtra>(opts) ?? new OrderExtra();
            x.QC ??= new(); x.Work ??= new(); x.Marks ??= new(); x.Items ??= new(); x.Chat ??= new(); x.Plan ??= new();
            x.SealNo ??= ""; x.Source ??= ""; x.Area ??= ""; x.Service ??= "shop"; x.Address ??= ""; x.VisitAt ??= "";
            x.ShipCompany ??= ""; x.ShipTracking ??= ""; x.ShipStatus ??= ""; x.Branch ??= "";
            return x;
        }
        catch { return new OrderExtra(); }
    }

    public static OrderExtra Copy(OrderExtra x) => From(ToJson(x));

    public static readonly (string Key, string Title)[] Services =
        { ("shop", "في المحل"), ("pickup", "استلام من المنزل"), ("onsite", "زيارة ميدانية"), ("ship", "شحن (محافظة أخرى)") };
    public static readonly string[] ShipStates = { "بانتظار الشحن", "شُحن للزبون", "وصل للزبون", "وصل للمحل" };
    public static string ServiceText(string key) => Services.FirstOrDefault(s => s.Key == key).Title ?? "في المحل";
}

// ============================== فحص الجودة قبل التسليم ==============================
public static class QC
{
    public static bool Required => Store.Flag("qc_required", true);
    public static string[] Items => Lists.Get("qc_checks");
    /// <summary>اكتمل الفحص: كل بنود القائمة الحالية مُعلَّمة (يعمل أو لا يعمل)</summary>
    public static bool Done(Order o) => Items.All(i => o.X.QC.ContainsKey(i));
    public static bool Passed(Order o) => Done(o) && !o.X.QC.Values.Contains("bad");
}

// ============================== توقيت العمل الفعلي ==============================
public static class WorkTimer
{
    public static WorkSpan Running(Order o) => o.X.Work.LastOrDefault(w => w.End == null);
    public static bool IsRunning(Order o) => Running(o) != null;

    public static TimeSpan Total(Order o)
    {
        var t = TimeSpan.Zero;
        foreach (var w in o.X.Work)
        {
            if (Txt.ParseTime(w.Start) is not DateTime a) continue;
            var d = (Txt.ParseTime(w.End) ?? DateTime.Now) - a;
            if (d > TimeSpan.Zero) t += d;
        }
        return t;
    }

    public static Order Toggle(Order o, string tech)
    {
        var n = o.Clone();
        var run = Running(n);
        if (run != null) run.End = Txt.Now;
        else n.X.Work.Add(new WorkSpan { Start = Txt.Now, Tech = tech ?? n.Technician });
        n.UpdatedAt = Txt.Now;
        Store.SaveOrder(n);
        return n;
    }

    public static string Text(TimeSpan t) => t.TotalMinutes < 1 ? "—" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} س {t.Minutes} د" : $"{t.Minutes} د";
}

// ============================== رصيد الزبون ==============================
/// <summary>
/// رصيد الزبون لا يُخزَّن منفصلاً: هو مجموع حركات «رصيد الزبون» في طلباته — تحويل زائد مدفوع إلى رصيد (بالسالب)
/// واستعماله في طلب جديد (بالموجب). هذه الحركات لا تدخل الصندوق لأن النقد قُبض أصلاً في الطلب الأول.
/// </summary>
public static class Credits
{
    public static double Balance(string customerKey) =>
        -Store.Orders.Where(o => Calc.CustomerKey(o) == customerKey).SelectMany(o => o.PaymentHistory).Where(p => p.IsCredit).Sum(p => p.Amount);

    public static double Balance(Order o) => Balance(Calc.CustomerKey(o));

    /// <summary>تحويل المبلغ الزائد في طلب إلى رصيد للزبون</summary>
    public static Order KeepOverpaid(Order o)
    {
        double over = Refunds.Overpaid(o);
        if (over <= 0) return o;
        var n = o.Clone();
        n.PaymentHistory.Add(new Payment { Id = Txt.Uid("cr"), Amount = -over, Date = Txt.Today, Method = Lists.Credit, Note = "تحويل الزائد إلى رصيد الزبون" });
        n.Paid -= over;
        n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
        n.UpdatedAt = Txt.Now;
        Locking.Log(n, $"حُفظ {Txt.Money(over)} رصيداً للزبون");
        Store.SaveOrder(n);
        return n;
    }
}

// ============================== أرقام الهواتف ==============================
public static class Phones
{
    /// <summary>وصف مشكلة الرقم، أو "" إن كان سليماً (للعراق: 07xxxxxxxxx)</summary>
    public static string Problem(string phone)
    {
        var d = new string(Txt.LatinDigits(phone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d == "") return "";
        if (Store.CountryCode == "964")
        {
            if (d.StartsWith("00964")) d = "0" + d[5..];
            else if (d.StartsWith("964")) d = "0" + d[3..];
            else if (d.Length == 10 && d.StartsWith("7")) d = "0" + d;
            if (!d.StartsWith("07")) return "الرقم العراقي يبدأ بـ 07";
            if (d.Length != 11) return $"الرقم العراقي 11 رقماً — هذا {d.Length}";
            return "";
        }
        return d.Length is < 7 or > 15 ? "الرقم قصير أو طويل أكثر من اللازم" : "";
    }
}

// ============================== أسماء الموديلات ==============================
/// <summary>قائمة موديلات شائعة + ما سُجّل في الطلبات، وتنبيه «هل تقصد» عند كتابة مشابهة لموديل موجود</summary>
public static class Models
{
    static List<string> builtIn;

    public static List<string> BuiltIn => builtIn ??= Build();

    static List<string> Build()
    {
        var l = new List<string>();
        foreach (var n in new[] { "6", "6s", "7", "8" }) { l.Add("iPhone " + n); l.Add("iPhone " + n + " Plus"); }
        l.AddRange(new[] { "iPhone X", "iPhone XR", "iPhone XS", "iPhone XS Max", "iPhone SE 2020", "iPhone SE 2022" });
        foreach (var n in new[] { "11", "12", "13", "14", "15", "16" })
        {
            l.Add("iPhone " + n); l.Add("iPhone " + n + " Pro"); l.Add("iPhone " + n + " Pro Max");
            if (n is "12" or "13") l.Add("iPhone " + n + " mini");
            if (n is "14" or "15" or "16") l.Add("iPhone " + n + " Plus");
        }
        foreach (var n in new[] { "01", "02", "03", "04", "05", "10", "11", "12", "13", "14", "15", "16", "20", "21", "22", "23", "24", "25", "30", "31", "32", "33", "34", "35", "50", "51", "52", "53", "54", "55", "70", "71", "72", "73" })
            l.Add("Galaxy A" + n);
        foreach (var n in new[] { "8", "9", "10", "20", "21", "22", "23", "24", "25" })
        { l.Add("Galaxy S" + n); l.Add("Galaxy S" + n + (n is "8" or "9" or "10" ? "+" : " Plus")); if (int.Parse(n) >= 20) l.Add("Galaxy S" + n + " Ultra"); }
        l.AddRange(new[] { "Galaxy Note 8", "Galaxy Note 9", "Galaxy Note 10", "Galaxy Note 20", "Galaxy Note 20 Ultra", "Galaxy M31", "Galaxy M51", "Galaxy Z Flip", "Galaxy Z Fold" });
        foreach (var n in new[] { "8", "9", "10", "11", "12", "13", "14" }) { l.Add("Redmi Note " + n); l.Add("Redmi Note " + n + " Pro"); }
        foreach (var n in new[] { "9", "9A", "9C", "10", "10C", "12", "12C", "13", "13C", "A1", "A2", "A3" }) l.Add("Redmi " + n);
        foreach (var n in new[] { "X3 Pro", "X5 Pro", "F3", "F5", "M3", "M4 Pro", "X6 Pro" }) l.Add("Poco " + n);
        foreach (var n in new[] { "Y7", "Y9", "Y9 Prime", "P30", "P30 Lite", "Nova 5T", "Nova 7i", "Nova 9", "Nova 11" }) l.Add("Huawei " + n);
        foreach (var n in new[] { "A5s", "A15", "A16", "A17", "A54", "A57", "A78", "Reno 5", "Reno 7", "Reno 8", "Reno 10" }) l.Add("Oppo " + n);
        foreach (var n in new[] { "C11", "C21", "C25", "C31", "C35", "C53", "C55", "7", "8", "9", "10", "11" }) l.Add("Realme " + n);
        foreach (var n in new[] { "Hot 10", "Hot 11", "Hot 12", "Hot 20", "Hot 30", "Hot 40", "Note 10", "Note 11", "Note 12", "Note 30", "Smart 6", "Smart 7", "Smart 8" }) l.Add("Infinix " + n);
        foreach (var n in new[] { "Spark 7", "Spark 8", "Spark 9", "Spark 10", "Spark 20", "Camon 17", "Camon 18", "Camon 19", "Camon 20", "Pova 4", "Pova 5" }) l.Add("Tecno " + n);
        foreach (var n in new[] { "X6", "X7", "X8", "X9", "70", "90", "Magic 5" }) l.Add("Honor " + n);
        foreach (var n in new[] { "Y11", "Y20", "Y21", "Y33s", "Y36", "V21", "V27" }) l.Add("Vivo " + n);
        return l;
    }

    /// <summary>كل الأسماء المعروفة: القائمة الجاهزة + الأكثر استعمالاً في طلباتك</summary>
    public static List<string> All() => Calc.GroupByName(BuiltIn.Concat(Store.Orders.Select(o => o.Device)).Concat(Store.Inventory.Select(i => i.Compatible)), x => x)
        .Select(g => g.Label).ToList();

    static string Key(string s) => new(Txt.Fold(s).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    static int Lev(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 9;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1]; cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }

    /// <summary>اسم موجود مشابه لما كُتب (غير مطابق تماماً)، أو null</summary>
    public static string Similar(string typed)
    {
        var k = Key(typed);
        if (k.Length < 4) return null;
        var all = All();
        if (all.Any(m => Txt.Fold(m) == Txt.Fold(typed))) return null;
        var same = all.FirstOrDefault(m => Key(m) == k);
        if (same != null) return same;
        // خطأ إملائي في الحروف فقط: الأرقام (رقم الموديل) يجب أن تتطابق تماماً، فـ «A5» لا تُصحَّح إلى «A54»
        static string Digits(string v) => new(v.Where(char.IsDigit).ToArray());
        var d = Digits(k);
        return all.FirstOrDefault(m => Digits(Key(m)) == d && Lev(Key(m), k) <= (k.Length >= 8 ? 2 : 1));
    }

    // ---------- معرفة الموديل من IMEI (يتعلّم من طلباتك) ----------
    /// <summary>أول 8 أرقام من IMEI تحدد الموديل: يُقترح اسم الجهاز من طلب سابق بنفس البداية</summary>
    public static string FromImei(string imei)
    {
        var d = new string(Txt.LatinDigits(imei ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d.Length < 8) return null;
        var tac = d[..8];
        return Calc.GroupByName(Store.Orders.Concat(Store.Trash).Where(o => o.Imei.Length >= 8 && o.Imei[..8] == tac), o => o.Device)
            .OrderByDescending(g => g.Items.Count).Select(g => g.Label).FirstOrDefault();
    }
}

// ============================== شروط خاصة لكل عطل ==============================
public static class IssueTerms
{
    static Dictionary<string, string> Map()
    {
        try { return JsonNode.Parse(Store.Get("issue_terms", "{}")) is JsonObject o ? o.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "") : new(); }
        catch { return new(); }
    }

    public static string For(string issueType) => Map().TryGetValue(issueType ?? "", out var t) ? t : "";

    public static void Set(string issueType, string text)
    {
        var m = Map();
        if (string.IsNullOrWhiteSpace(text)) m.Remove(issueType); else m[issueType] = text.Trim();
        Store.Set("issue_terms", m.Count == 0 ? "" : new JsonObject(m.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode)kv.Value))).ToJsonString());
    }
}

// ============================== التقسيط ==============================
public static class Installments
{
    /// <summary>أقساط مستحقة ولم تُغطَّ: القسط يُعتبر مدفوعاً إذا غطّت الدفعات مجموع الأقساط حتى تاريخه</summary>
    public static List<(Inst I, double Due, bool Paid, bool Late)> Status(Order o)
    {
        var res = new List<(Inst, double, bool, bool)>();
        double paid = o.Paid, cum = 0;
        // الأقساط تبدأ بعد الدفعة الأولى: ما دُفع قبل الخطة يُحسب لأول الأقساط
        foreach (var i in o.X.Plan.OrderBy(p => p.Date, StringComparer.Ordinal))
        {
            cum += i.Amount;
            bool ok = paid + 0.001 >= cum;
            res.Add((i, cum, ok, !ok && string.CompareOrdinal(i.Date, Txt.Today) < 0));
        }
        return res;
    }

    public static List<Order> Overdue() => Store.Orders.Where(o => o.X.Plan.Count > 0 && Calc.RemainingOf(o) > 0 && Status(o).Any(s => s.Late)).ToList();

    public static List<Inst> Make(double total, int count, DateTime first, int stepDays)
    {
        var list = new List<Inst>();
        if (count <= 0 || total <= 0) return list;
        double each = Math.Floor(total / count / 250) * 250;
        if (each <= 0) each = Math.Round(total / count, 2);
        for (int i = 0; i < count; i++)
        {
            double a = i == count - 1 ? total - each * (count - 1) : each;
            list.Add(new Inst { Date = Txt.Iso(stepDays == 30 ? first.AddMonths(i) : first.AddDays(stepDays * i)), Amount = Math.Round(a, 2) });
        }
        return list;
    }
}

// ============================== الطلبات المعلّقة ==============================
public static class Stale
{
    public static int Days => int.TryParse(Store.Get("stale_days", "7"), out var d) && d > 0 ? d : 7;
    public static List<Order> List() => Store.Orders.Where(o => o.Status == K.Approval && Calc.StatusDays(o) >= Days)
        .OrderByDescending(Calc.StatusDays).ToList();
}

// ============================== القطع المحجوزة ==============================
public static class Reserved
{
    /// <summary>القطع المضافة لطلبات ما زالت مفتوحة (خُصمت من الكمية، ومخصصة لها)</summary>
    public static Dictionary<string, int> Map() => Calc.InvUsage(Store.Orders.Where(Calc.IsOpen).SelectMany(o => o.Parts));

    public static List<Order> OrdersFor(string invId) => Store.Orders.Where(o => Calc.IsOpen(o) && o.Parts.Any(p => p.InventoryItemId == invId)).ToList();
}

// ============================== الفروع ==============================
public static class Branches
{
    /// <summary>اسم هذا الفرع (يُكتب على كل طلب جديد)</summary>
    public static string Current => Store.Get("branch_name");
}
