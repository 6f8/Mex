using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Workshop;

/// <summary>النسخ الاحتياطي: نسخة من قاعدة البيانات يدويًا، ونسخة يومية تلقائية في مجلد يختاره المستخدم</summary>
public static class Backup
{
    public static string DefaultDir => Path.Combine(Store.DataDir, "Backups");

    /// <summary>نسخة كاملة من قاعدة البيانات (مع الصور) في ملف .db</summary>
    public static void CopyTo(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        if (File.Exists(file)) File.Delete(file);
        using var src = Store.Open();
        using var dst = new SqliteConnection($"Data Source={file};Pooling=False");
        dst.Open();
        src.BackupDatabase(dst);
    }

    public static string Run(string dir = null)
    {
        dir ??= DefaultDir;
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var file = Path.Combine(dir, $"workshop_{stamp}.db");
        for (int k = 2; File.Exists(file); k++) file = Path.Combine(dir, $"workshop_{stamp}_{k}.db");
        CopyTo(file);
        Prune(dir, "workshop_*.db", 30);
        Store.Set("last_backup", Txt.Now);
        return file;
    }

    public static void Prune(string dir, string pattern, int keep)
    {
        foreach (var f in Directory.GetFiles(dir, pattern).OrderByDescending(x => x, StringComparer.Ordinal).Skip(keep))
            try { File.Delete(f); } catch { }
    }

    public static bool IsValid(string file, out string error)
    {
        error = "";
        try
        {
            using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('docs','settings','photos')";
            if (Convert.ToInt64(cmd.ExecuteScalar()) < 3) { error = "هذا الملف ليس نسخة احتياطية من برنامج الورشة."; return false; }
            return true;
        }
        catch (Exception ex) { error = "تعذرت قراءة الملف: " + ex.Message; return false; }
    }

    /// <summary>استعادة نسخة .db: تُحفظ نسخة أمان من البيانات الحالية أولًا</summary>
    public static void Restore(string file)
    {
        if (!IsValid(file, out var err)) throw new InvalidOperationException(err);
        try { Run(); } catch { }
        using (var src = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False"))
        using (var dst = Store.Open())
        {
            src.Open();
            src.BackupDatabase(dst);
        }
        SqliteConnection.ClearAllPools();
        Store.Init();
    }

    public static int DaysSinceLast()
    {
        var last = Store.Get("last_backup");
        return last == "" ? -1 : Txt.DaysBetween(Txt.Cut10(last), Txt.Today);
    }

    // ---------- نسخة ما قبل المسح ----------
    public static string SnapshotFile => Path.Combine(Store.DataDir, "pre_reset_snapshot.db");
    public static bool HasSnapshot => File.Exists(SnapshotFile);

    public static void ResetAll()
    {
        CopyTo(SnapshotFile);
        Store.ReplaceAll(new(), new(), new(), new(), new(), new(), new(), new(), new(), Store.ExtraKinds.ToDictionary(k => k, _ => new List<JsonObject>()));
    }

    public static void RestoreSnapshot()
    {
        using (var src = new SqliteConnection($"Data Source={SnapshotFile};Mode=ReadOnly;Pooling=False"))
        using (var dst = Store.Open())
        {
            src.Open();
            src.BackupDatabase(dst);
        }
        SqliteConnection.ClearAllPools();
        Store.Init();
        DropSnapshot();
    }

    public static void DropSnapshot() { try { File.Delete(SnapshotFile); } catch { } }
}

/// <summary>نسخة يومية تلقائية إلى مجلد (Google Drive / OneDrive / فلاشة) بعد كل مجموعة تعديلات، مع الاحتفاظ بآخر 30 يومًا</summary>
public static class AutoBackup
{
    static System.Windows.Forms.Timer timer;
    public static string Dir => Store.Get("auto_backup_dir");
    public static string LastAt => Store.Get("auto_backup_at");
    public static string Error { get; private set; } = "";

    public static void Schedule()
    {
        if (Dir == "") return;
        Arm();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Arm()
    {
        if (timer == null)
        {
            timer = new System.Windows.Forms.Timer { Interval = 45_000 };
            timer.Tick += (s, e) => { timer.Stop(); Run(); };
        }
        timer.Stop();
        timer.Start();
    }

    public static bool Run()
    {
        if (Dir == "") return false;
        try
        {
            Directory.CreateDirectory(Dir);
            Backup.CopyTo(Path.Combine(Dir, $"workshop-backup-{Txt.Today}.db"));   // ملف لكل يوم يُحدَّث خلال اليوم
            Backup.Prune(Dir, "workshop-backup-*.db", 30);
            Store.Set("auto_backup_at", Txt.Now);
            Store.Set("last_backup", Txt.Now);
            Error = "";
            return true;
        }
        catch (Exception ex) { Error = ex.Message; return false; }
    }
}

/// <summary>
/// نسخة JSON متوافقة مع نسخة المتصفح السابقة من البرنامج: لنقل البيانات منها إلى هذا البرنامج (دمج أو استبدال) أو العكس.
/// </summary>
public static class WebBackup
{
    public class Pending
    {
        public List<Order> Orders = new(), Trash = new();
        public List<InvItem> Inventory = new();
        public List<Expense> Expenses = new();
        public List<SupplierTx> SupplierTx = new();
        public List<Driver> Drivers = new();
        public List<Defect> Defects = new();
        public List<Reminder> Reminders = new();
        public List<Account> Accounts = new();
        public Dictionary<string, List<JsonObject>> Docs;
        public string Branch = "";
        public Dictionary<string, string> Photos = new();
        public JsonObject Settings;
        public bool LegacyArray;
    }

    static List<T> ReadList<T>(JsonNode n, Func<JsonNode, T> f) where T : class
    {
        var list = new List<T>();
        var seen = new HashSet<string>();
        if (n is JsonArray a)
            foreach (var x in a)
            {
                T v;
                try { v = f(x); } catch { continue; }
                if (v == null) continue;
                // المعرّف المكرر يُعطى معرّفًا جديدًا
                var idField = v.GetType().GetField("Id");
                var id = (string)idField.GetValue(v);
                if (!seen.Add(id)) { idField.SetValue(v, Txt.Uid("x")); seen.Add((string)idField.GetValue(v)); }
                list.Add(v);
            }
        return list;
    }

    public static Pending Read(string file)
    {
        var node = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8));
        var raw = node is JsonArray ? new JsonObject { ["orders"] = node.DeepClone() } : node as JsonObject;
        if (raw == null || raw["orders"] is not JsonArray) throw new InvalidOperationException("هذا الملف ليس نسخة احتياطية صالحة لبرنامج الورشة.");
        var p = new Pending
        {
            Orders = ReadList(raw["orders"], Json.Order), Trash = ReadList(raw["trash"], Json.Order),
            Expenses = ReadList(raw["expenses"], Json.Expense), Inventory = ReadList(raw["inventory"], Json.Inv),
            SupplierTx = ReadList(raw["supplierTx"], Json.Stx), Drivers = ReadList(raw["drivers"], Json.Driver),
            Defects = ReadList(raw["defects"], Json.Defect),
            Reminders = ReadList(raw["reminders"], Json.Reminder), Accounts = ReadList(raw["accounts"], Json.Account),
            Docs = raw["docs"] is JsonObject dd ? Store.ExtraKinds.ToDictionary(k => k, k => (dd[k] as JsonArray)?.OfType<JsonObject>().Select(x => (JsonObject)x.DeepClone()).ToList() ?? new List<JsonObject>()) : null,
            Settings = raw["settings"] as JsonObject, LegacyArray = node is JsonArray,
            Branch = raw["branch"]?.ToString() ?? "",
        };
        if (raw["photos"] is JsonObject ph)
            foreach (var (k, v) in ph) if (v != null) p.Photos[k] = v.ToString();
        // صور مضمّنة داخل الطلب نفسه (نسخ قديمة)
        if (raw["orders"] is JsonArray oa)
            foreach (var o in oa.Concat(raw["trash"] as JsonArray ?? new JsonArray()))
                if (o?["photo"]?.ToString() is string data && data.StartsWith("data:image") && o["id"]?.ToString() is string id)
                {
                    var r = "ph_" + id;
                    p.Photos[r] = data;
                    foreach (var x in p.Orders.Concat(p.Trash).Where(x => x.Id == id && x.PhotoRef == null)) x.PhotoRef = r;
                }
        return p;
    }

    static byte[] FromDataUrl(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.StartsWith("data:image/")) return null;
        int i = s.IndexOf("base64,", StringComparison.Ordinal);
        if (i < 0) return null;
        try { return Convert.FromBase64String(s[(i + 7)..].Trim()); } catch { return null; }
    }

    /// <summary>replace=true: استبدال كل البيانات، وإلا دمج (إضافة غير الموجود). يعيد عدد الطلبات المضافة</summary>
    public static int Apply(Pending p, bool replace)
    {
        foreach (var (r, data) in p.Photos)
        {
            var b = FromDataUrl(data);
            if (b != null) Store.SetPhoto(r, b);
        }
        int added = 0;
        if (replace)
        {
            Store.ReplaceAll(p.Orders, p.Trash,
                p.LegacyArray ? Store.Inventory : p.Inventory, p.LegacyArray ? Store.Expenses : p.Expenses,
                p.LegacyArray ? Store.SupplierTx : p.SupplierTx, p.LegacyArray ? Store.Drivers : p.Drivers,
                p.LegacyArray ? null : p.Defects, p.LegacyArray ? null : p.Reminders, p.LegacyArray ? null : p.Accounts, p.Docs);
            added = p.Orders.Count;
            if (p.Settings?["shop"] is JsonObject shop)
            {
                void S(string key, string jk) { if (shop[jk]?.ToString() is string v) Store.Set(key, v); }
                S("shop_name", "name"); S("shop_phone", "phone"); S("shop_address", "address"); S("shop_terms", "terms");
                if (p.Settings["currency"]?.ToString() is string cur && cur != "") Store.Set("currency", cur);
                if (p.Settings["countryCode"]?.ToString() is string cc && cc != "") Store.Set("country_code", cc);
                if (p.Settings["desktop"] is JsonObject desk)
                    foreach (var (k, v) in desk)
                        if (Store.IsCustomKey(k) && v is JsonValue) Store.Set(k, v.ToString());
                if (p.Settings["privacy"] is JsonObject pr)
                {
                    if (pr["labelPasscode"] is JsonValue lp) Store.SetFlag("privacy_label_passcode", lp.ToString() == "true");
                    if (pr["clearPasscodeOnDelivery"] is JsonValue cp) Store.SetFlag("privacy_clear_passcode", cp.ToString() == "true");
                }
            }
        }
        else
        {
            List<T> Merge<T>(List<T> cur, List<T> src, Func<T, string> id, ref int n)
            {
                var ids = cur.Select(id).ToHashSet();
                var res = cur.ToList();
                foreach (var x in src) if (ids.Add(id(x))) { res.Add(x); n++; }
                return res;
            }
            int dummy = 0;
            var orders = Merge(Store.Orders, p.Orders, o => o.Id, ref added);
            var trash = Merge(Store.Trash, p.Trash, o => o.Id, ref dummy);
            var inv = Merge(Store.Inventory, p.Inventory, i => i.Id, ref dummy);
            var exps = Merge(Store.Expenses, p.Expenses, e => e.Id, ref dummy);
            var stx = Merge(Store.SupplierTx, p.SupplierTx, t => t.Id, ref dummy);
            var drv = Merge(Store.Drivers, p.Drivers, d => d.Id, ref dummy);
            var defs = Merge(Store.Defects, p.Defects, d => d.Id, ref dummy);
            var rems = Merge(Store.Reminders, p.Reminders, r => r.Id, ref dummy);
            var accs = Merge(Store.Accounts, p.Accounts, a => a.Id, ref dummy);
            Dictionary<string, List<JsonObject>> docs = null;
            if (p.Docs != null)
                docs = Store.ExtraKinds.ToDictionary(k => k, k =>
                {
                    var cur = Store.Docs(k).Select(x => (JsonObject)x.DeepClone()).ToList();
                    var ids = cur.Select(x => x["id"]?.ToString()).ToHashSet();
                    cur.AddRange(p.Docs[k].Where(x => ids.Add(x["id"]?.ToString())));
                    return cur;
                });
            Store.ReplaceAll(orders, trash, inv, exps, stx, drv, defs, rems, accs, docs);
        }
        return added;
    }

    /// <summary>تصدير كل البيانات بصيغة نسخة المتصفح (مع الصور)</summary>
    public static void Export(string file)
    {
        var photos = new JsonObject();
        foreach (var (r, b) in Store.AllPhotos()) photos[r] = "data:image/jpeg;base64," + Convert.ToBase64String(b);
        var root = new JsonObject
        {
            ["app"] = "workshop", ["version"] = 3, ["exportedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["orders"] = new JsonArray(Store.Orders.Select(o => (JsonNode)Json.ToJson(o)).ToArray()),
            ["trash"] = new JsonArray(Store.Trash.Select(o => (JsonNode)Json.ToJson(o)).ToArray()),
            ["expenses"] = new JsonArray(Store.Expenses.Select(e => (JsonNode)Json.ToJson(e)).ToArray()),
            ["inventory"] = new JsonArray(Store.Inventory.Select(i => (JsonNode)Json.ToJson(i)).ToArray()),
            ["supplierTx"] = new JsonArray(Store.SupplierTx.Select(t => (JsonNode)Json.ToJson(t)).ToArray()),
            ["drivers"] = new JsonArray(Store.Drivers.Select(d => (JsonNode)Json.ToJson(d)).ToArray()),
            ["defects"] = new JsonArray(Store.Defects.Select(d => (JsonNode)Json.ToJson(d)).ToArray()),
            ["reminders"] = new JsonArray(Store.Reminders.Select(r => (JsonNode)Json.ToJson(r)).ToArray()),
            ["accounts"] = new JsonArray(Store.Accounts.Select(a => (JsonNode)Json.ToJson(a)).ToArray()),
            ["branch"] = Branches.Current,
            ["docs"] = new JsonObject(Store.ExtraKinds.Select(k => KeyValuePair.Create(k, (JsonNode)new JsonArray(Store.Docs(k).Select(x => (JsonNode)x.DeepClone()).ToArray())))),
            ["settings"] = new JsonObject
            {
                ["shop"] = new JsonObject { ["name"] = Store.ShopName, ["phone"] = Store.ShopPhone, ["address"] = Store.ShopAddress, ["terms"] = Store.Terms },
                ["currency"] = Store.Currency, ["countryCode"] = Store.CountryCode,
                ["privacy"] = new JsonObject { ["labelPasscode"] = Store.LabelPasscode, ["clearPasscodeOnDelivery"] = Store.ClearPasscodeOnDelivery },
                // إعدادات برنامج سطح المكتب: القوائم المعدّلة والفنيون وقوالب الرسائل
                ["desktop"] = new JsonObject(Store.CustomSettings().Select(kv => KeyValuePair.Create(kv.Key, (JsonNode)kv.Value))),
            },
            ["photos"] = photos,
        };
        File.WriteAllText(file, root.ToJsonString(), new UTF8Encoding(false));
        Store.Set("last_backup", Txt.Now);
    }
}

/// <summary>ملفات CSV (تفتح في Excel) وملف Excel حقيقي بعدة أوراق</summary>
public static class Csv
{
    public static string Cell(object v)
    {
        var s = v switch { null => "", double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) };
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;   // حماية من تنفيذ الصيغ عند الفتح في Excel
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    public static void Write(string file, IEnumerable<object[]> rows) =>
        File.WriteAllText(file, "﻿" + string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(Cell)))), new UTF8Encoding(false));

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool q = false;
        text = text.TrimStart('﻿');
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (q)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else q = false; }
                else cell.Append(ch);
            }
            else if (ch == '"') q = true;
            else if (ch is ',' or ';' or '\t') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); rows.Add(row); row = new(); cell.Clear();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows.Where(r => r.Any(c => c.Trim() != "")).ToList();
    }

    public static void ExportOrders(string file)
    {
        var head = new object[] { "المرجع", "الزبون", "الهاتف", "الجهاز", "IMEI", "نوع العطل", "العطل", "الحالة", "القطع", "تكلفة القطع", "السعر", "أجرة الفحص", "الربح", "حالة الدفع", "المدفوع", "طرق الدفع", "المتبقي", "الاستلام", "التسليم المتوقع", "التسليم الفعلي", "مدة العمل", "الضمان", "ينتهي الضمان", "طلب ضمان لـ", "ملاحظات", "الفني" };
        var rows = Store.Orders.Select(o => new object[]
        {
            o.RefNo, o.CustomerName, o.Phone, o.Device, o.Imei, o.IssueType, o.Issue, o.Status,
            string.Join(" | ", o.Parts.Select(p => $"{p.Name}{(p.Supplier != "" ? " / " + p.Supplier : "")} ({Txt.Num(p.Cost)})")),
            Calc.PartsCost(o), o.Price, o.CheckFee, Calc.ShownProfit(o), o.PaymentStatus, o.Paid,
            string.Join(" + ", o.PaymentHistory.Select(p => p.Method).Distinct()), Calc.RemainingOf(o),
            o.DateReceived, o.DateEstimated, o.DateDelivered, Calc.DurationText(o), o.Warranty, Calc.WarrantyEnd(o),
            Calc.Find(o.WarrantyOf)?.RefNo ?? "", o.Notes, o.Technician
        });
        Write(file, new[] { head }.Concat(rows));
    }

    public static readonly string[] InvHead = { "الموديل", "اسم القطعة", "التصنيف", "المورد", "سعر الشراء", "سعر البيع", "ملاحظات", "الكمية", "حد التنبيه", "ضمان المورد (أيام)" };

    public static void ExportInventory(string file) =>
        Write(file, new[] { InvHead.Cast<object>().ToArray() }.Concat(Store.Inventory.Select(i => new object[]
            { i.Compatible, i.Name, i.Category, i.Supplier, i.Cost, i.SalePrice, i.Notes, i.Qty?.ToString() ?? "", i.MinQty?.ToString() ?? "", i.SupWarranty?.ToString() ?? "" })));

    public static List<InvItem> ReadInventory(string file)
    {
        var rows = Parse(File.ReadAllText(file, Encoding.UTF8));
        string C(List<string> r, int i) => i < r.Count ? r[i].TrimStart('\'') : "";
        return rows.Skip(1).Select(r => Json.Inv(new JsonObject
        {
            ["compatible"] = C(r, 0), ["name"] = C(r, 1), ["category"] = C(r, 2), ["supplier"] = C(r, 3), ["cost"] = C(r, 4),
            ["salePrice"] = C(r, 5), ["notes"] = C(r, 6), ["qty"] = C(r, 7), ["minQty"] = C(r, 8), ["supWarranty"] = C(r, 9), ["id"] = Txt.Uid("inv")
        })).Where(i => i != null).ToList();
    }
}

/// <summary>كاتب ملفات Excel (xlsx) بسيط: أوراق من اليمين لليسار، سطر عنوان، رأس ملوّن، أرقام بفواصل</summary>
public static class Xlsx
{
    public record Sheet(string Name, string Title, string[] Header, List<object[]> Rows, int[] Widths = null);

    static string X(object v) => System.Security.SecurityElement.Escape(new string(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture).Where(c => c >= 0x20 || c is '\t' or '\n' or '\r').ToArray()));
    static string Col(int i) { var s = ""; i++; while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; } return s; }

    static string Cell(object v, int c, int r, int? style)
    {
        var rf = Col(c) + r;
        if (v is double or int or long)
        {
            double d = Convert.ToDouble(v);
            if (double.IsFinite(d)) return $"<c r=\"{rf}\" s=\"{style ?? 2}\"><v>{d.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        }
        if (v == null || v as string == "") return style is > 0 ? $"<c r=\"{rf}\" s=\"{style}\"/>" : "";
        return $"<c r=\"{rf}\" t=\"inlineStr\"{(style is > 0 ? $" s=\"{style}\"" : "")}><is><t xml:space=\"preserve\">{X(v)}</t></is></c>";
    }

    static string SheetXml(Sheet sh)
    {
        int n = Math.Max(sh.Header.Length, 1);
        var sb = new StringBuilder();
        sb.Append($"<row r=\"1\">{Cell(sh.Title ?? "", 0, 1, 3)}</row>");
        sb.Append($"<row r=\"3\">{string.Concat(sh.Header.Select((h, i) => Cell(h, i, 3, 1)))}</row>");
        for (int ri = 0; ri < sh.Rows.Count; ri++) sb.Append($"<row r=\"{ri + 4}\">{string.Concat(sh.Rows[ri].Select((v, ci) => Cell(v, ci, ri + 4, null)))}</row>");
        var cols = string.Concat(sh.Header.Select((_, i) => $"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{(sh.Widths != null && i < sh.Widths.Length ? sh.Widths[i] : 14)}\" customWidth=\"1\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
               $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"><pane ySplit=\"3\" topLeftCell=\"A4\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>{cols}</cols><sheetData>{sb}</sheetData>{(n > 1 ? $"<mergeCells count=\"1\"><mergeCell ref=\"A1:{Col(n - 1)}1\"/></mergeCells>" : "")}</worksheet>";
    }

    public static void Write(string file, List<Sheet> sheets)
    {
        if (File.Exists(file)) File.Delete(file);
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        void Add(string name, string data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(data);
        }
        var names = sheets.Select((s, i) => { var nm = new string((s.Name ?? $"ورقة {i + 1}").Select(ch => "[]:*?/\\".Contains(ch) ? ' ' : ch).ToArray()); return nm.Length > 31 ? nm[..31] : nm; }).ToList();
        const string H = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        Add("[Content_Types].xml", H + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) + "</Types>");
        Add("_rels/.rels", H + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Add("xl/workbook.xml", H + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView/></bookViews><sheets>" +
            string.Concat(names.Select((nm, i) => $"<sheet name=\"{X(nm)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
        Add("xl/_rels/workbook.xml.rels", H + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
            $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        Add("xl/styles.xml", H + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0\"/></numFmts><fonts count=\"3\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"14\"/><color rgb=\"FF1C3C95\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2B55C9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
        for (int i = 0; i < sheets.Count; i++) Add($"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
    }
}
