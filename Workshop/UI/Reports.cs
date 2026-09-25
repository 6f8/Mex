using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>التقارير والمصاريف: الإيراد والربح بيوم التسليم، المقارنة بالفترة السابقة، الأداء الشهري، التحليلات، Excel وطباعة</summary>
public class ReportsPage : StackPage
{
    public override string Title => "التقارير والمصاريف";
    public override string Desc => "الإيراد والربح يُحسبان بيوم تسليم الجهاز — " + RangeLabel();
    public override string PageIcon => "chart-column";

    readonly Seg range = new(("month", "هذا الشهر"), ("lastmonth", "الشهر الماضي"), ("year", "هذه السنة"), ("all", "الكل"), ("custom", "مخصص"));
    readonly DateTimePicker dFrom = new() { Width = 150, Format = DateTimePickerFormat.Short }, dTo = new() { Width = 150, Format = DateTimePickerFormat.Short };
    readonly Control customBox;
    readonly Ledger l1 = new(), l2 = new();
    readonly DualBarChart chart = new() { AllLabels = true };
    readonly TextBox tDesc = new() { Width = 220, PlaceholderText = "وصف المصروف (إيجار، كهرباء...)" };
    readonly NumericUpDown nAmount = W.Money(140);
    readonly DateTimePicker dExp = new() { Width = 140, Format = DateTimePickerFormat.Short };
    readonly DataGridView exps = W.Grid();
    readonly Box expBox;
    readonly MeterList types = new(), devices = new(), methods = new();
    readonly Label retText = new() { Dock = DockStyle.Top, Height = 96, Font = Theme.F(9.5f), ForeColor = Theme.Text2, BackColor = Theme.Surface, TextAlign = ContentAlignment.TopLeft };
    readonly DataGridView quality = W.Grid(), suppliers = W.Grid();
    readonly Box retBox, supBox, listBox;
    readonly OrdersGrid closed = new("duration", "estimated") { Height = 420 };
    List<Expense> expRows = new();

    public ReportsPage()
    {
        var head = Theme.Bar();
        var rp = new Panel { Width = range.Width + 8, Height = 64, BackColor = Theme.Surface };
        range.Location = new Point(0, 24);
        rp.Controls.Add(range);
        head.Controls.Add(rp);
        var cf = W.Flow(false);
        cf.Controls.Add(W.Labeled("من", dFrom));
        cf.Controls.Add(W.Labeled("إلى", dTo));
        customBox = cf;
        customBox.Visible = false;
        head.Controls.Add(customBox);
        var bXls = W.Btn("Excel", "file-spreadsheet", BtnKind.Secondary, 90); bXls.Margin = new Padding(4, 26, 4, 4);
        var bPrint = W.Btn("طباعة", "printer", BtnKind.Secondary, 90); bPrint.Margin = new Padding(4, 26, 4, 4);
        head.Controls.Add(bXls);
        head.Controls.Add(bPrint);
        var headWrap = new Panel { Height = 96, BackColor = Theme.Bg };
        head.Dock = DockStyle.Top;
        headWrap.Controls.Add(head);
        head.SizeChanged += (s, e) => { if (headWrap.Height != head.Height) { headWrap.Height = head.Height; Relayout(); } };

        var chartBox = new Box("الأداء الشهري", chart, "chart-line", "آخر 12 شهراً — الإيراد وصافي الربح");

        // ---------- المصاريف ----------
        var expPanel = new Panel { Height = 360, BackColor = Theme.Surface };
        var add = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Surface, WrapContents = false };
        add.Controls.Add(W.Labeled("المصروف", tDesc));
        add.Controls.Add(W.Labeled("المبلغ", nAmount));
        add.Controls.Add(W.Labeled("التاريخ", dExp));
        var bAdd = W.Btn("إضافة", "plus", BtnKind.Primary, 80); bAdd.Margin = new Padding(4, 26, 4, 4);
        add.Controls.Add(bAdd);
        exps.Columns.Add("desc", "المصروف");
        exps.Columns.Add("date", "التاريخ");
        exps.Columns.Add("amount", "المبلغ");
        exps.Columns.Add(new DataGridViewButtonColumn { Name = "rm", HeaderText = "", Text = "حذف", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, FillWeight = 40, DefaultCellStyle = { BackColor = Theme.DangerSoft, ForeColor = Theme.Danger, SelectionBackColor = Theme.DangerSoft, SelectionForeColor = Theme.Danger } });
        exps.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= expRows.Count || exps.Columns[e.ColumnIndex].Name != "rm") return;
            var x = expRows[e.RowIndex];
            if (!W.Confirm("حذف المصروف؟", $"{x.Description} — {Txt.Money(x.Amount)} — {Txt.FmtDate(x.Date)}", "حذف", true)) return;
            Store.DeleteExpense(x);
            Store.NotifyChanged();
            Toast.Show("حُذف مصروف " + x.Description);
        };
        expPanel.Controls.Add(exps);
        expPanel.Controls.Add(add);
        expBox = new Box("المصاريف العامة", expPanel, "receipt");
        bAdd.Click += (s, e) => AddExpense();
        nAmount.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddExpense(); } };
        var typesBox = new Box("أنواع الأعطال", types, "wrench", "الأجهزة المستلمة في الفترة");
        var c1 = new Cols { MinCol = 420 };
        c1.Controls.Add(expBox);
        c1.Controls.Add(typesBox);

        var devBox = new Box("الأجهزة الأكثر صيانة", devices, "smartphone", "المستلمة في الفترة");
        var metBox = new Box("المقبوض حسب طريقة الدفع", methods, "wallet", "دفعات الفترة");
        var c2 = new Cols { MinCol = 380 };
        c2.Controls.Add(devBox);
        c2.Controls.Add(metBox);

        // ---------- المرتجعات بالضمان وجودة الموردين ----------
        var retPanel = new Panel { Height = 320, BackColor = Theme.Surface };
        quality.Columns.Add("sup", "المورد");
        quality.Columns.Add("n", "طلبات بقطعه");
        quality.Columns.Add("r", "رجعت بالضمان");
        quality.Columns.Add("pct", "النسبة");
        retPanel.Controls.Add(quality);
        retPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 28, Text = "جودة قطع الموردين — كل الفترة", Font = Theme.FS(10), ForeColor = Theme.BrandDark, BackColor = Theme.Surface, TextAlign = ContentAlignment.BottomLeft });
        retPanel.Controls.Add(retText);
        retBox = new Box("المرتجعات بالضمان", retPanel, "rotate-ccw");
        var supPanel = new Panel { Height = 320, BackColor = Theme.Surface };
        suppliers.Columns.Add("sup", "المورد");
        suppliers.Columns.Add("n", "عدد القطع");
        suppliers.Columns.Add("t", "إجمالي التكلفة");
        supPanel.Controls.Add(suppliers);
        supBox = new Box("تكلفة القطع حسب المورد", supPanel, "store", "للأجهزة المسلّمة");
        var c3 = new Cols { MinCol = 420 };
        c3.Controls.Add(retBox);
        c3.Controls.Add(supBox);

        listBox = new Box("المُسلّمة والملغاة في الفترة", closed, "list");
        closed.OpenOrder += Acts.View;

        Stack.Controls.Add(headWrap);
        Stack.Controls.Add(l1);
        Stack.Controls.Add(l2);
        Stack.Controls.Add(chartBox);
        Stack.Controls.Add(c1);
        Stack.Controls.Add(c2);
        Stack.Controls.Add(c3);
        Stack.Controls.Add(listBox);

        range.Value = "month";
        var now = DateTime.Today;
        dFrom.Value = new DateTime(now.Year, now.Month, 1);
        dTo.Value = now;
        range.Changed += v => { customBox.Visible = v == "custom"; Reload(); };
        dFrom.ValueChanged += (s, e) => Reload();
        dTo.ValueChanged += (s, e) => Reload();
        bXls.Click += (s, e) => Excel();
        bPrint.Click += (s, e) => Print();
    }

    (string A, string B) Period(string r = null)
    {
        var now = DateTime.Today; int y = now.Year, m = now.Month;
        var first = new DateTime(y, m, 1);
        return (r ?? range.Value) switch
        {
            "month" => (Txt.Iso(first), Txt.Iso(first.AddMonths(1).AddDays(-1))),
            "lastmonth" => (Txt.Iso(first.AddMonths(-1)), Txt.Iso(first.AddDays(-1))),
            "year" => ($"{y}-01-01", $"{y}-12-31"),
            "custom" => (Txt.Iso(dFrom.Value), Txt.Iso(dTo.Value)),
            _ => ("0000-01-01", "9999-12-31"),
        };
    }

    (string A, string B)? Prev()
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return range.Value switch
        {
            "month" => (Txt.Iso(first.AddMonths(-1)), Txt.Iso(first.AddDays(-1))),
            "lastmonth" => (Txt.Iso(first.AddMonths(-2)), Txt.Iso(first.AddMonths(-1).AddDays(-1))),
            "year" => ($"{DateTime.Today.Year - 1}-01-01", $"{DateTime.Today.Year - 1}-12-31"),
            _ => null
        };
    }

    string RangeLabel() => range.Value switch
    {
        "custom" => $"من {Txt.FmtDate(Txt.Iso(dFrom.Value))} إلى {Txt.FmtDate(Txt.Iso(dTo.Value))}",
        "month" => "هذا الشهر", "lastmonth" => "الشهر الماضي", "year" => "هذه السنة", _ => "كل الفترة"
    };

    public override void Reload()
    {
        MainForm.Instance?.UpdateTitle(this);
        var (a, b) = Period();
        var S1 = Calc.Summarize(a, b);
        string margin = S1.Revenue != 0 ? (S1.Profit / S1.Revenue * 100).ToString("0.0") + "%" : "—";
        string growth = "—", growthFoot = "اختر شهراً أو سنة للمقارنة"; int gtone = 0;
        if (Prev() is var (pa, pb))
        {
            var P = Calc.Summarize(pa, pb);
            if (P.Revenue > 0) { double g = (S1.Revenue - P.Revenue) / P.Revenue * 100; growth = (g >= 0 ? "+" : "") + g.ToString("0.0") + "%"; gtone = g >= 0 ? 1 : -1; growthFoot = "مقارنة بإيراد " + Txt.Money(P.Revenue); }
            else growthFoot = "لا توجد بيانات للفترة السابقة";
        }
        l1.Set(new[]
        {
            new Ledger.Cell("أجهزة مُسلّمة", S1.Active.Count.ToString(), $"{S1.Received.Count} استُلم في الفترة — {S1.List.Count - S1.Active.Count} ملغى"),
            new Ledger.Cell("الإيراد", Txt.Money(S1.Revenue), S1.Fees > 0 ? "منها أجور فحص " + Txt.Money(S1.Fees) : "من الأجهزة المسلّمة", Pal.Primary),
            new Ledger.Cell("صافي الربح", Txt.Money(S1.Profit), $"قطع {Txt.Money(S1.Parts)} + مصاريف {Txt.Money(S1.Expenses)}{(S1.Loss > 0 ? " + ملغاة " + Txt.Money(S1.Loss) : "")}", Pal.Amber, S1.Profit >= 0 ? 1 : -1),
            new Ledger.Cell("خسائر الملغاة", Txt.Money(S1.Loss), null, null, S1.Loss > 0 ? -1 : 0),
        });
        l2.Set(new[]
        {
            new Ledger.Cell("المقبوض فعلياً", Txt.Money(S1.Cash), "دفعات مستلمة داخل الفترة", Pal.Good),
            new Ledger.Cell("ديون من هذه الفترة", Txt.Money(S1.Debt), "أجهزة سُلّمت ولم تُدفع كاملاً", null, S1.Debt > 0 ? -1 : 0),
            new Ledger.Cell("هامش الربح", margin, null, null, S1.Profit >= 0 ? 1 : -1),
            new Ledger.Cell("مقارنة بالفترة السابقة", growth, growthFoot, null, gtone),
        });

        // الأداء الشهري
        chart.Labels.Clear(); chart.Series.Clear();
        var rev = new double[12]; var net = new double[12];
        for (int i = 11, k = 0; i >= 0; i--, k++)
        {
            var d = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-i);
            var M = Calc.Summarize(Txt.Iso(d), Txt.Iso(d.AddMonths(1).AddDays(-1)));
            chart.Labels.Add(Txt.MonthName(d));
            rev[k] = M.Revenue; net[k] = M.Profit;
        }
        chart.Series.Add(("الإيراد", rev, Pal.Primary));
        chart.Series.Add(("صافي الربح", net, Pal.Amber));
        chart.Invalidate();

        // المصاريف
        expRows = S1.Exps.OrderByDescending(e => e.Date, StringComparer.Ordinal).ToList();
        exps.Rows.Clear();
        foreach (var e in expRows) exps.Rows.Add(e.Description, Txt.FmtShortDate(e.Date), Txt.Money(e.Amount));
        expBox.Subtitle = "المجموع " + Txt.Money(S1.Expenses);

        var tp = S1.Received.GroupBy(o => o.IssueType).OrderByDescending(g => g.Count()).ToList();
        types.Set(tp.Select(g => new MeterList.Row(g.Key, g.Count(), $"{g.Count()}  ({Math.Round(g.Count() * 100.0 / Math.Max(1, S1.Received.Count))}%)")));
        var dv = Calc.GroupByName(S1.Received, o => o.Device).OrderByDescending(g => g.Items.Count).Take(7);
        devices.Set(dv.Select(g => new MeterList.Row(g.Label, g.Items.Count, g.Items.Count + " مرة")));
        var m = Calc.PaymentsByMethod(d => Calc.InRange(d, a, b));
        double mt = m.Values.Sum();
        methods.EmptyText = "لا توجد دفعات في هذه الفترة";
        methods.Set(m.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Select(x => new MeterList.Row(x.Key, x.Value, $"{Txt.Money(x.Value)}  ({Math.Round(x.Value * 100 / Math.Max(1, mt))}%)")));

        // المرتجعات بالضمان
        var rets = Store.Orders.Where(o => o.WarrantyOf != null && Calc.InRange(o.DateReceived, a, b)).ToList();
        int rate = S1.Active.Count > 0 ? (int)Math.Round(rets.Count * 100.0 / S1.Active.Count) : 0;
        var byType = rets.GroupBy(r => (Calc.Find(r.WarrantyOf) ?? r).IssueType).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}: {g.Count()}");
        retText.Text = $"أجهزة رجعت بالضمان في الفترة:  {rets.Count}\nنسبتها إلى الأجهزة المسلّمة:  {(S1.Active.Count > 0 ? rate + "%" : "—")}\n" +
                       (rets.Count > 0 ? "حسب العطل الأصلي:  " + string.Join("   ", byType) : "");
        retText.ForeColor = rate > 10 ? Pal.Bad : Theme.Text2;
        retBox.Subtitle = rets.Count > 0 ? $"{rets.Count} في الفترة" : "لا مرتجعات في الفترة";
        var installed = new Dictionary<string, int>(); var returned = new Dictionary<string, int>();
        foreach (var o in Store.Orders.Where(o => o.Status == K.Done && o.WarrantyOf == null))
            foreach (var k in o.Parts.Select(p => p.Supplier).Where(x => x != "").Distinct()) installed[k] = installed.GetValueOrDefault(k) + 1;
        foreach (var r in Store.Orders.Where(o => o.WarrantyOf != null))
            if (Calc.Find(r.WarrantyOf) is Order src)
                foreach (var k in src.Parts.Select(p => p.Supplier).Where(x => x != "").Distinct()) returned[k] = returned.GetValueOrDefault(k) + 1;
        quality.Rows.Clear();
        foreach (var x in installed.Select(kv => (k: kv.Key, n: kv.Value, r: returned.GetValueOrDefault(kv.Key))).OrderByDescending(x => (double)x.r / x.n).ThenByDescending(x => x.n).Take(8))
        {
            int i = quality.Rows.Add(x.k, x.n, x.r, Math.Round(x.r * 100.0 / x.n) + "%");
            if ((double)x.r / x.n > 0.1) quality.Rows[i].DefaultCellStyle.ForeColor = Pal.Bad;
        }

        suppliers.Rows.Clear();
        foreach (var g in S1.Active.SelectMany(o => o.Parts).GroupBy(p => p.Supplier == "" ? "غير محدد" : p.Supplier).Select(g => (k: g.Key, n: g.Count(), t: g.Sum(p => p.Cost))).OrderByDescending(x => x.t))
            suppliers.Rows.Add(g.k, g.n, Txt.Money(g.t));

        var sorted = S1.List.OrderByDescending(Calc.ClosedDate, StringComparer.Ordinal).ToList();
        listBox.Subtitle = sorted.Count > 100 ? $"أحدث 100 من {sorted.Count}" : $"{sorted.Count} طلب";
        closed.Fill(sorted.Take(100));
        Relayout();
    }

    void AddExpense()
    {
        var desc = tDesc.Text.Trim();
        double amount = (double)nAmount.Value;
        if (desc == "") { tDesc.Focus(); Toast.Show("اكتب وصف المصروف.", Tone.Warning); return; }
        if (amount <= 0) { nAmount.Focus(); Toast.Show("اكتب مبلغ المصروف.", Tone.Warning); return; }
        Store.AddExpense(new Expense { Id = Txt.Uid("e"), Description = desc, Amount = amount, Date = Txt.Iso(dExp.Value) });
        tDesc.Clear();
        W.Set(nAmount, 0);
        Store.NotifyChanged();
        Toast.Show("سُجّل المصروف");
        tDesc.Focus();
    }

    void Excel()
    {
        var (a, b) = Period();
        var S1 = Calc.Summarize(a, b);
        var f = W.SaveFile("Excel|*.xlsx", $"تقرير_الورشة_{Txt.Today}.xlsx");
        if (f == null) return;
        var title = $"{Store.ShopName} — تقرير {RangeLabel()}";
        try
        {
            Xlsx.Write(f, new()
            {
                new("الملخص", title, new[] { "البند", "القيمة" }, new()
                {
                    new object[] { "الإيراد (أجهزة مُسلّمة)", S1.Revenue }, new object[] { "تكلفة القطع", S1.Parts }, new object[] { "المصاريف العامة", S1.Expenses },
                    new object[] { "خسائر الطلبات الملغاة", S1.Loss }, new object[] { "صافي الربح", S1.Profit }, new object[] { "المقبوض فعلياً", S1.Cash },
                    new object[] { "ديون على أجهزة سُلّمت في الفترة", S1.Debt }, new object[] { "عدد الأجهزة المسلّمة", S1.Active.Count },
                    new object[] { "عدد الأجهزة المستلمة", S1.Received.Count }, new object[] { "العملة", Store.Currency }
                }, new[] { 36, 20 }),
                new("الطلبات", title, new[] { "المرجع", "الزبون", "الهاتف", "الجهاز", "الحالة", "الاستلام", "التسليم / الإلغاء", "السعر", "تكلفة القطع", "الربح", "المدفوع", "المتبقي" },
                    S1.List.Select(o => new object[] { o.RefNo, o.CustomerName, o.Phone, o.Device, o.Status, o.DateReceived, Calc.ClosedDate(o), o.Price, Calc.PartsCost(o),
                        o.Status == K.Done ? Calc.ProfitOf(o) : -Calc.PartsCost(o), o.Paid, Calc.RemainingOf(o) }).ToList(),
                    new[] { 12, 20, 15, 20, 14, 12, 12, 13, 13, 13, 13, 13 }),
                new("المصاريف", title, new[] { "التاريخ", "المصروف", "المبلغ" }, S1.Exps.Select(e => new object[] { e.Date, e.Description, e.Amount }).ToList(), new[] { 14, 34, 16 }),
            });
            if (W.Confirm("تم التصدير", "حُفظ ملف Excel. هل تريد فتحه الآن؟", "فتح الملف")) W.OpenUrl(f);
        }
        catch (Exception ex) { Dialogs.Warn("تعذّر التصدير: " + ex.Message); }
    }

    void Print()
    {
        var (a, b) = Period();
        var S1 = Calc.Summarize(a, b);
        string Cell(string k, string v, string cls = "") => $"<div><span class=\"k\">{k}: </span><b class=\"{cls}\">{Txt.Esc(v)}</b></div>";
        var body = Printer.Header("تقرير الأرباح — " + RangeLabel()) +
            "<div class=\"grid\">" + Cell("الإيراد", Txt.Money(S1.Revenue)) + Cell("تكلفة القطع", Txt.Money(S1.Parts)) + Cell("المصاريف", Txt.Money(S1.Expenses)) +
            Cell("خسائر الملغاة", Txt.Money(S1.Loss)) + Cell("صافي الربح", Txt.Money(S1.Profit), S1.Profit >= 0 ? "good" : "bad") + Cell("المقبوض فعلياً", Txt.Money(S1.Cash)) +
            Cell("الديون", Txt.Money(S1.Debt)) + "</div>" +
            Printer.Table(new[] { "المرجع", "الزبون", "الجهاز", "الحالة", "التسليم / الإلغاء", "السعر", "الربح" },
                S1.List.Select(o => new[] { o.RefNo, o.CustomerName, o.Device, o.Status, Txt.FmtDate(Calc.ClosedDate(o)), Txt.Money(o.Status == K.Done ? o.Price : 0), Txt.Money(o.Status == K.Done ? Calc.ProfitOf(o) : -Calc.PartsCost(o)) })) +
            (S1.Exps.Count > 0 ? "<h3 style=\"font-size:14px;margin-top:18px\">المصاريف</h3>" + string.Concat(S1.Exps.Select(e => Printer.Row(Txt.Esc(e.Description) + " <span class=\"k\">" + Txt.FmtDate(e.Date) + "</span>", Txt.Money(e.Amount)))) : "");
        Printer.Doc(body, "تقرير الورشة", "900px");
    }
}
