using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Raseed;

// ============================== واتساب ==============================
public static class WhatsApp
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>وضع الإرسال التلقائي عبر WhatsApp Cloud API (يتطلب حساب واتساب أعمال)</summary>
    public static bool IsCloud =>
        Settings.Get("wa_mode") == "cloud" && Settings.Get("wa_token") != "" && Settings.Get("wa_phone_id") != "";

    /// <summary>interactive=true يسمح بفتح رابط wa.me عند عدم تفعيل الإرسال التلقائي</summary>
    public static async Task<bool> Send(string phone, string message, bool interactive = true)
    {
        var to = Ui.Phone(phone);
        if (to.Length < 8) return false;

        if (IsCloud)
        {
            try
            {
                var body = JsonSerializer.Serialize(new { messaging_product = "whatsapp", to, type = "text", text = new { body = message } });
                using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/v20.0/{Settings.Get("wa_phone_id")}/messages");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Get("wa_token"));
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var res = await http.SendAsync(req);
                return res.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        if (!interactive) return false;
        Process.Start(new ProcessStartInfo($"https://wa.me/{to}?text={Uri.EscapeDataString(message)}") { UseShellExecute = true });
        return true;
    }

    public static string InstallmentText(string name, long seq, double amount, string due) =>
        Settings.Get("wa_template")
            .Replace("{name}", name).Replace("{seq}", seq.ToString()).Replace("{amount}", Ui.M(amount))
            .Replace("{due}", due).Replace("{shop}", Settings.Get("shop_name"));

    /// <summary>تذكير مستحقي الأقساط. auto=true عند التشغيل من مدير المهام.</summary>
    public static async Task<int> SendInstallmentReminders(bool auto)
    {
        int days = Settings.Int("reminder_days", 3);
        var dt = Db.Query(@"SELECT t.id, t.seq, t.due_date, t.amount-t.paid AS rem, p.name, p.phone
            FROM installments t JOIN parties p ON p.id=t.party_id
            WHERE t.amount-t.paid>0.001 AND IFNULL(p.phone,'')<>''
              AND t.due_date<=date('now','localtime','+'||@p0||' day')
              AND (t.notified IS NULL OR (t.due_date<=date('now','localtime') AND t.notified<t.due_date))", days);

        if (auto && !IsCloud)
        {
            if (dt.Rows.Count > 0)
                Scheduler.Notify?.Invoke("أقساط مستحقة", $"يوجد {dt.Rows.Count} قسط مستحق. افتح شاشة الأقساط لإرسال التذكيرات.");
            return 0;
        }

        int sent = 0;
        foreach (DataRow r in dt.Rows)
        {
            var text = InstallmentText(Db.S(r["name"]), Db.L(r["seq"]), Db.D(r["rem"]), Db.S(r["due_date"]));
            if (await Send(Db.S(r["phone"]), text, !auto))
            {
                Db.Exec("UPDATE installments SET notified=@p0 WHERE id=@p1", Ui.Today, Db.L(r["id"]));
                sent++;
            }
        }
        return sent;
    }
}

// ============================== النسخ الاحتياطي ==============================
public static class Backup
{
    public static string LocalDir
    {
        get
        {
            var d = Settings.Get("backup_dir");
            return string.IsNullOrWhiteSpace(d) ? Path.Combine(Db.DataDir, "Backups") : d;
        }
    }

    public static string Run()
    {
        Directory.CreateDirectory(LocalDir);
        var file = Path.Combine(LocalDir, $"raseed_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        using (var src = Db.Open())
        using (var dst = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            dst.Open();
            src.BackupDatabase(dst);
        }

        int keep = Math.Max(3, Settings.Int("backup_keep", 30));
        Prune(LocalDir, keep);

        var cloud = Settings.Get("cloud_dir");
        if (!string.IsNullOrWhiteSpace(cloud))
        {
            Directory.CreateDirectory(cloud);
            File.Copy(file, Path.Combine(cloud, Path.GetFileName(file)), true);
            Prune(cloud, keep);
        }
        return file;
    }

    static void Prune(string dir, int keep)
    {
        foreach (var f in Directory.GetFiles(dir, "raseed_*.db").OrderByDescending(x => x).Skip(keep))
            try { File.Delete(f); } catch { }
    }

    /// <summary>يتحقق أن الملف نسخة سليمة من قاعدة بيانات رصيد قبل استبدال البيانات الحالية</summary>
    public static bool IsValidBackup(string file, out string error)
    {
        error = "";
        try
        {
            using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            if (Convert.ToString(cmd.ExecuteScalar()) != "ok") { error = "الملف تالف ولا يمكن استعادته."; return false; }
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('invoices','items','parties','users')";
            if (Convert.ToInt64(cmd.ExecuteScalar()) < 4) { error = "هذا الملف ليس نسخة احتياطية من برنامج رصيد."; return false; }
            return true;
        }
        catch (Exception ex) { error = "تعذر قراءة الملف: " + ex.Message; return false; }
    }

    public static void Restore(string file)
    {
        if (!IsValidBackup(file, out var err)) throw new InvalidOperationException(err);
        try { Run(); } catch { }           // نسخة أمان قبل الاستعادة
        SqliteConnection.ClearAllPools();
        File.Copy(file, Db.FilePath, true);
    }
}

// ============================== مدير المهام ==============================
public static class Scheduler
{
    public static Action<string, string> Notify;   // إشعار في شريط المهام
    public static Action<string, string> Alert;    // نافذة تنبيه
    static bool busy;

    public static async void Tick()
    {
        if (busy) return;
        busy = true;
        try
        {
            var dt = Db.Query("SELECT * FROM tasks WHERE active=1 AND run_at<=@p0 ORDER BY run_at", DateTime.Now.ToString(Ui.TFmt));
            foreach (DataRow t in dt.Rows)
            {
                long id = Db.L(t["id"]);
                string kind = Db.S(t["kind"]), title = Db.S(t["title"]), msg = Db.S(t["message"]), rep = Db.S(t["repeat"]);
                try { await Execute(kind, title, msg); }
                catch (Exception ex) { Notify?.Invoke("فشل تنفيذ مهمة", title + ": " + ex.Message); }

                if (rep == "بدون" || rep == "")
                    Db.Exec("UPDATE tasks SET active=0, last_run=@p0 WHERE id=@p1", Ui.Now, id);
                else
                {
                    var next = DateTime.TryParse(Db.S(t["run_at"]), out var d) ? d : DateTime.Now;
                    do next = rep switch { "أسبوعي" => next.AddDays(7), "شهري" => next.AddMonths(1), _ => next.AddDays(1) };
                    while (next <= DateTime.Now);
                    Db.Exec("UPDATE tasks SET run_at=@p0, last_run=@p1 WHERE id=@p2", next.ToString(Ui.TFmt), Ui.Now, id);
                }
            }
        }
        catch (Exception ex) { Notify?.Invoke("مدير المهام", ex.Message); }
        finally { busy = false; }
    }

    static async Task Execute(string kind, string title, string msg)
    {
        switch (kind)
        {
            case "نسخ احتياطي":
                var f = Backup.Run();
                Notify?.Invoke("نسخ احتياطي", "تم حفظ النسخة: " + Path.GetFileName(f));
                break;
            case "تذكير الأقساط":
                int n = await WhatsApp.SendInstallmentReminders(true);
                if (n > 0) Notify?.Invoke("تذكير الأقساط", $"تم إرسال {n} رسالة تذكير.");
                break;
            case "فحص الصلاحية":
                long exp = Stats.Expiring(Settings.Int("expiry_days", 30)), low = Stats.LowStock();
                if (exp + low > 0) Notify?.Invoke("تنبيه المخزون", $"مواد قاربت على الانتهاء: {exp} — مواد تحت حد الطلب: {low}");
                break;
            default:
                Alert?.Invoke(title, string.IsNullOrWhiteSpace(msg) ? title : msg);
                break;
        }
    }
}

// ============================== إحصائيات ==============================
public static class Stats
{
    public static double TodaySales() =>
        Db.D(Db.Scalar("SELECT SUM(CASE type WHEN 'Sale' THEN net ELSE -net END) FROM invoices WHERE type IN ('Sale','SaleReturn') AND date LIKE @p0", Ui.Today + "%"));
    public static double CashTotal() => Db.D(Db.Scalar("SELECT SUM(amount*rate) FROM cash_moves"));
    public static double Receivables() => Db.D(Db.Scalar("SELECT SUM(balance) FROM v_party_balance WHERE balance>0"));
    public static double Payables() => -Db.D(Db.Scalar("SELECT SUM(balance) FROM v_party_balance WHERE balance<0"));
    public static long LowStock() => Db.L(Db.Scalar(
        "SELECT COUNT(*) FROM items i WHERE i.min_qty>0 AND IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0)<=i.min_qty"));
    public static long Expiring(int days) => Db.L(Db.Scalar(
        "SELECT COUNT(*) FROM batches WHERE qty>0 AND expiry IS NOT NULL AND expiry<>'' AND expiry<=date('now','localtime','+'||@p0||' day')", days));
    public static long RepairsOpen() => Db.L(Db.Scalar("SELECT COUNT(*) FROM repairs WHERE status NOT IN ('تم التسليم','ملغي')"));
    public static long RepairsReady() => Db.L(Db.Scalar("SELECT COUNT(*) FROM repairs WHERE status='جاهز'"));
    public static long DueInstallments(int days) => Db.L(Db.Scalar(
        "SELECT COUNT(*) FROM installments WHERE amount-paid>0.001 AND due_date<=date('now','localtime','+'||@p0||' day')", days));
}

// ============================== ربط تطبيق الهاتف (REST API محلي) ==============================
public static class MobileApi
{
    static HttpListener listener;
    public static string Status = "متوقف";
    static readonly JsonSerializerOptions opts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Start()
    {
        if (Settings.Get("api_enabled") != "1") { Status = "غير مفعّل"; return; }
        int port = Settings.Int("api_port", 8085);
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{port}/");
            listener.Start();
            Status = $"يعمل على الشبكة المحلية — المنفذ {port}";
        }
        catch
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
                Status = "يعمل على هذا الجهاز فقط — نفّذ أمر netsh المذكور في README";
                localOnly = true;
            }
            catch (Exception ex) { Status = "فشل التشغيل: " + ex.Message; listener = null; return; }
        }
        _ = Task.Run(Loop);
    }

    public static void Stop() { try { listener?.Stop(); } catch { } listener = null; }

    static bool localOnly;

    /// <summary>روابط فتح تطبيق الهاتف من المتصفح</summary>
    public static List<string> Urls()
    {
        var list = new List<string>();
        if (listener == null) return list;
        int port = Settings.Int("api_port", 8085);
        string tok = Settings.Get("api_token");
        if (!localOnly)
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        list.Add($"http://{ip}:{port}/?token={tok}");
            }
            catch { }
        if (list.Count == 0) list.Add($"http://localhost:{port}/?token={tok}");
        return list;
    }

    static async Task Loop()
    {
        while (listener != null && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); } catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    static void Handle(HttpListenerContext ctx)
    {
        object result;
        int code = 200;
        var path0 = ctx.Request.Url.AbsolutePath.TrimEnd('/');
        if (path0 is "" or "/index.html" or "/manifest.json")
        {
            bool man = path0 == "/manifest.json";
            var page = Encoding.UTF8.GetBytes(man ? MobileWeb.Manifest(Settings.Get("shop_name")) : MobileWeb.Html);
            ctx.Response.ContentType = man ? "application/manifest+json; charset=utf-8" : "text/html; charset=utf-8";
            ctx.Response.OutputStream.Write(page, 0, page.Length);
            ctx.Response.Close();
            return;
        }
        try
        {
            var q = ctx.Request.QueryString;
            var like = "%" + (q["q"] ?? "") + "%";
            if (q["token"] != Settings.Get("api_token")) { code = 401; result = new { error = "unauthorized" }; }
            else
            {
                result = ctx.Request.Url.AbsolutePath.TrimEnd('/') switch
                {
                    "/api/summary" => new
                    {
                        shop = Settings.Get("shop_name"), today_sales = Stats.TodaySales(), cash = Stats.CashTotal(),
                        receivables = Stats.Receivables(), payables = Stats.Payables(), low_stock = Stats.LowStock(),
                        expiring = Stats.Expiring(Settings.Int("expiry_days", 30)), due_installments = Stats.DueInstallments(0), repairs_open = Stats.RepairsOpen()
                    },
                    "/api/items" => Rows(Db.Query(@"SELECT i.id,i.name,i.barcode,i.unit,i.price_retail,i.price_wholesale,i.price_special,
                        IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0) AS stock
                        FROM items i WHERE i.name LIKE @p0 OR i.barcode LIKE @p0 LIMIT 200", like)),
                    "/api/parties" => Rows(Db.Query(@"SELECT p.id,p.name,p.phone,p.kind,b.balance FROM parties p
                        JOIN v_party_balance b ON b.id=p.id WHERE p.name LIKE @p0 OR p.phone LIKE @p0 LIMIT 200", like)),
                    "/api/installments" => Rows(Db.Query(@"SELECT t.id,p.name,p.phone,t.seq,t.due_date,t.amount,t.paid
                        FROM installments t JOIN parties p ON p.id=t.party_id WHERE t.amount-t.paid>0.001
                        AND (p.name LIKE @p0 OR IFNULL(p.phone,'') LIKE @p0) ORDER BY t.due_date LIMIT 300", like)),
                    "/api/repairs" => Rows(Db.Query(@"SELECT r.id,r.date_in,r.customer,r.phone,r.device,r.fault,r.status,r.estimate,
                        IFNULL((SELECT SUM(amount*rate) FROM cash_moves m WHERE m.repair_id=r.id),0) AS paid
                        FROM repairs r WHERE (r.status NOT IN ('تم التسليم','ملغي') OR @p1<>'') AND
                        (@p1='' OR CAST(r.id AS TEXT)=@p1 OR r.customer LIKE @p0 OR r.phone LIKE @p0 OR r.serial LIKE @p0)
                        ORDER BY r.id DESC LIMIT 300", like, q["q"] ?? "")),
                    "/api/sales" => Rows(Db.Query(@"SELECT v.id, v.date, IFNULL(p.name,'نقدي') AS party, v.net, v.paid FROM invoices v
                        LEFT JOIN parties p ON p.id=v.party_id WHERE v.type='Sale' AND v.date>=date('now','localtime') ORDER BY v.id DESC LIMIT 300")),
                    _ => null
                };
                if (result == null) { code = 404; result = new { error = "not found" }; }
            }
        }
        catch (Exception ex) { code = 500; result = new { error = ex.Message }; }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, opts));
        var res = ctx.Response;
        res.StatusCode = code;
        res.ContentType = "application/json; charset=utf-8";
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.Close();
    }

    static List<Dictionary<string, object>> Rows(DataTable dt) =>
        dt.Rows.Cast<DataRow>().Select(r => dt.Columns.Cast<DataColumn>()
            .ToDictionary(c => c.ColumnName, c => r[c] is DBNull ? null : r[c])).ToList();
}
