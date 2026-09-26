using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Raseed;

namespace Workshop;

/// <summary>
/// بيانات البرنامج: قاعدة SQLite واحدة (workshop.db) والسجلات محمّلة في الذاكرة للعرض والحساب الفوري.
/// كل تعديل يُكتب فورًا في قاعدة البيانات (سطر واحد فقط، لا الملف كله).
/// </summary>
public static class Store
{
    public static string DataDir => AppPaths.DataDir;
    public static string FilePath => Path.Combine(DataDir, "workshop.db");

    public static List<Order> Orders { get; private set; } = new();
    public static List<Order> Trash { get; private set; } = new();
    public static List<InvItem> Inventory { get; private set; } = new();
    public static List<Expense> Expenses { get; private set; } = new();
    public static List<SupplierTx> SupplierTx { get; private set; } = new();
    public static List<Driver> Drivers { get; private set; } = new();
    public static List<Defect> Defects { get; private set; } = new();
    public static List<Reminder> Reminders { get; private set; } = new();
    public static List<Account> Accounts { get; private set; } = new();

    // ---------- سجلات إضافية عامة (الموظفون، الحضور، السُّلف، الرواتب) ----------
    public static readonly string[] ExtraKinds = { "employees", "attendance", "advances", "salaries", "withdrawals", "transfers", "credit_grants", "scrap", "tools" };
    static Dictionary<string, List<JsonObject>> extraDocs = new();
    public static List<JsonObject> Docs(string kind) => extraDocs.TryGetValue(kind, out var l) ? l : extraDocs[kind] = new();

    public static void PutDoc(string kind, string id, JsonObject data)
    {
        data["id"] = id;
        Put(kind, id, data);
        var l = Docs(kind);
        int k = l.FindIndex(x => x["id"]?.ToString() == id);
        if (k >= 0) l[k] = data; else l.Add(data);
        Touch();
    }

    public static void DelDoc(string kind, string id)
    {
        Del(kind, id);
        Docs(kind).RemoveAll(x => x["id"]?.ToString() == id);
        Touch();
    }

    /// <summary>بعد أي تعديل: الشاشة الحالية تُحدَّث والعدادات في القائمة الجانبية</summary>
    public static event Action Changed;
    public static void NotifyChanged()
    {
        Undo.Close();
        Changed?.Invoke();
    }

    static string connStr;
    public static SqliteConnection Open()
    {
        var c = new SqliteConnection(connStr);
        c.Open();
        return c;
    }

    const string Schema = @"
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS docs(kind TEXT NOT NULL, id TEXT NOT NULL, data TEXT NOT NULL, PRIMARY KEY(kind, id));
CREATE TABLE IF NOT EXISTS photos(ref TEXT PRIMARY KEY, data BLOB NOT NULL);";

    // أنواع السجلات في جدول docs
    const string KOrder = "orders", KTrash = "trash", KInv = "inventory", KExp = "expenses", KStx = "supplierTx", KDrv = "drivers", KDef = "defects", KRem = "reminders", KAcc = "accounts";

    public static void Init()
    {
        Directory.CreateDirectory(DataDir);
        connStr = new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = true }.ToString();
        using (var c = Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            try { cmd.ExecuteNonQuery(); } catch { }
            cmd.CommandText = Schema;
            cmd.ExecuteNonQuery();
        }
        LoadSettings();
        Undo.Clear();
        LoadAll();
    }

    // ================= القراءة =================
    static List<T> Load<T>(SqliteConnection c, string kind, Func<JsonNode, T> norm) where T : class
    {
        var list = new List<T>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT data FROM docs WHERE kind=$k";
        cmd.Parameters.AddWithValue("$k", kind);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            try { var x = norm(JsonNode.Parse(r.GetString(0))); if (x != null) list.Add(x); }
            catch { /* سجل تالف: يُتجاوز بدل إيقاف البرنامج */ }
        }
        return list;
    }

    public static void LoadAll()
    {
        using var c = Open();
        Orders = Load(c, KOrder, Json.Order).OrderByDescending(o => o.CreatedAt, StringComparer.Ordinal).ToList();
        Trash = Load(c, KTrash, Json.Order).OrderByDescending(o => o.DeletedAt ?? "", StringComparer.Ordinal).ToList();
        Inventory = Load(c, KInv, Json.Inv).OrderByDescending(i => i.UpdatedAt, StringComparer.Ordinal).ToList();
        Expenses = Load(c, KExp, Json.Expense).OrderByDescending(e => e.Date, StringComparer.Ordinal).ToList();
        SupplierTx = Load(c, KStx, Json.Stx);
        Drivers = Load(c, KDrv, Json.Driver);
        Reminders = Load(c, KRem, Json.Reminder).OrderBy(r => r.Date, StringComparer.Ordinal).ToList();
        Accounts = Load(c, KAcc, Json.Account).OrderBy(a => a.Name, StringComparer.CurrentCulture).ToList();
        extraDocs = ExtraKinds.ToDictionary(k => k, k => Load(c, k, n => n as JsonObject));
        Defects = Load(c, KDef, Json.Defect).OrderByDescending(d => d.Date, StringComparer.Ordinal).ThenByDescending(d => d.Id, StringComparer.Ordinal).ToList();
        // الأرقام المرجعية الناقصة تُكمَّل مرة واحدة (لا تدخل سجل التراجع)
        using var pause = Undo.Pause();
        var used = new HashSet<string>(Orders.Concat(Trash).Select(o => o.RefNo));
        foreach (var o in Orders.Concat(Trash).Where(o => o.RefNo == "").ToList())
        {
            o.RefNo = Txt.GenRef(used);
            used.Add(o.RefNo);
            Put(Trash.Contains(o) ? KTrash : KOrder, o.Id, Json.ToJson(o));
        }
    }

    // ================= الكتابة =================
    /// <summary>القيمة المحفوظة حالياً (لسجل التراجع)</summary>
    static string Current(string kind, string id, SqliteConnection c, SqliteTransaction t)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = "SELECT data FROM docs WHERE kind=$k AND id=$i";
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$i", id);
        return cmd.ExecuteScalar() as string;
    }

    static void Put(string kind, string id, JsonNode data, SqliteConnection c = null, SqliteTransaction t = null)
    {
        bool own = c == null;
        c ??= Open();
        try
        {
            if (Undo.Recording) Undo.Record(kind, id, Current(kind, id, c, t));
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;
            cmd.CommandText = "INSERT OR REPLACE INTO docs(kind,id,data) VALUES($k,$i,$d)";
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$i", id);
            cmd.Parameters.AddWithValue("$d", data.ToJsonString());
            cmd.ExecuteNonQuery();
        }
        finally { if (own) c.Dispose(); }
    }

    static void Del(string kind, string id, SqliteConnection c = null, SqliteTransaction t = null)
    {
        bool own = c == null;
        c ??= Open();
        try
        {
            if (Undo.Recording) Undo.Record(kind, id, Current(kind, id, c, t));
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;
            cmd.CommandText = "DELETE FROM docs WHERE kind=$k AND id=$i";
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$i", id);
            cmd.ExecuteNonQuery();
        }
        finally { if (own) c.Dispose(); }
    }

    static void Touch() { AutoBackup.Schedule(); }

    public static void SaveOrder(Order o)
    {
        Put(KOrder, o.Id, Json.ToJson(o));
        int i = Orders.FindIndex(x => x.Id == o.Id);
        if (i >= 0) Orders[i] = o; else Orders.Insert(0, o);
        Touch();
    }

    /// <summary>نقل الطلب إلى المحذوفات (يمكن استعادته)</summary>
    public static void MoveToTrash(Order o)
    {
        o.DeletedAt = Txt.Now;
        using (var c = Open())
        using (var t = c.BeginTransaction())
        {
            Del(KOrder, o.Id, c, t);
            Put(KTrash, o.Id, Json.ToJson(o), c, t);
            t.Commit();
        }
        Orders.RemoveAll(x => x.Id == o.Id);
        Trash.Insert(0, o);
        Touch();
    }

    public static Order RestoreFromTrash(string id)
    {
        var o = Trash.FirstOrDefault(x => x.Id == id);
        if (o == null) return null;
        string oldId = o.Id;
        o.DeletedAt = null;
        if (Orders.Any(x => x.Id == o.Id)) o.Id = Txt.Uid();
        using (var c = Open())
        using (var t = c.BeginTransaction())
        {
            Del(KTrash, oldId, c, t);
            Put(KOrder, o.Id, Json.ToJson(o), c, t);
            t.Commit();
        }
        Trash.Remove(o);
        Orders.Insert(0, o);
        Touch();
        return o;
    }

    public static void PurgeTrash(string id)
    {
        var o = Trash.FirstOrDefault(x => x.Id == id);
        if (o == null) return;
        Del(KTrash, id);
        if (o.PhotoRef != null && !Orders.Any(x => x.PhotoRef == o.PhotoRef)) RemovePhoto(o.PhotoRef);
        Trash.Remove(o);
        Touch();
    }

    public static void SaveInv(InvItem i)
    {
        Put(KInv, i.Id, Json.ToJson(i));
        int k = Inventory.FindIndex(x => x.Id == i.Id);
        if (k >= 0) Inventory[k] = i; else Inventory.Insert(0, i);
        Touch();
    }

    public static void SaveInvMany(IEnumerable<InvItem> items)
    {
        using (var c = Open())
        using (var t = c.BeginTransaction())
        {
            foreach (var i in items) Put(KInv, i.Id, Json.ToJson(i), c, t);
            t.Commit();
        }
        Touch();
    }

    public static void DeleteInv(InvItem i) { Del(KInv, i.Id); Inventory.Remove(i); Touch(); }

    public static void AddExpense(Expense e) { Put(KExp, e.Id, Json.ToJson(e)); Expenses.Insert(0, e); Touch(); }
    public static void DeleteExpense(Expense e) { Del(KExp, e.Id); Expenses.Remove(e); Touch(); }

    public static void AddSupplierTx(SupplierTx t) { Put(KStx, t.Id, Json.ToJson(t)); SupplierTx.Add(t); Touch(); }
    public static void DeleteSupplierTx(SupplierTx t) { Del(KStx, t.Id); SupplierTx.Remove(t); Touch(); }

    public static void SaveDriver(Driver d)
    {
        Put(KDrv, d.Id, Json.ToJson(d));
        int k = Drivers.FindIndex(x => x.Id == d.Id);
        if (k >= 0) Drivers[k] = d; else Drivers.Add(d);
        Touch();
    }
    public static void DeleteDriver(Driver d) { Del(KDrv, d.Id); Drivers.Remove(d); Touch(); }

    public static void SaveDefect(Defect d)
    {
        Put(KDef, d.Id, Json.ToJson(d));
        int k = Defects.FindIndex(x => x.Id == d.Id);
        if (k >= 0) Defects[k] = d; else Defects.Insert(0, d);
        Touch();
    }
    public static void DeleteDefect(Defect d) { Del(KDef, d.Id); Defects.Remove(d); Touch(); }

    public static void SaveReminder(Reminder r)
    {
        Put(KRem, r.Id, Json.ToJson(r));
        int k = Reminders.FindIndex(x => x.Id == r.Id);
        if (k >= 0) Reminders[k] = r; else Reminders.Add(r);
        Touch();
    }
    public static void DeleteReminder(Reminder r) { Del(KRem, r.Id); Reminders.Remove(r); Touch(); }

    public static void SaveAccount(Account a)
    {
        Put(KAcc, a.Id, Json.ToJson(a));
        int k = Accounts.FindIndex(x => x.Id == a.Id);
        if (k >= 0) Accounts[k] = a; else Accounts.Add(a);
        Touch();
    }
    public static void DeleteAccount(Account a) { Del(KAcc, a.Id); Accounts.Remove(a); Touch(); }

    /// <summary>استبدال كل البيانات (استعادة نسخة أو مسح) في معاملة واحدة. defects = null: تبقى القطع المعيبة الحالية</summary>
    public static void ReplaceAll(List<Order> orders, List<Order> trash, List<InvItem> inv, List<Expense> exps, List<SupplierTx> stx, List<Driver> drv, List<Defect> defects = null,
                                  List<Reminder> reminders = null, List<Account> accounts = null, Dictionary<string, List<JsonObject>> docs = null)
    {
        docs ??= ExtraKinds.ToDictionary(k => k, k => Docs(k).Select(x => (JsonObject)x.DeepClone()).ToList());
        Undo.Clear();
        using var pause = Undo.Pause();
        defects ??= Defects.ToList();
        reminders ??= Reminders.ToList();
        accounts ??= Accounts.ToList();
        using (var c = Open())
        using (var t = c.BeginTransaction())
        {
            using (var cmd = c.CreateCommand()) { cmd.Transaction = t; cmd.CommandText = "DELETE FROM docs"; cmd.ExecuteNonQuery(); }
            foreach (var o in orders) Put(KOrder, o.Id, Json.ToJson(o), c, t);
            foreach (var o in trash) Put(KTrash, o.Id, Json.ToJson(o), c, t);
            foreach (var i in inv) Put(KInv, i.Id, Json.ToJson(i), c, t);
            foreach (var e in exps) Put(KExp, e.Id, Json.ToJson(e), c, t);
            foreach (var s in stx) Put(KStx, s.Id, Json.ToJson(s), c, t);
            foreach (var d in drv) Put(KDrv, d.Id, Json.ToJson(d), c, t);
            foreach (var d in defects) Put(KDef, d.Id, Json.ToJson(d), c, t);
            foreach (var r in reminders) Put(KRem, r.Id, Json.ToJson(r), c, t);
            foreach (var a in accounts) Put(KAcc, a.Id, Json.ToJson(a), c, t);
            foreach (var (kind, list) in docs)
                foreach (var d in list) if (d["id"]?.ToString() is string did && did != "") Put(kind, did, d, c, t);
            // صور لم يعد يستعملها أي طلب
            var refs = orders.Concat(trash).Select(o => o.PhotoRef).Where(r => r != null).ToHashSet();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = t;
                cmd.CommandText = "SELECT ref FROM photos";
                var all = new List<string>();
                using (var r = cmd.ExecuteReader()) while (r.Read()) all.Add(r.GetString(0));
                foreach (var x in all.Where(x => !refs.Contains(x)))
                {
                    using var d = c.CreateCommand();
                    d.Transaction = t;
                    d.CommandText = "DELETE FROM photos WHERE ref=$r";
                    d.Parameters.AddWithValue("$r", x);
                    d.ExecuteNonQuery();
                }
            }
            t.Commit();
        }
        LoadAll();
        Touch();
    }

    // ================= صور الأجهزة =================
    public static byte[] GetPhoto(string photoRef)
    {
        if (string.IsNullOrEmpty(photoRef)) return null;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT data FROM photos WHERE ref=$r";
        cmd.Parameters.AddWithValue("$r", photoRef);
        return cmd.ExecuteScalar() as byte[];
    }

    public static Image LoadPhoto(string photoRef)
    {
        var b = GetPhoto(photoRef);
        if (b == null) return null;
        try
        {
            using var ms = new MemoryStream(b);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    public static void SetPhoto(string photoRef, byte[] data)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO photos(ref,data) VALUES($r,$d)";
        cmd.Parameters.AddWithValue("$r", photoRef);
        cmd.Parameters.AddWithValue("$d", data);
        cmd.ExecuteNonQuery();
    }

    public static void RemovePhoto(string photoRef)
    {
        if (string.IsNullOrEmpty(photoRef)) return;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM photos WHERE ref=$r";
        cmd.Parameters.AddWithValue("$r", photoRef);
        cmd.ExecuteNonQuery();
    }

    public static int PhotoCount()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM photos";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static Dictionary<string, byte[]> AllPhotos()
    {
        var d = new Dictionary<string, byte[]>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ref, data FROM photos";
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = (byte[])r[1];
        return d;
    }

    // ================= الإعدادات =================
    static readonly Dictionary<string, string> settings = new();

    static void LoadSettings()
    {
        settings.Clear();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings";
        using var r = cmd.ExecuteReader();
        while (r.Read()) settings[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
    }

    public static string Get(string key, string def = "") => settings.TryGetValue(key, out var v) ? v : def;
    public static bool Flag(string key, bool def = false) => settings.TryGetValue(key, out var v) ? v == "1" : def;

    /// <summary>كتابة سجل كما هو (للتراجع)</summary>
    internal static void Raw(string kind, string id, string json)
    {
        if (json == null) Del(kind, id); else Put(kind, id, JsonNode.Parse(json));
    }

    public static void Set(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings(key,value) VALUES($k,$v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value ?? "");
        cmd.ExecuteNonQuery();
        settings[key] = value ?? "";
    }
    public static void SetFlag(string key, bool on) => Set(key, on ? "1" : "0");

    /// <summary>الإعدادات التي تنتقل مع نسخة JSON: القوائم المعدّلة والفنيون وقوالب الرسائل</summary>
    public static bool IsCustomKey(string k) => k.StartsWith("list_") || k.StartsWith("tpl_") || k is "technicians" or "sup_warranty" or "lock_delivered" or "issue_terms" or "qc_required" or "stale_days"
        or "ext_warranties" or "loyalty" or "referral_reward" or "google_review_url" or "auto_assign" or "suggest_message" or "box_opening";
    public static IEnumerable<KeyValuePair<string, string>> CustomSettings() => settings.Where(kv => IsCustomKey(kv.Key) && kv.Value != "").ToList();

    public static string ShopName => Get("shop_name", "ورشة الصيانة") is var n && n != "" ? n : "ورشة الصيانة";
    public static string ShopPhone => Get("shop_phone");
    public static string ShopAddress => Get("shop_address");
    public const string DefaultTerms = "يسري الضمان من تاريخ التسليم ولا يشمل الكسر أو السقوط أو السوائل أو سوء الاستخدام.";
    public static string Terms => Get("shop_terms", DefaultTerms);
    public static string Currency => Get("currency", "د.ع") is var c && c != "" ? c : "د.ع";
    public static string CountryCode => Get("country_code", "964") is var c && c != "" ? c : "964";
    public static bool LabelPasscode => Flag("privacy_label_passcode");
    public static bool ClearPasscodeOnDelivery => Flag("privacy_clear_passcode");
    public static bool DashCell(string k) => Flag("dash_" + k, true);
    public static bool Col(string k) => Flag("col_" + k, true);
    public static bool Compact => Flag("compact");
}
