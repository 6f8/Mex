using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workshop;

// ============================== مستحقات الموردين حسب القِدم والموعد ==============================
public static class SupplierDues
{
    public record Due(string Supplier, SupplierTx Tx, double Left, string DueDate)
    {
        public int Age => Txt.DaysBetween(Tx.Date, Txt.Today);
        public bool Overdue => DueDate != "" && string.CompareOrdinal(DueDate, Txt.Today) < 0;
        public bool Soon(int days = 7) => DueDate != "" && !Overdue && Txt.DaysBetween(Txt.Today, DueDate) <= days;
    }

    /// <summary>ما لم يُسدَّد من كل مشترى: الدفعات والمرتجعات تُسدّد الأقدم أولاً</summary>
    public static List<Due> Open()
    {
        var res = new List<Due>();
        foreach (var g in Calc.SupplierBalances())
        {
            double credit = g.Payments + g.Returns;
            foreach (var t in g.Items.Where(t => t.Type == "purchase").OrderBy(t => t.Date, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal))
            {
                double used = Math.Min(credit, t.Amount);
                credit -= used;
                double left = t.Amount - used;
                if (left > 0.001) res.Add(new Due(g.Name, t, left, t.DueDate ?? ""));
            }
        }
        return res;
    }

    public static List<Due> Alerts() => Open().Where(d => d.Overdue || d.Soon(3)).OrderBy(d => d.DueDate, StringComparer.Ordinal).ToList();
}

// ============================== أعمار ديون الزبائن ==============================
public static class DebtAging
{
    public static readonly (string Title, int From, int To)[] Buckets = { ("أقل من شهر", 0, 29), ("من شهر إلى 3 أشهر", 30, 89), ("أكثر من 3 أشهر", 90, int.MaxValue) };

    public static double[] Totals()
    {
        var t = new double[Buckets.Length];
        foreach (var o in Calc.GetDebts().SelectMany(c => c.Unpaid))
        {
            int d = Txt.DaysBetween(Calc.ClosedDate(o), Txt.Today);
            for (int i = 0; i < Buckets.Length; i++) if (d >= Buckets[i].From && d <= Buckets[i].To) t[i] += Calc.RemainingOf(o);
        }
        return t;
    }
}

// ============================== توقع السيولة ==============================
public static class Forecast
{
    public class Result
    {
        public double InWorkshop, CustomerDebts, Dealers, SuppliersDue, SuppliersNoDate, Expenses, Salaries;
        public double In => InWorkshop + CustomerDebts + Dealers;
        public double Out => SuppliersDue + SuppliersNoDate + Expenses + Salaries;
        public double Net => In - Out;
    }

    /// <summary>الثلاثون يوماً القادمة: ما يُتوقع قبضه مقابل ما يجب دفعه</summary>
    public static Result Next30()
    {
        var r = new Result();
        var limit = Txt.Iso(DateTime.Today.AddDays(30));
        r.InWorkshop = Store.Orders.Where(o => o.AccountId == null && Calc.IsOpen(o)).Sum(Calc.RemainingOf);
        r.CustomerDebts = Calc.GetDebts().Sum(c => c.Debt);
        r.Dealers = Store.Accounts.Sum(a => Accounts.Due(a) + Accounts.Expected(a));
        var dues = SupplierDues.Open();
        r.SuppliersDue = dues.Where(d => d.DueDate != "" && string.CompareOrdinal(d.DueDate, limit) <= 0).Sum(d => d.Left);
        r.SuppliersNoDate = dues.Where(d => d.DueDate == "").Sum(d => d.Left);
        // متوسط المصاريف الشهرية لآخر 3 أشهر (دون الرواتب والسُّلف المحسوبة منفصلة)
        var from = Txt.Iso(DateTime.Today.AddMonths(-3));
        r.Expenses = Math.Round(Store.Expenses.Where(e => string.CompareOrdinal(e.Date, from) >= 0 && !e.Id.StartsWith("sal_") && !e.Id.StartsWith("adv_")).Sum(e => e.Amount) / 3);
        r.Salaries = Staff.Employees().Where(e => e.Active).Sum(e => e.Salary);
        return r;
    }
}

// ============================== أوقات الذروة ==============================
public static class Peak
{
    public static readonly string[] Days = { "السبت", "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };
    public static readonly (string Title, int From, int To)[] Slots = { ("قبل 11", 0, 10), ("11–1", 11, 12), ("1–3", 13, 14), ("3–5", 15, 16), ("5–7", 17, 18), ("7–9", 19, 20), ("بعد 9", 21, 23) };

    /// <summary>عدد الأجهزة المستلمة لكل يوم وفترة (من وقت تسجيل الطلب)</summary>
    public static int[,] Grid(string a, string b)
    {
        var g = new int[Days.Length, Slots.Length];
        foreach (var o in Store.Orders.Concat(Store.Trash))
        {
            if (!Calc.InRange(o.DateReceived, a, b) || Txt.ParseTime(o.CreatedAt) is not DateTime t) continue;
            int day = ((int)t.DayOfWeek + 1) % 7;   // السبت أولاً
            int slot = Array.FindIndex(Slots, s => t.Hour >= s.From && t.Hour <= s.To);
            if (slot >= 0) g[day, slot]++;
        }
        return g;
    }
}

// ============================== اقتراح كميات الشراء ==============================
public static class Reorder
{
    public record Suggestion(InvItem Item, int Used90, double PerMonth, int Suggest);

    /// <summary>من استهلاك آخر 90 يوماً: ما يكفي شهراً + حد التنبيه، ناقص الموجود</summary>
    public static List<Suggestion> List()
    {
        var from = Txt.Iso(DateTime.Today.AddDays(-90));
        var used = Calc.InvUsage(Store.Orders.Where(o => string.CompareOrdinal(o.DateReceived, from) >= 0 && o.Status != K.Cancelled).SelectMany(o => o.Parts));
        var res = new List<Suggestion>();
        foreach (var i in Store.Inventory.Where(i => i.Qty != null))
        {
            int u = used.GetValueOrDefault(i.Id);
            double perMonth = u / 3.0;
            int target = (int)Math.Ceiling(perMonth) + Calc.LowLimit(i);
            int sug = Math.Max(0, target - (i.Qty ?? 0));
            if (sug > 0 && (u > 0 || i.Qty <= Calc.LowLimit(i))) res.Add(new Suggestion(i, u, perMonth, sug));
        }
        return res.OrderBy(s => s.Item.Supplier, StringComparer.CurrentCulture).ThenByDescending(s => s.Used90).ToList();
    }
}

// ============================== الموظفون والحضور والرواتب ==============================
public class Employee { public string Id, Name = "", Phone = "", StartDate = "", Note = ""; public double Salary; public bool Active = true; }
public class Attend { public string Id, EmpId, Date, Status = "present", Note = ""; }
public class Advance { public string Id, EmpId, Date, Note = ""; public double Amount; }
public class SalaryPay { public string Id, EmpId, Month, Date, Note = ""; public double Base, Deduction, Advances, Commission, Bonus, Net; }

public static class Staff
{
    static readonly JsonSerializerOptions opts = new() { IncludeFields = true };
    public static readonly (string Key, string Title)[] States = { ("present", "حاضر"), ("late", "متأخر"), ("absent", "غائب"), ("leave", "إجازة") };

    static List<T> All<T>(string kind) => Store.Docs(kind).Select(d => { try { return d.Deserialize<T>(opts); } catch { return default; } }).Where(x => x != null).ToList();
    static void Put<T>(string kind, string id, T v) => Store.PutDoc(kind, id, (JsonObject)JsonSerializer.SerializeToNode(v, opts));

    public static List<Employee> Employees() => All<Employee>("employees").OrderBy(e => e.Name, StringComparer.CurrentCulture).ToList();
    public static void Save(Employee e) => Put("employees", e.Id, e);
    public static void Delete(Employee e) => Store.DelDoc("employees", e.Id);

    public static List<Attend> Attendance(string month = null) => All<Attend>("attendance").Where(a => month == null || a.Date.StartsWith(month)).ToList();
    public static void Mark(string empId, string date, string status, string note = "") =>
        Put("attendance", empId + "_" + date, new Attend { Id = empId + "_" + date, EmpId = empId, Date = date, Status = status, Note = note });
    public static string StateOf(string empId, string date) => Attendance(date[..7]).FirstOrDefault(a => a.EmpId == empId && a.Date == date)?.Status;

    public static List<Advance> Advances(string month = null) => All<Advance>("advances").Where(a => month == null || a.Date.StartsWith(month)).ToList();

    /// <summary>السلفة تُسجَّل مصروفاً فوراً (نقد خرج من الصندوق)، وتُخصم من الراتب</summary>
    public static void AddAdvance(Employee e, double amount, string date, string note)
    {
        var a = new Advance { Id = Txt.Uid("adv"), EmpId = e.Id, Date = date, Amount = amount, Note = note };
        Put("advances", a.Id, a);
        Store.AddExpense(new Expense { Id = "adv_" + a.Id, Description = $"سلفة راتب: {e.Name}{(note != "" ? " — " + note : "")}", Amount = amount, Date = date });
    }

    public static void DeleteAdvance(Advance a)
    {
        Store.DelDoc("advances", a.Id);
        if (Store.Expenses.FirstOrDefault(x => x.Id == "adv_" + a.Id) is Expense ex) Store.DeleteExpense(ex);
    }

    public static List<SalaryPay> Salaries() => All<SalaryPay>("salaries");
    public static SalaryPay PaidFor(string empId, string month) => Salaries().FirstOrDefault(s => s.EmpId == empId && s.Month == month);

    public static bool DeductAbsence => Store.Flag("hr_deduct_absence", true);

    /// <summary>كشف راتب شهر: الأساسي − خصم الغياب − السُّلف + عمولة الفني (إن كان اسمه فنياً)</summary>
    public static SalaryPay Slip(Employee e, string month)
    {
        var first = Txt.ParseDate(month + "-01") ?? DateTime.Today;
        string a = Txt.Iso(first), b = Txt.Iso(first.AddMonths(1).AddDays(-1));
        int absent = Attendance(month).Count(x => x.EmpId == e.Id && x.Status == "absent");
        double commission = Techs.Find(e.Name) != null ? Techs.Report(a, b).FirstOrDefault(r => Txt.Fold(r.Name) == Txt.Fold(e.Name))?.Commission ?? 0 : 0;
        var s = new SalaryPay
        {
            EmpId = e.Id, Month = month, Base = e.Salary,
            Deduction = DeductAbsence ? Math.Round(e.Salary / 30 * absent) : 0,
            Advances = Advances(month).Where(x => x.EmpId == e.Id).Sum(x => x.Amount),
            Commission = Math.Round(commission),
        };
        s.Net = Math.Max(0, s.Base - s.Deduction - s.Advances + s.Commission + s.Bonus);
        return s;
    }

    /// <summary>صرف الراتب: يُسجَّل مصروفاً بالصافي (السُّلف سُجّلت مصروفاً عند صرفها)</summary>
    public static SalaryPay Pay(Employee e, SalaryPay s, string date)
    {
        s.Id = Txt.Uid("sal");
        s.Date = date;
        s.Net = Math.Max(0, s.Base - s.Deduction - s.Advances + s.Commission + s.Bonus);
        Put("salaries", s.Id, s);
        if (s.Net > 0) Store.AddExpense(new Expense { Id = "sal_" + s.Id, Description = $"راتب {s.Month}: {e.Name}", Amount = s.Net, Date = date });
        return s;
    }

    public static void Unpay(SalaryPay s)
    {
        Store.DelDoc("salaries", s.Id);
        if (Store.Expenses.FirstOrDefault(x => x.Id == "sal_" + s.Id) is Expense ex) Store.DeleteExpense(ex);
    }
}

// ============================== التقارير الأسبوعية والشهرية ==============================
public static class Periodic
{
    public static bool WeeklyOn => Store.Flag("notify_weekly");
    public static bool MonthlyOn => Store.Flag("notify_monthly");
    /// <summary>يوم إرسال الملخص الأسبوعي (0 = الأحد ... 6 = السبت)، افتراضياً الخميس</summary>
    public static int WeekDay => int.TryParse(Store.Get("notify_weekly_day", "4"), out var d) ? Math.Clamp(d, 0, 6) : 4;

    public static string Text(string title, string a, string b, string pa, string pb)
    {
        var s = Calc.Summarize(a, b);
        var p = Calc.Summarize(pa, pb);
        string Cmp(double now, double prev) => prev > 0 ? $" ({(now >= prev ? "▲" : "▼")} {Math.Abs(Math.Round((now - prev) / prev * 100))}%)" : "";
        var L = new List<string> { $"📊 {title} — {Store.ShopName}", $"من {Txt.FmtDate(a)} إلى {Txt.FmtDate(b)}", "" };
        L.Add($"📱 استُلم: {s.Received.Count}{Cmp(s.Received.Count, p.Received.Count)} — سُلّم: {s.Active.Count}{Cmp(s.Active.Count, p.Active.Count)}");
        L.Add($"💵 الإيراد: {Txt.Money(s.Revenue)}{Cmp(s.Revenue, p.Revenue)}");
        L.Add($"📈 صافي الربح: {Txt.Money(s.Profit)}{Cmp(s.Profit, p.Profit)}");
        L.Add($"💰 المقبوض: {Txt.Money(s.Cash)} — المصاريف: {Txt.Money(s.Expenses)}");
        if (s.Refunds > 0) L.Add($"↩️ مُرجَع للزبائن: {Txt.Money(s.Refunds)}");
        var top = s.Received.GroupBy(o => o.IssueType).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} ({g.Count()})");
        if (s.Received.Count > 0) L.Add("🔧 أكثر الأعطال: " + string.Join("، ", top));
        var techs = Techs.Report(a, b).Where(t => t.Delivered > 0).Take(5).ToList();
        if (techs.Count > 0) { L.Add("👷 الفنيون:"); L.AddRange(techs.Select(t => $"   - {t.Name}: سلّم {t.Delivered} — عمولة {Txt.Money(t.Commission)}")); }
        var newCustomers = Calc.GetCustomers().Count(c => c.Orders.Min(o => o.DateReceived) is string first && Calc.InRange(first, a, b));
        L.Add($"🙋 زبائن جدد: {newCustomers}");
        L.Add($"📒 الديون الآن: {Txt.Money(Calc.GetDebts().Sum(c => c.Debt))}");
        return string.Join("\n", L);
    }

    public static string WeekText(DateTime end) =>
        Text("ملخص الأسبوع", Txt.Iso(end.AddDays(-6)), Txt.Iso(end), Txt.Iso(end.AddDays(-13)), Txt.Iso(end.AddDays(-7)));

    public static string MonthText(DateTime anyDayInMonth)
    {
        var first = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        return Text("ملخص شهر " + first.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-IQ")), Txt.Iso(first), Txt.Iso(first.AddMonths(1).AddDays(-1)),
            Txt.Iso(first.AddMonths(-1)), Txt.Iso(first.AddDays(-1)));
    }

    /// <summary>ما يجب إرساله الآن (بعد وقت التقرير اليومي): الأسبوعي في يومه، والشهري أول يوم من الشهر عن الشهر الماضي</summary>
    public static List<(string Key, string Value, string Subject, string Text)> Due()
    {
        var res = new List<(string, string, string, string)>();
        if (DateTime.Now.TimeOfDay < TimeSpan.Parse(Notify.DailyTime)) return res;
        var today = DateTime.Today;
        if (WeeklyOn && (int)today.DayOfWeek == WeekDay && Store.Get("notify_weekly_last") != Txt.Today)
            res.Add(("notify_weekly_last", Txt.Today, "ملخص الأسبوع — " + Store.ShopName, WeekText(today)));
        var lastMonth = today.AddMonths(-1);
        if (MonthlyOn && today.Day == 1 && Store.Get("notify_monthly_last") != lastMonth.ToString("yyyy-MM"))
            res.Add(("notify_monthly_last", lastMonth.ToString("yyyy-MM"), "ملخص الشهر — " + Store.ShopName, MonthText(lastMonth)));
        return res;
    }
}
