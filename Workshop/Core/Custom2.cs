using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Workshop;

// ============================== التذكيرات ==============================
/// <summary>تذكير على طلب (أو عام): «اتصل بالزبون غداً»، «راجع المورد بخصوص القطعة»</summary>
public class Reminder
{
    public string Id, OrderId, Date, Text = "", CreatedAt, DoneAt;
    public bool Done;
}

public static class Reminders
{
    public static readonly string[] Suggestions =
    {
        "اتصل بالزبون", "راجع المورد بخصوص القطعة", "ذكّر الزبون باستلام الجهاز", "تابع موافقة الزبون على السعر",
        "تأكد من الزبون أن الجهاز يعمل جيداً", "اطلب القطعة من المورد",
    };

    /// <summary>المستحق حتى اليوم (ومنه المتأخر) ولم يُنجز</summary>
    public static List<Reminder> Due(string day = null) => Store.Reminders.Where(r => !r.Done && string.CompareOrdinal(r.Date, day ?? Txt.Today) <= 0)
        .OrderBy(r => r.Date, StringComparer.Ordinal).ThenBy(r => r.CreatedAt, StringComparer.Ordinal).ToList();

    public static int DueCount => Store.Reminders.Count(r => !r.Done && string.CompareOrdinal(r.Date, Txt.Today) <= 0);

    public static List<Reminder> For(string orderId) => Store.Reminders.Where(r => r.OrderId == orderId)
        .OrderBy(r => r.Done).ThenBy(r => r.Date, StringComparer.Ordinal).ToList();

    public static Reminder Add(string orderId, string date, string text)
    {
        var r = new Reminder { Id = Txt.Uid("rm"), OrderId = orderId, Date = date, Text = Txt.Str(text), CreatedAt = Txt.Now };
        Store.SaveReminder(r);
        return r;
    }

    public static void SetDone(Reminder r, bool done)
    {
        r.Done = done;
        r.DoneAt = done ? Txt.Now : null;
        Store.SaveReminder(r);
    }

    /// <summary>النص مع الطلب المرتبط: «اتصل بالزبون — A-102 علي (iPhone 12)»</summary>
    public static string Label(Reminder r)
    {
        if (r.OrderId == null) return r.Text;
        var o = Calc.Find(r.OrderId);
        return o != null ? $"{r.Text} — {o.RefNo} {o.CustomerName} ({o.Device})" : r.Text + " — (طلب محذوف)";
    }

    public static string When(Reminder r)
    {
        int d = Txt.DaysBetween(Txt.Today, r.Date);
        return d switch { 0 => "اليوم", 1 => "غداً", -1 => "أمس", < 0 => $"متأخر {-d} يوم", _ => Txt.FmtDate(r.Date) };
    }
}

// ============================== حسابات التجار والشركات ==============================
/// <summary>تاجر أو شركة: أسعار خاصة وحساب مفتوح يُسدَّد دفعة واحدة، مع كشف حساب شهري</summary>
public class Account
{
    public string Id, Name = "", Phone = "", Kind = "dealer", Note = "", CreatedAt;
    /// <summary>خصم التاجر % على السعر المقترح من قائمة الأسعار</summary>
    public double Discount;
}

public static class Accounts
{
    public static readonly (string Key, string Title)[] Kinds = { ("dealer", "تاجر / محل"), ("company", "شركة / مؤسسة") };

    public static Account Find(string id) => id == null ? null : Store.Accounts.FirstOrDefault(a => a.Id == id);
    public static string NameOf(Order o) => Find(o.AccountId)?.Name ?? "";
    public static string KindText(Account a) => Kinds.FirstOrDefault(k => k.Key == a.Kind).Title ?? "تاجر / محل";

    public static List<Order> OrdersOf(Account a) => Store.Orders.Where(o => o.AccountId == a.Id).ToList();

    /// <summary>المستحق: أجهزة سُلّمت (أو أجور فحص) ولم تُدفع</summary>
    public static double Due(Account a) => OrdersOf(a).Where(o => o.Status is K.Done or K.Cancelled).Sum(Calc.RemainingOf);
    /// <summary>ما سيُستحق على أجهزة ما زالت في الورشة</summary>
    public static double Expected(Account a) => OrdersOf(a).Where(Calc.IsOpen).Sum(Calc.RemainingOf);

    /// <summary>سعر التاجر: السعر ناقص خصمه</summary>
    public static double PriceFor(Account a, double price) =>
        a == null || a.Discount <= 0 ? price : Math.Round(price * (1 - a.Discount / 100), Store.Currency == "د.ع" ? 0 : 2);

    public record Line(string Date, string Text, string RefNo, double Charge, double Paid);

    public class Statement
    {
        public double Opening, Charges, Payments;
        public double Closing => Opening + Charges - Payments;
        public List<Line> Lines = new();
    }

    static IEnumerable<(Order O, string Date, double Amount, string Note)> PaymentsOf(Account a)
    {
        foreach (var o in OrdersOf(a))
        {
            foreach (var p in o.PaymentHistory) yield return (o, p.Date, p.Amount, p.IsRefund ? p.Note : p.Method + (p.Note != "" ? " — " + p.Note : ""));
            double legacy = o.Paid - o.PaymentHistory.Sum(p => p.Amount);
            if (legacy > 0.001) yield return (o, o.DateReceived, legacy, "دفعة سابقة");
        }
    }

    /// <summary>كشف الحساب لفترة: الرصيد السابق، ثم كل جهاز يُستحق بيوم تسليمه وكل دفعة بيومها</summary>
    public static Statement StatementOf(Account a, string from, string to)
    {
        var st = new Statement();
        var charges = OrdersOf(a).Where(o => o.Status is K.Done or K.Cancelled && Calc.ChargeOf(o) > 0).Select(o => (O: o, Date: Calc.ClosedDate(o))).ToList();
        var pays = PaymentsOf(a).ToList();
        st.Opening = charges.Where(c => string.CompareOrdinal(c.Date, from) < 0).Sum(c => Calc.ChargeOf(c.O))
                   - pays.Where(p => string.CompareOrdinal(p.Date, from) < 0).Sum(p => p.Amount);
        foreach (var c in charges.Where(c => Calc.InRange(c.Date, from, to)))
            st.Lines.Add(new(c.Date, $"{c.O.Device} — {c.O.CustomerName}{(c.O.Status == K.Cancelled ? " (أجرة فحص)" : "")}", c.O.RefNo, Calc.ChargeOf(c.O), 0));
        foreach (var p in pays.Where(p => Calc.InRange(p.Date, from, to)))
            st.Lines.Add(new(p.Date, p.Amount < 0 ? "مبلغ مُرجَع — " + p.Note : "دفعة — " + p.Note, p.O.RefNo, 0, p.Amount));
        st.Lines = st.Lines.OrderBy(l => l.Date, StringComparer.Ordinal).ThenByDescending(l => l.Charge).ToList();
        st.Charges = st.Lines.Sum(l => l.Charge);
        st.Payments = st.Lines.Sum(l => l.Paid);
        return st;
    }

    /// <summary>تسديد دفعة للحساب: تُوزَّع على الأجهزة غير المسددة من الأقدم (المسلّمة أولاً ثم التي في الورشة)</summary>
    public static List<(Order O, double Amount)> Pay(Account a, double amount, string method, string date, string note)
    {
        var res = new List<(Order, double)>();
        var queue = OrdersOf(a).Where(o => Calc.RemainingOf(o) > 0)
            .OrderBy(o => Calc.IsOpen(o) ? 1 : 0).ThenBy(o => Calc.IsOpen(o) ? o.DateReceived : Calc.ClosedDate(o), StringComparer.Ordinal).ToList();
        double left = amount;
        foreach (var o in queue)
        {
            if (left <= 0.001) break;
            double part = Math.Min(left, Calc.RemainingOf(o));
            var n = o.Clone();
            n.PaymentHistory.Add(new Payment { Id = Txt.Uid("inst"), Amount = part, Date = date, Method = method, Note = note == "" ? "تسديد حساب" : note });
            n.Paid += part;
            n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
            n.UpdatedAt = Txt.Now;
            Store.SaveOrder(n);
            res.Add((n, part));
            left -= part;
        }
        return res;
    }
}

// ============================== إرجاع مبلغ للزبون ==============================
/// <summary>المبلغ المُرجَع يُسجَّل دفعة بالسالب: ينقص المدفوع وصندوق يومه، ويبقى أثره وسببه في الطلب</summary>
public static class Refunds
{
    public static readonly string[] Reasons = { "أُلغي الطلب", "لم يُصلح الجهاز", "رجع بالضمان", "خصم بعد الدفع", "دُفع أكثر من المطلوب" };

    public static double Overpaid(Order o) => Math.Max(0, o.Paid - Calc.ChargeOf(o));

    public static Order Apply(Order o, double amount, string method, string date, string reason)
    {
        var n = o.Clone();
        n.PaymentHistory.Add(new Payment { Id = Txt.Uid("rf"), Amount = -amount, Date = date, Method = method, Note = "استرجاع: " + reason });
        n.Paid -= amount;
        n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
        n.UpdatedAt = Txt.Now;
        Locking.Log(n, $"أُرجع للزبون {Txt.Money(amount)} ({method}) — {reason}");
        Store.SaveOrder(n);
        Notify.Alert("refund", $"↩️ إرجاع مبلغ للزبون\n{n.RefNo} — {n.CustomerName} — {n.Device}\nالمبلغ: {Txt.Money(amount)} ({method})\nالسبب: {reason}");
        return n;
    }
}

// ============================== قفل الطلبات المسلّمة ==============================
public static class Locking
{
    public static bool On => Store.Flag("lock_delivered", true);
    /// <summary>الطلب المسلّم أو الملغى: تعديل سعره أو دفعاته السابقة أو حالته يحتاج سبباً</summary>
    public static bool IsLocked(Order o) => On && o != null && (o.Status == K.Done || o.Status == K.Cancelled);
    public static void Log(Order o, string text) => o.History.Add(new Change { At = Txt.Now, Text = text });
}

// ============================== ضمان المورد ==============================
public static class SupWarranty
{
    static string raw;
    static Dictionary<string, int> map = new();

    static Dictionary<string, int> Map
    {
        get
        {
            var r = Store.Get("sup_warranty");
            if (r == raw) return map;
            var m = new Dictionary<string, int>();
            try
            {
                if (r != "" && JsonNode.Parse(r) is JsonObject o)
                    foreach (var (k, v) in o) if (Txt.OptInt(v?.ToString()) is int d && d > 0) m[k] = d;
            }
            catch { }
            raw = r;
            map = m;
            return m;
        }
    }

    /// <summary>ضمان المورد الافتراضي على قطعه (بالأيام)</summary>
    public static int? ForSupplier(string supplier) => Txt.Str(supplier) == "" ? null : Map.TryGetValue(Txt.Fold(supplier), out var d) ? d : null;

    public static void SetForSupplier(string supplier, int? days)
    {
        var m = new Dictionary<string, int>(Map);
        var k = Txt.Fold(supplier);
        if (k == "") return;
        if (days is int d && d > 0) m[k] = d; else m.Remove(k);
        Store.Set("sup_warranty", m.Count == 0 ? "" : new JsonObject(m.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode)kv.Value))).ToJsonString());
    }

    public static int? Of(Part p) => p.SupWarranty ?? ForSupplier(p.Supplier);

    /// <summary>نهاية ضمان المورد لقطعة رُكّبت في طلب: من يوم استلام الجهاز</summary>
    public static string EndFor(Part p, Order installedIn) =>
        Of(p) is int d && Txt.ParseDate(installedIn?.DateReceived) is DateTime start ? Txt.Iso(start.AddDays(d)) : "";

    /// <summary>وصف الحالة يوم تسجيل العطل</summary>
    public static string Text(string end, string on)
    {
        if (string.IsNullOrEmpty(end)) return "لا يوجد ضمان مورد مسجّل";
        return string.CompareOrdinal(on, end) <= 0 ? $"ضمن ضمان المورد (حتى {Txt.FmtDate(end)})" : $"انتهى ضمان المورد في {Txt.FmtDate(end)}";
    }

    public static bool Valid(string end, string on) => !string.IsNullOrEmpty(end) && string.CompareOrdinal(on, end) <= 0;
}

// ============================== تشفير الأسرار ==============================
/// <summary>رمز تيليجرام وكلمة مرور البريد تُشفَّر بحساب ويندوز الحالي فلا تُقرأ من نسخة احتياطية على جهاز آخر</summary>
public static class Secret
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            if (OperatingSystem.IsWindows())
                return "dp:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));
        }
        catch { }
        return "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            if (stored.StartsWith("dp:") && OperatingSystem.IsWindows())
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[3..]), null, DataProtectionScope.CurrentUser));
            if (stored.StartsWith("b64:")) return Encoding.UTF8.GetString(Convert.FromBase64String(stored[4..]));
        }
        catch { }
        return "";
    }
}

// ============================== التقرير اليومي والتنبيهات ==============================
/// <summary>
/// يصل لصاحب المحل ملخص اليوم على تيليجرام أو البريد في الوقت الذي يحدده، وتنبيه فوري عند حذف طلب،
/// أو تسليم جهاز بدين، أو إرجاع مبلغ، أو تعديل طلب مُسلَّم. الإرسال في الخلفية فلا يتجمد البرنامج.
/// </summary>
public static class Notify
{
    public static readonly (string Key, string Title)[] Alerts =
    {
        ("delete", "حذف طلب"), ("debt", "تسليم جهاز وعليه دين"), ("refund", "إرجاع مبلغ لزبون"), ("locked", "تعديل طلب مُسلَّم أو ملغى"),
    };

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static string LastError { get; private set; } = "";
    public static string LastSent { get; private set; } = "";

    /// <summary>بديل الإرسال (للاختبار)</summary>
    public static Func<string, string, Task<string>> Sender;

    public static string TgToken => Secret.Unprotect(Store.Get("notify_tg_token"));
    public static string TgChat => Store.Get("notify_tg_chat");
    public static bool TelegramOn => TgToken != "" && TgChat != "";
    public static bool EmailOn => Store.Get("notify_mail_host") != "" && Store.Get("notify_mail_to") != "";
    public static bool Configured => Sender != null || TelegramOn || EmailOn;
    public static bool DailyOn => Store.Flag("notify_daily");
    public static bool AlertOn(string kind) => Store.Flag("notify_alert_" + kind, true);
    public static string DailyTime => Store.Get("notify_daily_time", "22:00") is var t && TimeSpan.TryParse(t, out _) ? t : "22:00";

    /// <summary>يُرسل إلى كل القنوات المفعّلة. يعيد "" عند النجاح أو وصف الخطأ</summary>
    public static async Task<string> Send(string subject, string text)
    {
        if (Sender != null) return await Sender(subject, text);
        var errors = new List<string>();
        if (TelegramOn)
            try { await Telegram(text); }
            catch (Exception ex) { errors.Add("تيليجرام: " + ex.Message); }
        if (EmailOn)
            try { await Email(subject, text); }
            catch (Exception ex) { errors.Add("البريد: " + ex.Message); }
        if (!TelegramOn && !EmailOn) errors.Add("لم تُضبط أي قناة إرسال (تيليجرام أو البريد)");
        LastError = string.Join(" — ", errors);
        if (errors.Count == 0) LastSent = Txt.Now;
        return LastError;
    }

    static async Task Telegram(string text)
    {
        // حد تيليجرام 4096 حرفاً للرسالة
        foreach (var chunk in Chunks(text, 3900))
        {
            var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["chat_id"] = TgChat, ["text"] = chunk, ["disable_web_page_preview"] = "true" });
            using var res = await http.PostAsync($"https://api.telegram.org/bot{TgToken}/sendMessage", body);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException(JsonNode.Parse(json)?["description"]?.ToString() ?? res.ReasonPhrase);
        }
    }

    static IEnumerable<string> Chunks(string s, int n)
    {
        for (int i = 0; i < s.Length; i += n) yield return s.Substring(i, Math.Min(n, s.Length - i));
    }

    /// <summary>بعد أن يرسل صاحب المحل أي رسالة للبوت: رقم محادثته من آخر الرسائل</summary>
    public static async Task<string> FindChatId(string token)
    {
        using var res = await http.GetAsync($"https://api.telegram.org/bot{token}/getUpdates");
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        if (json?["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException(json?["description"]?.ToString() ?? "الرمز غير صحيح");
        var last = (json["result"] as JsonArray)?.LastOrDefault();
        var chat = last?["message"]?["chat"]?["id"] ?? last?["my_chat_member"]?["chat"]?["id"] ?? last?["channel_post"]?["chat"]?["id"];
        return chat?.ToString() ?? "";
    }

#pragma warning disable SYSLIB0014 // SmtpClient ما زال الأبسط للإرسال عبر Gmail/Outlook
    static async Task Email(string subject, string text)
    {
        int port = int.TryParse(Store.Get("notify_mail_port", "587"), out var p) ? p : 587;
        using var smtp = new System.Net.Mail.SmtpClient(Store.Get("notify_mail_host"), port)
        {
            EnableSsl = Store.Flag("notify_mail_ssl", true), Timeout = 20000,
            Credentials = new System.Net.NetworkCredential(Store.Get("notify_mail_user"), Secret.Unprotect(Store.Get("notify_mail_pass"))),
        };
        var from = Store.Get("notify_mail_user") is var u && u.Contains('@') ? u : Store.Get("notify_mail_to");
        using var msg = new System.Net.Mail.MailMessage(from, Store.Get("notify_mail_to"), subject, text)
        {
            BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8,
        };
        await smtp.SendMailAsync(msg);
    }
#pragma warning restore SYSLIB0014

    /// <summary>تنبيه فوري (في الخلفية) إن كان نوعه مفعّلاً وقناة الإرسال مضبوطة</summary>
    public static void Alert(string kind, string text)
    {
        if (!Configured || !AlertOn(kind)) return;
        var full = text + $"\n\n{Store.ShopName} — {DateTime.Now:yyyy-MM-dd HH:mm}";
        _ = Task.Run(async () =>
        {
            try { await Send("تنبيه — " + Store.ShopName, full); } catch (Exception ex) { LastError = ex.Message; }
        });
    }

    /// <summary>ملخص اليوم: التقفيل، الأجهزة، التذكيرات، القطع المعيبة، الديون</summary>
    public static string DailyText(string day)
    {
        var c = Calc.Close(day);
        var L = new List<string> { $"📊 تقرير يوم {Txt.FmtDate(day)} — {Store.ShopName}", "" };
        L.Add($"💵 المقبوض: {Txt.Money(c.CashIn + c.RefundTotal)}");
        if (c.RefundTotal > 0) { L.Add($"↩️ مُرجَع للزبائن: {Txt.Money(c.RefundTotal)}"); L.Add($"💵 صافي المقبوض: {Txt.Money(c.CashIn)}"); }
        L.AddRange(c.Methods.Where(x => x.Value != 0).Select(x => $"   - {x.Key}: {Txt.Money(x.Value)}"));
        L.Add($"🧾 المصاريف: {Txt.Money(c.ExpTotal)}");
        if (c.SupPaid > 0) L.Add($"🏪 دفعات الموردين: {Txt.Money(c.SupPaid)}");
        L.Add($"💰 صافي حركة النقد: {Txt.Money(c.Drawer)}");
        L.Add("");
        L.Add($"📱 استُلم: {c.Received.Count} — سُلّم: {c.Delivered.Count} — أُلغي: {c.Cancelled.Count}");
        L.Add($"📈 الإيراد: {Txt.Money(c.Revenue)} — صافي الربح: {Txt.Money(c.Profit)}");
        if (c.NewDebt > 0) L.Add($"⚠️ ديون جديدة اليوم: {Txt.Money(c.NewDebt)}");
        L.Add("");
        int open = Store.Orders.Count(Calc.IsOpen), late = Store.Orders.Count(Calc.IsLate), ready = Store.Orders.Count(o => o.Status == K.Ready);
        L.Add($"🔧 في الورشة: {open} — متأخر: {late} — جاهز لم يُستلم: {ready}");
        var rem = Reminders.Due(Txt.Iso((Txt.ParseDate(day) ?? DateTime.Today).AddDays(1)));
        if (rem.Count > 0)
        {
            L.Add($"⏰ تذكيرات حتى الغد: {rem.Count}");
            L.AddRange(rem.Take(6).Select(r => "   - " + Reminders.Label(r)));
        }
        if (Defects.PendingCount > 0) L.Add($"🔩 قطع معيبة بانتظار الإرجاع: {Defects.PendingCount}");
        double debts = Calc.GetDebts().Sum(x => x.Debt), acc = Store.Accounts.Sum(Accounts.Due);
        if (debts > 0 || acc > 0) L.Add($"📒 ديون الزبائن: {Txt.Money(debts)}{(acc > 0 ? $" — حسابات التجار: {Txt.Money(acc)}" : "")}");
        return string.Join("\n", L);
    }

    static bool busy;

    /// <summary>يُستدعى كل دقيقة تقريباً: يرسل تقرير اليوم عند وقته، وتقرير أمس إذا كان البرنامج مغلقاً وقتها</summary>
    public static async void Tick()
    {
        if (busy || !DailyOn || !Configured) return;
        var last = Store.Get("notify_daily_last");
        string today = Txt.Today, yesterday = Txt.Iso(DateTime.Today.AddDays(-1));
        string day = null;
        if (last != today && DateTime.Now.TimeOfDay >= TimeSpan.Parse(DailyTime)) day = today;
        else if (last != "" && string.CompareOrdinal(last, yesterday) < 0) day = yesterday;
        if (day == null) return;
        busy = true;
        try
        {
            var err = await Send("تقرير يوم " + day + " — " + Store.ShopName, DailyText(day) + (day != today ? "\n\n(لم يُرسل في وقته لأن البرنامج كان مغلقاً)" : ""));
            if (err == "") Store.Set("notify_daily_last", day);
        }
        catch (Exception ex) { LastError = ex.Message; }   // لا تظهر رسالة خطأ كل دقيقة
        finally { busy = false; }
    }

    /// <summary>عند إغلاق البرنامج بعد الظهر ولم يُرسل تقرير اليوم: يُرسل الآن (بحد أقصى ثوانٍ قليلة)</summary>
    public static void OnClosing()
    {
        if (!DailyOn || !Configured || Store.Get("notify_daily_last") == Txt.Today || DateTime.Now.Hour < 12) return;
        try
        {
            var t = Task.Run(() => Send("تقرير يوم " + Txt.Today + " — " + Store.ShopName, DailyText(Txt.Today) + "\n\n(أُرسل عند إغلاق البرنامج)"));
            if (t.Wait(TimeSpan.FromSeconds(10)) && t.Result == "") Store.Set("notify_daily_last", Txt.Today);
        }
        catch { }
    }
}
