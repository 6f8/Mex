using System.Data;

namespace Raseed;

/// <summary>التقارير: كشف الحساب، الأرباح وتوزيعها، الصناديق، التوصيل، مراكز الكلفة</summary>
public class ReportsForm : BaseForm
{
    readonly DateTimePicker dFrom = new() { Width = 140, Format = DateTimePickerFormat.Short },
                            dTo = new() { Width = 140, Format = DateTimePickerFormat.Short };
    string A => dFrom.Value.ToString(Ui.DFmt);
    string B => dTo.Value.ToString(Ui.DFmt) + " 23:59:59";

    public ReportsForm()
    {
        dFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        dTo.Value = DateTime.Today;
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("من تاريخ", dFrom));
        bar.Controls.Add(Ui.Labeled("إلى تاريخ", dTo));

        var tabs = new TabControl { Dock = DockStyle.Fill, RightToLeftLayout = true, Font = Theme.F(10, FontStyle.Bold) };
        tabs.TabPages.Add(Statement());
        if (Session.Can("profit")) tabs.TabPages.Add(Profit());
        tabs.TabPages.Add(Boxes());
        tabs.TabPages.Add(Daily());
        tabs.TabPages.Add(ItemSales());
        tabs.TabPages.Add(ItemMovement());
        tabs.TabPages.Add(Aging());
        tabs.TabPages.Add(Repairs());
        tabs.TabPages.Add(Delivery());
        tabs.TabPages.Add(CostCenters());
        if (Session.IsAdmin) tabs.TabPages.Add(AuditLog());

        Controls.Add(tabs);
        Controls.Add(bar);
        Controls.Add(Theme.Title("التقارير"));
    }

    TabPage Page(string title, DataGridView grid, Control top, Control bottom = null)
    {
        Ui.GridTools(top, grid, () => title, () => $"الفترة من {A} إلى {dTo.Value.ToString(Ui.DFmt)}");
        var p = new TabPage(title) { BackColor = Theme.Bg, Font = Theme.F() };
        p.Controls.Add(grid);
        if (bottom != null) p.Controls.Add(bottom);
        p.Controls.Add(top);
        return p;
    }

    // ---------- كشف حساب ----------
    TabPage Statement()
    {
        var grid = Ui.NewGrid();
        var cb = Ui.Combo(260);
        Ui.FillCombo(cb, "SELECT id,name,phone FROM parties ORDER BY name");
        Ui.MakeSearchable(cb);
        var lbl = new Label { Dock = DockStyle.Bottom, Height = 40, Font = Theme.F(12, FontStyle.Bold), ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White };
        var bShow = Theme.Btn("عرض الكشف", Theme.Accent, 130);
        var bWa = Theme.Btn("إرسال الرصيد واتساب", Theme.Success, 190);
        var top = Theme.Bar();
        top.Controls.Add(Ui.Labeled("الجهة", cb));
        top.Controls.Add(bShow);
        top.Controls.Add(bWa);
        double closing = 0;

        bShow.Click += (s, e) =>
        {
            long pid = Ui.GetId(cb);
            if (pid == 0) return;
            var dt = Db.Query(@"
                SELECT date AS d, 'INV:'||type AS kind, id AS ref,
                       CASE type WHEN 'Sale' THEN net WHEN 'PurchaseReturn' THEN net ELSE 0 END AS debit,
                       CASE type WHEN 'Purchase' THEN net WHEN 'SaleReturn' THEN net ELSE 0 END AS credit, notes
                FROM invoices WHERE party_id=@p0
                UNION ALL
                SELECT date, kind, id, CASE WHEN amount<0 THEN -amount*rate ELSE 0 END,
                       CASE WHEN amount>0 THEN amount*rate ELSE 0 END, note
                FROM cash_moves WHERE party_id=@p0
                ORDER BY d", pid);

            var res = new DataTable();
            res.Columns.Add("التاريخ"); res.Columns.Add("البيان"); res.Columns.Add("المرجع");
            res.Columns.Add("مدين (عليه)", typeof(double)); res.Columns.Add("دائن (له)", typeof(double));
            res.Columns.Add("الرصيد", typeof(double)); res.Columns.Add("ملاحظات");

            double bal = Db.D(Db.Scalar("SELECT opening_balance FROM parties WHERE id=@p0", pid));
            var rows = dt.Rows.Cast<DataRow>().ToList();
            foreach (var r in rows.Where(r => string.CompareOrdinal(Db.S(r["d"]), A) < 0))
                bal += Db.D(r["debit"]) - Db.D(r["credit"]);
            res.Rows.Add(A, "رصيد سابق", "", DBNull.Value, DBNull.Value, bal, "");
            foreach (var r in rows.Where(r => string.CompareOrdinal(Db.S(r["d"]), A) >= 0 && string.CompareOrdinal(Db.S(r["d"]), B) <= 0))
            {
                double dr = Db.D(r["debit"]), cr = Db.D(r["credit"]);
                bal += dr - cr;
                var kind = Db.S(r["kind"]);
                kind = kind.StartsWith("INV:") ? "فاتورة " + Ui.TypeName(kind[4..]) : kind;
                res.Rows.Add(Db.S(r["d"]), kind, Db.S(r["ref"]), dr, cr, bal, Db.S(r["notes"]));
            }
            grid.DataSource = res;
            closing = bal;
            lbl.Text = $"   الرصيد النهائي: {Ui.M(bal)}  " + (bal > 0 ? "(مدين — عليه)" : bal < 0 ? "(دائن — له)" : "(متوازن)");
        };
        bWa.Click += (s, e) =>
        {
            var r = Ui.GetRow(cb);
            if (r == null || Db.S(r["phone"]) == "") { Ui.Warn("لا يوجد رقم هاتف لهذه الجهة."); return; }
            double bal = Ui.PartyBalance(Db.L(r["id"]));
            _ = WhatsApp.Send(Db.S(r["phone"]), $"{Settings.Get("shop_name")}\nعزيزي {Db.S(r["name"])}، رصيد حسابكم لدينا بتاريخ {DateTime.Today:yyyy/MM/dd}: {Ui.M(Math.Abs(bal))} د.ع {(bal >= 0 ? "(مطلوب)" : "(لكم)")}.");
        };
        return Page("كشف حساب", grid, top, lbl);
    }

    // ---------- الأرباح وتوزيعها ----------
    TabPage Profit()
    {
        var grid = Ui.NewGrid();
        var summary = new Label { Dock = DockStyle.Bottom, Height = 175, Font = Theme.F(11), BackColor = Color.White, Padding = new Padding(12) };
        var cbBox = Ui.Combo(200);
        Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        var bCalc = Theme.Btn("احتساب الأرباح", Theme.Accent, 150);
        var bPost = Theme.Btn("صرف حصص الشركاء", Theme.Purple, 170);
        var top = Theme.Bar();
        top.Controls.Add(bCalc);
        top.Controls.Add(Ui.Labeled("صندوق الصرف", cbBox));
        top.Controls.Add(bPost);
        double netProfit = 0;

        bCalc.Click += (s, e) =>
        {
            double Q(string sql) => Db.D(Db.Scalar(sql, A, B));
            const string L = "FROM invoice_lines l JOIN invoices i ON i.id=l.invoice_id WHERE i.date BETWEEN @p0 AND @p1 AND i.type=";
            double sales = Q($"SELECT SUM(l.qty*l.price) {L}'Sale'");
            double disc = Q("SELECT SUM(discount) FROM invoices WHERE type='Sale' AND date BETWEEN @p0 AND @p1");
            double ret = Q($"SELECT SUM(l.qty*l.price) {L}'SaleReturn'");
            double cogs = Q($"SELECT SUM(l.qty*l.cost) {L}'Sale'") - Q($"SELECT SUM(l.qty*l.cost) {L}'SaleReturn'");
            double damage = Q($"SELECT SUM(l.qty*l.cost) {L}'Damage'");
            double exp = -Q("SELECT SUM(amount*rate) FROM cash_moves WHERE kind IN ('مصروف','راتب','سلفة') AND date BETWEEN @p0 AND @p1");
            double rep = Q("SELECT SUM(final_price) FROM repairs WHERE status='تم التسليم' AND date_out BETWEEN @p0 AND @p1");
            double repParts = Q("SELECT SUM(p.qty*p.cost) FROM repair_parts p JOIN repairs r ON r.id=p.repair_id WHERE r.status='تم التسليم' AND r.date_out BETWEEN @p0 AND @p1");
            double fees = Q("SELECT SUM(delivery_fee) FROM invoices WHERE type='Sale' AND date BETWEEN @p0 AND @p1");
            netProfit = sales - disc - ret - cogs - damage - exp + (rep - repParts);
            summary.Text =
                $"المبيعات: {Ui.M(sales)}    |    الخصومات: {Ui.M(disc)}    |    المرتجعات: {Ui.M(ret)}    |    أجور التوصيل المحصلة: {Ui.M(fees)} (أمانة لشركات التوصيل)\n" +
                $"كلفة البضاعة المباعة: {Ui.M(cogs)}    |    المواد المتلفة: {Ui.M(damage)}\n" +
                $"إيراد الصيانة: {Ui.M(rep)}    |    كلفة قطع الصيانة: {Ui.M(repParts)}    |    ربح الصيانة: {Ui.M(rep - repParts)}\n" +
                $"المصروفات والرواتب والسلف: {Ui.M(exp)}\n\n" +
                $"صافي الربح للفترة: {Ui.M(netProfit)}";
            summary.ForeColor = netProfit >= 0 ? Theme.Success : Theme.Danger;
            grid.DataSource = Db.Query("SELECT id, name AS [الشريك], share AS [النسبة %], ROUND(@p0*share/100.0,0) AS [الحصة] FROM partners ORDER BY id", netProfit);
            double totalShare = Db.D(Db.Scalar("SELECT SUM(share) FROM partners"));
            if (totalShare > 0 && Math.Abs(totalShare - 100) > 0.01)
                summary.Text += $"\n⚠ مجموع نسب الشركاء {Ui.M(totalShare)}% وليس 100%";
        };
        bPost.Click += (s, e) =>
        {
            if (!Session.Guard("profit")) return;
            if (netProfit <= 0) { Ui.Warn("احسب الأرباح أولاً (يجب أن يكون الربح موجباً)."); return; }
            if (!Ui.Confirm($"صرف حصص الشركاء من صافي ربح {Ui.M(netProfit)} للفترة {A} — {dTo.Value:yyyy-MM-dd}؟")) return;
            long box = Ui.GetId(cbBox);
            double rate = Ui.BoxRate(box);
            using var tx = new Tx();
            foreach (DataRow p in Db.Query("SELECT name, share FROM partners WHERE share>0").Rows)
            {
                double amt = Math.Round(netProfit * Db.D(p["share"]) / 100.0);
                tx.Exec("INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,note,user_id) VALUES(@p0,'توزيع أرباح',@p1,@p2,@p3,@p4,@p5)",
                    Ui.Now, box, -amt / rate, rate, $"حصة الشريك {Db.S(p["name"])} عن الفترة {A} إلى {dTo.Value:yyyy-MM-dd}", Session.UserId);
            }
            tx.Commit();
            Ui.Info("تم تسجيل صرف حصص الشركاء.");
        };
        return Page("الأرباح وتوزيعها", grid, top, summary);
    }

    // ---------- الصناديق والخزائن ----------
    TabPage Boxes()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var b = Theme.Btn("تحديث", Theme.Accent, 110);
        top.Controls.Add(b);
        void Reload() => grid.DataSource = Db.Query(@"SELECT c.id, c.name AS [الصندوق / الخزينة], c.kind AS [النوع], c.currency AS [العملة],
            IFNULL(SUM(m.amount),0) AS [الرصيد], IFNULL(SUM(m.amount*m.rate),0) AS [المعادل بالدينار],
            IFNULL(SUM(CASE WHEN m.amount>0 AND m.date BETWEEN @p0 AND @p1 THEN m.amount END),0) AS [وارد الفترة],
            IFNULL(SUM(CASE WHEN m.amount<0 AND m.date BETWEEN @p0 AND @p1 THEN -m.amount END),0) AS [صادر الفترة]
            FROM cashboxes c LEFT JOIN cash_moves m ON m.cashbox_id=c.id GROUP BY c.id ORDER BY c.id", A, B);
        b.Click += (s, e) => Reload();
        Reload();
        return Page("الصناديق والخزائن", grid, top);
    }

    // ---------- اليومية (حركة الصناديق) ----------
    TabPage Daily()
    {
        var grid = Ui.NewGrid();
        var cb = Ui.Combo(200);
        Ui.FillCombo(cb, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id", true, "— كل الصناديق —");
        var top = Theme.Bar();
        var b = Theme.Btn("عرض", Theme.Accent, 90);
        top.Controls.Add(Ui.Labeled("الصندوق", cb));
        top.Controls.Add(b);
        var lbl = new Label { Dock = DockStyle.Bottom, Height = 34, Font = Theme.F(11, FontStyle.Bold), BackColor = Color.White, ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleLeft };
        void Reload()
        {
            var dt = Db.Query(@"SELECT m.id, m.date AS [التاريخ], m.kind AS [النوع], c.name AS [الصندوق],
                CASE WHEN m.amount>0 THEN m.amount END AS [وارد], CASE WHEN m.amount<0 THEN -m.amount END AS [صادر], c.currency AS [العملة],
                COALESCE(p.name, e.name, '') AS [الجهة / الموظف], m.note AS [البيان], u.full_name AS [المستخدم]
                FROM cash_moves m JOIN cashboxes c ON c.id=m.cashbox_id LEFT JOIN parties p ON p.id=m.party_id
                LEFT JOIN employees e ON e.id=m.employee_id LEFT JOIN users u ON u.id=m.user_id
                WHERE m.date BETWEEN @p0 AND @p1 AND (@p2=0 OR m.cashbox_id=@p2) ORDER BY m.date, m.id", A, B, Ui.GetId(cb));
            grid.DataSource = dt;
            double inn = dt.Rows.Cast<DataRow>().Sum(r => Db.D(r["وارد"])), outt = dt.Rows.Cast<DataRow>().Sum(r => Db.D(r["صادر"]));
            lbl.Text = Ui.GetId(cb) == 0 ? "   اختر صندوقاً لعرض المجاميع بعملته" : $"   الوارد: {Ui.M(inn)}    |    الصادر: {Ui.M(outt)}    |    الصافي: {Ui.M(inn - outt)}";
        }
        b.Click += (s, e) => Reload();
        Reload();
        return Page("اليومية", grid, top, lbl);
    }

    // ---------- مبيعات المواد ----------
    TabPage ItemSales()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var b = Theme.Btn("عرض", Theme.Accent, 90);
        top.Controls.Add(b);
        void Reload() => grid.DataSource = Db.Query(@"SELECT it.id, it.name AS [المادة], it.category AS [الصنف],
            SUM(CASE WHEN i.type='Sale' THEN l.qty ELSE -l.qty END) AS [الكمية المباعة],
            SUM(CASE WHEN i.type='Sale' THEN l.qty*l.price ELSE -l.qty*l.price END) AS [قيمة المبيعات],
            SUM(CASE WHEN i.type='Sale' THEN l.qty*l.cost ELSE -l.qty*l.cost END) AS [الكلفة],
            SUM(CASE WHEN i.type='Sale' THEN l.qty*(l.price-l.cost) ELSE -l.qty*(l.price-l.cost) END) AS [الربح الإجمالي]
            FROM invoice_lines l JOIN invoices i ON i.id=l.invoice_id JOIN items it ON it.id=l.item_id
            WHERE i.type IN ('Sale','SaleReturn') AND i.date BETWEEN @p0 AND @p1
            GROUP BY it.id ORDER BY [قيمة المبيعات] DESC", A, B);
        b.Click += (s, e) => Reload();
        Reload();
        return Page("مبيعات المواد", grid, top);
    }

    // ---------- حركة مادة ----------
    TabPage ItemMovement()
    {
        var grid = Ui.NewGrid();
        var cb = Ui.Combo(280);
        Ui.FillCombo(cb, "SELECT id, name FROM items ORDER BY name");
        Ui.MakeSearchable(cb);
        var top = Theme.Bar();
        var b = Theme.Btn("عرض الحركة", Theme.Accent, 120);
        top.Controls.Add(Ui.Labeled("المادة", cb));
        top.Controls.Add(b);
        b.Click += (s, e) =>
        {
            long item = Ui.GetId(cb);
            var dt = Db.Query($@"SELECT d, kind, ref, party, inq, outq FROM (
                SELECT i.date AS d, {string.Format(Ui.TypeCaseSql, "i.type")} || CASE WHEN i.notes LIKE 'تسوية جرد%' THEN ' (جرد)' ELSE '' END AS kind,
                    i.id AS ref, IFNULL(p.name,'') AS party,
                    CASE WHEN i.type IN ('Purchase','SaleReturn') THEN l.qty ELSE 0 END AS inq,
                    CASE WHEN i.type IN ('Sale','PurchaseReturn','Damage') THEN l.qty ELSE 0 END AS outq
                FROM invoice_lines l JOIN invoices i ON i.id=l.invoice_id LEFT JOIN parties p ON p.id=i.party_id WHERE l.item_id=@p0
                UNION ALL
                SELECT p.date, 'صيانة', r.id, r.customer, 0, p.qty FROM repair_parts p JOIN repairs r ON r.id=p.repair_id WHERE p.item_id=@p0
            ) ORDER BY d", item);
            var res = new DataTable();
            res.Columns.Add("التاريخ"); res.Columns.Add("الحركة"); res.Columns.Add("المرجع"); res.Columns.Add("الجهة");
            res.Columns.Add("وارد", typeof(double)); res.Columns.Add("صادر", typeof(double)); res.Columns.Add("الرصيد", typeof(double));
            double bal = 0;
            foreach (DataRow r in dt.Rows)
            {
                bal += Db.D(r["inq"]) - Db.D(r["outq"]);
                if (string.CompareOrdinal(Db.S(r["d"]), A) < 0 || string.CompareOrdinal(Db.S(r["d"]), B) > 0) continue;
                res.Rows.Add(Db.S(r["d"]), Db.S(r["kind"]), Db.S(r["ref"]), Db.S(r["party"]), Db.D(r["inq"]), Db.D(r["outq"]), bal);
            }
            grid.DataSource = res;
        };
        return Page("حركة مادة", grid, top);
    }

    // ---------- أعمار الديون ----------
    TabPage Aging()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var b = Theme.Btn("تحديث", Theme.Accent, 90);
        var bWa = Theme.Btn("تذكير المحدد واتساب", Theme.Success, 180);
        top.Controls.Add(b);
        top.Controls.Add(bWa);
        void Reload() => grid.DataSource = Db.Query(@"SELECT p.id, p.name AS [العميل], p.phone AS [الهاتف], b.balance AS [الرصيد المطلوب],
            (SELECT MAX(date) FROM invoices WHERE party_id=p.id AND type='Sale') AS [آخر بيع],
            (SELECT MAX(date) FROM cash_moves WHERE party_id=p.id AND amount>0) AS [آخر دفعة],
            CAST(julianday('now','localtime')-julianday(IFNULL((SELECT MAX(date) FROM cash_moves WHERE party_id=p.id AND amount>0),
                 (SELECT MIN(date) FROM invoices WHERE party_id=p.id))) AS INTEGER) AS [أيام بلا تسديد],
            p.credit_limit AS [سقف الذمة]
            FROM parties p JOIN v_party_balance b ON b.id=p.id WHERE b.balance>0.5 ORDER BY [أيام بلا تسديد] DESC");
        b.Click += (s, e) => Reload();
        bWa.Click += (s, e) =>
        {
            if (grid.CurrentRow == null) return;
            var ph = Convert.ToString(grid.CurrentRow.Cells["الهاتف"].Value);
            if (string.IsNullOrWhiteSpace(ph)) { Ui.Warn("لا يوجد رقم هاتف."); return; }
            _ = WhatsApp.Send(ph, $"{Settings.Get("shop_name")}\nعزيزي {grid.CurrentRow.Cells["العميل"].Value}، نود تذكيركم بأن رصيد حسابكم المطلوب هو {Ui.M(Db.D(grid.CurrentRow.Cells["الرصيد المطلوب"].Value))} د.ع. شكراً لتعاونكم.");
        };
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || !grid.Columns.Contains("أيام بلا تسديد")) return;
            long d = Db.L(grid.Rows[e.RowIndex].Cells["أيام بلا تسديد"].Value);
            if (d > 90) e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
            else if (d > 30) e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
        };
        Reload();
        return Page("أعمار الديون", grid, top);
    }

    // ---------- تقرير الصيانة ----------
    TabPage Repairs()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var b = Theme.Btn("عرض", Theme.Accent, 90);
        top.Controls.Add(b);
        void Reload() => grid.DataSource = Db.Query(@"SELECT r.id, r.id AS [الوصل], r.date_out AS [التسليم], r.customer AS [الزبون], r.device AS [الجهاز],
            e.name AS [الفني], r.final_price AS [السعر], IFNULL((SELECT SUM(qty*cost) FROM repair_parts p WHERE p.repair_id=r.id),0) AS [كلفة القطع],
            r.final_price-IFNULL((SELECT SUM(qty*cost) FROM repair_parts p WHERE p.repair_id=r.id),0) AS [الربح]
            FROM repairs r LEFT JOIN employees e ON e.id=r.technician_id
            WHERE r.status='تم التسليم' AND r.date_out BETWEEN @p0 AND @p1 ORDER BY r.date_out", A, B);
        b.Click += (s, e) => Reload();
        Reload();
        return Page("الصيانة المُسلَّمة", grid, top);
    }

    // ---------- سجل العمليات ----------
    TabPage AuditLog()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var b = Theme.Btn("عرض", Theme.Accent, 90);
        top.Controls.Add(b);
        void Reload() => grid.DataSource = Db.Query(@"SELECT a.id, a.date AS [التاريخ], u.full_name AS [المستخدم], a.action AS [العملية], a.details AS [التفاصيل]
            FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE a.date BETWEEN @p0 AND @p1 ORDER BY a.id DESC", A, B);
        b.Click += (s, e) => Reload();
        Reload();
        return Page("سجل العمليات", grid, top);
    }

    // ---------- طلبات التوصيل ----------
    TabPage Delivery()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var bLoad = Theme.Btn("عرض", Theme.Accent, 100);
        var bDone = Theme.Btn("تم التسليم", Theme.Success, 120);
        var bBack = Theme.Btn("راجع", Theme.Danger, 100);
        top.Controls.AddRange(new Control[] { bLoad, bDone, bBack });
        void Reload() => grid.DataSource = Db.Query(@"SELECT i.id, i.id AS [الفاتورة], i.date AS [التاريخ], p.name AS [العميل], p.phone AS [الهاتف],
            d.name AS [شركة التوصيل], i.delivery_fee AS [الأجور], i.net AS [الصافي], i.net-i.paid AS [المتبقي للتحصيل], i.delivery_status AS [الحالة]
            FROM invoices i JOIN delivery_companies d ON d.id=i.delivery_id LEFT JOIN parties p ON p.id=i.party_id
            WHERE i.date BETWEEN @p0 AND @p1 ORDER BY i.id DESC", A, B);
        void SetStatus(string st)
        {
            if (grid.CurrentRow == null) return;
            Db.Exec("UPDATE invoices SET delivery_status=@p0 WHERE id=@p1", st, Db.L(grid.CurrentRow.Cells["id"].Value));
            Reload();
        }
        bLoad.Click += (s, e) => Reload();
        bDone.Click += (s, e) => SetStatus("تم التسليم");
        bBack.Click += (s, e) => SetStatus("راجع");
        Reload();
        return Page("طلبات التوصيل", grid, top);
    }

    // ---------- مراكز الكلفة ----------
    TabPage CostCenters()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var b = Theme.Btn("عرض", Theme.Accent, 100);
        top.Controls.Add(b);
        void Reload() => grid.DataSource = Db.Query(@"SELECT c.id, c.name AS [مركز الكلفة],
            IFNULL((SELECT SUM(net) FROM invoices WHERE cost_center_id=c.id AND type='Sale' AND date BETWEEN @p0 AND @p1),0) AS [المبيعات],
            IFNULL((SELECT SUM(l.qty*(l.price-l.cost)) FROM invoice_lines l JOIN invoices i ON i.id=l.invoice_id
                    WHERE i.cost_center_id=c.id AND i.type='Sale' AND i.date BETWEEN @p0 AND @p1),0) AS [الربح الإجمالي],
            IFNULL((SELECT -SUM(amount*rate) FROM cash_moves WHERE cost_center_id=c.id AND amount<0 AND invoice_id IS NULL AND date BETWEEN @p0 AND @p1),0) AS [المصروفات]
            FROM cost_centers c ORDER BY c.id", A, B);
        b.Click += (s, e) => Reload();
        Reload();
        return Page("مراكز الكلفة", grid, top);
    }
}

/// <summary>سجل الفواتير مع تفاصيل الأسطر</summary>
public class InvoicesListForm : BaseForm
{
    readonly ComboBox cbType = Ui.Combo(150);
    readonly TextBox search = new() { Width = 220, PlaceholderText = "اسم الجهة أو رقم الفاتورة" };
    readonly DataGridView grid = Ui.NewGrid(), lines = Ui.NewGrid();
    static readonly string[] Codes = { "", "Sale", "Purchase", "SaleReturn", "PurchaseReturn", "Damage" };

    public InvoicesListForm()
    {
        cbType.Items.AddRange(new object[] { "الكل", "بيع", "شراء", "إرجاع بيع", "إرجاع شراء", "إتلاف" });
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("النوع", cbType));
        bar.Controls.Add(Ui.Labeled("بحث", search));
        var bPrint = Theme.Btn("طباعة الفاتورة", Theme.Purple, 130);
        var bEdit = Theme.Btn("تعديل", Theme.Accent, 90);
        var bDel = Theme.Btn("حذف", Theme.Danger, 80);
        var bWa = Theme.Btn("واتساب", Theme.Success, 90);
        if (Session.Can("print")) bar.Controls.Add(bPrint);
        if (Session.Can("edit_invoice")) { bar.Controls.Add(bEdit); bar.Controls.Add(bDel); }
        bar.Controls.Add(bWa);
        Ui.GridTools(bar, grid, () => "سجل الفواتير");
        bPrint.Click += (s, e) => { if (Sel > 0) InvoiceOps.BuildPrint(Sel)?.Print(); };
        bEdit.Click += (s, e) => Edit();
        bDel.Click += (s, e) => Delete();
        bWa.Click += (s, e) => SendWa();
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && Session.Can("print")) InvoiceOps.BuildPrint(Sel)?.Print(); };
        lines.Dock = DockStyle.Bottom;
        lines.Height = 230;

        Controls.Add(grid);
        Controls.Add(lines);
        Controls.Add(bar);
        Controls.Add(Theme.Title("سجل الفواتير"));

        cbType.SelectedIndexChanged += (s, e) => LoadGrid();
        search.TextChanged += (s, e) => LoadGrid();
        grid.SelectionChanged += (s, e) => LoadLines();
        cbType.SelectedIndex = 0;
    }

    void LoadGrid()
    {
        var q = search.Text.Trim();
        grid.DataSource = Db.Query($@"SELECT v.id, v.id AS [الرقم], {string.Format(Ui.TypeCaseSql, "v.type")} AS [النوع], v.date AS [التاريخ],
            IFNULL(p.name,'—') AS [الجهة], w.name AS [المخزن], v.pay_type AS [الدفع], v.net AS [الصافي], v.paid AS [المدفوع],
            v.net-v.paid AS [المتبقي], u.full_name AS [المستخدم]
            FROM invoices v LEFT JOIN parties p ON p.id=v.party_id LEFT JOIN warehouses w ON w.id=v.warehouse_id LEFT JOIN users u ON u.id=v.user_id
            WHERE (@p0='' OR v.type=@p0) AND (IFNULL(p.name,'') LIKE @p1 OR CAST(v.id AS TEXT)=@p2)
            ORDER BY v.id DESC LIMIT 1000", Codes[Math.Max(0, cbType.SelectedIndex)], "%" + q + "%", q);
    }

    long Sel => grid.CurrentRow == null ? 0 : Db.L(grid.CurrentRow.Cells["id"].Value);

    void Edit()
    {
        long id = Sel;
        if (id == 0 || !Session.Guard("edit_invoice")) return;
        var type = Db.S(Db.Scalar("SELECT type FROM invoices WHERE id=@p0", id));
        var perm = type switch { "Sale" => "sales", "Purchase" => "purchases", "Damage" => "damage", _ => "returns" };
        if (!Session.Guard(perm)) return;
        if (Db.S(Db.Scalar("SELECT notes FROM invoices WHERE id=@p0", id)).StartsWith("تسوية جرد")) { Ui.Warn("قيود الجرد لا تُعدَّل، يمكن حذفها فقط."); return; }
        var f = new InvoiceForm(type, id);
        if (Parent?.FindForm() is MainForm m) m.Open(f.Text, f);
        else { f.StartPosition = FormStartPosition.CenterScreen; f.WindowState = FormWindowState.Maximized; f.Show(); }
    }

    void Delete()
    {
        long id = Sel;
        if (id == 0 || !Session.Guard("edit_invoice") || !Session.Guard("delete")) return;
        if (!Ui.Confirm($"حذف الفاتورة رقم {id} مع إرجاع المخزون وحذف المبالغ المرتبطة بها؟")) return;
        using (var tx = new Tx())
        {
            try { InvoiceOps.Remove(tx, id); tx.Commit(); }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return; }
        }
        Db.Audit("حذف فاتورة", $"رقم {id}");
        LoadGrid();
    }

    void SendWa()
    {
        long id = Sel;
        if (id == 0) return;
        var r = Db.Query("SELECT v.net, v.paid, v.date, v.party_id, p.phone FROM invoices v LEFT JOIN parties p ON p.id=v.party_id WHERE v.id=@p0", id).Rows[0];
        if (Db.S(r["phone"]) == "") { Ui.Warn("لا يوجد رقم هاتف لجهة هذه الفاتورة."); return; }
        var items = Db.Query("SELECT i.name, SUM(l.qty) q, l.price FROM invoice_lines l JOIN items i ON i.id=l.item_id WHERE l.invoice_id=@p0 GROUP BY l.item_id, l.price", id);
        var msg = $"{Settings.Get("shop_name")}\nفاتورة رقم {id} — {Db.S(r["date"])[..10]}\n" +
                  string.Join("\n", items.Rows.Cast<DataRow>().Select(x => $"• {Db.S(x["name"])} × {Ui.M(Db.D(x["q"]))} = {Ui.M(Db.D(x["q"]) * Db.D(x["price"]))}")) +
                  $"\nالصافي: {Ui.M(Db.D(r["net"]))}\nالمدفوع: {Ui.M(Db.D(r["paid"]))}\nرصيدكم الحالي: {Ui.M(Ui.PartyBalance(Db.L(r["party_id"])))}";
        _ = WhatsApp.Send(Db.S(r["phone"]), msg);
    }

    void LoadLines()
    {
        if (grid.CurrentRow == null) { lines.DataSource = null; return; }
        lines.DataSource = Db.Query(@"SELECT l.id, i.name AS [المادة], l.length AS [الطول], l.width AS [العرض], l.qty AS [الكمية],
            l.price AS [السعر], l.qty*l.price AS [المجموع], l.cost AS [الكلفة], l.expiry AS [الصلاحية]
            FROM invoice_lines l JOIN items i ON i.id=l.item_id WHERE l.invoice_id=@p0", Db.L(grid.CurrentRow.Cells["id"].Value));
    }
}

/// <summary>لوحة التحكم الرئيسية</summary>
public class DashboardForm : BaseForm
{
    public DashboardForm(MainForm main)
    {
        int expDays = Settings.Int("expiry_days", 30), remDays = Settings.Int("reminder_days", 3);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        cards.Controls.Add(Theme.Card("صافي مبيعات اليوم", Ui.M(Stats.TodaySales()), Theme.Accent));
        cards.Controls.Add(Theme.Card("النقد في الصناديق (د.ع)", Ui.M(Stats.CashTotal()), Theme.Success));
        cards.Controls.Add(Theme.Card("ديون لنا", Ui.M(Stats.Receivables()), Theme.Warning));
        cards.Controls.Add(Theme.Card("ديون علينا", Ui.M(Stats.Payables()), Theme.Danger));
        cards.Controls.Add(Theme.Card("مواد تحت حد الطلب", Stats.LowStock().ToString(), Theme.Purple));
        cards.Controls.Add(Theme.Card($"صلاحية خلال {expDays} يوم", Stats.Expiring(expDays).ToString(), Theme.Danger));
        cards.Controls.Add(Theme.Card("أقساط مستحقة", Stats.DueInstallments(remDays).ToString(), Theme.Warning));
        cards.Controls.Add(Theme.Card("أجهزة في الصيانة", Stats.RepairsOpen().ToString(), Theme.Accent));
        cards.Controls.Add(Theme.Card("أجهزة جاهزة للتسليم", Stats.RepairsReady().ToString(), Theme.Success));

        var quick = Theme.Bar();
        quick.BackColor = Theme.Bg;
        void Q(string text, string perm, Color c, Func<Form> make)
        {
            if (!Session.Can(perm)) return;
            var b = Theme.Btn(text, c, 150);
            b.Click += (s, e) => main.Open(text, make());
            quick.Controls.Add(b);
        }
        Q("فاتورة بيع", "sales", Theme.Accent, () => new InvoiceForm("Sale"));
        Q("فاتورة شراء", "purchases", Theme.Success, () => new InvoiceForm("Purchase"));
        Q("استلام جهاز صيانة", "repairs", Theme.Warning, () => new RepairsForm());
        Q("سند مالي", "vouchers", Theme.Purple, () => new VoucherForm());
        Q("الأقساط", "installments", Theme.Gray, () => new InstallmentsForm());

        var grid = Ui.NewGrid();
        grid.DataSource = Db.Query(@"
            SELECT 'مخزون منخفض' AS [التنبيه], i.name AS [المادة / الجهة], 'الرصيد: '||IFNULL(s.q,0)||'  —  حد الطلب: '||i.min_qty AS [التفاصيل]
            FROM items i LEFT JOIN (SELECT item_id, SUM(qty) q FROM batches GROUP BY item_id) s ON s.item_id=i.id
            WHERE i.min_qty>0 AND IFNULL(s.q,0)<=i.min_qty
            UNION ALL
            SELECT CASE WHEN b.expiry<date('now','localtime') THEN 'منتهية الصلاحية' ELSE 'قاربت على الانتهاء' END, i.name,
                   'تاريخ الصلاحية: '||b.expiry||'  —  الكمية: '||b.qty
            FROM batches b JOIN items i ON i.id=b.item_id
            WHERE b.qty>0 AND b.expiry IS NOT NULL AND b.expiry<>'' AND b.expiry<=date('now','localtime','+'||@p0||' day')
            UNION ALL
            SELECT 'قسط مستحق', p.name, 'الاستحقاق: '||t.due_date||'  —  المتبقي: '||(t.amount-t.paid)
            FROM installments t JOIN parties p ON p.id=t.party_id
            WHERE t.amount-t.paid>0.001 AND t.due_date<=date('now','localtime','+'||@p1||' day')
            UNION ALL
            SELECT 'جهاز جاهز للتسليم', r.customer, r.device||'  —  وصل رقم '||r.id||'  —  '||IFNULL(r.phone,'') FROM repairs r WHERE r.status='جاهز'", expDays, remDays);

        Controls.Add(grid);
        Controls.Add(Theme.Title("التنبيهات"));
        Controls.Add(quick);
        Controls.Add(cards);
    }
}
