using System.Data;

namespace Raseed;

/// <summary>باركود الميزان: بادئة + رمز المادة + الوزن (+ رقم تحقق). مثال: 2 000123 01250 7</summary>
public static class ScaleCode
{
    public static bool TryParse(string code, out long plu, out double qty) =>
        TryParse(code, Settings.Get("scale_prefix", "2"), Settings.Int("scale_code_len", 6), Settings.Int("scale_value_len", 5),
                 Math.Max(1, Settings.Int("scale_divisor", 1000)), out plu, out qty);

    public static bool TryParse(string code, string prefix, int codeLen, int valueLen, int divisor, out long plu, out double qty)
    {
        plu = 0; qty = 0;
        code = (code ?? "").Trim();
        int need = prefix.Length + codeLen + valueLen;
        if (prefix == "" || codeLen <= 0 || valueLen <= 0 || !code.StartsWith(prefix) || code.Length < need || code.Length > need + 1) return false;
        if (!code.All(char.IsAsciiDigit)) return false;
        plu = long.Parse(code.Substring(prefix.Length, codeLen));
        qty = long.Parse(code.Substring(prefix.Length + codeLen, valueLen)) / (double)divisor;
        return qty > 0;
    }
}

/// <summary>الباركودات المتعددة للمادة</summary>
public static class Barcodes
{
    /// <summary>كل باركودات المادة، والأساسي (المحفوظ في items.barcode) أولًا</summary>
    public static List<string> Of(long itemId) =>
        Db.Query(@"SELECT b.barcode FROM item_barcodes b JOIN items i ON i.id=b.item_id WHERE b.item_id=@p0
                   ORDER BY (b.barcode=IFNULL(i.barcode,'')) DESC, b.id", itemId)
          .Rows.Cast<DataRow>().Select(r => Db.S(r[0])).ToList();

    /// <summary>المادة صاحبة الباركود (0 إن لم يوجد)، مع استثناء مادة معيّنة عند التعديل</summary>
    public static long Owner(string barcode, long exceptItem = 0) =>
        Db.L(Db.Scalar("SELECT item_id FROM item_barcodes WHERE barcode=@p0 AND item_id<>@p1", barcode, exceptItem));

    /// <summary>باركود داخلي غير مستخدم (يبدأ بـ 2 ومكوّن من 11 رقمًا حتى لا يختلط بباركود الميزان)</summary>
    public static string Generate(long itemId, IEnumerable<string> taken = null)
    {
        long id = itemId > 0 ? itemId : Db.L(Db.Scalar("SELECT IFNULL(MAX(id),0)+1 FROM items"));
        var used = new HashSet<string>(taken ?? Enumerable.Empty<string>());
        for (int k = 0; k < 1000; k++)
        {
            var c = "2" + (id * 1000 + k).ToString("D10");
            if (!used.Contains(c) && Owner(c) == 0 && Db.L(Db.Scalar("SELECT COUNT(*) FROM items WHERE barcode=@p0", c)) == 0) return c;
        }
        return "2" + DateTime.Now.Ticks.ToString()[^10..];
    }

    /// <summary>حفظ قائمة الباركودات: الأول يصبح الأساسي</summary>
    public static void Save(Tx tx, long itemId, IList<string> codes)
    {
        tx.Exec("DELETE FROM item_barcodes WHERE item_id=@p0", itemId);
        foreach (var c in codes.Where(c => c.Trim() != "").Distinct())
            tx.Exec("INSERT INTO item_barcodes(item_id, barcode) VALUES(@p0,@p1)", itemId, c.Trim());
        tx.Exec("UPDATE items SET barcode=@p0 WHERE id=@p1", codes.FirstOrDefault(c => c.Trim() != "")?.Trim() ?? "", itemId);
    }
}

/// <summary>تنبيهات المواد: الحد الأدنى والأعلى وحد الأمان والركود وهدف البيع والصلاحية</summary>
public static class ItemAlerts
{
    const string Qty = "IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0)";
    const string LastMove = @"(SELECT MAX(v.date) FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id WHERE l.item_id=i.id AND v.type='Sale')";
    const string FirstIn = "(SELECT MIN(created) FROM batches b WHERE b.item_id=i.id)";
    const string MonthSold = @"IFNULL((SELECT SUM(CASE v.type WHEN 'Sale' THEN l.qty ELSE -l.qty END) FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id
        WHERE l.item_id=i.id AND v.type IN ('Sale','SaleReturn') AND v.date>=strftime('%Y-%m-01','now','localtime')),0)";

    /// <summary>كل التنبيهات: [التنبيه]، [المادة / الجهة]، [التفاصيل] — @p0 أيام الصلاحية الافتراضية</summary>
    public const string Sql = $@"
        SELECT 'مخزون منخفض' AS [التنبيه], i.name AS [المادة / الجهة], 'الرصيد: '||{Qty}||'  —  الحد الأدنى: '||i.min_qty AS [التفاصيل]
        FROM items i WHERE i.active=1 AND i.min_qty>0 AND {Qty}<=i.min_qty
        UNION ALL
        SELECT 'وصل حد الأمان', i.name, 'الرصيد: '||{Qty}||'  —  حد الأمان: '||i.safety_qty
        FROM items i WHERE i.active=1 AND i.safety_qty>0 AND {Qty}<=i.safety_qty AND (i.min_qty<=0 OR {Qty}>i.min_qty)
        UNION ALL
        SELECT 'تجاوز الحد الأعلى', i.name, 'الرصيد: '||{Qty}||'  —  الحد الأعلى: '||i.max_qty
        FROM items i WHERE i.active=1 AND i.max_qty>0 AND {Qty}>i.max_qty
        UNION ALL
        SELECT 'مادة راكدة', i.name, 'لم تُبع منذ '||IFNULL(substr({LastMove},1,10),'إدخالها')||'  —  الرصيد: '||{Qty}
        FROM items i WHERE i.active=1 AND i.stagnant_days>0 AND {Qty}>0
          AND IFNULL({LastMove},{FirstIn})<datetime('now','localtime','-'||i.stagnant_days||' day')
        UNION ALL
        SELECT 'هدف البيع', i.name, 'بيع هذا الشهر: '||{MonthSold}||' من '||i.sales_target
        FROM items i WHERE i.active=1 AND i.sales_target>0 AND {MonthSold}<i.sales_target
        UNION ALL
        SELECT CASE WHEN b.expiry<date('now','localtime') THEN 'منتهية الصلاحية' ELSE 'قاربت على الانتهاء' END, i.name,
               'تاريخ الصلاحية: '||b.expiry||'  —  الكمية: '||b.qty
        FROM batches b JOIN items i ON i.id=b.item_id
        WHERE b.qty>0 AND IFNULL(b.expiry,'')<>''
          AND b.expiry<=date('now','localtime','+'||(CASE WHEN i.expiry_alert_days>0 THEN i.expiry_alert_days ELSE @p0 END)||' day')";

    public static DataTable Query(int expiryDays) => Db.Query(Sql, expiryDays);
    public static long Count(int expiryDays) => Db.L(Db.Scalar($"SELECT COUNT(*) FROM ({Sql})", expiryDays));
    public static long Count(int expiryDays, string kind) => Db.L(Db.Scalar($"SELECT COUNT(*) FROM ({Sql}) WHERE [التنبيه]=@p1", expiryDays, kind));
}
