using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Workshop;

/// <summary>سجلات عامة بسيطة فوق Store.Docs (تُحفظ وتنتقل مع النسخ الاحتياطية)</summary>
static class Docs
{
    static readonly JsonSerializerOptions opts = new() { IncludeFields = true };
    public static List<T> All<T>(string kind) => Store.Docs(kind).Select(d => { try { return d.Deserialize<T>(opts); } catch { return default; } }).Where(x => x != null).ToList();
    public static void Put<T>(string kind, string id, T v) => Store.PutDoc(kind, id, (JsonObject)JsonSerializer.SerializeToNode(v, opts));
    public static void Del(string kind, string id) => Store.DelDoc(kind, id);
}

// ============================== الصناديق والمسحوبات والتحويلات ==============================
public class Withdrawal { public string Id, Date, Box = Lists.Cash, Note = ""; public double Amount; }
public class Transfer { public string Id, Date, From = Lists.Cash, To = "", Note = ""; public double Amount; }

/// <summary>
/// كل طريقة دفع صندوق مستقل (النقد في الدرج، زين كاش، البنك...). الرصيد = الافتتاحي + ما قُبض − ما أُرجع
/// − المصاريف − دفعات الموردين − مسحوبات صاحب المحل ± التحويلات بين الصناديق.
/// </summary>
public static class Boxes
{
    public record Move(string Date, string Box, double Amount, string Text);

    public static List<Withdrawal> Withdrawals() => Docs.All<Withdrawal>("withdrawals");
    public static List<Transfer> Transfers() => Docs.All<Transfer>("transfers");

    public static void AddWithdrawal(double amount, string box, string date, string note) =>
        Docs.Put("withdrawals", Txt.Uid("wd"), new Withdrawal { Amount = amount, Box = box, Date = date, Note = note });

    public static void AddTransfer(double amount, string from, string to, string date, string note) =>
        Docs.Put("transfers", Txt.Uid("tr"), new Transfer { Amount = amount, From = from, To = to, Date = date, Note = note });

    static Dictionary<string, double> Opening()
    {
        try { return JsonNode.Parse(Store.Get("box_opening", "{}")) is JsonObject o ? o.ToDictionary(kv => kv.Key, kv => Txt.ParseMoney(kv.Value?.ToString())) : new(); }
        catch { return new(); }
    }

    public static double OpeningOf(string box) => Opening().GetValueOrDefault(box);

    public static void SetOpening(string box, double v)
    {
        var o = Opening();
        o[box] = v;
        Store.Set("box_opening", new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode)kv.Value))).ToJsonString());
    }

    public static List<string> Names() => K.PayMethods.Concat(Store.Expenses.Select(e => e.Box)).Concat(Store.SupplierTx.Select(t => t.Box))
        .Concat(Withdrawals().Select(w => w.Box)).Concat(Transfers().SelectMany(t => new[] { t.From, t.To })).Where(b => !string.IsNullOrEmpty(b)).Distinct().ToList();

    /// <summary>كل حركات الصناديق (للكشف والرصيد)</summary>
    public static List<Move> Moves()
    {
        var m = new List<Move>();
        foreach (var o in Store.Orders)
        {
            foreach (var p in o.PaymentHistory.Where(p => !p.IsNonCash))
                m.Add(new(p.Date, p.Method, p.Amount, (p.IsRefund ? "إرجاع لزبون " : "دفعة ") + $"{o.RefNo} {o.CustomerName}"));
            double legacy = o.Paid - o.PaymentHistory.Sum(p => p.Amount);
            if (legacy > 0.001) m.Add(new(o.DateReceived, Lists.Cash, legacy, $"دفعة سابقة {o.RefNo}"));
        }
        foreach (var e in Store.Expenses) m.Add(new(e.Date, e.Box, -e.Amount, "مصروف: " + e.Description));
        foreach (var t in Store.SupplierTx.Where(t => t.Type == "payment")) m.Add(new(t.Date, t.Box, -t.Amount, "دفعة للمورد " + t.Supplier));
        foreach (var w in Withdrawals()) m.Add(new(w.Date, w.Box, -w.Amount, "مسحوبات صاحب المحل" + (w.Note != "" ? ": " + w.Note : "")));
        foreach (var t in Transfers())
        {
            m.Add(new(t.Date, t.From, -t.Amount, $"تحويل إلى {t.To}" + (t.Note != "" ? " — " + t.Note : "")));
            m.Add(new(t.Date, t.To, t.Amount, $"تحويل من {t.From}" + (t.Note != "" ? " — " + t.Note : "")));
        }
        return m.OrderBy(x => x.Date, StringComparer.Ordinal).ToList();
    }

    public static Dictionary<string, double> Balances()
    {
        var b = Names().ToDictionary(n => n, OpeningOf);
        foreach (var mv in Moves()) b[mv.Box] = b.GetValueOrDefault(mv.Box) + mv.Amount;
        return b;
    }

    public static double WithdrawnIn(string a, string c) => Withdrawals().Where(w => Calc.InRange(w.Date, a, c)).Sum(w => w.Amount);
}

// ============================== منح الرصيد (مكافآت الإحالة، رصيد يدوي) ==============================
public class Grant { public string Id, CustomerKey, Name = "", Date, Note = "", OrderId = ""; public double Amount; }

public static class Grants
{
    public static List<Grant> All() => Docs.All<Grant>("credit_grants");
    public static List<Grant> For(string key) => All().Where(g => g.CustomerKey == key).ToList();
    public static void Add(string key, string name, double amount, string note, string orderId = "") =>
        Docs.Put("credit_grants", Txt.Uid("gr"), new Grant { CustomerKey = key, Name = name, Amount = amount, Date = Txt.Today, Note = note, OrderId = orderId });
}

// ============================== نقاط الولاء ==============================
/// <summary>
/// نقطة لكل مبلغ يدفعه الزبون على الأجهزة المسلّمة (افتراضياً نقطة لكل 1,000)، وقيمة النقطة عند الاستبدال
/// (افتراضياً 25). الاستبدال دفعة بطريقة «نقاط الولاء» لا تدخل الصندوق.
/// </summary>
public static class Loyalty
{
    static JsonObject Cfg() { try { return JsonNode.Parse(Store.Get("loyalty", "{}")) as JsonObject ?? new(); } catch { return new(); } }
    public static bool On => Cfg()["on"]?.ToString() == "true";
    public static double PerPoint => Txt.ParseMoney(Cfg()["per"]?.ToString()) is var v && v > 0 ? v : 1000;
    public static double PointValue => Txt.ParseMoney(Cfg()["value"]?.ToString()) is var v && v > 0 ? v : 25;
    public static int MinRedeem => Txt.OptInt(Cfg()["min"]?.ToString()) is int m && m > 0 ? m : 100;

    public static void Save(bool on, double per, double value, int min) =>
        Store.Set("loyalty", new JsonObject { ["on"] = on ? "true" : "false", ["per"] = per, ["value"] = value, ["min"] = min }.ToJsonString());

    public static int Earned(string key) => (int)Math.Floor(Store.Orders.Where(o => o.Status == K.Done && o.AccountId == null && Calc.CustomerKey(o) == key)
        .Sum(o => o.PaymentHistory.Where(p => !p.IsNonCash).Sum(p => p.Amount) + Math.Max(0, o.Paid - o.PaymentHistory.Sum(p => p.Amount))) / PerPoint);

    public static int Used(string key) => (int)Math.Round(Store.Orders.Where(o => Calc.CustomerKey(o) == key).SelectMany(o => o.PaymentHistory).Where(p => p.IsPoints).Sum(p => p.Amount) / PointValue);

    public static int Balance(string key) => On ? Math.Max(0, Earned(key) - Used(key)) : 0;
    public static double Worth(int points) => points * PointValue;
}

// ============================== الإحالة ==============================
public static class Referral
{
    public static double Reward => Txt.ParseMoney(Store.Get("referral_reward", "0"));

    /// <summary>عند تسليم أول جهاز لزبون أحاله زبون آخر: يُمنح المحيل رصيداً (مرة واحدة لكل زبون جديد)</summary>
    public static void OnDelivered(Order o)
    {
        if (Reward <= 0 || o.X.ReferredBy == "" || o.Status != K.Done) return;
        var newKey = Calc.CustomerKey(o);
        if (Store.Orders.Any(x => x.Id != o.Id && Calc.CustomerKey(x) == newKey && x.Status == K.Done && string.CompareOrdinal(x.DateDelivered, o.DateDelivered) < 0)) return;
        var refKey = Calc.CustomerKey(o.X.ReferredBy, o.X.ReferredPhone);
        if (refKey == newKey || Grants.All().Any(g => g.OrderId == o.Id)) return;
        Grants.Add(refKey, o.X.ReferredBy, Reward, $"مكافأة إحالة: {o.CustomerName}", o.Id);
    }

    public static List<(string Referrer, int Count, double Rewards)> Report() =>
        Store.Orders.Where(o => o.X.ReferredBy != "").GroupBy(o => Calc.CustomerKey(o.X.ReferredBy, o.X.ReferredPhone))
            .Select(g => (g.First().X.ReferredBy, g.Select(Calc.CustomerKey).Distinct().Count(), Grants.For(g.Key).Where(x => x.OrderId != "").Sum(x => x.Amount)))
            .OrderByDescending(x => x.Item2).ToList();
}

// ============================== أرقام الفواتير المتسلسلة ==============================
public static class InvoiceNumbers
{
    /// <summary>يعطي الطلب رقماً متسلسلاً إن لم يكن له (لا يُعاد استعمال رقم أبداً)</summary>
    public static Order Ensure(Order o)
    {
        if (o.X.InvoiceNo != "") return o;
        int next = (int.TryParse(Store.Get("invoice_seq", "0"), out var n) ? n : 0) + 1;
        Store.Set("invoice_seq", next.ToString());
        var c = o.Clone();
        c.X.InvoiceNo = (Store.Get("invoice_prefix", "INV-") is var pfx && pfx != "" ? pfx : "INV-") + next.ToString("000000");
        Store.SaveOrder(c);
        return c;
    }
}

// ============================== التكلفة الحقيقية لكل إصلاح ==============================
public static class TrueCost
{
    public record Row(Order O, double Hours, double Overhead, double RealProfit);
    public class Result
    {
        public double Overhead, Hours, RatePerHour, PerOrder, Consumables;
        public List<Row> Rows = new();
    }

    /// <summary>
    /// المصاريف الثابتة للفترة (الإيجار، الرواتب، الكهرباء...) توزَّع على الأجهزة المسلّمة حسب ساعات العمل الفعلية؛
    /// الجهاز بلا توقيت يأخذ حصة متساوية.
    /// </summary>
    public static Result For(string a, string b)
    {
        var s = Calc.Summarize(a, b);
        var r = new Result { Overhead = s.Expenses };
        var hours = s.Active.ToDictionary(o => o.Id, o => WorkTimer.Total(o).TotalHours);
        r.Hours = hours.Values.Sum();
        var timed = s.Active.Where(o => hours[o.Id] > 0.05).ToList();
        var untimed = s.Active.Except(timed).ToList();
        double timedShare = s.Active.Count == 0 ? 0 : r.Overhead * timed.Count / s.Active.Count;
        r.RatePerHour = r.Hours > 0 ? timedShare / r.Hours : 0;
        r.PerOrder = untimed.Count > 0 ? (r.Overhead - timedShare) / untimed.Count : 0;
        var cons = Store.Inventory.Where(i => i.Consumable).Select(i => i.Id).ToHashSet();
        r.Consumables = s.Active.SelectMany(o => o.Parts).Where(p => p.InventoryItemId != null && cons.Contains(p.InventoryItemId)).Sum(p => p.Cost);
        foreach (var o in s.Active)
        {
            double oh = hours[o.Id] > 0.05 ? hours[o.Id] * r.RatePerHour : r.PerOrder;
            r.Rows.Add(new Row(o, hours[o.Id], oh, Calc.ProfitOf(o) - oh));
        }
        return r;
    }
}

// ============================== التوزيع التلقائي على الفنيين ==============================
public static class AutoAssign
{
    public static bool On => Store.Flag("auto_assign");

    /// <summary>الفني الذي يتقن هذا العطل (أو الكل) وعنده أقل أجهزة مفتوحة</summary>
    public static string Pick(string issueType)
    {
        var techs = Techs.All;
        if (techs.Count == 0) return "";
        var fit = techs.Where(t => t.Skills.Count == 0 || t.Skills.Contains(issueType)).ToList();
        if (fit.Count == 0) fit = techs;
        return fit.OrderBy(t => Store.Orders.Count(o => Calc.IsOpen(o) && Txt.Fold(o.Technician) == Txt.Fold(t.Name))).ThenBy(t => t.Name).First().Name;
    }
}

// ============================== الضمان الممتد ==============================
public static class ExtWarranty
{
    /// <summary>الخيارات من الإعدادات: سطر لكل خيار «6 أشهر = 20000»</summary>
    public static List<(string Name, double Price)> Options()
    {
        var raw = Store.Get("ext_warranties", "3 أشهر = 10000\n6 أشهر = 20000");
        return raw.Split('\n').Select(l => l.Split('=')).Where(p => p.Length == 2 && p[0].Trim() != "")
            .Select(p => (p[0].Trim(), Txt.ParseMoney(p[1]))).Where(x => x.Item2 >= 0).ToList();
    }

    public static double SoldIn(string a, string b) => Calc.Summarize(a, b).Active.Sum(o => o.X.ExtWarrantyFee);
}

// ============================== اقتراح إضافات عند التسليم ==============================
public static class Upsell
{
    public static List<InvItem> For(Order o) => Store.Inventory.Where(i => i.Upsell && (i.Qty == null || i.Qty > 0)
        && (i.UpsellFor == "" || i.UpsellFor.Split(new[] { '،', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Contains(o.IssueType))
        && (i.Compatible == "" || Txt.Matches(o.Device, Txt.Fold(i.Compatible)) || Txt.Matches(i.Compatible, Txt.Fold(o.Device))))
        .OrderByDescending(i => i.Compatible != "").Take(8).ToList();

    /// <summary>إضافة قطعة مبيعة للطلب: بند بسعرها + قطعة بتكلفتها (وتُخصم من المخزون)</summary>
    public static Order Add(Order o, InvItem i)
    {
        var n = o.Clone();
        if (n.X.Items.Count == 0 && n.Price > 0) n.X.Items.Add(new JobItem { Desc = "الإصلاح", Price = n.Price });
        n.X.Items.Add(new JobItem { Desc = i.Name + (i.Compatible != "" ? " " + i.Compatible : ""), Price = i.SalePrice });
        n.Parts.Add(new Part { Name = i.Name, Supplier = i.Supplier, Cost = i.Cost, InventoryItemId = i.Id });
        n.Price += i.SalePrice;
        n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
        n.UpdatedAt = Txt.Now;
        Store.SaveOrder(n);
        Calc.ApplyStockChange(o.Parts, n.Parts);
        return n;
    }
}

// ============================== أجهزة القطع (سكراب) ==============================
public class ScrapDevice { public string Id, Device = "", Imei = "", Source = "", Date, Note = "", FromOrder = ""; public double Cost; public List<Harvest> Parts = new(); }
public class Harvest { public string Name = "", InvId = "", Date; public double Value; }

public static class Scrap
{
    public static readonly string[] Sources = { "تركه صاحبه", "اشتريته للقطع", "جهاز المحل التالف", "أخرى" };
    public static List<ScrapDevice> All() => Docs.All<ScrapDevice>("scrap").OrderByDescending(s => s.Date, StringComparer.Ordinal).ToList();
    public static void Save(ScrapDevice s) { if (string.IsNullOrEmpty(s.Id)) s.Id = Txt.Uid("sc"); Docs.Put("scrap", s.Id, s); }
    public static void Delete(ScrapDevice s) => Docs.Del("scrap", s.Id);

    /// <summary>فكّ قطعة من الجهاز: تدخل المخزون (قطعة موجودة +1 أو صنف جديد) بالقيمة المقدّرة</summary>
    public static void Harvest(ScrapDevice s, string name, string model, string category, double value)
    {
        var item = Store.Inventory.FirstOrDefault(i => Txt.Fold(i.Name) == Txt.Fold(name) && Txt.Fold(i.Compatible) == Txt.Fold(model));
        if (item == null)
        {
            item = new InvItem { Id = Txt.Uid("inv"), Name = name, Compatible = model, Category = category, Cost = value, SalePrice = 0, Qty = 0, Supplier = "من أجهزة القطع", UpdatedAt = Txt.Now, Notes = "مفكوكة من جهاز" };
        }
        if (item.Qty != null) item.Qty++;
        item.UpdatedAt = Txt.Now;
        Store.SaveInv(item);
        s.Parts.Add(new Harvest { Name = name, InvId = item.Id, Value = value, Date = Txt.Today });
        Save(s);
    }
}

// ============================== سجل أدوات الورشة ==============================
public class ToolItem { public string Id, Name = "", Bought = "", LastService = "", Note = ""; public double Price; public int IntervalDays; public List<string> Log = new(); }

public static class Tools
{
    public static List<ToolItem> All() => Docs.All<ToolItem>("tools").OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList();
    public static void Save(ToolItem t) { if (string.IsNullOrEmpty(t.Id)) t.Id = Txt.Uid("tl"); Docs.Put("tools", t.Id, t); }
    public static void Delete(ToolItem t) => Docs.Del("tools", t.Id);
    public static string NextService(ToolItem t) => t.IntervalDays > 0 && Txt.ParseDate(t.LastService != "" ? t.LastService : t.Bought) is DateTime d ? Txt.Iso(d.AddDays(t.IntervalDays)) : "";
    public static List<ToolItem> Due() => All().Where(t => NextService(t) is var n && n != "" && string.CompareOrdinal(n, Txt.Iso(DateTime.Today.AddDays(3))) <= 0).ToList();
}

// ============================== رقم الدور ==============================
public static class Queue
{
    public static int Last => Store.Get("queue_day") == Txt.Today && int.TryParse(Store.Get("queue_last", "0"), out var n) ? n : 0;
    public static int Serving => Store.Get("queue_day") == Txt.Today && int.TryParse(Store.Get("queue_serving", "0"), out var n) ? n : 0;

    public static int Take()
    {
        int n = Last + 1;
        if (Store.Get("queue_day") != Txt.Today) { Store.Set("queue_day", Txt.Today); Store.Set("queue_serving", "0"); }
        Store.Set("queue_last", n.ToString());
        return n;
    }

    public static int Next()
    {
        int s = Math.Min(Serving + 1, Last);
        Store.Set("queue_serving", s.ToString());
        return s;
    }
}

// ============================== توافق القطع بين الموديلات ==============================
public static class Compat
{
    /// <summary>الموديلات المكتوبة في «الموديل المتوافق» مفصولة بفاصلة أو «/»</summary>
    public static List<string> Of(InvItem i) => (i.Compatible ?? "").Split(new[] { '،', ',', '/', '|', '+' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim()).Where(x => x != "").ToList();

    public static bool Fits(InvItem i, string device) => Of(i).Any(m => Txt.Fold(m) == Txt.Fold(device));
}

// ============================== استيراد قائمة أسعار المورد ==============================
public static class PriceImport
{
    /// <summary>قراءة أول ورقة من ملف xlsx (بدون مكتبات)</summary>
    public static List<List<string>> ReadXlsx(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is ZipArchiveEntry se)
            using (var s = se.Open())
                shared = XDocument.Load(s).Descendants(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToList();
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.First(e => e.FullName.StartsWith("xl/worksheets/sheet"));
        using var ss = sheet.Open();
        var rows = new List<List<string>>();
        foreach (var r in XDocument.Load(ss).Descendants(ns + "row"))
        {
            var row = new List<string>();
            foreach (var c in r.Elements(ns + "c"))
            {
                var refAttr = c.Attribute("r")?.Value ?? "";
                int col = 0;
                foreach (var ch in refAttr.TakeWhile(char.IsLetter)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                while (row.Count < col - 1) row.Add("");
                var v = c.Element(ns + "v")?.Value ?? string.Concat(c.Descendants(ns + "t").Select(t => t.Value));
                if (c.Attribute("t")?.Value == "s" && int.TryParse(v, out var si) && si < shared.Count) v = shared[si];
                row.Add(v);
            }
            rows.Add(row);
        }
        return rows;
    }

    public static List<List<string>> Read(string file) =>
        file.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(file) : Csv.Parse(File.ReadAllText(file, System.Text.Encoding.UTF8));

    public record Match(int Row, string Name, string Model, double NewCost, InvItem Item)
    {
        public double OldCost => Item?.Cost ?? 0;
        public double Margin => Item == null || Item.SalePrice <= 0 ? 0 : (Item.SalePrice - NewCost) / Item.SalePrice * 100;
    }

    /// <summary>مطابقة صفوف الملف مع قطع هذا المورد (بالاسم والموديل)</summary>
    public static List<Match> Matches(List<List<string>> rows, int nameCol, int modelCol, int costCol, string supplier, bool hasHeader)
    {
        var res = new List<Match>();
        var items = Store.Inventory.Where(i => supplier == "" || Txt.Fold(i.Supplier) == Txt.Fold(supplier)).ToList();
        for (int r = hasHeader ? 1 : 0; r < rows.Count; r++)
        {
            string C(int c) => c >= 0 && c < rows[r].Count ? rows[r][c].Trim() : "";
            var name = C(nameCol);
            var model = C(modelCol);
            double cost = Txt.ParseMoney(C(costCol));
            if (name == "" || cost <= 0) continue;
            var item = items.FirstOrDefault(i => Txt.Fold(i.Name) == Txt.Fold(name) && (model == "" || Compat.Of(i).Any(m => Txt.Fold(m) == Txt.Fold(model)) || Txt.Fold(i.Compatible) == Txt.Fold(model)))
                       ?? (model == "" ? null : items.FirstOrDefault(i => Txt.Fold(i.Name + " " + i.Compatible) == Txt.Fold(name + " " + model)));
            res.Add(new Match(r, name, model, cost, item));
        }
        return res;
    }
}
