using System.Data;

namespace Raseed;

/// <summary>التقارير: كشف الحساب، الأرباح وتوزيعها، الصناديق، التوصيل، مراكز الكلفة</summary>
public class ReportsForm : BaseForm
{
    readonly DateTimePicker dFrom = new() { Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" },
                            dTo = new() { Width = 160, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
    string A => dFrom.Value.ToString(Ui.DFmt);
    string B => dTo.Value.ToString(Ui.DFmt) + " 23:59:59";

    readonly long preItem, preParty;

    /// <summary>كل التقارير كتبويبات، أو تقرير واحد فقط (عند فتحه من القائمة الجانبية)</summary>
    public ReportsForm(string only = null, long itemId = 0, long partyId = 0)
    {
        preItem = itemId;
        preParty = partyId;
        dFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        dTo.Value = DateTime.Today;
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("من تاريخ", dFrom));
        bar.Controls.Add(Ui.Labeled("إلى تاريخ", dTo));

        var all = new List<(string Title, string Icon, Func<TabPage> Make)>
        {
            ("كشف حساب", "scroll-text", Statement),
            ("الأرباح وتوزيعها", "trending-up", Profit),
            ("الصناديق والخزائن", "wallet", Boxes),
            ("المصاريف", "receipt", Expenses),
            ("اليومية", "calendar-days", Daily),
            ("مبيعات المواد", "shopping-bag", ItemSales),
            ("حركة مادة", "arrow-left-right", ItemMovement),
            ("أعمار الديون", "history", Aging),
            ("الصيانة المُسلَّمة", "wrench", Repairs),
            ("طلبات التوصيل", "truck", Delivery),
            ("مراكز الكلفة", "layers", CostCenters),
            ("سجل العمليات", "shield-check", AuditLog),
        };
        all.RemoveAll(x => x.Title == "الأرباح وتوزيعها" && !Session.Can("profit") || x.Title == "سجل العمليات" && !Session.IsAdmin);

        if (only != null && all.FirstOrDefault(x => x.Title == only) is { Make: not null } one)
        {
            Text = one.Title;
            // محتوى التقرير مباشرة بلا شريط تبويبات
            var page = one.Make();
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            foreach (var c in page.Controls.Cast<Control>().ToList()) host.Controls.Add(c);
            Controls.Add(host);
            Controls.Add(bar);
            return;
        }
        var tabs = new ModernTabs(vertical: true) { Dock = DockStyle.Fill };
        foreach (var x in all) tabs.Add(x.Make(), x.Icon);
        Controls.Add(tabs);
        Controls.Add(bar);
        Controls.Add(Theme.Title("التقارير"));
    }

    // ---------- المصاريف حسب النوع ----------
    TabPage Expenses()
    {
        var grid = Ui.NewGrid();
        var top = Theme.Bar();
        var cb = Ui.Combo(220);
        Ui.FillCombo(cb, "SELECT id,name FROM expense_types ORDER BY name", true, "— كل الأنواع —");
        var b = Theme.Btn("عرض", Theme.Accent, 100);
        var lbl = new Label { Dock = DockStyle.Bottom, Height = 48, Font = Theme.FS(12), ForeColor = Theme.BrandDark, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.BrandSoft, Padding = new Padding(12, 0, 12, 0) };
        top.Controls.Add(Ui.Labeled("نوع المصروف", cb));
        top.Controls.Add(b);
        void Reload()
        {
            var dt = Db.Query(@"SELECT m.id, substr(m.date,1,16) AS [التاريخ], IFNULL(t.name,'بدون نوع') AS [نوع المصروف], c.name AS [الصندوق],
                -m.amount AS [المبلغ], c.currency AS [العملة], -m.amount*m.rate AS [بالدينار], m.note AS [البيان]
                FROM cash_moves m JOIN cashboxes c ON c.id=m.cashbox_id LEFT JOIN expense_types t ON t.id=m.expense_type_id
                WHERE m.kind='مصروف' AND m.date BETWEEN @p0 AND @p1 AND (@p2=0 OR m.expense_type_id=@p2) ORDER BY m.date", A, B, Ui.GetId(cb));
            grid.DataSource = dt;
            var by = dt.Rows.Cast<DataRow>().GroupBy(r => Db.S(r["نوع المصروف"])).Select(g => $"{g.Key}: {Ui.M(g.Sum(r => Db.D(r["بالدينار"])))}");
            lbl.Text = $"الإجمالي: {Ui.M(dt.Rows.Cast<DataRow>().Sum(r => Db.D(r["بالدينار"])))} د.ع    •    " + string.Join("   |   ", by);
        }
        b.Click += (s, e) => Reload();
        Reload();
        return Page("المصاريف", grid, top, lbl);
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
        var lbl = new Label { Dock = DockStyle.Bottom, Height = 48, Font = Theme.FS(12), ForeColor = Theme.BrandDark, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.BrandSoft, Padding = new Padding(12, 0, 12, 0) };
        var bShow = Theme.Btn("عرض الكشف", Theme.Accent, 130);
        var bWa = Theme.Btn("إرسال الرصيد واتساب", Theme.Success, 190);
        var top = Theme.Bar();
        top.Controls.Add(Ui.Labeled("الجهة", cb));
        top.Controls.Add(bShow);
        top.Controls.Add(bWa);
        double closing = 0;
        // فتح الكشف من شاشة أخرى (سند قبض مثلًا): الحساب محدد والكشف معروض من البداية
        if (preParty > 0)
        {
            Ui.SelectId(cb, preParty);
            dFrom.Value = new DateTime(2000, 1, 1);
            Load += (s, e) => bShow.PerformClick();
        }

        bShow.Click += (s, e) =>
        {
            long pid = Ui.GetId(cb);
            if (pid == 0) return;
            var dt = Ledger.StatementRows(pid);

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
        var summary = new Label { Dock = DockStyle.Bottom, Height = 190, Font = Theme.F(11), BackColor = Theme.Surface, ForeColor = Theme.Text2, Padding = new Padding(16) };
        var cbBox = Ui.Combo(200);
        Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        var bCalc = Theme.Btn("احتساب الأرباح", Theme.Accent, 150);
        var bPost = Theme.Btn("صرف حصص الشركاء", Theme.Purple, 170);
        var top = Theme.Bar();
        top.Controls.Add(bCalc);
        top.Controls.Add(Ui.Labeled("صندوق الصرف", cbBox));
        top.Controls.Add(bPost);
        double netProfit = 0;

        void Calc()
        {
            double Q(string sql) => Db.D(Db.Scalar(sql, A, B));
            const string L = "FROM invoice_lines l JOIN invoices i ON i.id=l.invoice_id WHERE i.date BETWEEN @p0 AND @p1 AND i.type=";
            double sales = Q($"SELECT SUM(l.qty*l.price) {L}'Sale'");
            double disc = Q("SELECT SUM(discount) FROM invoices WHERE type='Sale' AND date BETWEEN @p0 AND @p1");
            double ret = Q($"SELECT SUM(l.qty*l.price) {L}'SaleReturn'");
            double cogs = Q($"SELECT SUM(l.qty*l.cost) {L}'Sale'") - Q($"SELECT SUM(l.qty*l.cost) {L}'SaleReturn'");
            double damage = Q($"SELECT SUM(l.qty*l.cost) {L}'Damage'") + Q($"SELECT SUM(l.qty*l.cost) {L}'StockOut'");
            double exp = -Q("SELECT SUM(amount*rate) FROM cash_moves WHERE kind IN ('مصروف','راتب','سلفة') AND date BETWEEN @p0 AND @p1");
            double rep = Q("SELECT SUM(final_price) FROM repairs WHERE status='تم التسليم' AND date_out BETWEEN @p0 AND @p1");
            double repParts = Q("SELECT SUM(p.qty*p.cost) FROM repair_parts p JOIN repairs r ON r.id=p.repair_id WHERE r.status='تم التسليم' AND r.date_out BETWEEN @p0 AND @p1");
            double fees = Q("SELECT SUM(delivery_fee) FROM invoices WHERE type='Sale' AND date BETWEEN @p0 AND @p1");
            netProfit = sales - disc - ret - cogs - damage - exp + (rep - repParts);
            summary.Text =
                $"المبيعات: {Ui.M(sales)}    |    الخصومات: {Ui.M(disc)}    |    المرتجعات: {Ui.M(ret)}    |    أجور التوصيل المحصلة: {Ui.M(fees)} (أمانة لشركات التوصيل)\n" +
                $"كلفة البضاعة المباعة: {Ui.M(cogs)}    |    المواد المتلفة والمصروفة داخليًا: {Ui.M(damage)}\n" +
                $"إيراد الصيانة: {Ui.M(rep)}    |    كلفة قطع الصيانة: {Ui.M(repParts)}    |    ربح الصيانة: {Ui.M(rep - repParts)}\n" +
                $"المصروفات والرواتب والسلف: {Ui.M(exp)}\n\n" +
                $"صافي الربح للفترة: {Ui.M(netProfit)}";
            summary.ForeColor = netProfit >= 0 ? Theme.Success : Theme.Danger;
            grid.DataSource = Db.Query("SELECT id, name AS [الشريك], share AS [النسبة %], ROUND(@p0*share/100.0,0) AS [الحصة] FROM partners ORDER BY id", netProfit);
            double totalShare = Db.D(Db.Scalar("SELECT SUM(share) FROM partners"));
            if (totalShare > 0 && Math.Abs(totalShare - 100) > 0.01)
                summary.Text += $"\nتنبيه: مجموع نسب الشركاء {Ui.M(totalShare)}% وليس 100%";
        }
        bCalc.Click += (s, e) => Calc();
        bPost.Click += (s, e) =>
        {
            if (!Session.Guard("profit")) return;
            if (netProfit <= 0) { Ui.Warn("احسب الأرباح أولًا (يجب أن يكون الربح موجبًا)."); return; }
            if (!Ui.Confirm($"صرف حصص الشركاء من صافي ربح {Ui.M(netProfit)} للفترة {A} — {dTo.Value:yyyy-MM-dd}؟")) return;
            long box = Ui.GetId(cbBox);
            if (box == 0) { Ui.Warn("اختر صندوق الصرف."); return; }
            double rate = Ui.BoxRate(box);
            var partners = Db.Query("SELECT name, share FROM partners WHERE share>0");
            using var tx = new Tx();
            foreach (DataRow p in partners.Rows)
            {
                double amt = Math.Round(netProfit * Db.D(p["share"]) / 100.0);
                tx.Exec("INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,note,user_id) VALUES(@p0,'توزيع أرباح',@p1,@p2,@p3,@p4,@p5)",
                    Ui.Now, box, -amt / rate, rate, $"حصة الشريك {Db.S(p["name"])} عن الفترة {A} إلى {dTo.Value:yyyy-MM-dd}", Session.UserId);
            }
            tx.Commit();
            Ui.Info("تم تسجيل صرف حصص الشركاء.");
        };
        Calc();   // عرض الأرباح مباشرة عند فتح التقرير
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
        var lbl = new Label { Dock = DockStyle.Bottom, Height = 44, Font = Theme.FS(11), BackColor = Theme.BrandSoft, ForeColor = Theme.BrandDark, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0) };
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
            lbl.Text = Ui.GetId(cb) == 0 ? "   اختر صندوقًا لعرض المجاميع بعملته" : $"   الوارد: {Ui.M(inn)}    |    الصادر: {Ui.M(outt)}    |    الصافي: {Ui.M(inn - outt)}";
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
        // فتح التقرير من شاشة المواد: المادة محددة والحركة معروضة مباشرة (من أول تاريخ)
        if (preItem > 0)
        {
            Ui.SelectId(cb, preItem);
            dFrom.Value = new DateTime(2000, 1, 1);
            Load += (s, e) => b.PerformClick();
        }
        b.Click += (s, e) =>
        {
            long item = Ui.GetId(cb);
            var dt = Db.Query($@"SELECT d, kind, ref, party, inq, outq FROM (
                SELECT i.date AS d, {string.Format(Ui.TypeCaseSql, "i.type")} || CASE WHEN i.notes LIKE 'تسوية جرد%' THEN ' (جرد)' ELSE '' END AS kind,
                    i.id AS ref, IFNULL(p.name,'') AS party,
                    CASE WHEN i.type IN ('Purchase','SaleReturn','StockIn') THEN l.qty ELSE 0 END AS inq,
                    CASE WHEN i.type IN ('Sale','PurchaseReturn','Damage','StockOut') THEN l.qty ELSE 0 END AS outq
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
            _ = WhatsApp.Send(ph, $"{Settings.Get("shop_name")}\nعزيزي {grid.CurrentRow.Cells["العميل"].Value}، نود تذكيركم بأن رصيد حسابكم المطلوب هو {Ui.M(Db.D(grid.CurrentRow.Cells["الرصيد المطلوب"].Value))} د.ع. شكرًا لتعاونكم.");
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
    static readonly string[] Codes = { "", "Sale", "Purchase", "SaleReturn", "PurchaseReturn", "Quote", "Damage", "StockIn", "StockOut" };

    public InvoicesListForm()
    {
        cbType.Items.AddRange(new object[] { "الكل", "بيع", "شراء", "إرجاع بيع", "إرجاع شراء", "عرض سعر", "إتلاف", "إدخال مخزني", "إخراج مخزني" });
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("النوع", cbType));
        bar.Controls.Add(Ui.Labeled("بحث", search));
        var bPrint = Theme.Btn("طباعة القائمة", Theme.Purple, 130);
        var bConvert = Theme.Btn("تحويل عرض السعر إلى بيع", Theme.Accent, 190, "repeat");
        bConvert.Click += (s, e) =>
        {
            if (Sel == 0 || !Session.Guard("sales")) return;
            if (Db.S(Db.Scalar("SELECT type FROM invoices WHERE id=@p0", Sel)) != "Quote") { Ui.Warn("اختر عرض سعر من القائمة."); return; }
            if (Parent?.FindForm() is MainForm m) m.Open($"قائمة بيع — من عرض {Sel}", InvoiceForm.SaleFromQuote(Sel));
        };
        var bEdit = Theme.Btn("تعديل", Theme.Accent, 90);
        var bDel = Theme.Btn("حذف", Theme.Danger, 80);
        var bWa = Theme.Btn("واتساب", Theme.Success, 90);
        if (Session.Can("print")) bar.Controls.Add(bPrint);
        if (Session.Can("edit_invoice")) { bar.Controls.Add(bEdit); bar.Controls.Add(bDel); }
        bar.Controls.Add(bWa);
        if (Session.Can("sales")) bar.Controls.Add(bConvert);
        Ui.GridTools(bar, grid, () => "سجل القوائم");
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
        Controls.Add(Theme.Title("سجل القوائم"));

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
        var perm = type switch { "Sale" or "Quote" => "sales", "Purchase" => "purchases", "Damage" => "damage", "StockIn" or "StockOut" => "stock", _ => "returns" };
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

/// <summary>لوحة التحكم الرئيسية: ملخص اليوم، مخطط المبيعات، إجراءات سريعة، وتنبيهات</summary>
public class DashboardForm : BaseForm
{
    readonly MainForm main;

    public DashboardForm(MainForm main)
    {
        this.main = main;
        AutoScroll = true;   // الشاشات القصيرة: تمرير عمودي بدل إخفاء التنبيهات
        int expDays = Settings.Int("expiry_days", 30), remDays = Settings.Int("reminder_days", 3);

        // ---------- الترحيب والوصول السريع (شريط كحلي) ----------
        var hello = new HomeHero(main) { Dock = DockStyle.Top, Height = 250 };

        // ---------- المؤشرات ----------
        double todaySales = Stats.TodaySales();
        double yesterday = Db.D(Db.Scalar("SELECT SUM(CASE type WHEN 'Sale' THEN net ELSE -net END) FROM invoices WHERE type IN ('Sale','SaleReturn') AND date LIKE @p0",
            DateTime.Today.AddDays(-1).ToString(Ui.DFmt) + "%"));
        long todayCount = Db.L(Db.Scalar("SELECT COUNT(*) FROM invoices WHERE type='Sale' AND date LIKE @p0", Ui.Today + "%"));
        double pct = yesterday > 0 ? Math.Round((todaySales - yesterday) / yesterday * 100) : 0;
        string trend = yesterday > 0
            ? (pct >= 0 ? "+" : "−") + Math.Abs(pct).ToString("0") + "% عن أمس  •  " + todayCount + " قائمة"
            : todayCount + " قائمة اليوم";
        var kpis = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        var cards = new[]
        {
            Kpi("صافي مبيعات اليوم", Ui.M(todaySales), Theme.Orange, "trending-up", trend, "تقرير المبيعات"),
            Kpi("النقد في الصناديق (د.ع)", Ui.M(Stats.CashTotal()), Theme.Success, "wallet", "كل الصناديق بالدينار", "حساب الصناديق"),
            Kpi("ديون لنا", Ui.M(Stats.Receivables()), Theme.Warning, "hand-coins", "على الزبائن", "حساب الزبائن"),
            Kpi("ديون علينا", Ui.M(Stats.Payables()), Theme.Danger, "landmark", "للمجهزين", "حساب المجهزين"),
        };
        if (todaySales < yesterday && yesterday > 0) cards[0].Accent = Theme.Warning;
        kpis.Controls.AddRange(cards);
        kpis.Resize += (s, e) =>
        {
            int w = (kpis.ClientSize.Width - 4 - cards.Length * 16) / cards.Length;
            foreach (var c in cards) { c.Width = Math.Max(150, w); c.Height = 122; }
        };

        // ---------- المخطط والإجراءات ----------
        var mid = new Panel { Dock = DockStyle.Top, Height = 330, Padding = new Padding(0, 6, 0, 8) };
        var chartCard = new CardPanel { Dock = DockStyle.Fill, Title = "المبيعات — آخر 14 يومًا", Subtitle = "صافي المبيعات اليومي بعد المرتجعات", IconName = "chart-column" };
        var chart = new BarChart { Dock = DockStyle.Fill };
        var from = DateTime.Today.AddDays(-13);
        var rows = Db.Query(@"SELECT substr(date,1,10) AS d, SUM(CASE type WHEN 'Sale' THEN net ELSE -net END) AS v FROM invoices
            WHERE type IN ('Sale','SaleReturn') AND date>=@p0 GROUP BY substr(date,1,10)", from.ToString(Ui.DFmt));
        var byDay = rows.Rows.Cast<DataRow>().ToDictionary(r => Db.S(r["d"]), r => Db.D(r["v"]));
        for (int i = 0; i < 14; i++)
        {
            var d = from.AddDays(i);
            chart.Data.Add((d.ToString("d/M"), byDay.TryGetValue(d.ToString(Ui.DFmt), out var v) ? Math.Max(0, v) : 0));
        }
        chartCard.Controls.Add(chart);

        mid.Controls.Add(chartCard);

        // ---------- التنبيهات ----------
        var alertsCard = new CardPanel { Dock = DockStyle.Fill, Title = "التنبيهات", Subtitle = "ما يحتاج انتباهك اليوم", IconName = "bell" };
        var chips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, WrapContents = false, BackColor = Theme.Surface };
        chips.Controls.Add(Chip("تنبيهات المواد", ItemAlerts.Count(expDays) - Stats.Expiring(expDays), Theme.Purple, "package-minus", "أرصدة المخازن"));
        chips.Controls.Add(Chip($"صلاحية خلال {expDays} يوم", Stats.Expiring(expDays), Theme.Danger, "clock", "أرصدة المخازن"));
        chips.Controls.Add(Chip("أقساط مستحقة", Stats.DueInstallments(remDays), Theme.Warning, "calendar-clock", "الأقساط"));
        chips.Controls.Add(Chip("أجهزة في الصيانة", Stats.RepairsOpen(), Theme.Info, "wrench", "الصيانة"));
        chips.Controls.Add(Chip("جاهزة للتسليم", Stats.RepairsReady(), Theme.Success, "package-check", "الصيانة"));

        var grid = Ui.NewGrid();
        grid.DataSource = Db.Query(ItemAlerts.Sql + @"
            UNION ALL
            SELECT 'قسط مستحق', p.name, 'الاستحقاق: '||t.due_date||'  —  المتبقي: '||(t.amount-t.paid)
            FROM installments t JOIN parties p ON p.id=t.party_id
            WHERE t.amount-t.paid>0.001 AND t.due_date<=date('now','localtime','+'||@p1||' day')
            UNION ALL
            SELECT 'جهاز جاهز للتسليم', r.customer, r.device||'  —  وصل رقم '||r.id||'  —  '||IFNULL(r.phone,'') FROM repairs r WHERE r.status='جاهز'"
            + (Credit.Enabled ? " UNION ALL " + Credit.AlertsSql.Replace("@p9", Ui.Rate("USD").ToString(System.Globalization.CultureInfo.InvariantCulture)) : ""), expDays, remDays);
        alertsCard.Controls.Add(grid);
        alertsCard.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Surface });
        alertsCard.Controls.Add(chips);

        var bottom = new Panel { Dock = DockStyle.Top, Height = 420, Padding = new Padding(0, 8, 0, 0) };
        bottom.Controls.Add(alertsCard);

        Controls.Add(bottom);
        Controls.Add(mid);
        Controls.Add(kpis);
        Controls.Add(hello);
        // التنبيهات تملأ باقي الشاشة، وبحد أدنى يسمح بعرض بضعة أسطر (مع تمرير في الشاشات القصيرة)
        Resize += (s, e) => bottom.Height = Math.Max(320, ClientSize.Height - hello.Height - kpis.Height - mid.Height);
    }

    // لا تقفز الصفحة للأسفل عند تركيز جدول التنبيهات
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    KpiCard Kpi(string title, string value, Color color, string icon, string hint, string page)
    {
        var k = new KpiCard { Title = title, Value = value, Accent = color, IconName = icon, Hint = hint };
        if (main.Pages.Any(p => p.Text == page)) { k.Cursor = Cursors.Hand; k.Click += (s, e) => main.Go(page); }
        return k;
    }

    Control Chip(string text, long count, Color color, string icon, string page)
    {
        var c = new StatChip { Text = text, Count = count, Accent = color, IconName = icon, Size = new Size(206, 62), Margin = new Padding(0, 0, 10, 8) };
        if (main.Pages.Any(p => p.Text == page)) { c.Cursor = Cursors.Hand; c.Click += (s, e) => main.Go(page); }
        return c;
    }
}
