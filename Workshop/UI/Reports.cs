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
    readonly MeterList types = new(), devices = new(), methods = new(), sources = new(), areas = new();
    readonly Ledger forecast = new() { MinCell = 170 };
    readonly DataGridView peak = W.Grid();
    readonly Label retText = new() { Dock = DockStyle.Top, Height = 96, Font = Theme.F(9.5f), ForeColor = Theme.Text2, BackColor = Theme.Surface, TextAlign = ContentAlignment.TopLeft };
    readonly DataGridView quality = W.Grid(), suppliers = W.Grid(), techs = W.Grid();
    readonly Box retBox, supBox, listBox, techBox;
    List<Techs.Row> techRows = new();
    int peakMax;
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
        var bBranches = W.Btn("الفروع", "store", BtnKind.Secondary, 90); bBranches.Margin = new Padding(4, 26, 4, 4);
        bBranches.Click += (s, e) => BranchReportDialog.Open();
        head.Controls.Add(bXls);
        head.Controls.Add(bPrint);
        head.Controls.Add(bBranches);
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
        var c2b = new Cols { MinCol = 380 };
        c2b.Controls.Add(new Box("كيف عرف الزبائن بالمحل", sources, "users", "الطلبات المستلمة في الفترة"));
        c2b.Controls.Add(new Box("المناطق", areas, "pin", "الطلبات المستلمة في الفترة"));

        // ---------- المرتجعات بالضمان وجودة الموردين ----------
        var retPanel = new Panel { Height = 320, BackColor = Theme.Surface };
        quality.Columns.Add("sup", "المورد");
        quality.Columns.Add("n", "طلبات بقطعه");
        quality.Columns.Add("r", "رجعت بالضمان");
        quality.Columns.Add("d", "قطع معيبة");
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

        // ---------- الفنيون ----------
        var techPanel = new Panel { Height = 300, BackColor = Theme.Surface };
        techs.Columns.Add("name", "الفني");
        techs.Columns.Add("received", "استلم");
        techs.Columns.Add("delivered", "سلّم");
        techs.Columns.Add("revenue", "الإيراد");
        techs.Columns.Add("profit", "الربح");
        techs.Columns.Add("commission", "العمولة");
        techs.Columns.Add("returns", "رجع بالضمان");
        techs.Columns.Add("time", "متوسط مدة الإصلاح");
        techs.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= techRows.Count) return;
            var c = techs.Columns[e.ColumnIndex].Name;
            if (c == "commission") e.CellStyle.ForeColor = Pal.Primary;
            if (c == "returns" && techRows[e.RowIndex].ReturnRate > 10) e.CellStyle.ForeColor = Pal.Bad;
        };
        techs.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.RowIndex < techRows.Count) PrintTech(techRows[e.RowIndex]); };
        var techBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Surface, WrapContents = false };
        var bTechPrint = W.Btn("كشف عمولة الفني المحدد", "printer", BtnKind.Secondary, 190);
        bTechPrint.Click += (s, e) => { if (techs.CurrentRow is DataGridViewRow r && r.Index < techRows.Count) PrintTech(techRows[r.Index]); };
        techBar.Controls.Add(bTechPrint);
        techPanel.Controls.Add(techs);
        techPanel.Controls.Add(techBar);
        techBox = new Box("الفنيون", techPanel, "wrench", "حسب يوم التسليم — المرتجع بالضمان يُحسب على فني الطلب الأصلي");

        listBox = new Box("المُسلّمة والملغاة في الفترة", closed, "list");
        closed.OpenOrder += Acts.View;

        Stack.Controls.Add(headWrap);
        Stack.Controls.Add(l1);
        Stack.Controls.Add(l2);
        Stack.Controls.Add(chartBox);
        Stack.Controls.Add(c1);
        Stack.Controls.Add(c2);
        Stack.Controls.Add(c2b);
        Stack.Controls.Add(c3);
        Stack.Controls.Add(techBox);
        Stack.Controls.Add(new Box("توقع السيولة — الثلاثون يوماً القادمة", forecast, "wallet", "ما يُتوقع قبضه مقابل ما يجب دفعه (الرواتب ومتوسط المصاريف الشهرية مشمولة)"));
        var peakHost = new Panel { Height = 300, BackColor = Theme.Surface };
        peak.Columns.Add("day", "اليوم");
        foreach (var sl in Peak.Slots) peak.Columns.Add(sl.Title, sl.Title);
        peak.Columns.Add("total", "المجموع");
        peak.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex <= 0 || e.ColumnIndex > Peak.Slots.Length || e.Value is not int v || peakMax == 0) return;
            e.CellStyle.BackColor = Gfx.Mix(Pal.Primary, Theme.Surface, 1 - 0.75f * v / peakMax);
            e.CellStyle.ForeColor = v * 2 > peakMax ? Color.White : Theme.Ink;
        };
        peakHost.Controls.Add(peak);
        Stack.Controls.Add(new Box("أوقات الذروة", peakHost, "clock", "عدد الأجهزة المستلمة حسب اليوم والساعة — لتنظيم دوام الموظفين"));
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
            new Ledger.Cell("المقبوض فعلياً", Txt.Money(S1.Cash), S1.Refunds > 0 ? $"بعد إرجاع {Txt.Money(S1.Refunds)} للزبائن" : "دفعات مستلمة داخل الفترة", Pal.Good),
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

        // مصادر الزبائن والمناطق
        MeterList.Row[] Top(Func<Order, string> key)
        {
            var src = S1.Received.Where(o => key(o) != "").ToList();
            return Calc.GroupByName(src, key).OrderByDescending(g => g.Items.Count).Take(8)
                .Select(g => new MeterList.Row(g.Label, g.Items.Count, $"{g.Items.Count}  ({Math.Round(g.Items.Count * 100.0 / Math.Max(1, src.Count))}%) — إيراد {Txt.Money(g.Items.Where(o => o.Status == K.Done).Sum(o => o.Price))}")).ToArray();
        }
        sources.EmptyText = "لم يُسجَّل مصدر الزبون في طلبات الفترة";
        sources.Set(Top(o => o.X.Source));
        areas.EmptyText = "لم تُسجَّل مناطق في طلبات الفترة";
        areas.Set(Top(o => o.X.Area));

        // المرتجعات بالضمان
        var rets = Store.Orders.Where(o => o.WarrantyOf != null && Calc.InRange(o.DateReceived, a, b)).ToList();
        int rate = S1.Active.Count > 0 ? (int)Math.Round(rets.Count * 100.0 / S1.Active.Count) : 0;
        var byType = rets.GroupBy(r => (Calc.Find(r.WarrantyOf) ?? r).IssueType).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}: {g.Count()}");
        retText.Text = $"أجهزة رجعت بالضمان في الفترة:  {rets.Count}\nنسبتها إلى الأجهزة المسلّمة:  {(S1.Active.Count > 0 ? rate + "%" : "—")}\n" +
                       (rets.Count > 0 ? "حسب العطل الأصلي:  " + string.Join("   ", byType) : "");
        retText.ForeColor = rate > 10 ? Pal.Bad : Theme.Text2;
        retBox.Subtitle = rets.Count > 0 ? $"{rets.Count} في الفترة" : "لا مرتجعات في الفترة";
        quality.Rows.Clear();
        foreach (var x in Defects.SupplierQuality().Where(x => x.Orders > 0 || x.DefectCount > 0)
                     .OrderByDescending(x => Math.Max(x.ReturnRate, x.DefectRate)).ThenByDescending(x => x.Orders).Take(8))
        {
            double pct = Math.Max(x.ReturnRate, x.DefectRate);
            int i = quality.Rows.Add(x.Name, x.Orders, x.WarrantyReturns, x.DefectCount, Math.Round(pct) + "%");
            if (pct > 10) quality.Rows[i].DefaultCellStyle.ForeColor = Pal.Bad;
        }

        // توقع السيولة
        var fc = Forecast.Next30();
        forecast.Set(new[]
        {
            new Ledger.Cell("من أجهزة في الورشة", Txt.Money(fc.InWorkshop), "تُقبض عند التسليم", Pal.Good),
            new Ledger.Cell("ديون الزبائن", Txt.Money(fc.CustomerDebts), "إن سُدّدت", Pal.Good),
            new Ledger.Cell("حسابات التجار", Txt.Money(fc.Dealers), null, Pal.Good),
            new Ledger.Cell("للموردين (مستحق خلال 30 يوماً)", Txt.Money(fc.SuppliersDue), fc.SuppliersNoDate > 0 ? $"+ {Txt.Money(fc.SuppliersNoDate)} بدون موعد" : null, Pal.Bad),
            new Ledger.Cell("رواتب ومصاريف شهرية", Txt.Money(fc.Salaries + fc.Expenses), $"رواتب {Txt.Money(fc.Salaries)} — مصاريف {Txt.Money(fc.Expenses)}", Pal.Bad),
            new Ledger.Cell("الصافي المتوقع", Txt.Money(fc.Net), fc.Net >= 0 ? "يكفي للالتزامات" : "انتبه: الالتزامات أكبر", null, fc.Net >= 0 ? 1 : -1),
        });

        // أوقات الذروة
        var pg = Peak.Grid(a, b);
        peakMax = 0;
        peak.Rows.Clear();
        for (int d = 0; d < Peak.Days.Length; d++)
        {
            var row = new object[Peak.Slots.Length + 2];
            row[0] = Peak.Days[d];
            int sum = 0;
            for (int sl = 0; sl < Peak.Slots.Length; sl++) { row[sl + 1] = pg[d, sl]; sum += pg[d, sl]; peakMax = Math.Max(peakMax, pg[d, sl]); }
            row[^1] = sum;
            peak.Rows.Add(row);
        }

        // الفنيون
        techRows = Techs.Report(a, b);
        techBox.Visible = techRows.Count > 0;
        techs.Rows.Clear();
        foreach (var t in techRows)
            techs.Rows.Add(t.Name, t.Received, t.Delivered, Txt.Money(t.Revenue), Txt.Money(t.Profit), Txt.Money(t.Commission),
                t.Delivered > 0 ? $"{t.Returns}  ({Math.Round(t.ReturnRate)}%)" : t.Returns.ToString(), Calc.FmtDuration(t.AvgTime));
        techBox.Subtitle = techRows.Sum(t => t.Commission) is var tc && tc > 0 ? "مجموع العمولات " + Txt.Money(tc) : "حسب يوم التسليم — المرتجع بالضمان يُحسب على فني الطلب الأصلي";

        suppliers.Rows.Clear();
        foreach (var g in S1.Active.SelectMany(o => o.Parts).GroupBy(p => p.Supplier == "" ? "غير محدد" : p.Supplier).Select(g => (k: g.Key, n: g.Count(), t: g.Sum(p => p.Cost))).OrderByDescending(x => x.t))
            suppliers.Rows.Add(g.k, g.n, Txt.Money(g.t));

        var sorted = S1.List.OrderByDescending(Calc.ClosedDate, StringComparer.Ordinal).ToList();
        listBox.Subtitle = sorted.Count > 100 ? $"أحدث 100 من {sorted.Count}" : $"{sorted.Count} طلب";
        closed.Fill(sorted.Take(100));
        Relayout();
    }

    /// <summary>كشف عمولة فني: الأجهزة التي سلّمها في الفترة وعمولة كل جهاز</summary>
    void PrintTech(Techs.Row t)
    {
        if (t.Orders.Count == 0) { Toast.Show($"لا أجهزة مُسلّمة لـ {t.Name} في هذه الفترة", Tone.Info); return; }
        var tech = Techs.Find(t.Name);
        var body = Printer.Header($"كشف عمولة — {t.Name}") +
            $"<div class=\"grid\"><div><span class=\"k\">الفترة: </span><b>{Txt.Esc(RangeLabel())}</b></div><div><span class=\"k\">طريقة العمولة: </span><b>{Txt.Esc(tech != null ? Techs.BasisText(tech) : "—")}</b></div>" +
            $"<div><span class=\"k\">الأجهزة المسلّمة: </span><b>{t.Delivered}</b></div><div><span class=\"k\">رجع بالضمان: </span><b>{t.Returns}</b></div></div>" +
            Printer.Table(new[] { "المرجع", "التسليم", "الجهاز", "السعر", "الربح", "العمولة" },
                t.Orders.OrderBy(Calc.ClosedDate, StringComparer.Ordinal).Select(o => new[] { o.RefNo, Txt.FmtDate(Calc.ClosedDate(o)), o.Device, Txt.Money(o.Price), Txt.Money(Calc.ProfitOf(o)), Txt.Money(Techs.Commission(o)) })) +
            $"<div class=\"row total\"><span>مجموع العمولة</span><span>{Txt.Esc(Txt.Money(t.Commission))}</span></div>";
        Printer.Doc(body, "عمولة " + t.Name, "760px");
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
                new("الفنيون", title, new[] { "الفني", "استلم", "سلّم", "الإيراد", "تكلفة القطع", "الربح", "العمولة", "رجع بالضمان", "متوسط مدة الإصلاح" },
                    Techs.Report(a, b).Select(t => new object[] { t.Name, t.Received, t.Delivered, t.Revenue, t.Parts, t.Profit, t.Commission, t.Returns, Calc.FmtDuration(t.AvgTime) }).ToList(),
                    new[] { 20, 10, 10, 15, 15, 15, 15, 14, 18 }),
                new("جودة الموردين", "جودة الموردين — كل الفترة", new[] { "المورد", "قطع رُكّبت", "تعطلت", "نسبة العطل %", "أجهزة رجعت بالضمان", "قيمة المعيب", "المسترد", "الخسارة" },
                    Defects.SupplierQuality().Select(x => new object[] { x.Name, x.Parts, x.DefectCount, Math.Round(x.DefectRate, 1), x.WarrantyReturns, x.DefectCost, x.Recovered, x.Lost }).ToList(),
                    new[] { 22, 12, 10, 12, 18, 15, 15, 15 }),
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
            (Techs.Report(a, b) is var tr && tr.Count > 0 ? "<h3 style=\"font-size:14px;margin-top:18px\">الفنيون</h3>" +
                Printer.Table(new[] { "الفني", "سلّم", "الإيراد", "الربح", "العمولة", "رجع بالضمان" },
                    tr.Select(t => new[] { t.Name, t.Delivered.ToString(), Txt.Money(t.Revenue), Txt.Money(t.Profit), Txt.Money(t.Commission), t.Returns.ToString() })) : "") +
            (S1.Exps.Count > 0 ? "<h3 style=\"font-size:14px;margin-top:18px\">المصاريف</h3>" + string.Concat(S1.Exps.Select(e => Printer.Row(Txt.Esc(e.Description) + " <span class=\"k\">" + Txt.FmtDate(e.Date) + "</span>", Txt.Money(e.Amount)))) : "");
        Printer.Doc(body, "تقرير الورشة", "900px");
    }
}
