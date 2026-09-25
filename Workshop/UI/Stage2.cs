using Raseed;
using static Raseed.Dpi;

namespace Workshop;

// ============================== شاشة الفني ==============================
/// <summary>كل فني يرى أجهزته فقط، المتأخر والأقرب موعداً أولاً، مع تغيير الحالة والتوقيت بضغطة</summary>
public class TechPage : Page
{
    public override string Title => "شاشة الفني";
    public override string Desc => sub;
    public override string PageIcon => "wrench";
    string sub = "أجهزتك المفتوحة مرتبة حسب الأولوية";

    readonly ComboBox cbTech = W.Combo(220, Array.Empty<string>());
    readonly DataGridView grid = W.Grid();
    readonly Label summary = new() { Dock = DockStyle.Top, Height = 36, Font = Theme.F(9.5f), ForeColor = Theme.Text2, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0) };
    List<Order> rows = new();

    public TechPage()
    {
        var bar = Theme.Bar();
        bar.Controls.Add(W.Labeled("الفني", cbTech, "user"));
        ModernButton B(string t, string icon, BtnKind k, Action a) { var b = W.Btn(t, icon, k, 110); b.Margin = new Padding(4, 26, 4, 4); b.Click += (s, e) => a(); bar.Controls.Add(b); return b; }
        B("فتح", "clipboard-list", BtnKind.Secondary, () => { if (Sel is Order o) Acts.View(o); });
        B("بدء / إيقاف", "clock", BtnKind.Success, () => { if (Sel is Order o) { WorkTimer.Toggle(o, Tech ?? o.Technician); Store.NotifyChanged(); } });
        B("قيد الإصلاح", "wrench", BtnKind.Secondary, () => { if (Sel is Order o) Acts.SetStatus(o, K.Repair); });
        B("بانتظار قطعة", "package", BtnKind.Secondary, () => { if (Sel is Order o) Acts.SetStatus(o, K.Part); });
        B("جاهز", "check", BtnKind.Primary, () => { if (Sel is Order o) Acts.SetStatus(o, K.Ready); });
        B("بطاقة عمل", "printer", BtnKind.Ghost, () => { if (Sel is Order o) Printer.JobCard(o); });
        grid.Columns.Add("pri", "الأولوية");
        grid.Columns.Add("ref", "المرجع");
        grid.Columns.Add("device", "الجهاز");
        grid.Columns.Add("issue", "العطل");
        grid.Columns.Add("status", "الحالة");
        grid.Columns.Add("due", "الموعد");
        grid.Columns.Add("time", "وقت العمل");
        grid.Columns["issue"].FillWeight = 200;
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            var o = rows[e.RowIndex];
            var c = grid.Columns[e.ColumnIndex].Name;
            if (c == "pri" || c == "due") e.CellStyle.ForeColor = Calc.IsLate(o) ? Pal.Bad : o.X.Service == "onsite" ? Pal.Primary : Theme.Ink;
            if (c == "time" && WorkTimer.IsRunning(o)) e.CellStyle.ForeColor = Pal.Good;
        };
        grid.CellDoubleClick += (s, e) => { if (Sel is Order o) Acts.View(o); };
        Controls.Add(grid);
        Controls.Add(summary);
        Controls.Add(bar);
        cbTech.SelectedIndexChanged += (s, e) => { Store.Set("tech_screen", cbTech.Text); Reload(); };
    }

    string Tech => cbTech.SelectedIndex <= 0 ? null : cbTech.Text;
    Order Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    public override void Reload()
    {
        if (cbTech.Items.Count == 0)
        {
            cbTech.Items.Add("كل الفنيين");
            cbTech.Items.AddRange(Techs.All.Select(t => (object)t.Name).ToArray());
            int i = cbTech.Items.IndexOf(Store.Get("tech_screen"));
            cbTech.SelectedIndex = Math.Max(0, i);
            return;   // SelectedIndexChanged يعيد التحميل
        }
        var t = Tech;
        rows = Store.Orders.Where(o => Calc.IsOpen(o) && o.Status != K.Ready && o.Status != K.Approval && (t == null || Txt.Fold(o.Technician) == Txt.Fold(t)))
            .OrderByDescending(Calc.IsLate).ThenByDescending(WorkTimer.IsRunning)
            .ThenBy(o => o.DateEstimated == "" ? "9999" : o.DateEstimated, StringComparer.Ordinal).ThenBy(o => o.DateReceived, StringComparer.Ordinal).ToList();
        grid.Rows.Clear();
        foreach (var o in rows)
            grid.Rows.Add(Calc.IsLate(o) ? $"متأخر {Calc.LateDays(o)} يوم" : o.X.Service == "onsite" ? "زيارة" : o.DateEstimated == Txt.Today ? "اليوم" : "—",
                o.RefNo, o.Device, (o.X.Items.Count > 0 ? string.Join("، ", o.X.Items.Where(i => i.Approved).Select(i => i.Desc)) : o.IssueType) + (o.Issue != "" ? " — " + o.Issue : ""),
                o.Status, Txt.FmtShortDate(o.DateEstimated), (WorkTimer.IsRunning(o) ? "⏱ " : "") + WorkTimer.Text(WorkTimer.Total(o)));
        var today = Store.Orders.SelectMany(o => o.X.Work.Where(w => (t == null || Txt.Fold(w.Tech) == Txt.Fold(t)) && Txt.Cut10(w.Start) == Txt.Today))
            .Sum(w => ((Txt.ParseTime(w.End) ?? DateTime.Now) - (Txt.ParseTime(w.Start) ?? DateTime.Now)).TotalMinutes);
        int ready = Store.Orders.Count(o => o.Status == K.Ready && (t == null || Txt.Fold(o.Technician) == Txt.Fold(t)));
        summary.Text = $"{rows.Count} جهاز على الطاولة     •     متأخر {rows.Count(Calc.IsLate)}     •     جاهز بانتظار الزبون {ready}     •     وقت العمل اليوم {WorkTimer.Text(TimeSpan.FromMinutes(today))}";
        sub = t == null ? "كل الأجهزة المفتوحة — اختر فنياً لعرض أجهزته فقط" : $"أجهزة {t} المفتوحة";
        MainForm.Instance?.UpdateTitle(this);
    }
}

// ============================== اقتراح كميات الشراء ==============================
public class ReorderDialog : DialogShell
{
    readonly DataGridView grid = W.Grid();
    List<Reorder.Suggestion> rows = new();

    ReorderDialog() : base("اقتراح كميات الشراء", 980, 640, "package", Pal.Primary)
    {
        grid.Columns.Add("sup", "المورد");
        grid.Columns.Add("item", "القطعة");
        grid.Columns.Add("qty", "الموجود");
        grid.Columns.Add("used", "استُعمل في 90 يوماً");
        grid.Columns.Add("month", "شهرياً");
        grid.Columns.Add("sug", "اقترح شراء");
        grid.Columns.Add("cost", "التكلفة التقريبية");
        grid.Columns["item"].FillWeight = 220;
        Body.Controls.Add(grid);
        Body.Controls.Add(W.Note("من استهلاكك في آخر 90 يوماً: ما يكفي شهراً + حد التنبيه، ناقص الموجود. للقطع التي تتابع كميتها فقط.", 900, 30));
        AddButton("طباعة", DialogResult.None, BtnKind.Primary, "printer").Click += (s, e) => Print();
        AddButton("واتساب للمورد المحدد", DialogResult.None, BtnKind.Success, "message-circle").Click += (s, e) => SendSupplier();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        rows = Reorder.List();
        foreach (var r in rows)
            grid.Rows.Add(r.Item.Supplier == "" ? "—" : r.Item.Supplier, r.Item.Name + (r.Item.Compatible != "" ? $" ({r.Item.Compatible})" : ""), r.Item.Qty, r.Used90,
                Math.Round(r.PerMonth, 1), r.Suggest, Txt.Money(r.Suggest * r.Item.Cost));
        Text = rows.Count == 0 ? "اقتراح كميات الشراء — المخزون يكفي" : $"اقتراح كميات الشراء — {rows.Count} صنف بتكلفة {Txt.Money(rows.Sum(r => r.Suggest * r.Item.Cost))}";
    }

    public static void Open() { using var d = new ReorderDialog(); d.ShowModal(); }

    void Print()
    {
        if (rows.Count == 0) return;
        var body = Printer.Header("قائمة شراء مقترحة") + string.Concat(rows.GroupBy(r => r.Item.Supplier == "" ? "بدون مورد" : r.Item.Supplier).Select(g =>
            $"<h3 style=\"font-size:14px;margin-top:14px\">{Txt.Esc(g.Key)}</h3>" +
            Printer.Table(new[] { "القطعة", "الموجود", "المقترح", "التكلفة" }, g.Select(r => new[] { r.Item.Name + (r.Item.Compatible != "" ? $" ({r.Item.Compatible})" : ""), r.Item.Qty?.ToString() ?? "", r.Suggest.ToString(), Txt.Money(r.Suggest * r.Item.Cost) }))));
        Printer.Doc(body, "قائمة شراء", "760px");
    }

    void SendSupplier()
    {
        var sup = grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index].Item.Supplier : "";
        var list = rows.Where(r => r.Item.Supplier == sup).ToList();
        if (list.Count == 0) return;
        var text = $"مرحباً،\nأحتاج القطع التالية:\n{string.Join("\n", list.Select(r => $"- {r.Item.Name}{(r.Item.Compatible != "" ? " " + r.Item.Compatible : "")}: {r.Suggest}"))}\nشكراً.\n\n{Store.ShopName}";
        using var d = new WaDialog(sup, "", new() { new("order", "طلب قطع", text) }, 0);
        d.ShowModal();
    }
}

// ============================== الموظفون ==============================
public class StaffPage : Page
{
    public override string Title => "الموظفون";
    public override string Desc => "الحضور والسُّلف والرواتب — السلفة والراتب يُسجَّلان مصروفاً تلقائياً";
    public override string PageIcon => "users";

    readonly Ledger ledger = new() { Dock = DockStyle.Top };
    readonly DataGridView grid = W.Grid();
    readonly DateTimePicker dMonth = new() { Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "MM / yyyy", ShowUpDown = true };
    List<Employee> rows = new();

    string Month => dMonth.Value.ToString("yyyy-MM");

    public StaffPage()
    {
        var bar = Theme.Bar();
        bar.Controls.Add(W.Labeled("الشهر", dMonth, "calendar"));
        ModernButton B(string t, string icon, BtnKind k, Action a) { var b = W.Btn(t, icon, k, 110); b.Margin = new Padding(4, 26, 4, 4); b.Click += (s, e) => a(); bar.Controls.Add(b); return b; }
        B("موظف جديد", "plus", BtnKind.Primary, () => EmployeeDialog.Open(null));
        B("حضور اليوم", "check", BtnKind.Success, AttendanceDialog.Open);
        B("سلفة", "wallet", BtnKind.Secondary, () => { if (Sel is Employee e) AdvanceDialog.Open(e); });
        B("الراتب", "receipt", BtnKind.Secondary, () => { if (Sel is Employee e) SalaryDialog.Open(e, Month); });
        B("تعديل", "pencil", BtnKind.Ghost, () => { if (Sel is Employee e) EmployeeDialog.Open(e); });
        grid.Columns.Add("name", "الموظف");
        grid.Columns.Add("salary", "الراتب");
        grid.Columns.Add("present", "حضور");
        grid.Columns.Add("absent", "غياب");
        grid.Columns.Add("adv", "سُلف الشهر");
        grid.Columns.Add("net", "الصافي المتوقع");
        grid.Columns.Add("paid", "صُرف؟");
        grid.CellDoubleClick += (s, e) => { if (Sel is Employee x) SalaryDialog.Open(x, Month); };
        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        Controls.Add(ledger);
        dMonth.Value = DateTime.Today;
        dMonth.ValueChanged += (s, e) => Reload();
    }

    Employee Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    public override void Reload()
    {
        rows = Staff.Employees().Where(e => e.Active).ToList();
        var att = Staff.Attendance(Month);
        grid.Rows.Clear();
        double totalNet = 0, totalAdv = 0;
        foreach (var e in rows)
        {
            var slip = Staff.Slip(e, Month);
            var paid = Staff.PaidFor(e.Id, Month);
            totalNet += paid?.Net ?? slip.Net;
            totalAdv += slip.Advances;
            grid.Rows.Add(e.Name, Txt.Money(e.Salary), att.Count(a => a.EmpId == e.Id && a.Status is "present" or "late"), att.Count(a => a.EmpId == e.Id && a.Status == "absent"),
                Txt.Money(slip.Advances), Txt.Money(paid?.Net ?? slip.Net), paid != null ? "✓ " + Txt.FmtShortDate(paid.Date) : "—");
        }
        int markedToday = Staff.Attendance(Txt.Today[..7]).Count(a => a.Date == Txt.Today);
        ledger.Set(new[]
        {
            new Ledger.Cell("الموظفون", rows.Count.ToString(), markedToday > 0 ? $"سُجّل حضور {markedToday} اليوم" : "لم يُسجَّل حضور اليوم"),
            new Ledger.Cell("سُلف الشهر", Txt.Money(totalAdv), null, Pal.Amber),
            new Ledger.Cell("رواتب الشهر (الصافي)", Txt.Money(totalNet), null, Pal.Primary),
        });
        ledger.Height = ledger.HeightFor(Math.Max(S(400), ledger.Width));
    }
}

public class EmployeeDialog : DialogShell
{
    EmployeeDialog(Employee e) : base(e == null ? "موظف جديد" : "تعديل الموظف", 600, 470, "user")
    {
        var tName = new TextBox { Width = 320, Text = e?.Name ?? "" };
        var tPhone = new TextBox { Width = 200, Text = e?.Phone ?? "" };
        var nSalary = W.Money(200);
        var tNote = new TextBox { Width = 520, Text = e?.Note ?? "" };
        var tgActive = new Toggle { Text = "على رأس العمل", Width = 300, Checked = e?.Active ?? true };
        W.Set(nSalary, e?.Salary ?? 0);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        var r = W.Flow();
        r.Controls.Add(W.Labeled("الاسم *", tName, "user"));
        r.Controls.Add(W.Labeled("الهاتف", tPhone, "phone"));
        flow.Controls.Add(r);
        flow.Controls.Add(W.Labeled("الراتب الشهري", nSalary));
        flow.Controls.Add(W.Labeled("ملاحظة", tNote));
        flow.Controls.Add(tgActive);
        flow.Controls.Add(W.Note("إذا كان اسمه نفس اسم فني في الإعدادات تُضاف عمولته إلى راتبه تلقائياً.", 520));
        Body.Controls.Add(flow);
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, _) =>
        {
            if (tName.Text.Trim() == "") { tName.Focus(); return; }
            var x = e ?? new Employee { Id = Txt.Uid("emp"), StartDate = Txt.Today };
            x.Name = tName.Text.Trim(); x.Phone = Txt.LatinDigits(tPhone.Text.Trim()); x.Salary = (double)nSalary.Value; x.Note = tNote.Text.Trim(); x.Active = tgActive.Checked;
            Staff.Save(x);
            DialogResult = DialogResult.OK;
            Close();
            Store.NotifyChanged();
        };
    }

    public static void Open(Employee e) { using var d = new EmployeeDialog(e); d.ShowModal(); }
}

public class AttendanceDialog : DialogShell
{
    readonly DateTimePicker dDate = new() { Width = 200, Format = DateTimePickerFormat.Long };
    readonly FlowLayoutPanel list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
    readonly List<(Employee E, Seg S)> segs = new();

    AttendanceDialog() : base("تسجيل الحضور", 720, 600, "check", Pal.Good)
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Surface };
        top.Controls.Add(W.Labeled("اليوم", dDate, "calendar"));
        Body.Controls.Add(list);
        Body.Controls.Add(top);
        dDate.Value = DateTime.Today;
        dDate.ValueChanged += (s, e) => Render();
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var day = Txt.Iso(dDate.Value);
            foreach (var (emp, seg) in segs) Staff.Mark(emp.Id, day, seg.Value);
            Store.NotifyChanged();
            Toast.Show("سُجّل الحضور ليوم " + Txt.FmtDate(day));
            DialogResult = DialogResult.OK;
            Close();
        };
        Render();
    }

    public static void Open()
    {
        if (Staff.Employees().Count(e => e.Active) == 0) { Toast.Show("أضف الموظفين أولاً", Tone.Info); return; }
        using var d = new AttendanceDialog();
        d.ShowModal();
    }

    void Render()
    {
        list.SuspendLayout();
        foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
        segs.Clear();
        var day = Txt.Iso(dDate.Value);
        foreach (var e in Staff.Employees().Where(e => e.Active))
        {
            var row = W.Flow(false);
            row.Controls.Add(new Label { Text = e.Name, AutoSize = false, Width = 200, Height = 40, Font = Theme.FS(10), TextAlign = ContentAlignment.MiddleLeft });
            var seg = new Seg(Staff.States) { Value = Staff.StateOf(e.Id, day) ?? "present", Margin = new Padding(6, 4, 6, 4) };
            row.Controls.Add(seg);
            segs.Add((e, seg));
            list.Controls.Add(Dpi.Fit(list, row));
        }
        list.ResumeLayout();
    }
}

public class AdvanceDialog : DialogShell
{
    AdvanceDialog(Employee e) : base("سلفة — " + e.Name, 560, 440, "wallet", Pal.Amber)
    {
        var n = W.Money(200);
        var d = new DateTimePicker { Width = 180, Format = DateTimePickerFormat.Short };
        var note = new TextBox { Width = 480 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        var month = Txt.Today[..7];
        var prev = Staff.Advances(month).Where(a => a.EmpId == e.Id).ToList();
        flow.Controls.Add(W.Note(prev.Count > 0 ? $"سُلف هذا الشهر: {Txt.Money(prev.Sum(a => a.Amount))} ({prev.Count})" : "لا سُلف هذا الشهر", 480));
        var r = W.Flow();
        r.Controls.Add(W.Labeled("المبلغ *", n));
        r.Controls.Add(W.Labeled("التاريخ", d));
        flow.Controls.Add(r);
        flow.Controls.Add(W.Labeled("ملاحظة", note));
        flow.Controls.Add(W.Note("تُسجَّل مصروفاً اليوم (خرجت من الصندوق)، وتُخصم من راتب شهرها.", 480));
        Body.Controls.Add(flow);
        var ok = AddButton("تسجيل", DialogResult.None, BtnKind.Primary, "check");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, _) =>
        {
            if (n.Value <= 0) { n.Focus(); return; }
            Staff.AddAdvance(e, (double)n.Value, Txt.Iso(d.Value), note.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
            Store.NotifyChanged();
            Toast.Show($"سُجّلت سلفة {Txt.Money((double)n.Value)} — {e.Name}");
        };
    }

    public static void Open(Employee e) { using var d = new AdvanceDialog(e); d.ShowModal(); }
}

public class SalaryDialog : DialogShell
{
    SalaryDialog(Employee e, string month) : base($"راتب {month} — {e.Name}", 620, 560, "receipt", Pal.Primary)
    {
        var paid = Staff.PaidFor(e.Id, month);
        var s = paid ?? Staff.Slip(e, month);
        var bonus = W.Money(180);
        W.Set(bonus, s.Bonus);
        bonus.Enabled = paid == null;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        Label Line(string k, string v, Color? c = null) => new() { Text = $"{k}:   {v}", AutoSize = false, Width = 540, Height = 30, Font = Theme.F(10.5f), ForeColor = c ?? Theme.Ink, TextAlign = ContentAlignment.MiddleLeft };
        var net = Line("الصافي", Txt.Money(s.Net), Pal.Good);
        net.Font = Theme.FS(12);
        flow.Controls.Add(Line("الراتب الأساسي", Txt.Money(s.Base)));
        flow.Controls.Add(Line("خصم الغياب", "- " + Txt.Money(s.Deduction), s.Deduction > 0 ? Pal.Bad : null));
        flow.Controls.Add(Line("السُّلف", "- " + Txt.Money(s.Advances), s.Advances > 0 ? Pal.Bad : null));
        flow.Controls.Add(Line("عمولة الإصلاحات", "+ " + Txt.Money(s.Commission), s.Commission > 0 ? Pal.Good : null));
        flow.Controls.Add(W.Labeled("مكافأة", bonus));
        flow.Controls.Add(net);
        if (paid != null) flow.Controls.Add(W.Note($"صُرف في {Txt.FmtDate(paid.Date)}", 540));
        Body.Controls.Add(flow);
        bonus.ValueChanged += (_, _) => { s.Bonus = (double)bonus.Value; s.Net = Math.Max(0, s.Base - s.Deduction - s.Advances + s.Commission + s.Bonus); net.Text = "الصافي:   " + Txt.Money(s.Net); };
        if (paid == null)
            AddButton("صرف الراتب", DialogResult.None, BtnKind.Primary, "check").Click += (_, _) =>
            {
                if (!W.Confirm("صرف الراتب", $"{e.Name} — {month}\nالصافي {Txt.Money(s.Net)} يُسجَّل مصروفاً اليوم.", "صرف")) return;
                Staff.Pay(e, s, Txt.Today);
                Store.NotifyChanged();
                Toast.Show("صُرف الراتب");
                Print(e, Staff.PaidFor(e.Id, month));
                DialogResult = DialogResult.OK;
                Close();
            };
        else
            AddButton("إلغاء الصرف", DialogResult.None, BtnKind.Danger, "rotate-ccw").Click += (_, _) =>
            {
                if (!W.Confirm("إلغاء صرف الراتب؟", "يُحذف المصروف المسجّل له.", "إلغاء الصرف", true)) return;
                Staff.Unpay(paid);
                Store.NotifyChanged();
                Close();
            };
        AddButton("طباعة الكشف", DialogResult.None, BtnKind.Secondary, "printer").Click += (_, _) => Print(e, s);
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
    }

    static void Print(Employee e, SalaryPay s)
    {
        if (s == null) return;
        var body = Printer.Header("كشف راتب") +
            $"<div class=\"grid\"><div><span class=\"k\">الموظف: </span><b>{Txt.Esc(e.Name)}</b></div><div><span class=\"k\">الشهر: </span><b>{s.Month}</b></div></div>" +
            Printer.Row("الراتب الأساسي", Txt.Money(s.Base)) + Printer.Row("خصم الغياب", Txt.Money(s.Deduction), "bad") + Printer.Row("السُّلف", Txt.Money(s.Advances), "bad") +
            Printer.Row("عمولة الإصلاحات", Txt.Money(s.Commission), "good") + Printer.Row("مكافأة", Txt.Money(s.Bonus), "good") +
            $"<div class=\"row total\"><span>الصافي</span><span class=\"good\">{Txt.Esc(Txt.Money(s.Net))}</span></div>" +
            "<div class=\"foot\">توقيع الموظف: ..............................</div>";
        Printer.Doc(body, "راتب " + e.Name, "520px");
    }

    public static void Open(Employee e, string month) { using var d = new SalaryDialog(e, month); d.ShowModal(); }
}
