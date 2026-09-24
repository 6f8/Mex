using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Raseed;

/// <summary>طبقة قاعدة البيانات (SQLite) — ملف واحد محلي سهل النسخ الاحتياطي.</summary>
public static class Db
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Raseed");

    public static string FilePath => Path.Combine(DataDir, "raseed.db");

    public static SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={FilePath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return c;
    }

    internal static void Bind(SqliteCommand cmd, object[] p)
    {
        if (p == null) return;
        for (int i = 0; i < p.Length; i++)
            cmd.Parameters.AddWithValue("@p" + i, p[i] ?? DBNull.Value);
    }

    public static int Exec(string sql, params object[] p) { using var t = new Tx(); var r = t.Exec(sql, p); t.Commit(); return r; }
    public static long Insert(string sql, params object[] p) { using var t = new Tx(); var id = t.Insert(sql, p); t.Commit(); return id; }
    public static object Scalar(string sql, params object[] p) { using var t = new Tx(); return t.Scalar(sql, p); }
    public static DataTable Query(string sql, params object[] p) { using var t = new Tx(); return t.Query(sql, p); }

    // تحويلات آمنة
    public static double D(object o) => o == null || o is DBNull ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture);
    public static long L(object o) => o == null || o is DBNull ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);
    public static string S(object o) => o == null || o is DBNull ? "" : Convert.ToString(o, CultureInfo.InvariantCulture);
    public static object N(long id) => id > 0 ? (object)id : DBNull.Value;

    public static void Init()
    {
        Directory.CreateDirectory(DataDir);
        using (var t = new Tx())
        {
            t.Exec(Schema);
            foreach (var s in Settings.All)
                t.Exec("INSERT OR IGNORE INTO settings(key,value) VALUES(@p0,@p1)", s.Key, s.Def);
            Migrate(t);
            t.Exec(BalanceView);
            if (L(t.Scalar("SELECT COUNT(*) FROM users")) == 0) Seed(t);
            t.Commit();
        }
        if (Settings.Get("api_token") == "")
            Settings.Set("api_token", Guid.NewGuid().ToString("N")[..12]);
    }

    /// <summary>ترقية قواعد البيانات القديمة: إضافة الأعمدة الجديدة إن لم تكن موجودة</summary>
    static void Migrate(Tx t)
    {
        var cols = new (string Table, string Col, string Def)[]
        {
            ("cash_moves", "repair_id", "INTEGER"),
            ("items", "warranty_days", "INTEGER DEFAULT 0"),
        };
        foreach (var (table, col, def) in cols)
        {
            bool exists = t.Query($"PRAGMA table_info({table})").Rows.Cast<DataRow>().Any(r => S(r["name"]) == col);
            if (!exists) t.Exec($"ALTER TABLE {table} ADD COLUMN {col} {def}");
        }
    }

    /// <summary>سجل العمليات الحساسة (حذف، تعديل فاتورة ...)</summary>
    public static void Audit(string action, string details)
    {
        try { Exec("INSERT INTO audit_log(date,user_id,action,details) VALUES(@p0,@p1,@p2,@p3)", Ui.Now, Session.UserId, action, details); } catch { }
    }

    // رصيد الجهة: موجب = مدين (عليه لنا)، سالب = دائن (له علينا)
    const string BalanceView = @"
DROP VIEW IF EXISTS v_party_balance;
CREATE VIEW v_party_balance AS
SELECT p.id, p.opening_balance
  + IFNULL((SELECT SUM(CASE i.type WHEN 'Sale' THEN i.net WHEN 'PurchaseReturn' THEN i.net
                                   WHEN 'Purchase' THEN -i.net WHEN 'SaleReturn' THEN -i.net ELSE 0 END)
            FROM invoices i WHERE i.party_id=p.id),0)
  + IFNULL((SELECT SUM(r.final_price) FROM repairs r WHERE r.party_id=p.id AND r.status='تم التسليم'),0)
  - IFNULL((SELECT SUM(m.amount*m.rate) FROM cash_moves m WHERE m.party_id=p.id),0) AS balance
FROM parties p;";

    static void Seed(Tx t)
    {
        t.Exec("INSERT INTO users(username,pass_hash,full_name,is_admin) VALUES('admin',@p0,'المدير',1)", Session.Hash("admin", "admin"));
        t.Exec("INSERT INTO warehouses(name) VALUES('المخزن الرئيسي')");
        t.Exec("INSERT INTO cashboxes(name,kind,currency) VALUES('الصندوق الرئيسي','صندوق','IQD'),('خزينة الدولار','خزينة','USD')");
        t.Exec("INSERT INTO cost_centers(name) VALUES('عام')");
        var d = DateTime.Today;
        t.Exec(@"INSERT INTO tasks(title,kind,run_at,repeat) VALUES
            ('نسخ احتياطي يومي','نسخ احتياطي',@p0,'يومي'),
            ('إرسال تذكير الأقساط','تذكير الأقساط',@p1,'يومي'),
            ('فحص الصلاحيات والنفاد','فحص الصلاحية',@p2,'يومي')",
            d.AddHours(23).ToString(Ui.TFmt), d.AddDays(1).AddHours(10).ToString(Ui.TFmt), d.AddDays(1).AddHours(9).ToString(Ui.TFmt));
    }

    const string Schema = @"
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY, username TEXT UNIQUE NOT NULL, pass_hash TEXT NOT NULL,
    full_name TEXT, is_admin INTEGER DEFAULT 0, active INTEGER DEFAULT 1);
CREATE TABLE IF NOT EXISTS user_perms(user_id INTEGER, perm TEXT, PRIMARY KEY(user_id,perm));
CREATE TABLE IF NOT EXISTS warehouses(id INTEGER PRIMARY KEY, name TEXT NOT NULL, location TEXT, notes TEXT);
CREATE TABLE IF NOT EXISTS cashboxes(id INTEGER PRIMARY KEY, name TEXT NOT NULL, kind TEXT DEFAULT 'صندوق', currency TEXT DEFAULT 'IQD', notes TEXT);
CREATE TABLE IF NOT EXISTS cost_centers(id INTEGER PRIMARY KEY, name TEXT NOT NULL, notes TEXT);
CREATE TABLE IF NOT EXISTS delivery_companies(id INTEGER PRIMARY KEY, name TEXT NOT NULL, phone TEXT, fee REAL DEFAULT 0, notes TEXT);
CREATE TABLE IF NOT EXISTS partners(id INTEGER PRIMARY KEY, name TEXT NOT NULL, phone TEXT, share REAL DEFAULT 0);
CREATE TABLE IF NOT EXISTS employees(id INTEGER PRIMARY KEY, name TEXT NOT NULL, phone TEXT, job TEXT, salary REAL DEFAULT 0,
    hire_date TEXT, active INTEGER DEFAULT 1, notes TEXT);
CREATE TABLE IF NOT EXISTS items(id INTEGER PRIMARY KEY, code TEXT, barcode TEXT, name TEXT NOT NULL, category TEXT,
    unit TEXT DEFAULT 'قطعة', price_retail REAL DEFAULT 0, price_wholesale REAL DEFAULT 0, price_special REAL DEFAULT 0,
    min_qty REAL DEFAULT 0, by_measure INTEGER DEFAULT 0, medical_info TEXT, alert_note TEXT, active INTEGER DEFAULT 1);
CREATE INDEX IF NOT EXISTS ix_items_barcode ON items(barcode);
CREATE TABLE IF NOT EXISTS parties(id INTEGER PRIMARY KEY, name TEXT NOT NULL, kind TEXT DEFAULT 'عميل', phone TEXT, address TEXT,
    price_level TEXT DEFAULT 'مفرد', credit_limit REAL DEFAULT 0, opening_balance REAL DEFAULT 0, notes TEXT);
CREATE TABLE IF NOT EXISTS batches(id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL REFERENCES items(id),
    warehouse_id INTEGER NOT NULL REFERENCES warehouses(id), expiry TEXT, qty REAL NOT NULL DEFAULT 0, cost REAL DEFAULT 0, created TEXT);
CREATE INDEX IF NOT EXISTS ix_batches_item ON batches(item_id, warehouse_id);
CREATE TABLE IF NOT EXISTS invoices(id INTEGER PRIMARY KEY, type TEXT NOT NULL, date TEXT NOT NULL,
    party_id INTEGER REFERENCES parties(id), warehouse_id INTEGER REFERENCES warehouses(id), cashbox_id INTEGER, cost_center_id INTEGER,
    price_level TEXT, pay_type TEXT, total REAL, discount REAL DEFAULT 0, delivery_id INTEGER, delivery_fee REAL DEFAULT 0,
    delivery_status TEXT, net REAL, paid REAL DEFAULT 0, notes TEXT, user_id INTEGER);
CREATE TABLE IF NOT EXISTS invoice_lines(id INTEGER PRIMARY KEY, invoice_id INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    item_id INTEGER NOT NULL REFERENCES items(id), batch_id INTEGER, length REAL, width REAL, qty REAL NOT NULL, price REAL NOT NULL,
    cost REAL DEFAULT 0, expiry TEXT);
CREATE TABLE IF NOT EXISTS cash_moves(id INTEGER PRIMARY KEY, date TEXT NOT NULL, kind TEXT NOT NULL,
    cashbox_id INTEGER NOT NULL REFERENCES cashboxes(id), amount REAL NOT NULL, rate REAL DEFAULT 1,
    party_id INTEGER REFERENCES parties(id), employee_id INTEGER REFERENCES employees(id), cost_center_id INTEGER,
    invoice_id INTEGER, installment_id INTEGER, ref TEXT, note TEXT, user_id INTEGER);
CREATE TABLE IF NOT EXISTS installments(id INTEGER PRIMARY KEY, invoice_id INTEGER, party_id INTEGER NOT NULL REFERENCES parties(id),
    seq INTEGER, due_date TEXT NOT NULL, amount REAL NOT NULL, paid REAL DEFAULT 0, paid_date TEXT, notified TEXT);
CREATE TABLE IF NOT EXISTS tasks(id INTEGER PRIMARY KEY, title TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'تذكير',
    run_at TEXT NOT NULL, repeat TEXT DEFAULT 'بدون', message TEXT, active INTEGER DEFAULT 1, last_run TEXT);

CREATE TABLE IF NOT EXISTS repairs(id INTEGER PRIMARY KEY, date_in TEXT NOT NULL, party_id INTEGER REFERENCES parties(id),
    customer TEXT, phone TEXT, device TEXT, serial TEXT, fault TEXT, accessories TEXT, lock_code TEXT, technician_id INTEGER,
    status TEXT DEFAULT 'مستلم', estimate REAL DEFAULT 0, final_price REAL DEFAULT 0, warehouse_id INTEGER, date_ready TEXT,
    date_out TEXT, warranty_days INTEGER DEFAULT 0, report TEXT, notes TEXT, user_id INTEGER);
CREATE INDEX IF NOT EXISTS ix_repairs_status ON repairs(status);
CREATE TABLE IF NOT EXISTS repair_parts(id INTEGER PRIMARY KEY, repair_id INTEGER NOT NULL REFERENCES repairs(id) ON DELETE CASCADE,
    item_id INTEGER NOT NULL REFERENCES items(id), batch_id INTEGER, qty REAL NOT NULL, cost REAL DEFAULT 0, date TEXT);
CREATE TABLE IF NOT EXISTS attendance(id INTEGER PRIMARY KEY, employee_id INTEGER NOT NULL REFERENCES employees(id), date TEXT NOT NULL,
    status TEXT DEFAULT 'حاضر', time_in TEXT, time_out TEXT, note TEXT, UNIQUE(employee_id, date));
CREATE TABLE IF NOT EXISTS hr_moves(id INTEGER PRIMARY KEY, employee_id INTEGER NOT NULL REFERENCES employees(id), date TEXT NOT NULL,
    kind TEXT NOT NULL, amount REAL NOT NULL, note TEXT, cash_ref TEXT, user_id INTEGER);
CREATE TABLE IF NOT EXISTS audit_log(id INTEGER PRIMARY KEY, date TEXT, user_id INTEGER, action TEXT, details TEXT);

";
}

/// <summary>معاملة (Transaction) — كل عملية مركبة تُحفظ كاملة أو لا تُحفظ.</summary>
public sealed class Tx : IDisposable
{
    readonly SqliteConnection c;
    readonly SqliteTransaction t;

    public Tx() { c = Db.Open(); t = c.BeginTransaction(); }

    SqliteCommand Cmd(string sql, object[] p)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        Db.Bind(cmd, p);
        return cmd;
    }

    public int Exec(string sql, params object[] p) { using var cmd = Cmd(sql, p); return cmd.ExecuteNonQuery(); }
    public object Scalar(string sql, params object[] p) { using var cmd = Cmd(sql, p); return cmd.ExecuteScalar(); }
    public long Insert(string sql, params object[] p) { Exec(sql, p); return Db.L(Scalar("SELECT last_insert_rowid()")); }

    /// <summary>قراءة يدوية إلى DataTable (تتجنب مشاكل القيود في DataTable.Load)</summary>
    public DataTable Query(string sql, params object[] p)
    {
        var dt = new DataTable();
        using var cmd = Cmd(sql, p);
        using var r = cmd.ExecuteReader();
        int n = r.FieldCount;
        var rows = new List<object[]>();
        while (r.Read()) { var v = new object[n]; r.GetValues(v); rows.Add(v); }

        for (int i = 0; i < n; i++)
        {
            Type type = null;
            foreach (var row in rows)
            {
                var v = row[i];
                if (v is DBNull) continue;
                var vt = v is long ? typeof(long) : v is double ? typeof(double) : typeof(string);
                if (type == null) type = vt;
                else if (type != vt)
                    type = (type != typeof(string) && vt != typeof(string)) ? typeof(double) : typeof(string);
            }
            string name = r.GetName(i), nm = name; int k = 1;
            while (dt.Columns.Contains(nm)) nm = name + (++k);
            dt.Columns.Add(nm, type ?? typeof(string));
        }

        foreach (var row in rows)
        {
            var dr = dt.NewRow();
            for (int i = 0; i < n; i++)
            {
                var v = row[i];
                var ct = dt.Columns[i].DataType;
                dr[i] = v is DBNull ? DBNull.Value
                      : ct == typeof(string) ? Convert.ToString(v, CultureInfo.InvariantCulture)
                      : ct == typeof(double) ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
                      : v;
            }
            dt.Rows.Add(dr);
        }
        return dt;
    }

    public void Commit() => t.Commit();
    public void Dispose() { t.Dispose(); c.Dispose(); }
}
