using System.Data;

namespace Raseed;

/// <summary>
/// تقارير القوائم: المبيعات، المشتريات، عروض الأسعار، تسديد المبيعات، تسديد المشتريات، وملخص البيع والشراء.
/// فلاتر الحساب والمستخدم والعملية والفترة والبحث، وعرض «عام» (قائمة لكل سطر) أو «مفصل» (مادة لكل سطر)، مع المجاميع أسفل الجدول.
/// </summary>
public class ListReportForm : BaseForm
{
    public enum Kind { Sales, Purchases, Quotes, SalesPayments, PurchasePayments, Summary }

    readonly Kind kind;
    readonly ComboBox cbParty = Ui.Combo(220), cbUser = Ui.Combo(170), cbOp = Ui.Combo(150);
    readonly TextBox search = new() { Width = 220, PlaceholderText = "رقم القائمة أو الاسم أو الملاحظة" };
    readonly RadioButton rGeneral = new() { Text = "عام", Checked = true, AutoSize = true, Margin = new Padding(6, 34, 6, 0) },
                         rDetail = new() { Text = "مفصل", AutoSize = true, Margin = new Padding(6, 34, 6, 0) };
    readonly DateTimePicker dFrom = new() { Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" },
                            dTo = new() { Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" };
    readonly DataGridView grid = Ui.NewGrid();
    readonly FlowLayoutPanel totals = new() { Dock = DockStyle.Bottom, Height = 76, WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(10, 6, 10, 6) };

    static string TitleOf(Kind k) => k switch
    {
        Kind.Sales => "تقرير المبيعات", Kind.Purchases => "تقرير المشتريات", Kind.Quotes => "عروض الأسعار",
        Kind.SalesPayments => "تسديد المبيعات", Kind.PurchasePayments => "تسديد المشتريات", _ => "ملخص البيع والشراء"
    };

    bool SaleSide => kind is Kind.Sales or Kind.Quotes or Kind.SalesPayments;
    bool IsInvoiceList => kind is Kind.Sales or Kind.Purchases or Kind.Quotes;

    public ListReportForm(Kind k, long partyId = 0)
    {
        kind = k;
        Text = TitleOf(k);
        dFrom.Value = DateTime.Today;
        dTo.Value = DateTime.Today.AddDays(1).AddMinutes(-1);

        var bar = Theme.Bar();
        if (kind != Kind.Summary)
        {
            Ui.FillCombo(cbParty, "SELECT id,name FROM parties WHERE kind IN (@p0,'عميل ومورد') ORDER BY name", true, "الكل", SaleSide ? "عميل" : "مورد");
            Ui.MakeSearchable(cbParty);
            bar.Controls.Add(Ui.Labeled("الحساب", cbParty));
            if (partyId > 0)
            {
                // من سند أو كشف: كل حركات الحساب منذ البداية
                Ui.SelectId(cbParty, partyId);
                dFrom.Value = new DateTime(2000, 1, 1);
            }
        }
        Ui.FillCombo(cbUser, "SELECT id, IFNULL(NULLIF(full_name,''),username) FROM users ORDER BY id", true, "الكل");
        bar.Controls.Add(Ui.Labeled("المستخدم", cbUser));
        if (kind is Kind.Sales or Kind.Purchases)
        {
            cbOp.Items.AddRange(kind == Kind.Sales ? new object[] { "بيع", "إرجاع بيع", "الكل" } : new object[] { "شراء", "إرجاع شراء", "الكل" });
            cbOp.SelectedIndex = 0;
            bar.Controls.Add(Ui.Labeled("العملية", cbOp));
        }
        if (kind != Kind.Summary) bar.Controls.Add(Ui.Labeled("بحث", search));
        if (IsInvoiceList) { bar.Controls.Add(rGeneral); bar.Controls.Add(rDetail); }
        bar.Controls.Add(Ui.Labeled("من", dFrom));
        bar.Controls.Add(Ui.Labeled("إلى", dTo));
        var bRefresh = new ModernButton { Text = "تحديث", IconName = "refresh-cw", Kind = BtnKind.Success, Height = 42, Margin = new Padding(6, 27, 6, 4) };
        bRefresh.FitWidth(110);
        bar.Controls.Add(bRefresh);
        Ui.GridTools(bar, grid, () => Text, () => $"من {dFrom.Value:yyyy-MM-dd HH:mm} إلى {dTo.Value:yyyy-MM-dd HH:mm}");

        var totalsCard = new CardPanel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(10, 8, 10, 8) };
        totalsCard.Controls.Add(totals);
        totals.Dock = DockStyle.Fill;

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 10 });
        Controls.Add(totalsCard);
        Controls.Add(bar);

        bRefresh.Click += (s, e) => Reload();
        cbParty.SelectedIndexChanged += (s, e) => Reload();
        cbUser.SelectedIndexChanged += (s, e) => Reload();
        cbOp.SelectedIndexChanged += (s, e) => Reload();
        rGeneral.CheckedChanged += (s, e) => { if (rGeneral.Checked) Reload(); };
        rDetail.CheckedChanged += (s, e) => { if (rDetail.Checked) Reload(); };
        search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Reload(); } };
        grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || !IsInvoiceList || !grid.Columns.Contains("رقم القائمة") || !Session.Can("print")) return;
            InvoiceOps.BuildPrint(Db.L(grid.Rows[e.RowIndex].Cells["رقم القائمة"].Value))?.Print();
        };
        Reload();
    }

    string A => dFrom.Value.ToString(Ui.DtFmt);
    string B => dTo.Value.ToString(Ui.DtFmt);

    string[] Types() => kind switch
    {
        Kind.Sales => cbOp.SelectedIndex switch { 0 => new[] { "Sale" }, 1 => new[] { "SaleReturn" }, _ => new[] { "Sale", "SaleReturn" } },
        Kind.Purchases => cbOp.SelectedIndex switch { 0 => new[] { "Purchase" }, 1 => new[] { "PurchaseReturn" }, _ => new[] { "Purchase", "PurchaseReturn" } },
        _ => new[] { "Quote" }
    };

    public void Reload()
    {
        DataTable dt = kind switch
        {
            Kind.SalesPayments or Kind.PurchasePayments => Payments(),
            Kind.Summary => Summary(),
            _ => rDetail.Checked ? InvoiceLines() : Invoices()
        };
        grid.DataSource = dt;
        ShowTotals(dt);
    }

    /// <summary>القوائم: قائمة لكل سطر (المرتجع بقيم سالبة حتى يصح المجموع)</summary>
    DataTable Invoices()
    {
        var types = Types();
        string sign = "CASE WHEN v.type IN ('SaleReturn','PurchaseReturn') THEN -1 ELSE 1 END";
        return Db.Query($@"SELECT {string.Format(Ui.TypeCaseSql, "v.type")} AS [النوع], v.id AS [رقم القائمة],
                IFNULL(p.name, '{(SaleSide ? "زبون نقدي" : "بدون مورد")}') AS [{(SaleSide ? "اسم الزبون" : "اسم المورد")}],
                {sign}*v.total AS [المجموع], {sign}*v.discount AS [الخصم], {sign}*v.net AS [الصافي], {sign}*v.paid AS [الواصل],
                {sign}*(v.net-v.paid) AS [الباقي], IFNULL(d.name,'') AS [الشركات], IFNULL(NULLIF(u.full_name,''),u.username) AS [اسم المستخدم],
                substr(v.date,1,16) AS [التأريخ], v.notes AS [الملاحظات]
            FROM invoices v LEFT JOIN parties p ON p.id=v.party_id LEFT JOIN users u ON u.id=v.user_id LEFT JOIN delivery_companies d ON d.id=v.delivery_id
            WHERE v.type IN ({string.Join(",", types.Select(t => $"'{t}'"))}) AND v.date BETWEEN @p0 AND @p1
              AND (@p2=0 OR v.party_id=@p2) AND (@p3=0 OR v.user_id=@p3)
              AND (@p4='' OR CAST(v.id AS TEXT)=@p4 OR IFNULL(p.name,'') LIKE '%'||@p4||'%' OR IFNULL(v.notes,'') LIKE '%'||@p4||'%')
            ORDER BY v.date, v.id", A, B, Ui.GetId(cbParty), Ui.GetId(cbUser), search.Text.Trim());
    }

    /// <summary>مفصل: مادة لكل سطر</summary>
    DataTable InvoiceLines()
    {
        var types = Types();
        string sign = "CASE WHEN v.type IN ('SaleReturn','PurchaseReturn') THEN -1 ELSE 1 END";
        return Db.Query($@"SELECT {string.Format(Ui.TypeCaseSql, "v.type")} AS [النوع], v.id AS [رقم القائمة], substr(v.date,1,16) AS [التأريخ],
                IFNULL(p.name, '{(SaleSide ? "زبون نقدي" : "بدون مورد")}') AS [{(SaleSide ? "اسم الزبون" : "اسم المورد")}],
                i.name AS [المادة], {sign}*SUM(l.qty) AS [العدد], l.price AS [السعر], {sign}*SUM(l.qty*l.price) AS [المجموع],
                MAX(IFNULL(l.note,'')) AS [الملاحظة]
            FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id JOIN items i ON i.id=l.item_id LEFT JOIN parties p ON p.id=v.party_id
            WHERE v.type IN ({string.Join(",", types.Select(t => $"'{t}'"))}) AND v.date BETWEEN @p0 AND @p1
              AND (@p2=0 OR v.party_id=@p2) AND (@p3=0 OR v.user_id=@p3)
              AND (@p4='' OR CAST(v.id AS TEXT)=@p4 OR IFNULL(p.name,'') LIKE '%'||@p4||'%' OR i.name LIKE '%'||@p4||'%')
            GROUP BY v.id, l.item_id, l.price ORDER BY v.date, v.id", A, B, Ui.GetId(cbParty), Ui.GetId(cbUser), search.Text.Trim());
    }

    /// <summary>تسديد المبيعات: كل مبلغ مستلم من زبون. تسديد المشتريات: كل مبلغ مدفوع لمورد</summary>
    DataTable Payments() => Db.Query($@"SELECT m.id AS [رقم السند], substr(m.date,1,16) AS [التأريخ], p.name AS [الحساب], m.kind AS [النوع],
            c.name AS [الصندوق], ABS(m.amount) AS [المبلغ], c.currency AS [العملة], ABS(m.amount*m.rate) AS [بالدينار],
            m.invoice_id AS [القائمة], IFNULL(NULLIF(u.full_name,''),u.username) AS [اسم المستخدم], m.note AS [البيان]
        FROM cash_moves m JOIN parties p ON p.id=m.party_id JOIN cashboxes c ON c.id=m.cashbox_id LEFT JOIN users u ON u.id=m.user_id
        WHERE m.amount {(kind == Kind.SalesPayments ? ">" : "<")} 0 AND p.kind IN ('{(kind == Kind.SalesPayments ? "عميل" : "مورد")}','عميل ومورد')
          AND m.date BETWEEN @p0 AND @p1
          AND (@p2=0 OR m.party_id=@p2) AND (@p3=0 OR m.user_id=@p3)
          AND (@p4='' OR p.name LIKE '%'||@p4||'%' OR IFNULL(m.note,'') LIKE '%'||@p4||'%')
        ORDER BY m.date, m.id", A, B, Ui.GetId(cbParty), Ui.GetId(cbUser), search.Text.Trim());

    /// <summary>ملخص يومي: المبيعات والمرتجعات والمشتريات والمبالغ المستلمة والمدفوعة</summary>
    DataTable Summary() => Db.Query(@"SELECT d AS [اليوم],
            SUM(sale) AS [المبيعات], SUM(sret) AS [إرجاع البيع], SUM(sale)-SUM(sret) AS [صافي المبيعات],
            SUM(pur) AS [المشتريات], SUM(pret) AS [إرجاع الشراء], SUM(pur)-SUM(pret) AS [صافي المشتريات],
            SUM(inn) AS [المستلم من الزبائن], SUM(outt) AS [المدفوع للمجهزين]
        FROM (
            SELECT substr(date,1,10) AS d,
                CASE type WHEN 'Sale' THEN net ELSE 0 END AS sale, CASE type WHEN 'SaleReturn' THEN net ELSE 0 END AS sret,
                CASE type WHEN 'Purchase' THEN net ELSE 0 END AS pur, CASE type WHEN 'PurchaseReturn' THEN net ELSE 0 END AS pret, 0 AS inn, 0 AS outt
            FROM invoices WHERE type IN ('Sale','SaleReturn','Purchase','PurchaseReturn') AND date BETWEEN @p0 AND @p1 AND (@p2=0 OR user_id=@p2)
            UNION ALL
            SELECT substr(m.date,1,10), 0, 0, 0, 0, CASE WHEN m.amount>0 THEN m.amount*m.rate ELSE 0 END, CASE WHEN m.amount<0 THEN -m.amount*m.rate ELSE 0 END
            FROM cash_moves m WHERE m.party_id IS NOT NULL AND m.date BETWEEN @p0 AND @p1 AND (@p2=0 OR m.user_id=@p2)
        ) GROUP BY d ORDER BY d", A, B, Ui.GetId(cbUser));

    void ShowTotals(DataTable dt)
    {
        totals.Controls.Clear();
        double Sum(string col) => dt.Columns.Contains(col) ? dt.Rows.Cast<DataRow>().Sum(r => Db.D(r[col])) : 0;
        void Add(string caption, double v, Color? color = null)
        {
            totals.Controls.Add(new StatLabel { Caption = caption, Value = Ui.M(v), Size = new Size(170, 64), Margin = new Padding(8, 0, 8, 0), ValueColor = color ?? Theme.Ink });
        }
        totals.Controls.Add(new StatLabel { Caption = "عدد السطور", Value = dt.Rows.Count.ToString(), Size = new Size(110, 64), Margin = new Padding(8, 0, 8, 0) });
        switch (kind)
        {
            case Kind.Sales:
            case Kind.Purchases:
            case Kind.Quotes:
                if (rDetail.Checked) { Add("مجموع العدد", Sum("العدد")); Add("المجموع", Sum("المجموع"), Theme.Brand); break; }
                string what = kind == Kind.Sales ? "المبيعات" : kind == Kind.Purchases ? "المشتريات" : "العروض";
                Add("مجموع " + what, Sum("المجموع"));
                Add("مجموع الخصم", Sum("الخصم"), Theme.Warning);
                Add("صافي " + what, Sum("الصافي"), Theme.Brand);
                if (kind != Kind.Quotes) { Add("مجموع الواصل", Sum("الواصل"), Theme.Success); Add("مجموع الباقي", Sum("الباقي"), Theme.Danger); }
                break;
            case Kind.SalesPayments:
            case Kind.PurchasePayments:
                Add(kind == Kind.SalesPayments ? "مجموع المستلم (د.ع)" : "مجموع المدفوع (د.ع)", Sum("بالدينار"), Theme.Brand);
                break;
            default:
                Add("صافي المبيعات", Sum("صافي المبيعات"), Theme.Brand);
                Add("صافي المشتريات", Sum("صافي المشتريات"));
                Add("المستلم من الزبائن", Sum("المستلم من الزبائن"), Theme.Success);
                Add("المدفوع للمجهزين", Sum("المدفوع للمجهزين"), Theme.Danger);
                break;
        }
    }
}

/// <summary>تحليل البيانات: مخططات المبيعات والأرباح الشهرية وأكثر المواد مبيعًا وأفضل الزبائن</summary>
public class AnalysisForm : BaseForm
{
    public AnalysisForm()
    {
        Text = "تحليل البيانات";
        AutoScroll = true;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Bg, MinimumSize = new Size(900, 640) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var months = Enumerable.Range(0, 12).Select(i => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(i - 11)).ToList();
        var sales = Db.Query(@"SELECT substr(date,1,7) AS m, SUM(CASE type WHEN 'Sale' THEN net ELSE -net END) AS v FROM invoices
            WHERE type IN ('Sale','SaleReturn') AND date>=@p0 GROUP BY substr(date,1,7)", months[0].ToString(Ui.DFmt))
            .Rows.Cast<DataRow>().ToDictionary(r => Db.S(r["m"]), r => Db.D(r["v"]));
        var profit = Db.Query(@"SELECT substr(v.date,1,7) AS m,
                SUM(CASE v.type WHEN 'Sale' THEN 1 ELSE -1 END * l.qty*(l.price-l.cost)) AS v FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id
            WHERE v.type IN ('Sale','SaleReturn') AND v.date>=@p0 GROUP BY substr(v.date,1,7)", months[0].ToString(Ui.DFmt))
            .Rows.Cast<DataRow>().ToDictionary(r => Db.S(r["m"]), r => Db.D(r["v"]));
        var disc = Db.Query("SELECT substr(date,1,7) AS m, SUM(discount) AS v FROM invoices WHERE type='Sale' AND date>=@p0 GROUP BY substr(date,1,7)", months[0].ToString(Ui.DFmt))
            .Rows.Cast<DataRow>().ToDictionary(r => Db.S(r["m"]), r => Db.D(r["v"]));
        string K(DateTime d) => d.ToString("yyyy-MM");
        string L(DateTime d) => d.ToString("M/yy");

        table.Controls.Add(ChartCard("صافي المبيعات الشهري", "آخر 12 شهرًا بعد المرتجعات", "chart-column",
            months.Select(d => (L(d), Math.Max(0, sales.GetValueOrDefault(K(d))))), true), 0, 0);
        table.Controls.Add(ChartCard("إجمالي الربح الشهري", "الفرق بين سعر البيع والكلفة ناقص الخصم", "trending-up",
            months.Select(d => (L(d), Math.Max(0, profit.GetValueOrDefault(K(d)) - disc.GetValueOrDefault(K(d))))), true), 1, 0);

        string from = DateTime.Today.AddDays(-29).ToString(Ui.DFmt);
        var topItems = Db.Query(@"SELECT i.name, SUM(CASE v.type WHEN 'Sale' THEN 1 ELSE -1 END * l.qty*l.price) AS v FROM invoice_lines l
            JOIN invoices v ON v.id=l.invoice_id JOIN items i ON i.id=l.item_id WHERE v.type IN ('Sale','SaleReturn') AND v.date>=@p0
            GROUP BY l.item_id HAVING v>0 ORDER BY v DESC LIMIT 8", from);
        var topParties = Db.Query(@"SELECT IFNULL(p.name,'زبون نقدي') AS name, SUM(CASE v.type WHEN 'Sale' THEN v.net ELSE -v.net END) AS v FROM invoices v
            LEFT JOIN parties p ON p.id=v.party_id WHERE v.type IN ('Sale','SaleReturn') AND v.date>=@p0
            GROUP BY v.party_id HAVING v>0 ORDER BY v DESC LIMIT 8", from);
        static string Short(string s) => s.Length > 14 ? s[..13] + "…" : s;
        table.Controls.Add(ChartCard("أكثر المواد مبيعًا", "قيمة المبيعات خلال آخر 30 يومًا", "package",
            topItems.Rows.Cast<DataRow>().Select(r => (Short(Db.S(r["name"])), Db.D(r["v"]))), false), 0, 1);
        table.Controls.Add(ChartCard("أفضل الزبائن", "صافي المشتريات خلال آخر 30 يومًا", "users",
            topParties.Rows.Cast<DataRow>().Select(r => (Short(Db.S(r["name"])), Db.D(r["v"]))), false), 1, 1);
        Controls.Add(table);
    }

    static Control ChartCard(string title, string subtitle, string icon, IEnumerable<(string, double)> data, bool series)
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Title = title, Subtitle = subtitle, IconName = icon, Margin = new Padding(8) };
        var chart = new BarChart { Dock = DockStyle.Fill, HighlightLast = series };
        chart.Data.AddRange(data);
        if (chart.Data.Count == 0)
            card.Controls.Add(new Label { Text = "لا توجد بيانات في هذه الفترة", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, Font = Theme.F(10.5f) });
        else card.Controls.Add(chart);
        return card;
    }
}
