using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>صفحة قابلة للتمرير تتكون من بطاقات فوق بعضها</summary>
public abstract class StackPage : Page
{
    protected readonly VStack Stack = new() { Dock = DockStyle.Top, Padding = new Padding(0, 0, 4, 16) };
    protected StackPage()
    {
        AutoScroll = true;
        Controls.Add(Stack);
    }
    // لا تقفز الصفحة عند تركيز جدول داخلها
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
    protected void Relayout() => Stack.Relayout();
}

// ============================== الرئيسية ==============================
public class DashboardPage : StackPage
{
    public override string Title => "الرئيسية";
    public override string Desc => "نظرة سريعة على الورشة اليوم";
    public override string PageIcon => "house";

    readonly Label greet = new() { AutoSize = false, Height = 64, Font = Theme.FS(18), ForeColor = Theme.Ink, TextAlign = ContentAlignment.BottomLeft, Dock = DockStyle.Fill };
    readonly Seg range = new(("today", "اليوم"), ("week", "الأسبوع"), ("month", "الشهر"), ("all", "الكل"));
    readonly Ledger ledger = new();
    readonly AlertsPanel alerts = new();
    readonly BenchStrip bench = new();
    readonly Box benchBox;
    readonly DualBarChart chart = new();
    readonly MeterList statuses = new();
    readonly Box statusBox;
    readonly OrdersGrid recent = new("received", "estimated", "issue", "duration") { Height = 330 };

    public DashboardPage()
    {
        var head = new Panel { Height = 70, BackColor = Theme.Bg };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 18, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        var bClose = W.Btn("تقفيل اليوم", "receipt", BtnKind.Secondary, 120);
        bClose.Click += (s, e) => CloseDayDialog.Open();
        tools.Controls.Add(bClose);
        tools.Controls.Add(range);
        head.Controls.Add(greet);
        head.Controls.Add(tools);
        range.Changed += _ => Reload();

        benchBox = new Box("على طاولة العمل الآن", bench, "wrench", "كل الأجهزة المفتوحة — المتأخر أولًا");
        bench.OpenOrder += Acts.View;
        var chartBox = new Box("الإيراد والربح", chart, "chart-column", "آخر 14 يوماً، حسب يوم التسليم");
        statusBox = new Box("توزيع الحالات", statuses, "chart-pie");
        var cols = new Cols { MinCol = 360 };
        cols.Controls.Add(chartBox);
        cols.Controls.Add(statusBox);
        var recentBox = new Box("أحدث الطلبات", recent, "clipboard-list");
        var all = W.Btn("عرض كل الطلبات", "list", BtnKind.Ghost, 120);
        all.Height = 34;
        all.Click += (s, e) => MainForm.Instance?.Go("orders");
        recentBox.Tools.Controls.Add(all);
        recent.OpenOrder += Acts.View;

        Stack.Controls.Add(head);
        Stack.Controls.Add(ledger);
        Stack.Controls.Add(alerts);
        Stack.Controls.Add(benchBox);
        Stack.Controls.Add(cols);
        Stack.Controls.Add(recentBox);
        range.Value = "today";
    }

    bool InRange(string d)
    {
        if (range.Value == "all") return true;
        if (string.IsNullOrEmpty(d)) return false;
        var t = Txt.Today;
        return range.Value switch
        {
            "today" => d == t,
            "week" => string.CompareOrdinal(d, Txt.Iso(DateTime.Today.AddDays(-6))) >= 0 && string.CompareOrdinal(d, t) <= 0,
            "month" => d.StartsWith(t[..7]) && string.CompareOrdinal(d, t) <= 0,
            _ => true
        };
    }

    public override void Reload()
    {
        greet.Text = $"{(DateTime.Now.Hour < 12 ? "صباح الخير" : "مساء الخير")}   ·   {Txt.FmtLong(DateTime.Today)}";
        // المال يُحسب بيوم إغلاق الطلب (تسليم أو إلغاء)، لا بيوم دخول الجهاز
        var closed = Store.Orders.Where(o => (o.Status == K.Done || o.Status == K.Cancelled) && InRange(Calc.ClosedDate(o))).ToList();
        var active = closed.Where(o => o.Status == K.Done).ToList();
        var scope = Store.Orders.Where(o => InRange(o.DateReceived)).ToList();
        double revenue = closed.Sum(Calc.RevenueOf);
        double fees = closed.Where(o => o.Status == K.Cancelled).Sum(Calc.RevenueOf);
        double cost = active.Sum(Calc.PartsCost);
        double expenses = Store.Expenses.Where(e => InRange(e.Date)).Sum(e => e.Amount);
        double loss = closed.Where(o => o.Status == K.Cancelled).Sum(Calc.PartsCost);
        double profit = revenue - cost - expenses - loss;
        double cash = Store.Orders.Sum(o => Calc.PaymentsInRange(o, InRange));
        string label = range.Value switch { "today" => "اليوم", "week" => "آخر 7 أيام", "month" => "هذا الشهر", _ => "كل الفترة" };
        var cells = new Dictionary<string, Ledger.Cell>
        {
            ["revenue"] = new("الإيراد", Txt.Money(revenue), $"{active.Count} جهاز مُسلّم{(fees > 0 ? " + أجور فحص " + Txt.Money(fees) : "")} — {label}", Pal.Primary),
            ["cash"] = new("المقبوض فعلياً", Txt.Money(cash), "دفعات مستلمة في الفترة", Pal.Good),
            ["cost"] = new("تكلفة القطع", Txt.Money(cost), expenses > 0 ? "+ مصاريف " + Txt.Money(expenses) : "بدون مصاريف عامة", Pal.Slate),
            ["profit"] = new("صافي الربح", Txt.Money(profit), loss > 0 ? "بعد القطع والمصاريف وخسائر الملغاة" : "بعد القطع والمصاريف", Pal.Amber, profit >= 0 ? 1 : -1),
            ["loss"] = new("خسائر الملغاة", Txt.Money(loss), "قطع طلبات ألغيت", Pal.Bad, loss > 0 ? -1 : 0),
        };
        var visible = K.DashCells.Where(c => Store.DashCell(c.Key)).Select(c => cells[c.Key]).ToList();
        ledger.Visible = visible.Count > 0;
        ledger.Set(visible);

        alerts.Set(BuildAlerts());

        var open = Store.Orders.Where(Calc.IsOpen)
            .OrderByDescending(Calc.IsLate).ThenBy(o => o.DateEstimated == "" ? "9999" : o.DateEstimated, StringComparer.Ordinal).ThenBy(o => o.CreatedAt, StringComparer.Ordinal).ToList();
        bench.Set(open);
        benchBox.Subtitle = open.Count > 0 ? $"{open.Count} جهاز مفتوح — المتأخر أولًا" : "لا توجد أجهزة مفتوحة";

        // مخطط آخر 14 يومًا
        chart.Labels.Clear(); chart.Series.Clear();
        var rev = new double[14]; var prof = new double[14];
        for (int i = 13, k = 0; i >= 0; i--, k++)
        {
            var d = DateTime.Today.AddDays(-i); var key = Txt.Iso(d);
            var list = Store.Orders.Where(o => (o.Status == K.Done || o.Status == K.Cancelled) && Calc.ClosedDate(o) == key).ToList();
            chart.Labels.Add(i == 0 ? "اليوم" : Txt.FmtDayMonth(d));
            rev[k] = list.Sum(Calc.RevenueOf);
            prof[k] = list.Sum(o => Calc.RevenueOf(o) - Calc.PartsCost(o));
        }
        chart.Series.Add(("الإيراد", rev, Pal.Primary));
        chart.Series.Add(("ربح القطع", prof, Pal.Amber));
        chart.EmptyText = "لا توجد أجهزة مُسلّمة في آخر 14 يوماً";
        chart.Invalidate();

        statusBox.Subtitle = "المستلمة — " + label;
        int total = scope.Count;
        statuses.EmptyText = "لا توجد طلبات في هذه الفترة";
        statuses.Set(K.Statuses.Select(s => (s, n: scope.Count(o => o.Status == s))).Where(x => x.n > 0)
            .Select(x => new MeterList.Row(x.s, x.n, $"{x.n}  ({Math.Round(x.n * 100.0 / Math.Max(1, total))}%)", true)));

        recent.Fill(Store.Orders.OrderByDescending(o => o.CreatedAt, StringComparer.Ordinal).Take(6));
        Relayout();
    }

    List<AlertsPanel.Alert> BuildAlerts()
    {
        var list = new List<AlertsPanel.Alert>();
        AlertsPanel.Item Item(Order o, string extra = "") => new($"{o.CustomerName} — {o.Device}{extra}", () => Acts.View(o));
        void More(List<AlertsPanel.Item> items, int n, string quick) { if (n > 0) items.Add(new($"+{n} أخرى", () => MainForm.Instance?.Go("orders", p => ((OrdersPage)p).Quick(quick)))); }

        var late = Store.Orders.Where(Calc.IsLate).ToList();
        if (late.Count > 0)
        {
            var items = late.Take(6).Select(o => Item(o)).ToList();
            More(items, late.Count - 6, "late");
            list.Add(new(2, "triangle-alert", $"{late.Count} طلب تجاوز موعد التسليم المتوقع", items));
        }
        var waiting = Store.Orders.Where(o => Calc.ReadyDays(o) >= 3).OrderByDescending(Calc.ReadyDays).ToList();
        if (waiting.Count > 0)
        {
            var items = waiting.Take(6).Select(o => Item(o)).ToList();
            More(items, waiting.Count - 6, "ready");
            list.Add(new(0, "clock", $"{waiting.Count} جهاز جاهز لم يُستلم منذ 3 أيام أو أكثر", items));
        }
        var approval = Store.Orders.Where(o => o.Status == K.Approval && Calc.StatusDays(o) >= 2).OrderByDescending(Calc.StatusDays).ToList();
        if (approval.Count > 0)
            list.Add(new(0, "message-circle", $"{approval.Count} جهاز بانتظار موافقة الزبون منذ يومين أو أكثر", approval.Take(6).Select(o => Item(o, $"  ({Calc.StatusDays(o)} يوم)")).ToList()));
        var outS = Store.Inventory.Where(i => Calc.StockState(i) == "out").ToList();
        var lowS = Store.Inventory.Where(i => Calc.StockState(i) == "low").ToList();
        if (outS.Count + lowS.Count > 0)
        {
            var title = (outS.Count > 0 ? $"{outS.Count} قطعة نفدت" : "") + (outS.Count > 0 && lowS.Count > 0 ? " و" : "") + (lowS.Count > 0 ? $"{lowS.Count} قطعة كميتها منخفضة" : "");
            void GoLow() => MainForm.Instance?.Go("inventory", p => ((InventoryPage)p).LowOnly());
            var items = outS.Concat(lowS).Take(6).Select(i => new AlertsPanel.Item($"{i.Name} {i.Compatible} ({i.Qty})", GoLow)).ToList();
            if (outS.Count + lowS.Count > 6) items.Add(new($"+{outS.Count + lowS.Count - 6} أخرى", GoLow));
            list.Add(new(0, "package", title, items));
        }
        var debts = Calc.GetDebts();
        if (debts.Count > 0)
            list.Add(new(1, "wallet", $"ديون مستحقة على {debts.Count} زبون بمجموع {Txt.Money(debts.Sum(c => c.Debt))}", new(), null, "متابعة الديون", () => MainForm.Instance?.Go("debts")));
        if (AutoBackup.Dir != "" && AutoBackup.Error != "")
            list.Add(new(2, "database", "تعذّر النسخ التلقائي إلى المجلد", new(), AutoBackup.Dir + " — " + AutoBackup.Error, "حفظ الآن", () => { if (AutoBackup.Run()) Toast.Show("حُفظت نسخة في المجلد"); Reload(); }));
        int days = Backup.DaysSinceLast();
        if (Store.Orders.Count > 0 && (days < 0 || days >= 7))
            list.Add(new(2, "database", days < 0 ? "لم تُحفظ أي نسخة احتياطية بعد" : $"آخر نسخة احتياطية قبل {days} يوم", new(),
                "احتفظ بنسخة على فلاشة أو في Google Drive. يمكنك تفعيل النسخ التلقائي من الإعدادات.", "نسخة الآن", () => MainForm.Instance?.BackupNow()));
        return list;
    }
}

// ============================== الطلبات ==============================
public class OrdersPage : Page
{
    public override string Title => "طلبات الصيانة";
    public override string Desc => sub;
    public override string PageIcon => "clipboard-list";
    string sub = "";

    readonly TextBox search = new() { Width = 300, PlaceholderText = "ابحث باسم الزبون أو الهاتف أو الجهاز أو المرجع أو IMEI" };
    readonly Seg quick = new(("all", "الكل"), ("open", "مفتوحة"), ("today", "اليوم"), ("ready", "جاهزة"), ("late", "متأخرة"), ("unpaid", "غير مسددة"));
    readonly Seg mode = new(("table", "جدول"), ("kanban", "لوحة"));
    readonly ComboBox cbStatus = W.Combo(170, new[] { "كل الحالات" }.Concat(K.Statuses)), cbPay = W.Combo(170, new[] { "كل حالات الدفع" }.Concat(K.PayList)),
                      cbType = W.Combo(170, new[] { "كل أنواع الأعطال" }.Concat(K.IssueTypes)),
                      cbSort = W.Combo(190, new[] { "الأحدث أولاً", "الأقدم أولاً", "موعد التسليم الأقرب", "السعر الأعلى", "السعر الأقل", "الربح الأعلى", "المتبقي الأكبر" });
    readonly DateTimePicker dFrom = new() { Width = 150, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false },
                            dTo = new() { Width = 150, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    readonly FlowLayoutPanel filters;
    readonly ModernButton bFilters;
    readonly Label summary = new() { Dock = DockStyle.Top, Height = 36, Font = Theme.F(9.5f), ForeColor = Theme.Text2, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0) };
    readonly OrdersGrid grid = new();
    readonly Panel boardHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Visible = false };
    readonly KanbanBoard board = new();

    public OrdersPage()
    {
        var bar = Theme.Bar();
        bar.Controls.Add(W.Labeled("بحث", search, "search"));
        var q = new Panel { Width = quick.Width + 8, Height = 64, BackColor = Theme.Surface };
        quick.Location = new Point(0, 24);
        q.Controls.Add(quick);
        bar.Controls.Add(q);
        var m = new Panel { Width = mode.Width + 8, Height = 64, BackColor = Theme.Surface };
        mode.Location = new Point(0, 24);
        m.Controls.Add(mode);
        bar.Controls.Add(m);
        bFilters = W.Btn("فلاتر", "filter", BtnKind.Secondary, 90); bFilters.Margin = new Padding(4, 26, 4, 4);
        var bCols = W.Btn("الأعمدة", "layout-list", BtnKind.Secondary, 90); bCols.Margin = new Padding(4, 26, 4, 4);
        var bCsv = W.Btn("CSV", "file-spreadsheet", BtnKind.Secondary, 70); bCsv.Margin = new Padding(4, 26, 4, 4);
        var bNew = W.Btn("طلب جديد", "plus", BtnKind.Primary, 120); bNew.Margin = new Padding(4, 26, 4, 4);
        bar.Controls.AddRange(new Control[] { bFilters, bCols, bCsv, bNew });

        filters = Theme.Bar();
        filters.Visible = false;
        filters.Controls.Add(W.Labeled("الحالة", cbStatus));
        filters.Controls.Add(W.Labeled("الدفع", cbPay));
        filters.Controls.Add(W.Labeled("نوع العطل", cbType));
        filters.Controls.Add(W.Labeled("من (الاستلام)", dFrom));
        filters.Controls.Add(W.Labeled("إلى", dTo));
        filters.Controls.Add(W.Labeled("الترتيب", cbSort));
        var bClear = W.Btn("مسح الفلاتر", "x", BtnKind.Ghost, 110); bClear.Margin = new Padding(4, 26, 4, 4);
        filters.Controls.Add(bClear);

        boardHost.Controls.Add(board);
        Controls.Add(grid);
        Controls.Add(boardHost);
        Controls.Add(summary);
        Controls.Add(filters);
        Controls.Add(bar);

        Ui2.OnIdle(search, Reload, 150);
        quick.Changed += _ => Reload();
        mode.Changed += v => { grid.Visible = v == "table"; boardHost.Visible = v == "kanban"; cbStatus.Enabled = v == "table"; Reload(); };
        foreach (var c in new[] { cbStatus, cbPay, cbType, cbSort }) c.SelectedIndexChanged += (s, e) => Reload();
        dFrom.ValueChanged += (s, e) => Reload();
        dTo.ValueChanged += (s, e) => Reload();
        bFilters.Click += (s, e) => filters.Visible = !filters.Visible;
        bClear.Click += (s, e) => ClearFilters();
        bCols.Click += (s, e) => { ColumnsDialog.Open(); grid.ApplyColumns(); };
        bCsv.Click += (s, e) =>
        {
            if (Store.Orders.Count == 0) { Toast.Show("لا توجد طلبات لتصديرها.", Tone.Info); return; }
            var f = W.SaveFile("CSV|*.csv", $"طلبات_الورشة_{Txt.Today}.csv");
            if (f == null) return;
            try { Csv.ExportOrders(f); Toast.Show("تم تصدير الطلبات"); } catch (Exception ex) { Dialogs.Warn("تعذّر التصدير: " + ex.Message); }
        };
        bNew.Click += (s, e) => Acts.New();
        grid.OpenOrder += Acts.View;
        board.OpenOrder += Acts.View;
        board.Drop += (o, st) => Acts.SetStatus(o, st);
        boardHost.MouseWheel += (s, e) => { if ((ModifierKeys & Keys.Shift) != 0) boardHost.AutoScrollPosition = new Point(-boardHost.AutoScrollPosition.X - e.Delta, -boardHost.AutoScrollPosition.Y); };
    }

    public void Quick(string v) { ClearFilters(false); quick.Value = v; Reload(); }

    void ClearFilters(bool reload = true)
    {
        search.Text = "";
        cbStatus.SelectedIndex = cbPay.SelectedIndex = cbType.SelectedIndex = cbSort.SelectedIndex = 0;
        dFrom.Checked = dTo.Checked = false;
        quick.Value = "all";
        if (reload) Reload();
    }

    /// <summary>البحث من خارج الصفحة (مثل البحث السريع)</summary>
    public void Search(string text) { ClearFilters(false); search.Text = text; Reload(); }

    List<Order> Filtered(bool ignoreStatus)
    {
        var q = Txt.Fold(search.Text);
        string st = ignoreStatus || cbStatus.SelectedIndex <= 0 ? null : cbStatus.Text;
        string pay = cbPay.SelectedIndex <= 0 ? null : cbPay.Text, type = cbType.SelectedIndex <= 0 ? null : cbType.Text;
        string from = dFrom.Checked ? Txt.Iso(dFrom.Value) : null, to = dTo.Checked ? Txt.Iso(dTo.Value) : null, t = Txt.Today;
        IEnumerable<Order> list = Store.Orders.Where(o => (q == "" || Txt.Matches(Calc.Haystack(o), q)) && (st == null || o.Status == st) &&
            (pay == null || o.PaymentStatus == pay) && (type == null || o.IssueType == type) &&
            (from == null || string.CompareOrdinal(o.DateReceived, from) >= 0) && (to == null || string.CompareOrdinal(o.DateReceived, to) <= 0));
        list = quick.Value switch
        {
            "open" => list.Where(Calc.IsOpen),
            "today" => list.Where(o => o.DateReceived == t),
            "ready" => list.Where(o => o.Status == K.Ready),
            "late" => list.Where(Calc.IsLate),
            "unpaid" => list.Where(o => Calc.RemainingOf(o) > 0),
            _ => list
        };
        list = cbSort.SelectedIndex switch
        {
            1 => list.OrderBy(o => o.CreatedAt, StringComparer.Ordinal),
            2 => list.OrderBy(o => o.DateEstimated == "" ? "9999" : o.DateEstimated, StringComparer.Ordinal),
            3 => list.OrderByDescending(o => o.Price),
            4 => list.OrderBy(o => o.Price),
            5 => list.OrderByDescending(Calc.ProfitOf),
            6 => list.OrderByDescending(Calc.RemainingOf),
            _ => list.OrderByDescending(o => o.CreatedAt, StringComparer.Ordinal),
        };
        return list.ToList();
    }

    public override void Reload()
    {
        int all = Store.Orders.Count, open = Store.Orders.Count(Calc.IsOpen);
        sub = all > 0 ? $"{all} طلب مسجّل، منها {open} قيد العمل" : "لا توجد طلبات بعد — كل جهاز يدخل الورشة يبدأ من «طلب جديد»";
        MainForm.Instance?.UpdateTitle(this);
        bool filtered = cbStatus.SelectedIndex > 0 || cbPay.SelectedIndex > 0 || cbType.SelectedIndex > 0 || dFrom.Checked || dTo.Checked || cbSort.SelectedIndex > 0;
        bFilters.Kind = filtered ? BtnKind.Soft : BtnKind.Secondary;
        if (mode.Value == "kanban")
        {
            board.Set(Filtered(true));
            summary.Text = "اسحب البطاقة إلى عمود آخر لتغيير حالتها، وانقرها لفتح الطلب. Shift + عجلة الفأرة للتمرير أفقيًا.";
            return;
        }
        var list = Filtered(false);
        var active = list.Where(o => o.Status != K.Cancelled).ToList();
        double prof = active.Sum(Calc.ProfitOf);
        summary.Text = $"المعروض {list.Count} من {all}     •     مجموع الأسعار {Txt.Money(active.Sum(o => o.Price))}     •     الربح {Txt.Money(prof)}     •     المتبقي على الزبائن {Txt.Money(active.Sum(Calc.RemainingOf))}";
        grid.Fill(list);
    }
}

// ============================== الديون ==============================
public class DebtsPage : StackPage
{
    public override string Title => "الديون المستحقة";
    public override string Desc => "أجهزة سُلّمت لأصحابها ولم يُدفع ثمنها كاملاً، الأقدم أولاً";
    public override string PageIcon => "wallet";

    readonly Ledger ledger = new();
    readonly DataGridView debts = W.Grid();
    readonly Panel debtsHost = new() { Height = 300, BackColor = Theme.Surface };
    readonly Box debtsBox, expectedBox;
    readonly OrdersGrid expected = new("issue", "profit", "received", "estimated", "duration");
    readonly Panel expectedHost = new() { Height = 300, BackColor = Theme.Surface };
    List<Calc.Customer> rows = new();

    public DebtsPage()
    {
        debts.Columns.Add("name", "الزبون");
        debts.Columns.Add("phone", "الهاتف");
        debts.Columns.Add("orders", "طلبات غير مسددة");
        debts.Columns.Add("debt", "المستحق");
        debts.Columns.Add("days", "منذ التسليم");
        debts.Columns["orders"].FillWeight = 160;
        debtsHost.Controls.Add(debts);
        debtsBox = new Box("الديون", debtsHost, "wallet");
        var bPay = W.Btn("دفعة", "wallet", BtnKind.Success, 80); bPay.Height = 34;
        var bRemind = W.Btn("تذكير", "message-circle", BtnKind.Secondary, 80); bRemind.Height = 34;
        debtsBox.Tools.Controls.Add(bPay);
        debtsBox.Tools.Controls.Add(bRemind);
        bPay.Click += (s, e) => { if (Sel is Calc.Customer c) QuickPayDialog.ForCustomer(c.Key); };
        bRemind.Click += (s, e) => { if (Sel is Calc.Customer c) Acts.DebtReminder(c); };
        debts.CellDoubleClick += (s, e) => { if (Sel is Calc.Customer c) CustomerDialog.Open(c.Key); };
        debts.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (debts.Columns[e.ColumnIndex].Name == "debt") e.CellStyle.ForeColor = Pal.Bad;
            if (debts.Columns[e.ColumnIndex].Name == "days" && e.RowIndex < rows.Count && Txt.DaysBetween(rows[e.RowIndex].Oldest, Txt.Today) > 30) e.CellStyle.ForeColor = Pal.Bad;
        };
        expectedHost.Controls.Add(expected);
        expectedBox = new Box("مبالغ متوقعة عند التسليم", expectedHost, "clock", "أجهزة ما زالت في الورشة وعليها مبلغ. ليست ديناً بعد، تُقبض عند الاستلام.");
        expected.OpenOrder += Acts.View;
        Stack.Controls.Add(ledger);
        Stack.Controls.Add(debtsBox);
        Stack.Controls.Add(expectedBox);
    }

    Calc.Customer Sel => debts.CurrentRow != null && debts.CurrentRow.Index < rows.Count ? rows[debts.CurrentRow.Index] : null;

    public override void Reload()
    {
        rows = Calc.GetDebts();
        double total = rows.Sum(c => c.Debt);
        int count = rows.Sum(c => c.Unpaid.Count);
        int oldest = rows.Count > 0 ? Txt.DaysBetween(rows[0].Oldest, Txt.Today) : 0;
        var pending = Store.Orders.Where(o => Calc.IsOpen(o) && Calc.RemainingOf(o) > 0)
            .OrderByDescending(o => o.Status == K.Ready).ThenBy(o => o.DateReceived, StringComparer.Ordinal).ToList();
        double exp = pending.Sum(Calc.RemainingOf);
        ledger.Set(new[]
        {
            new Ledger.Cell("إجمالي الديون", Txt.Money(total), "على أجهزة مُسلّمة", Pal.Bad, total > 0 ? -1 : 1),
            new Ledger.Cell("عدد الزبائن", rows.Count.ToString(), $"{count} طلب غير مسدد"),
            new Ledger.Cell("أقدم دين", rows.Count > 0 ? $"{oldest} يوم" : "—", rows.Count > 0 ? rows[0].Name : ""),
            new Ledger.Cell("متوقع عند التسليم", Txt.Money(exp), $"{pending.Count} جهاز في الورشة", Pal.Wait),
        });
        debts.Rows.Clear();
        foreach (var c in rows)
            debts.Rows.Add(c.Name, c.Phone == "" ? "—" : c.Phone, $"{c.Unpaid.Count}   {string.Join("، ", c.Unpaid.Select(o => o.Device).Take(2))}", Txt.Money(c.Debt), $"{Txt.DaysBetween(c.Oldest, Txt.Today)} يوم");
        debtsBox.Subtitle = rows.Count == 0 ? "لا توجد ديون مستحقة — كل الأجهزة المسلّمة مدفوعة بالكامل" : "انقر مرتين لفتح ملف الزبون";
        debtsBox.ContentHeight = S(44) + Math.Clamp(rows.Count, 2, 10) * debts.RowTemplate.Height + S(4);
        expected.Fill(pending);
        expectedBox.Subtitle = pending.Count > 0 ? $"{pending.Count} جهاز — الجاهزة أولاً" : "لا توجد مبالغ معلّقة على أجهزة في الورشة";
        expectedBox.ContentHeight = S(44) + Math.Clamp(pending.Count, 2, 10) * expected.RowTemplate.Height + S(4);
        Relayout();
    }
}

// ============================== الزبائن ==============================
public class CustomersPage : Page
{
    public override string Title => "الزبائن";
    public override string Desc => sub;
    public override string PageIcon => "users";
    string sub = "";

    readonly TextBox search = new() { Width = 300, PlaceholderText = "اسم الزبون أو الهاتف" };
    readonly ComboBox sort = W.Combo(170, new[] { "آخر زيارة", "الأكثر طلبات", "الأعلى إنفاقاً", "الاسم" });
    readonly DataGridView grid = W.Grid();
    List<Calc.Customer> rows = new();

    public CustomersPage()
    {
        var bar = Theme.Bar();
        bar.Controls.Add(W.Labeled("بحث", search, "search"));
        bar.Controls.Add(W.Labeled("الترتيب", sort));
        bar.Controls.Add(W.Note("يُضاف الزبون تلقائياً عند تسجيل أول طلب له. انقر مرتين لفتح ملفه.", 420, 60));
        grid.Columns.Add("name", "الزبون");
        grid.Columns.Add("phone", "الهاتف");
        grid.Columns.Add("orders", "الطلبات");
        grid.Columns.Add("spent", "مجموع التعامل");
        grid.Columns.Add("debt", "الدين");
        grid.Columns.Add("expected", "متوقع عند التسليم");
        grid.Columns.Add("last", "آخر زيارة");
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            if (grid.Columns[e.ColumnIndex].Name == "debt" && rows[e.RowIndex].Debt > 0) e.CellStyle.ForeColor = Pal.Bad;
        };
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.RowIndex < rows.Count) CustomerDialog.Open(rows[e.RowIndex].Key); };
        grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count) { e.Handled = true; CustomerDialog.Open(rows[grid.CurrentRow.Index].Key); } };
        Controls.Add(grid);
        Controls.Add(bar);
        Ui2.OnIdle(search, Reload, 150);
        sort.SelectedIndexChanged += (s, e) => Reload();
    }

    public override void Reload()
    {
        var all = Calc.GetCustomers();
        sub = $"{all.Count} زبون";
        MainForm.Instance?.UpdateTitle(this);
        var q = Txt.Fold(search.Text);
        IEnumerable<Calc.Customer> list = all.Where(c => q == "" || Txt.Fold(c.Name + " " + c.Phone).Contains(q));
        list = sort.SelectedIndex switch
        {
            1 => list.OrderByDescending(c => c.Orders.Count),
            2 => list.OrderByDescending(c => c.Spent),
            3 => list.OrderBy(c => c.Name, StringComparer.CurrentCulture),
            _ => list.OrderByDescending(c => c.Last, StringComparer.Ordinal),
        };
        rows = list.ToList();
        grid.Rows.Clear();
        foreach (var c in rows)
            grid.Rows.Add(c.Name, c.Phone == "" ? "—" : c.Phone, c.Orders.Count, Txt.Money(c.Spent), c.Debt > 0 ? Txt.Money(c.Debt) : "—",
                c.Expected > 0 ? Txt.Money(c.Expected) : "—", Txt.FmtDate(c.Last));
    }
}
