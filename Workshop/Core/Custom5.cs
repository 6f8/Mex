using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Workshop;

// ============================== تغيير الحالة (بدون واجهة) ==============================
public static class StatusOps
{
    /// <summary>نسخة من الطلب بالحالة الجديدة مع تواريخها (التسليم، الجاهزية، الإلغاء). لا يحفظ.</summary>
    public static Order Apply(Order o, string status)
    {
        var prev = o.Status;
        var n = o.Clone();
        var now = Txt.Now;
        n.Status = status; n.UpdatedAt = now; n.StatusAt = now;
        n.ReadyAt = status == K.Ready ? now : null;
        n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
        n.CancelledAt = status == K.Cancelled ? now : null;
        if (status == K.Done)
        {
            n.CompletedAt ??= now;
            if (n.DateDelivered == "") n.DateDelivered = Txt.Today;
            if (Store.ClearPasscodeOnDelivery) n.Passcode = "";
        }
        else
        {
            n.CompletedAt = null;
            if (prev == K.Done) n.DateDelivered = "";
        }
        return n;
    }
}

// ============================== رسائل SMS ==============================
/// <summary>
/// إرسال SMS عبر مزوّد رسائل (رابط HTTP يعطيك إياه المزوّد، فيه {phone} و{text})، أو فتح تطبيق الرسائل
/// في ويندوز (Phone Link) إن لم يُضبط مزوّد.
/// </summary>
public static class Sms
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static string Url => Store.Get("sms_url");
    public static bool Post => Store.Get("sms_method", "GET") == "POST";
    public static string Body => Store.Get("sms_body");
    public static bool Configured => Url != "";

    static string Fill(string template, string phone, string text, bool encode)
    {
        string E(string v) => encode ? Uri.EscapeDataString(v) : v.Replace("\"", "\\\"").Replace("\n", "\\n");
        var intl = Txt.WaPhone(phone);
        return template.Replace("{phone}", E(phone)).Replace("{phone_intl}", E(intl)).Replace("{text}", E(text));
    }

    /// <summary>يعيد "" عند النجاح أو وصف الخطأ</summary>
    public static async Task<string> Send(string phone, string text)
    {
        if (!Configured) return "لم يُضبط مزوّد الرسائل";
        if (string.IsNullOrWhiteSpace(phone)) return "لا يوجد رقم هاتف";
        try
        {
            HttpResponseMessage res;
            if (Post)
            {
                var body = Fill(Body, phone, text, false);
                var type = body.TrimStart().StartsWith("{") ? "application/json" : "application/x-www-form-urlencoded";
                res = await http.PostAsync(Fill(Url, phone, text, true), new StringContent(body, Encoding.UTF8, type));
            }
            else res = await http.GetAsync(Fill(Url, phone, text, true));
            using (res)
            {
                if (res.IsSuccessStatusCode) return "";
                var err = await res.Content.ReadAsStringAsync();
                return $"{(int)res.StatusCode} {res.ReasonPhrase} {(err.Length > 200 ? err[..200] : err)}";
            }
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>فتح تطبيق الرسائل في ويندوز بالنص جاهزاً</summary>
    public static string AppUri(string phone, string text) => $"sms:{new string(Txt.LatinDigits(phone ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray())}?body={Uri.EscapeDataString(text)}";
}

// ============================== وضع التدريب ==============================
/// <summary>نسخة ببيانات تجريبية منفصلة تماماً (مجلد training) يتدرب عليها الموظف الجديد</summary>
public static class Training
{
    public static string RealDir { get; set; } = Raseed.AppPaths.DataDir;
    public static string FlagFile => Path.Combine(RealDir, "training.on");
    public static string Dir => Path.Combine(RealDir, "training");
    public static bool Active { get; set; }

    public static void Enable(bool on)
    {
        if (on) File.WriteAllText(FlagFile, DateTime.Now.ToString("s"));
        else if (File.Exists(FlagFile)) File.Delete(FlagFile);
    }

    /// <summary>حذف بيانات التدريب لتبدأ من جديد ببيانات تجريبية نظيفة</summary>
    public static void ResetData()
    {
        foreach (var f in new[] { "workshop.db", "workshop.db-wal", "workshop.db-shm" })
            try { File.Delete(Path.Combine(Dir, f)); } catch { }
    }
}

public static class Demo
{
    /// <summary>بيانات تجريبية واقعية: طلبات بكل الحالات، قطع، مصاريف، موردون، فني، تاجر</summary>
    public static void Seed()
    {
        var rnd = new Random(7);
        string D(int daysAgo) => Txt.Iso(DateTime.Today.AddDays(-daysAgo));
        string T(int daysAgo, int hour) => DateTime.Today.AddDays(-daysAgo).AddHours(hour).ToString("yyyy-MM-ddTHH:mm:ss");
        Store.Set("shop_name", "ورشة التدريب");
        Techs.Save(new[] { new Tech { Name = "علي", Basis = "profit", Value = 25 }, new Tech { Name = "حسين", Basis = "fixed", Value = 3000 } });
        var inv = new List<InvItem>
        {
            new() { Id = "dinv1", Name = "شاشة", Compatible = "iPhone 11", Category = "شاشة", Supplier = "مورد الرشيد", Cost = 35000, SalePrice = 60000, Qty = 3, MinQty = 1 },
            new() { Id = "dinv2", Name = "بطارية", Compatible = "iPhone 12", Category = "بطارية", Supplier = "مورد الرشيد", Cost = 15000, SalePrice = 30000, Qty = 5, MinQty = 2 },
            new() { Id = "dinv3", Name = "منفذ شحن", Compatible = "Galaxy A54", Category = "منفذ شحن", Supplier = "مورد الكرادة", Cost = 5000, SalePrice = 15000, Qty = 1, MinQty = 2 },
            new() { Id = "dinv4", Name = "شاشة", Compatible = "Redmi Note 12", Category = "شاشة", Supplier = "مورد الكرادة", Cost = 25000, SalePrice = 45000, Qty = 2, MinQty = 1 },
        };
        foreach (var i in inv) { i.UpdatedAt = Txt.Now; Store.SaveInv(i); }
        var people = new[] { ("أحمد كريم", "07701234567"), ("زينب علي", "07811234567"), ("مصطفى حسن", "07501234567"), ("نور محمد", "07721234567"), ("حيدر جاسم", "07831234567"), ("سارة عادل", "07711234568") };
        var devices = new[] { "iPhone 11", "iPhone 12", "Galaxy A54", "Redmi Note 12", "iPhone 13 Pro", "Galaxy S23" };
        var plan = new (string Status, int Days, string Type, double Price, double Paid)[]
        {
            (K.Check, 0, "شاشة", 0, 0), (K.Approval, 3, "بطارية", 30000, 0), (K.Repair, 2, "منفذ شحن", 15000, 5000), (K.Part, 5, "شاشة", 45000, 10000),
            (K.Ready, 4, "شاشة", 60000, 20000), (K.Done, 1, "بطارية", 30000, 30000), (K.Done, 8, "شاشة", 60000, 40000), (K.Done, 15, "كاميرا", 25000, 25000),
            (K.Cancelled, 6, "مياه / رطوبة", 0, 5000), (K.Repair, 1, "برمجيات", 10000, 0), (K.Done, 25, "صوت / سماعة", 20000, 20000), (K.Ready, 9, "بطارية", 30000, 30000),
        };
        var used = new HashSet<string>();
        for (int k = 0; k < plan.Length; k++)
        {
            var (st, days, type, price, paid) = plan[k];
            var (name, phone) = people[k % people.Length];
            var o = new Order
            {
                Id = Txt.Uid(), RefNo = Txt.GenRef(used), CustomerName = name, Phone = phone, Device = devices[k % devices.Length], IssueType = type,
                Issue = type == "شاشة" ? "الشاشة مكسورة واللمس لا يعمل" : type == "بطارية" ? "البطارية تنفد بسرعة" : "لا يعمل " + type,
                Status = st, Price = price, Paid = paid, Technician = k % 2 == 0 ? "علي" : "حسين", Warranty = "شهر واحد",
                DateReceived = D(days + 1), DateEstimated = D(Math.Max(0, days - 2)), CreatedAt = T(days + 1, 10 + k % 8), StartedAt = T(days + 1, 10 + k % 8),
                CheckFee = st == K.Cancelled ? 5000 : 0,
            };
            used.Add(o.RefNo);
            if (paid > 0) o.PaymentHistory.Add(new Payment { Id = Txt.Uid("inst"), Amount = paid, Date = D(days), Method = k % 3 == 0 ? "زين كاش" : Lists.Cash });
            if (price > 0 && type == "شاشة") o.Parts.Add(new Part { Name = "شاشة", Supplier = "مورد الرشيد", Cost = 35000 });
            if (price > 0 && type == "بطارية") o.Parts.Add(new Part { Name = "بطارية", Supplier = "مورد الرشيد", Cost = 15000 });
            if (st == K.Done) { o.DateDelivered = D(days); o.CompletedAt = T(days, 17); }
            if (st == K.Cancelled) o.CancelledAt = T(days, 12);
            if (st == K.Ready) o.ReadyAt = T(days, 15);
            o.StatusAt = T(days, 12);
            o.UpdatedAt = o.StatusAt;
            o.PaymentStatus = Json.DerivePay(Calc.ChargeOf(o), o.Paid);
            o.X.Source = new[] { "زبون سابق", "إنستغرام", "صديق أو قريب", "مرّ من أمام المحل" }[k % 4];
            o.X.Area = new[] { "المنصور", "الكرادة", "زيونة", "الأعظمية" }[k % 4];
            Store.SaveOrder(o);
        }
        Store.AddExpense(new Expense { Id = Txt.Uid("e"), Description = "إيجار المحل", Amount = 500000, Date = D(10) });
        Store.AddExpense(new Expense { Id = Txt.Uid("e"), Description = "كهرباء", Amount = 75000, Date = D(3) });
        Store.AddSupplierTx(new SupplierTx { Id = Txt.Uid("st"), Supplier = "مورد الرشيد", Type = "purchase", Amount = 250000, Date = D(12), DueDate = D(-5) });
        Store.AddSupplierTx(new SupplierTx { Id = Txt.Uid("st"), Supplier = "مورد الرشيد", Type = "payment", Amount = 100000, Date = D(6) });
        Store.SaveAccount(new Account { Id = Txt.Uid("ac"), Name = "محل النجمة للموبايل", Phone = "07901234567", Discount = 10, CreditLimit = 300000, CreatedAt = Txt.Now });
        Reminders.Add(null, Txt.Today, "جرّب: افتح طلباً واضغط «تذكير جديد»");
        Store.SetFlag("demo_seeded", true);
    }
}

// ============================== تقرير الفروع ==============================
public static class BranchReport
{
    public record Row(string Branch, int Received, int Delivered, double Revenue, double Parts, double Expenses, double Profit, double Cash, double Debt);

    public static Row For(string branch, List<Order> orders, List<Expense> exps, string a, string b)
    {
        var s = Calc.Summarize(a, b, orders, exps);
        return new Row(branch, s.Received.Count, s.Active.Count, s.Revenue, s.Parts, s.Expenses, s.Profit, s.Cash, s.Debt);
    }

    public static Row Total(IEnumerable<Row> rows) => rows.Aggregate(new Row("المجموع", 0, 0, 0, 0, 0, 0, 0, 0), (t, r) =>
        new Row("المجموع", t.Received + r.Received, t.Delivered + r.Delivered, t.Revenue + r.Revenue, t.Parts + r.Parts, t.Expenses + r.Expenses, t.Profit + r.Profit, t.Cash + r.Cash, t.Debt + r.Debt));
}
