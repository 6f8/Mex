using System.Data;

namespace Raseed;

/// <summary>عمليات المخزون المشتركة</summary>
public static class StockOps
{
    public record Take(long BatchId, double Qty, double Cost, object Expiry);

    public static double Available(Tx tx, long itemId, long whId) =>
        Db.D(tx.Scalar("SELECT IFNULL(SUM(qty),0) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1", itemId, whId));

    /// <summary>صرف كمية بطريقة FEFO (الأقرب انتهاءً أولًا). يرمي استثناء إذا لم تكفِ الكمية.</summary>
    public static List<Take> TakeFefo(Tx tx, long itemId, long whId, double qty)
    {
        if (Available(tx, itemId, whId) + 1e-9 < qty)
        {
            var name = Db.S(tx.Scalar("SELECT name FROM items WHERE id=@p0", itemId));
            throw new InvalidOperationException($"الكمية غير كافية للمادة «{name}». المتوفر: {Ui.M(Available(tx, itemId, whId))} — المطلوب: {Ui.M(qty)}");
        }
        var list = new List<Take>();
        double need = qty;
        var bs = tx.Query("SELECT id,qty,cost,expiry FROM batches WHERE item_id=@p0 AND warehouse_id=@p1 AND qty>0 ORDER BY (expiry IS NULL OR expiry=''), expiry, id", itemId, whId);
        foreach (DataRow b in bs.Rows)
        {
            double take = Math.Min(need, Db.D(b["qty"]));
            tx.Exec("UPDATE batches SET qty=qty-@p0 WHERE id=@p1", take, Db.L(b["id"]));
            list.Add(new Take(Db.L(b["id"]), take, Db.D(b["cost"]), b["expiry"]));
            need -= take;
            if (need <= 1e-9) break;
        }
        return list;
    }

    /// <summary>المادة الخدمية لا مخزون لها (صيانة، اشتراك، شحن رصيد...)</summary>
    public static bool IsService(Tx tx, long itemId) => Db.S(tx.Scalar("SELECT item_type FROM items WHERE id=@p0", itemId)) == "خدمية";

    /// <summary>إنشاء سند إدخال مخزني (مثل رصيد أول المدة) وإضافة الكميات كوجبات جديدة</summary>
    public static long StockIn(Tx tx, long whId, IEnumerable<(long Item, double Qty, double Cost, string Expiry)> lines, string notes)
    {
        var list = lines.Where(l => l.Qty > 0).ToList();
        double total = list.Sum(l => l.Qty * l.Cost);
        long inv = tx.Insert("INSERT INTO invoices(type,date,warehouse_id,total,discount,net,paid,notes,user_id) VALUES('StockIn',@p0,@p1,@p2,0,@p2,0,@p3,@p4)",
            Ui.Now, whId, total, notes, Session.UserId);
        foreach (var l in list)
        {
            long bid = tx.Insert("INSERT INTO batches(item_id,warehouse_id,expiry,qty,cost,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                l.Item, whId, string.IsNullOrEmpty(l.Expiry) ? null : l.Expiry, l.Qty, l.Cost, Ui.Now);
            tx.Exec("INSERT INTO invoice_lines(invoice_id,item_id,batch_id,qty,price,cost,expiry) VALUES(@p0,@p1,@p2,@p3,@p4,@p4,@p5)",
                inv, l.Item, bid, l.Qty, l.Cost, string.IsNullOrEmpty(l.Expiry) ? null : l.Expiry);
        }
        return inv;
    }

    /// <summary>نقل كمية من مخزن إلى آخر مع الحفاظ على الصلاحية والكلفة لكل وجبة (FEFO)</summary>
    public static void Transfer(Tx tx, long transferId, long itemId, long fromWh, long toWh, double qty)
    {
        foreach (var t in TakeFefo(tx, itemId, fromWh, qty))
        {
            long bid = tx.Insert("INSERT INTO batches(item_id,warehouse_id,expiry,qty,cost,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                itemId, toWh, t.Expiry is DBNull ? null : t.Expiry, t.Qty, t.Cost, Ui.Now);
            tx.Exec("INSERT INTO transfer_lines(transfer_id,item_id,qty,src_batch,dst_batch,expiry,cost) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                transferId, itemId, t.Qty, t.BatchId, bid, t.Expiry, t.Cost);
        }
    }

    public static double AvgCost(long itemId)
    {
        using var tx = new Tx(write: false);
        return AvgCost(tx, itemId);
    }

    /// <summary>متوسط الكلفة ضمن المعاملة الحالية (يرى تعديلاتها غير المحفوظة بعد)</summary>
    public static double AvgCost(Tx tx, long itemId)
    {
        var avg = Db.D(tx.Scalar("SELECT SUM(qty*cost)/NULLIF(SUM(qty),0) FROM batches WHERE item_id=@p0 AND qty>0", itemId));
        return avg > 0 ? avg : Db.D(tx.Scalar("SELECT cost FROM batches WHERE item_id=@p0 ORDER BY id DESC LIMIT 1", itemId));
    }
}

/// <summary>حذف الفاتورة أو عكسها (للتعديل) مع إرجاع المخزون والمبالغ</summary>
public static class InvoiceOps
{
    public static bool IsOut(string type) => type is "Sale" or "PurchaseReturn" or "Damage" or "StockOut";

    /// <summary>يعكس أثر الفاتورة على المخزون والصناديق ويحذفها. يرمي استثناء برسالة واضحة إذا تعذر ذلك.</summary>
    public static void Remove(Tx tx, long id)
    {
        var inv = tx.Query("SELECT type FROM invoices WHERE id=@p0", id);
        if (inv.Rows.Count == 0) throw new InvalidOperationException("الفاتورة غير موجودة.");
        string type = Db.S(inv.Rows[0]["type"]);

        if (Db.D(tx.Scalar("SELECT IFNULL(SUM(paid),0) FROM installments WHERE invoice_id=@p0", id)) > 0.001)
            throw new InvalidOperationException("توجد أقساط مسددة على هذه الفاتورة، لا يمكن تعديلها أو حذفها.");

        var lines = tx.Query("SELECT l.batch_id, l.qty, i.name FROM invoice_lines l JOIN items i ON i.id=l.item_id WHERE l.invoice_id=@p0", id);
        foreach (DataRow l in lines.Rows)
        {
            long bid = Db.L(l["batch_id"]);
            double q = Db.D(l["qty"]);
            if (bid == 0) continue;
            if (IsOut(type))
                tx.Exec("UPDATE batches SET qty=qty+@p0 WHERE id=@p1", q, bid);
            else
            {
                double have = Db.D(tx.Scalar("SELECT qty FROM batches WHERE id=@p0", bid));
                if (have + 1e-9 < q)
                    throw new InvalidOperationException($"لا يمكن عكس الفاتورة: جزء من المادة «{Db.S(l["name"])}» الداخلة بهذه الفاتورة تم صرفه أو نقله.");
                tx.Exec("UPDATE batches SET qty=qty-@p0 WHERE id=@p1", q, bid);
            }
        }
        tx.Exec("DELETE FROM cash_moves WHERE invoice_id=@p0", id);
        tx.Exec("DELETE FROM installments WHERE invoice_id=@p0", id);
        tx.Exec("UPDATE item_serials SET invoice_id=NULL WHERE invoice_id=@p0", id);   // الأرقام التسلسلية المباعة تعود متوفرة
        tx.Exec("DELETE FROM invoice_lines WHERE invoice_id=@p0", id);
        tx.Exec("DELETE FROM invoices WHERE id=@p0", id);
        if (!IsOut(type)) tx.Exec("DELETE FROM batches WHERE qty<=0.0000001 AND id NOT IN (SELECT batch_id FROM invoice_lines WHERE batch_id IS NOT NULL) AND id NOT IN (SELECT batch_id FROM repair_parts WHERE batch_id IS NOT NULL)");
    }

    /// <summary>بيانات الفاتورة للطباعة</summary>
    public static PrintDoc BuildPrint(long id)
    {
        var dt = Db.Query(@"SELECT v.*, p.name AS pname, p.phone AS pphone, w.name AS wname, u.full_name AS uname, d.name AS dname
            FROM invoices v LEFT JOIN parties p ON p.id=v.party_id LEFT JOIN warehouses w ON w.id=v.warehouse_id
            LEFT JOIN users u ON u.id=v.user_id LEFT JOIN delivery_companies d ON d.id=v.delivery_id WHERE v.id=@p0", id);
        if (dt.Rows.Count == 0) return null;
        var v = dt.Rows[0];
        string type = Db.S(v["type"]);
        var lines = Db.Query(@"SELECT i.name, l.length, l.width, i.by_measure, SUM(l.qty) AS qty, l.price
            FROM invoice_lines l JOIN items i ON i.id=l.item_id WHERE l.invoice_id=@p0
            GROUP BY l.item_id, l.price, l.length, l.width ORDER BY MIN(l.id)", id);

        var doc = PrintDoc.Header(Ui.DocTitle(type));
        doc.Pair(Ui.IsStockDoc(type) ? "رقم السند" : "رقم الفاتورة", id.ToString(), "التاريخ", Db.S(v["date"]).Length >= 16 ? Db.S(v["date"])[..16] : Db.S(v["date"]));
        if (Db.S(v["pname"]) != "") doc.Pair(type is "Sale" or "SaleReturn" ? "العميل" : "المورد", Db.S(v["pname"]), "الهاتف", Db.S(v["pphone"]));
        if (Db.S(v["pay_type"]) != "") doc.Pair("طريقة الدفع", Db.S(v["pay_type"]), "المستخدم", Db.S(v["uname"]));
        doc.Space();
        var rows = new List<string[]>();
        int n = 0;
        foreach (DataRow l in lines.Rows)
        {
            double q = Db.D(l["qty"]), pr = Db.D(l["price"]);
            string name = Db.S(l["name"]);
            if (Db.L(l["by_measure"]) == 1 && Db.D(l["width"]) > 0) name += $" ({Ui.M(Db.D(l["length"]))}×{Ui.M(Db.D(l["width"]))})";
            rows.Add(new[] { (++n).ToString(), name, Ui.M(q), Ui.M(pr), Ui.M(q * pr) });
        }
        doc.Table(new[] { "#", "المادة", "الكمية", "السعر", "المجموع" }, new[] { 6f, 44, 14, 17, 19 }, rows);
        doc.Line();
        doc.Pair("الإجمالي", Ui.M(Db.D(v["total"])), "الخصم", Ui.M(Db.D(v["discount"])));
        if (Db.D(v["delivery_fee"]) > 0) doc.Pair("التوصيل", Db.S(v["dname"]), "الأجور", Ui.M(Db.D(v["delivery_fee"])));
        doc.Text("الصافي: " + Ui.M(Db.D(v["net"])) + " د.ع", 13, true, StringAlignment.Center);
        if (type != "Damage" && !Ui.IsStockDoc(type))
            doc.Pair("المدفوع", Ui.M(Db.D(v["paid"])), "المتبقي", Ui.M(Db.D(v["net"]) - Db.D(v["paid"])));
        long pid = Db.L(v["party_id"]);
        if (pid > 0) doc.Text("رصيد الحساب الحالي: " + Ui.M(Ui.PartyBalance(pid)), 10, false, StringAlignment.Center);

        var inst = Db.Query("SELECT seq, due_date, amount FROM installments WHERE invoice_id=@p0 ORDER BY seq", id);
        if (inst.Rows.Count > 0)
        {
            doc.Space();
            doc.Text("جدول الأقساط", 11, true, StringAlignment.Center);
            doc.Table(new[] { "القسط", "الاستحقاق", "المبلغ" }, new[] { 20f, 45, 35 },
                inst.Rows.Cast<DataRow>().Select(r => new[] { Db.S(r["seq"]), Db.S(r["due_date"]), Ui.M(Db.D(r["amount"])) }).ToList());
        }
        if (Db.S(v["notes"]) != "") doc.Text("ملاحظات: " + Db.S(v["notes"]), 9);
        doc.Footer();
        doc.Barcode("INV" + id);
        return doc;
    }
}

/// <summary>الحسابات: حركات الجهات</summary>
public static class Ledger
{
    /// <summary>تعديل رصيد الحساب ليصبح القيمة المطلوبة (عبر الرصيد الافتتاحي) — يعيد مقدار التعديل</summary>
    public static double AdjustTo(long partyId, double target)
    {
        double current = Ui.PartyBalance(partyId), diff = Math.Round(target - current, 2);
        if (Math.Abs(diff) < 0.005) return 0;
        Db.Exec("UPDATE parties SET opening_balance=IFNULL(opening_balance,0)+@p0 WHERE id=@p1", diff, partyId);
        Db.Audit("تعديل رصيد", $"الحساب {partyId}: من {Ui.M(current)} إلى {Ui.M(target)}");
        return diff;
    }

    /// <summary>حركات الجهة (مدين/دائن) — يجب أن يطابق مجموعها رصيد v_party_balance</summary>
    public static DataTable StatementRows(long pid) => Db.Query(@"
        SELECT date AS d, 'INV:'||type AS kind, id AS ref,
               CASE type WHEN 'Sale' THEN net WHEN 'PurchaseReturn' THEN net ELSE 0 END AS debit,
               CASE type WHEN 'Purchase' THEN net WHEN 'SaleReturn' THEN net ELSE 0 END AS credit, notes
        FROM invoices WHERE party_id=@p0
        UNION ALL
        SELECT date, kind, id, CASE WHEN amount<0 THEN -amount*rate ELSE 0 END,
               CASE WHEN amount>0 THEN amount*rate ELSE 0 END, note
        FROM cash_moves WHERE party_id=@p0
        UNION ALL
        SELECT date_out, 'صيانة — وصل رقم '||id, id, final_price, 0, device
        FROM repairs WHERE party_id=@p0 AND status='تم التسليم'
        ORDER BY d", pid);
}
