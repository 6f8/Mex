namespace Workshop;

/// <summary>القيم المشتقة: الربح، المتبقي، الضمان، التأخير، الزبائن والديون، الموردون، التقارير، تقفيل اليوم</summary>
public static class Calc
{
    public static double PartsCost(Order o) => o.Parts.Sum(p => p.Cost);
    public static double ProfitOf(Order o) => o.Price - PartsCost(o);
    /// <summary>ما على الزبون: سعر الإصلاح، أو أجرة الفحص فقط إذا أُلغي الطلب</summary>
    public static double ChargeOf(Order o) => o.Status == K.Cancelled ? o.CheckFee : o.Price;
    public static double RemainingOf(Order o) => Math.Max(0, ChargeOf(o) - o.Paid);
    /// <summary>الإيراد في يوم إغلاق الطلب (تسليم أو إلغاء)</summary>
    public static double RevenueOf(Order o) => o.Status == K.Done ? o.Price : o.Status == K.Cancelled ? o.CheckFee : 0;
    /// <summary>الربح المعروض: الملغى = أجرة الفحص ناقص القطع</summary>
    public static double ShownProfit(Order o) => o.Status == K.Cancelled ? o.CheckFee - PartsCost(o) : ProfitOf(o);
    public static int StatusDays(Order o) => Txt.DaysBetween(Txt.Cut10(o.StatusAt ?? o.UpdatedAt ?? o.CreatedAt ?? ""), Txt.Today);

    // ---------- الضمان ----------
    public static int WarrantyDays(string w)
    {
        var t = Txt.LatinDigits(w ?? "");
        if (t == "" || t.Contains("بدون")) return 0;
        // مثل parseInt: الأرقام في بداية النص فقط («7 أيام» ← 7، «شهر واحد» ← 1)
        var digits = new string(t.TrimStart().TakeWhile(char.IsAsciiDigit).ToArray());
        bool dual = t.Contains("شهران") || t.Contains("شهرين") || t.Contains("أسبوعان") || t.Contains("اسبوعان") || t.Contains("أسبوعين") || t.Contains("اسبوعين")
                    || t.Contains("سنتان") || t.Contains("سنتين") || t.Contains("يومان") || t.Contains("يومين");
        int n = int.TryParse(digits, out var v) ? v : dual ? 2 : (t.Contains("شهر") || t.Contains("أسبوع") || t.Contains("اسبوع") || t.Contains("سنة") || t.Contains("يوم") ? 1 : 0);
        if (t.Contains("سن") || t.Contains("عام")) return n * 365;
        if (t.Contains("شهر") || t.Contains("أشهر")) return n * 30;
        if (t.Contains("أسبوع") || t.Contains("اسبوع") || t.Contains("أسابيع")) return n * 7;
        return n;
    }

    public static string WarrantyEnd(Order o)
    {
        if (o.Status != K.Done || o.DateDelivered == "") return "";
        int days = WarrantyDays(o.Warranty);
        if (days == 0 || Txt.ParseDate(o.DateDelivered) is not DateTime d) return "";
        return Txt.Iso(d.AddDays(days));
    }

    public static bool InWarranty(Order o, string on = null)
    {
        var e = WarrantyEnd(o);
        return e != "" && string.CompareOrdinal(on ?? Txt.Today, e) <= 0;
    }

    public static bool IsOpen(Order o) => K.OpenStatuses.Contains(o.Status);
    public static bool IsLate(Order o) => K.WorkStatuses.Contains(o.Status) && o.DateEstimated != "" && string.CompareOrdinal(o.DateEstimated, Txt.Today) < 0;
    public static int LateDays(Order o) => Txt.DaysBetween(o.DateEstimated, Txt.Today);
    /// <summary>أيام انتظار جهاز جاهز لم يُستلم</summary>
    public static int ReadyDays(Order o) => o.Status == K.Ready ? Txt.DaysBetween(Txt.Cut10(o.ReadyAt ?? o.UpdatedAt ?? o.DateReceived ?? ""), Txt.Today) : 0;

    /// <summary>يوم دخول الطلب في الحسابات: يوم التسليم للمسلَّم، ويوم الإلغاء للملغى. المفتوح ليس إيرادًا بعد</summary>
    public static string ClosedDate(Order o)
    {
        if (o.Status == K.Done) return o.DateDelivered != "" ? o.DateDelivered : Txt.Cut10(o.CompletedAt ?? "") is var c && c != "" ? c : o.DateReceived;
        if (o.Status == K.Cancelled) return o.DateDelivered != "" ? o.DateDelivered : Txt.Cut10(o.CancelledAt ?? o.UpdatedAt ?? "") is var c2 && c2 != "" ? c2 : o.DateReceived;
        return "";
    }

    // ---------- مدة العمل ----------
    public static TimeSpan? Duration(Order o)
    {
        var start = Txt.ParseTime(o.StartedAt ?? o.CreatedAt);
        if (start == null) return null;
        DateTime end = o.CompletedAt != null ? Txt.ParseTime(o.CompletedAt) ?? DateTime.Now
                     : o.Status == K.Cancelled ? Txt.ParseTime(o.CancelledAt ?? o.UpdatedAt) ?? DateTime.Now : DateTime.Now;
        return end - start.Value;
    }

    public static string FmtDuration(TimeSpan? t)
    {
        if (t == null || t.Value.TotalMilliseconds < 0) return "—";
        long m = (long)Math.Floor(t.Value.TotalMinutes);
        if (m < 1) return "أقل من دقيقة";
        long d = m / 1440, h = m % 1440 / 60, mm = m % 60;
        if (d > 0) return d + " يوم" + (h > 0 && d < 3 ? " و" + h + " ساعة" : "");
        if (h > 0) return h + " ساعة" + (mm > 0 ? " و" + mm + " د" : "");
        return mm + " دقيقة";
    }

    public static string DurationText(Order o) => o.Status == K.Cancelled ? "—" : FmtDuration(Duration(o));

    // ---------- الدفعات ----------
    /// <summary>المقبوض فعلًا في فترة: الدفعات المؤرخة، وأي مبلغ قديم بلا تاريخ يُنسب ليوم الاستلام</summary>
    public static double PaymentsInRange(Order o, Func<string, bool> test)
    {
        double sum = o.PaymentHistory.Where(p => test(p.Date)).Sum(p => p.Amount);
        double undated = o.Paid - o.PaymentHistory.Sum(p => p.Amount);
        if (undated > 0 && test(o.DateReceived)) sum += undated;
        return sum;
    }

    public static Dictionary<string, double> PaymentsByMethod(Func<string, bool> test)
    {
        var m = K.PayMethods.ToDictionary(x => x, _ => 0.0);
        foreach (var o in Store.Orders)
        {
            foreach (var p in o.PaymentHistory.Where(p => test(p.Date)))
                m[p.Method ?? Lists.Cash] = m.GetValueOrDefault(p.Method ?? Lists.Cash) + p.Amount;
            double undated = o.Paid - o.PaymentHistory.Sum(p => p.Amount);
            if (undated > 0 && test(o.DateReceived)) m[Lists.Cash] = m.GetValueOrDefault(Lists.Cash) + undated;
        }
        return m;
    }

    // ---------- IMEI ----------
    /// <summary>IMEI: 15 رقمًا برقم تحقق Luhn</summary>
    public static bool ImeiValid(string v)
    {
        var d = new string((v ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d.Length != 15) return false;
        int sum = 0;
        for (int i = 0; i < 15; i++) { int n = d[i] - '0'; if (i % 2 == 1) { n *= 2; if (n > 9) n -= 9; } sum += n; }
        return sum % 10 == 0;
    }

    // ---------- الزبائن ----------
    public static string CustomerKey(string name, string phone)
    {
        var p = Txt.NormPhoneKey(phone);
        return p.Length >= 6 ? "p:" + p : "n:" + Txt.Fold(name);
    }
    public static string CustomerKey(Order o) => CustomerKey(o.CustomerName, o.Phone);

    public class Customer
    {
        public string Key, Name, Phone, Latest = "", Last = "", Oldest = "";
        public List<Order> Orders = new(), Unpaid = new(), Pending = new();
        public double Spent, Profit, Debt, Expected;
    }

    public static List<Customer> GetCustomers()
    {
        var map = new Dictionary<string, Customer>();
        foreach (var o in Store.Orders)
        {
            var key = CustomerKey(o);
            if (!map.TryGetValue(key, out var c)) map[key] = c = new Customer { Key = key, Name = o.CustomerName, Phone = o.Phone };
            c.Orders.Add(o);
            if (string.CompareOrdinal(o.CreatedAt ?? "", c.Latest) >= 0) { c.Latest = o.CreatedAt ?? ""; c.Name = o.CustomerName; if (o.Phone != "") c.Phone = o.Phone; }
        }
        foreach (var c in map.Values)
        {
            var active = c.Orders.Where(o => o.Status != K.Cancelled).ToList();
            c.Spent = active.Sum(o => o.Price);
            c.Profit = active.Sum(ProfitOf);
            c.Last = c.Orders.Select(o => o.DateReceived).DefaultIfEmpty("").Max(StringComparer.Ordinal);
            // الدين = جهاز سُلّم ولم يُدفع كاملًا. المبلغ على جهاز ما زال في الورشة «متوقع» وليس دينًا
            // طلبات التجار والشركات تُسدَّد من حساباتهم فلا تُحسب ديناً على الزبون
            c.Unpaid = c.Orders.Where(o => o.AccountId == null && (o.Status == K.Done || o.Status == K.Cancelled) && RemainingOf(o) > 0)
                .OrderBy(ClosedDate, StringComparer.Ordinal).ToList();
            c.Pending = active.Where(o => o.AccountId == null && IsOpen(o) && RemainingOf(o) > 0).OrderBy(o => o.DateReceived, StringComparer.Ordinal).ToList();
            c.Debt = c.Unpaid.Sum(RemainingOf);
            c.Expected = c.Pending.Sum(RemainingOf);
            c.Oldest = c.Unpaid.Count > 0 ? ClosedDate(c.Unpaid[0]) : "";
        }
        return map.Values.ToList();
    }

    public static List<Customer> GetDebts() => GetCustomers().Where(c => c.Debt > 0).OrderBy(c => c.Oldest, StringComparer.Ordinal).ToList();

    /// <summary>جهاز رجع للورشة: آخر طلب مُسلَّم لنفس IMEI، أو لنفس الجهاز ونفس الزبون</summary>
    public static Order FindPreviousRepair(string id, string name, string phone, string device, string imei)
    {
        var key = CustomerKey(name, phone);
        var dev = Txt.Fold(device);
        var im = (imei ?? "").Replace(" ", "");
        return Store.Orders.Where(o => o.Id != id && o.Status == K.Done &&
                ((im != "" && o.Imei != "" && o.Imei == im) || ((im == "" || o.Imei == "") && dev != "" && Txt.Fold(o.Device) == dev && CustomerKey(o) == key)))
            .OrderByDescending(o => o.DateDelivered, StringComparer.Ordinal).FirstOrDefault();
    }

    // ---------- المخزون ----------
    public static Dictionary<string, int> InvUsage(IEnumerable<Part> parts)
    {
        var m = new Dictionary<string, int>();
        foreach (var p in parts ?? Enumerable.Empty<Part>())
            if (p.InventoryItemId != null) m[p.InventoryItemId] = m.GetValueOrDefault(p.InventoryItemId) + 1;
        return m;
    }

    /// <summary>تحريك الكميات بفرق قطع الطلب القديمة والجديدة. يعيد أسماء القطع التي نفدت</summary>
    public static List<string> ApplyStockChange(IEnumerable<Part> oldParts, IEnumerable<Part> newParts)
    {
        var o = InvUsage(oldParts); var n = InvUsage(newParts);
        var ranOut = new List<string>();
        var changed = new List<InvItem>();
        foreach (var id in o.Keys.Union(n.Keys))
        {
            int d = n.GetValueOrDefault(id) - o.GetValueOrDefault(id);
            if (d == 0) continue;
            var it = Store.Inventory.FirstOrDefault(i => i.Id == id);
            if (it == null || it.Qty == null) continue;
            it.Qty -= d;
            changed.Add(it);
            if (d > 0 && it.Qty <= 0) ranOut.Add(it.Name + (it.Compatible != "" ? " (" + it.Compatible + ")" : ""));
        }
        if (changed.Count > 0) Store.SaveInvMany(changed);
        return ranOut;
    }

    public static int LowLimit(InvItem i) => i.MinQty ?? 1;
    /// <summary>none: غير متابَعة، out: نفدت، low: منخفضة، ok</summary>
    public static string StockState(InvItem i) => i.Qty == null ? "none" : i.Qty <= 0 ? "out" : i.Qty <= LowLimit(i) ? "low" : "ok";

    /// <summary>تجميع الأسماء المتشابهة («iPhone 13» = «iphone  13»)، والعنوان هو الكتابة الأكثر استعمالًا</summary>
    public static List<(string Key, string Label, List<T> Items)> GroupByName<T>(IEnumerable<T> list, Func<T, string> nameOf)
    {
        var groups = new Dictionary<string, (List<T> Items, Dictionary<string, int> Spell)>();
        var order = new List<string>();
        foreach (var item in list)
        {
            var raw = Txt.Str(nameOf(item));
            var k = Txt.Fold(raw);
            if (k == "") continue;
            if (!groups.TryGetValue(k, out var g)) { g = (new List<T>(), new Dictionary<string, int>()); groups[k] = g; order.Add(k); }
            g.Items.Add(item);
            g.Spell[raw] = g.Spell.GetValueOrDefault(raw) + 1;
        }
        static int Mixed(string t) => t.Any(char.IsLower) && t.Any(char.IsUpper) ? 1 : 0;
        return order.Select(k =>
        {
            var g = groups[k];
            var label = g.Spell.OrderByDescending(x => x.Value).ThenByDescending(x => Mixed(x.Key)).First().Key;
            return (k, label, g.Items);
        }).ToList();
    }

    // ---------- الموردون ----------
    public class SupplierBalance
    {
        public string Key, Name, Last = "";
        public List<SupplierTx> Items;
        public double Purchases, Payments, Returns;
        public double Balance => Purchases - Payments - Returns;
    }

    /// <summary>أثر الحركة على ما تدين به للمورد: الشراء يزيده، والدفعة والمرتجع ينقصانه</summary>
    public static double StxSign(SupplierTx t) => t.Type == "purchase" ? t.Amount : -t.Amount;
    public static string StxLabel(SupplierTx t) => t.Type switch { "payment" => "دفعة", "return" => "مرتجع قطعة معيبة", _ => "شراء" };

    public static List<SupplierBalance> SupplierBalances() =>
        GroupByName(Store.SupplierTx, t => t.Supplier).Select(g => new SupplierBalance
        {
            Key = g.Key, Name = g.Label, Items = g.Items,
            Purchases = g.Items.Where(t => t.Type == "purchase").Sum(t => t.Amount),
            Payments = g.Items.Where(t => t.Type == "payment").Sum(t => t.Amount),
            Returns = g.Items.Where(t => t.Type == "return").Sum(t => t.Amount),
            Last = g.Items.Select(t => t.Date).DefaultIfEmpty("").Max(StringComparer.Ordinal)
        }).ToList();

    // ---------- التقارير ----------
    public class Summary
    {
        public List<Order> List, Active, Received;
        public List<Expense> Exps;
        public double Revenue, Fees, Parts, Expenses, Loss, Profit, Cash, Debt, Refunds;
    }

    public static bool InRange(string d, string a, string b) => !string.IsNullOrEmpty(d) && string.CompareOrdinal(d, a) >= 0 && string.CompareOrdinal(d, b) <= 0;

    /// <summary>الحسابات تتبع يوم الإغلاق: المسلَّم بيوم تسليمه، والملغى بيوم إلغائه</summary>
    public static Summary Summarize(string a, string b)
    {
        bool inR(string d) => InRange(d, a, b);
        var list = Store.Orders.Where(o => (o.Status == K.Done || o.Status == K.Cancelled) && inR(ClosedDate(o))).ToList();
        var active = list.Where(o => o.Status == K.Done).ToList();
        var exps = Store.Expenses.Where(e => inR(e.Date)).ToList();
        var s = new Summary
        {
            List = list, Active = active, Received = Store.Orders.Where(o => inR(o.DateReceived)).ToList(), Exps = exps,
            Revenue = list.Sum(RevenueOf), Parts = active.Sum(PartsCost),
            Fees = list.Where(o => o.Status == K.Cancelled).Sum(RevenueOf),
            Expenses = exps.Sum(e => e.Amount),
            Loss = list.Where(o => o.Status == K.Cancelled).Sum(PartsCost),
            Cash = Store.Orders.Sum(o => PaymentsInRange(o, inR)),
            Debt = list.Where(o => o.AccountId == null).Sum(RemainingOf),
            Refunds = -Store.Orders.SelectMany(o => o.PaymentHistory).Where(p => p.IsRefund && inR(p.Date)).Sum(p => p.Amount),
        };
        s.Profit = s.Revenue - s.Parts - s.Expenses - s.Loss;
        return s;
    }

    // ---------- تقفيل اليوم ----------
    public class CloseDay
    {
        public string D;
        public Dictionary<string, double> Methods;
        public double CashIn, ExpTotal, SupPaid, Revenue, Parts, Loss, NewDebt, Profit, Drawer, RefundTotal;
        public List<Expense> Exps;
        public List<Order> Received, Delivered, Cancelled;
        public List<(Order O, Payment P)> Payments, Refunds;
    }

    public static CloseDay Close(string d)
    {
        bool on(string x) => x == d;
        var c = new CloseDay { D = d, Methods = PaymentsByMethod(on) };
        c.CashIn = c.Methods.Values.Sum();
        c.Exps = Store.Expenses.Where(e => e.Date == d).ToList();
        c.ExpTotal = c.Exps.Sum(e => e.Amount);
        c.SupPaid = Store.SupplierTx.Where(t => t.Type == "payment" && t.Date == d).Sum(t => t.Amount);
        c.Received = Store.Orders.Where(o => o.DateReceived == d).ToList();
        c.Delivered = Store.Orders.Where(o => o.Status == K.Done && ClosedDate(o) == d).ToList();
        c.Cancelled = Store.Orders.Where(o => o.Status == K.Cancelled && ClosedDate(o) == d).ToList();
        var closed = c.Delivered.Concat(c.Cancelled).ToList();
        c.Revenue = closed.Sum(RevenueOf);
        c.Parts = c.Delivered.Sum(PartsCost);
        c.Loss = c.Cancelled.Sum(PartsCost);
        c.NewDebt = closed.Where(o => o.AccountId == null).Sum(RemainingOf);
        var all = Store.Orders.SelectMany(o => o.PaymentHistory.Where(p => p.Date == d).Select(p => (o, p))).ToList();
        c.Payments = all.Where(x => !x.p.IsRefund).OrderByDescending(x => x.p.Amount).ToList();
        c.Refunds = all.Where(x => x.p.IsRefund).ToList();
        c.RefundTotal = -c.Refunds.Sum(x => x.P.Amount);
        c.Profit = c.Revenue - c.Parts - c.ExpTotal - c.Loss;
        c.Drawer = c.Methods.GetValueOrDefault(Lists.Cash) - c.ExpTotal - c.SupPaid;
        return c;
    }

    public static string CloseText(CloseDay c)
    {
        var L = new List<string> { $"تقفيل {Txt.FmtDate(c.D)} — {Store.ShopName}", "", $"المقبوض: {Txt.Money(c.CashIn + c.RefundTotal)}" };
        if (c.RefundTotal > 0) { L.Add($"مُرجَع للزبائن: {Txt.Money(c.RefundTotal)}"); L.Add($"صافي المقبوض: {Txt.Money(c.CashIn)}"); }
        L.AddRange(c.Methods.Where(x => x.Value != 0).Select(x => $"  - {x.Key}: {Txt.Money(x.Value)}"));
        L.Add($"المصاريف: {Txt.Money(c.ExpTotal)}");
        if (c.SupPaid > 0) L.Add($"دفعات الموردين: {Txt.Money(c.SupPaid)}");
        L.Add($"صافي حركة النقد: {Txt.Money(c.Drawer)}");
        L.Add("");
        L.Add($"أجهزة مستلمة: {c.Received.Count} — مسلّمة: {c.Delivered.Count} — ملغاة: {c.Cancelled.Count}");
        L.Add($"الإيراد: {Txt.Money(c.Revenue)} — صافي الربح: {Txt.Money(c.Profit)}");
        if (c.NewDebt > 0) L.Add($"ديون جديدة: {Txt.Money(c.NewDebt)}");
        return string.Join("\n", L);
    }

    // ---------- الطلبات ----------
    public static Order Find(string id) => id == null ? null : Store.Orders.FirstOrDefault(o => o.Id == id);

    /// <summary>كل نصوص الطلب للبحث (الزبون، الهاتف، الجهاز، المرجع، IMEI، العطل، الملاحظات، القطع والموردون)</summary>
    public static string Haystack(Order o) => string.Join(" ", new[] { o.CustomerName, o.Phone, o.Device, o.RefNo, o.Imei, o.Issue, o.IssueType, o.Notes, o.Technician, Accounts.NameOf(o) }
        .Concat(o.Parts.SelectMany(p => new[] { p.Name, p.Supplier })));

    public static HashSet<string> UsedRefs() => Store.Orders.Concat(Store.Trash).Select(o => o.RefNo).ToHashSet();
}
