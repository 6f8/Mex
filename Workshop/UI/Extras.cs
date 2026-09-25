using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>سؤال بنص (السبب) مع اقتراحات جاهزة</summary>
public static class Ask
{
    public static string Reason(string title, string body, IEnumerable<string> suggestions = null, string ok = "تأكيد")
    {
        using var d = new DialogShell(title, 600, 380, "pencil", Pal.Amber);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note(body, 540, 60));
        var cb = W.Combo(520, suggestions ?? Array.Empty<string>(), true);
        flow.Controls.Add(W.Labeled("السبب *", cb));
        d.Body.Controls.Add(flow);
        var bOk = d.AddButton(ok, DialogResult.None, BtnKind.Primary, "check");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        string result = null;
        bOk.Click += (s, e) =>
        {
            var v = cb.Text.Trim();
            if (v == "") { cb.Focus(); Toast.Show("اكتب السبب", Tone.Warning); return; }
            result = v;
            d.DialogResult = DialogResult.OK;
            d.Close();
        };
        d.Shown += (s, e) => cb.Focus();
        return d.ShowModal() == DialogResult.OK ? result : null;
    }
}

// ============================== إرجاع مبلغ ==============================
public class RefundDialog : DialogShell
{
    readonly NumericUpDown nAmount = W.Money(200);
    readonly Seg method = Seg.Of(K.PayMethods);
    readonly DateTimePicker dDate = new() { Width = 200, Format = DateTimePickerFormat.Short };
    readonly ComboBox cbReason = W.Combo(520, Refunds.Reasons, true);
    readonly double max;
    public double Amount => (double)nAmount.Value;
    public string Method => method.Value;
    public string Date => Txt.Iso(dDate.Value);
    public string Reason => cbReason.Text.Trim();

    public RefundDialog(string title, double paid, double suggested) : base("إرجاع مبلغ للزبون", 640, 520, "rotate-ccw", Pal.Bad)
    {
        max = paid;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Head(title, 560));
        flow.Controls.Add(W.Note($"المدفوع حتى الآن: {Txt.Money(paid)} — يُخصم المبلغ المُرجَع من صندوق يومه ويبقى مسجلاً في الطلب بسببه.", 560, 44));
        var r = W.Flow();
        r.Controls.Add(W.Labeled("المبلغ المُرجَع *", nAmount));
        r.Controls.Add(W.Labeled("التاريخ", dDate));
        flow.Controls.Add(r);
        flow.Controls.Add(W.Note("أُرجع بطريقة", 560, 22));
        method.Margin = new Padding(6, 0, 6, 6);
        flow.Controls.Add(method);
        flow.Controls.Add(W.Labeled("السبب *", cbReason));
        Body.Controls.Add(flow);
        W.Set(nAmount, suggested > 0 ? suggested : paid);
        var ok = AddButton("تسجيل الإرجاع", DialogResult.None, BtnKind.Danger, "rotate-ccw");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            if (Amount <= 0) { nAmount.Focus(); Toast.Show("اكتب المبلغ", Tone.Warning); return; }
            if (Amount > max + 0.001) { Dialogs.Warn($"لا يمكن إرجاع أكثر من المدفوع ({Txt.Money(max)})."); return; }
            if (Reason == "") { cbReason.Focus(); Toast.Show("اكتب سبب الإرجاع", Tone.Warning); return; }
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    /// <summary>إرجاع مبلغ من طلب محفوظ</summary>
    public static void ForOrder(Order o)
    {
        o = Calc.Find(o?.Id);
        if (o == null) return;
        if (o.Paid <= 0) { Toast.Show("لا توجد دفعات على هذا الطلب", Tone.Info); return; }
        using var d = new RefundDialog($"{o.RefNo} — {o.CustomerName} — {o.Device}", o.Paid, Refunds.Overpaid(o));
        if (d.ShowModal() != DialogResult.OK) return;
        Refunds.Apply(o, d.Amount, d.Method, d.Date, d.Reason);
        Store.NotifyChanged();
        Toast.Show($"سُجّل إرجاع {Txt.Money(d.Amount)} للزبون");
    }
}

// ============================== التذكيرات ==============================
public class ReminderDialog : DialogShell
{
    readonly Reminder existing;
    readonly string orderId;
    readonly ComboBox cbText = W.Combo(560, Reminders.Suggestions, true);
    readonly DateTimePicker dDate = new() { Width = 200, Format = DateTimePickerFormat.Long };

    ReminderDialog(string orderId, Reminder r) : base(r == null ? "تذكير جديد" : "تعديل التذكير", 640, 400, "bell", Pal.Amber)
    {
        existing = r;
        this.orderId = r?.OrderId ?? orderId;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        if (Calc.Find(this.orderId) is Order o) flow.Controls.Add(W.Note($"الطلب {o.RefNo} — {o.CustomerName} — {o.Device}", 580));
        flow.Controls.Add(W.Labeled("ماذا تريد أن تتذكر؟ *", cbText, "bell"));
        var r1 = W.Flow(false);
        r1.Controls.Add(W.Labeled("اليوم", dDate, "calendar"));
        foreach (var (t, days) in new[] { ("غداً", 1), ("بعد 3 أيام", 3), ("بعد أسبوع", 7) })
        {
            var b = W.Btn(t, null, BtnKind.Soft, 80);
            b.Margin = new Padding(3, 27, 3, 3);
            b.Click += (s, e) => dDate.Value = DateTime.Today.AddDays(days);
            r1.Controls.Add(b);
        }
        flow.Controls.Add(r1);
        Body.Controls.Add(flow);
        cbText.Text = r?.Text ?? "";
        dDate.Value = Txt.ParseDate(r?.Date) ?? DateTime.Today.AddDays(1);
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var text = cbText.Text.Trim();
            if (text == "") { cbText.Focus(); Toast.Show("اكتب نص التذكير", Tone.Warning); return; }
            if (existing != null) { existing.Text = text; existing.Date = Txt.Iso(dDate.Value); Store.SaveReminder(existing); }
            else Reminders.Add(this.orderId, Txt.Iso(dDate.Value), text);
            DialogResult = DialogResult.OK;
            Close();
            Store.NotifyChanged();
            Toast.Show("سُجّل التذكير — " + Txt.FmtDate(Txt.Iso(dDate.Value)));
        };
        Shown += (s, e) => cbText.Focus();
    }

    public static void New(string orderId = null) { using var d = new ReminderDialog(orderId, null); d.ShowModal(); }
    public static void Edit(Reminder r) { using var d = new ReminderDialog(null, r); d.ShowModal(); }
}

public class RemindersDialog : DialogShell
{
    readonly Seg filter = new(("due", "المستحقة"), ("upcoming", "القادمة"), ("done", "المنجزة"));
    readonly DataGridView grid = W.Grid();
    List<Reminder> rows = new();

    RemindersDialog() : base("التذكيرات", 940, 640, "bell", Pal.Amber)
    {
        grid.Columns.Add("when", "الموعد");
        grid.Columns.Add("text", "التذكير");
        grid.Columns["text"].FillWeight = 320;
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || grid.Columns[e.ColumnIndex].Name != "when") return;
            var r = rows[e.RowIndex];
            if (!r.Done && string.CompareOrdinal(r.Date, Txt.Today) < 0) e.CellStyle.ForeColor = Pal.Bad;
        };
        grid.CellDoubleClick += (s, e) => OpenOrder();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Surface };
        filter.Margin = new Padding(4, 6, 4, 4);
        top.Controls.Add(filter);
        Body.Controls.Add(grid);
        Body.Controls.Add(top);
        AddButton("تم", DialogResult.None, BtnKind.Success, "check").Click += (s, e) => { if (Sel is Reminder r) { Reminders.SetDone(r, !r.Done); Store.NotifyChanged(); Render(); } };
        AddButton("فتح الطلب", DialogResult.None, BtnKind.Secondary, "clipboard-list").Click += (s, e) => OpenOrder();
        AddButton("تذكير جديد", DialogResult.None, BtnKind.Secondary, "plus").Click += (s, e) => { ReminderDialog.New(); Render(); };
        AddButton("تعديل", DialogResult.None, BtnKind.Secondary, "pencil").Click += (s, e) => { if (Sel is Reminder r) { ReminderDialog.Edit(r); Render(); } };
        AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) =>
        {
            if (Sel is not Reminder r || !W.Confirm("حذف التذكير؟", Reminders.Label(r), "حذف", true)) return;
            Store.DeleteReminder(r); Store.NotifyChanged(); Render();
        };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        filter.Changed += _ => Render();
        if (Reminders.DueCount == 0) filter.Value = "upcoming";
        Render();
    }

    public static void Open() { using var d = new RemindersDialog(); d.ShowModal(); }

    Reminder Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    void OpenOrder()
    {
        if (Sel?.OrderId is string id && Calc.Find(id) is Order o) Acts.View(o);
        else if (Sel != null) Toast.Show("هذا التذكير غير مرتبط بطلب", Tone.Info);
    }

    void Render()
    {
        var t = Txt.Today;
        rows = (filter.Value switch
        {
            "done" => Store.Reminders.Where(r => r.Done).OrderByDescending(r => r.DoneAt ?? "", StringComparer.Ordinal),
            "upcoming" => Store.Reminders.Where(r => !r.Done && string.CompareOrdinal(r.Date, t) > 0).OrderBy(r => r.Date, StringComparer.Ordinal),
            _ => Reminders.Due().AsEnumerable(),
        }).ToList();
        grid.Rows.Clear();
        foreach (var r in rows) grid.Rows.Add(r.Done ? "✓ " + Txt.FmtShortDate(r.Date) : Reminders.When(r), Reminders.Label(r));
    }
}

// ============================== حسابات التجار والشركات ==============================
public class AccountsPage : Page
{
    public override string Title => "التجار والشركات";
    public override string Desc => "أسعار خاصة وحساب مفتوح يُسدَّد دفعة واحدة، مع كشف حساب شهري";
    public override string PageIcon => "store";

    readonly Ledger ledger = new() { Dock = DockStyle.Top };
    readonly DataGridView grid = W.Grid();
    List<Account> rows = new();

    public AccountsPage()
    {
        var bar = Theme.Bar();
        var bNew = W.Btn("حساب جديد", "plus", BtnKind.Primary, 120);
        var bOrder = W.Btn("طلب جديد لهذا الحساب", "file-plus", BtnKind.Secondary, 170);
        var bPay = W.Btn("تسديد دفعة", "wallet", BtnKind.Success, 120);
        var bSt = W.Btn("كشف الحساب", "scroll-text", BtnKind.Secondary, 120);
        var bOrders = W.Btn("طلبات الحساب", "clipboard-list", BtnKind.Secondary, 120);
        var bEdit = W.Btn("تعديل", "pencil", BtnKind.Ghost, 90);
        bar.Controls.AddRange(new Control[] { bNew, bOrder, bPay, bSt, bOrders, bEdit });
        grid.Columns.Add("name", "الاسم");
        grid.Columns.Add("kind", "النوع");
        grid.Columns.Add("phone", "الهاتف");
        grid.Columns.Add("disc", "الخصم");
        grid.Columns.Add("open", "أجهزة في الورشة");
        grid.Columns.Add("due", "المستحق");
        grid.Columns.Add("exp", "متوقع");
        grid.Columns.Add("limit", "الحد");
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || grid.Columns[e.ColumnIndex].Name != "due") return;
            e.CellStyle.ForeColor = Accounts.Due(rows[e.RowIndex]) > 0 ? Pal.Bad : Pal.Good;
        };
        grid.CellDoubleClick += (s, e) => { if (Sel is Account a) AccountStatementDialog.Open(a); };
        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        Controls.Add(ledger);
        bNew.Click += (s, e) => AccountDialog.Open(null);
        bEdit.Click += (s, e) => { if (Sel is Account a) AccountDialog.Open(a); };
        bPay.Click += (s, e) => { if (Sel is Account a) AccountPayDialog.Open(a); };
        bSt.Click += (s, e) => { if (Sel is Account a) AccountStatementDialog.Open(a); };
        bOrder.Click += (s, e) => { if (Sel is Account a) Acts.New(new Order { AccountId = a.Id, CustomerName = a.Name, Phone = a.Phone }); };
        bOrders.Click += (s, e) => { if (Sel is Account a) MainForm.Instance?.Go("orders", p => ((OrdersPage)p).Search(a.Name)); };
    }

    Account Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    public override void Reload()
    {
        rows = Store.Accounts.OrderByDescending(Accounts.Due).ThenBy(a => a.Name, StringComparer.CurrentCulture).ToList();
        double due = rows.Sum(Accounts.Due), exp = rows.Sum(Accounts.Expected);
        var month = Txt.Today[..7];
        double paidMonth = rows.SelectMany(Accounts.OrdersOf).SelectMany(o => o.PaymentHistory).Where(p => p.Date.StartsWith(month)).Sum(p => p.Amount);
        ledger.Set(new[]
        {
            new Ledger.Cell("المستحق على الحسابات", Txt.Money(due), $"{rows.Count(a => Accounts.Due(a) > 0)} حساب", Pal.Bad, due > 0 ? -1 : 1),
            new Ledger.Cell("متوقع (أجهزة في الورشة)", Txt.Money(exp), null, Pal.Slate),
            new Ledger.Cell("المُسدَّد هذا الشهر", Txt.Money(paidMonth), null, Pal.Good),
        });
        ledger.Height = ledger.HeightFor(Math.Max(S(400), ledger.Width));
        grid.Rows.Clear();
        foreach (var a in rows)
            grid.Rows.Add(a.Name, Accounts.KindText(a), a.Phone, a.Discount > 0 ? Txt.Num(a.Discount) + "%" : "—", Accounts.OrdersOf(a).Count(Calc.IsOpen),
                Txt.Money(Accounts.Due(a)), Txt.Money(Accounts.Expected(a)),
                a.CreditLimit > 0 ? Txt.Money(a.CreditLimit) + (Accounts.Due(a) + Accounts.Expected(a) > a.CreditLimit ? "  ⚠ تجاوز" : "") : "—");
    }
}

public class AccountDialog : DialogShell
{
    readonly Account existing;
    readonly TextBox tName = new() { Width = 360 }, tPhone = new() { Width = 200 }, tNote = new() { Width = 580 };
    readonly Seg kind = new(Accounts.Kinds);
    readonly NumericUpDown nDisc = new() { Width = 140, Minimum = 0, Maximum = 100, DecimalPlaces = 1, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10) };
    readonly NumericUpDown nLimit = W.Money(200);

    AccountDialog(Account a) : base(a == null ? "حساب تاجر أو شركة" : "تعديل الحساب", 660, 500, "store")
    {
        existing = a;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        kind.Margin = new Padding(6, 4, 6, 8);
        flow.Controls.Add(kind);
        var r = W.Flow();
        r.Controls.Add(W.Labeled("الاسم *", tName, "store"));
        r.Controls.Add(W.Labeled("الهاتف", tPhone, "phone"));
        flow.Controls.Add(r);
        var rl = W.Flow();
        rl.Controls.Add(W.Labeled("خصم على أسعار القطع % (سعر التاجر)", nDisc));
        rl.Controls.Add(W.Labeled("حد الائتمان (0 = بلا حد)", nLimit));
        flow.Controls.Add(rl);
        flow.Controls.Add(W.Labeled("ملاحظة (طريقة التسديد، الموعد الشهري...)", tNote));
        Body.Controls.Add(flow);
        tName.Text = a?.Name ?? ""; tPhone.Text = a?.Phone ?? ""; tNote.Text = a?.Note ?? "";
        kind.Value = a?.Kind ?? "dealer";
        nDisc.Value = (decimal)(a?.Discount ?? 0);
        W.Set(nLimit, a?.CreditLimit ?? 0);
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        if (a != null)
            AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) =>
            {
                int n = Accounts.OrdersOf(a).Count;
                if (!W.Confirm("حذف الحساب؟", a.Name + (n > 0 ? $"\nعليه {n} طلب — تبقى الطلبات وتصبح طلبات زبون عادي." : ""), "حذف", true)) return;
                foreach (var o in Accounts.OrdersOf(a)) { var x = o.Clone(); x.AccountId = null; Store.SaveOrder(x); }
                Store.DeleteAccount(a);
                DialogResult = DialogResult.OK;
                Close();
                Store.NotifyChanged();
            };
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var name = tName.Text.Trim();
            if (name == "") { tName.Focus(); Toast.Show("اكتب اسم الحساب", Tone.Warning); return; }
            if (Store.Accounts.Any(x => x != existing && Txt.Fold(x.Name) == Txt.Fold(name))) { Dialogs.Warn("يوجد حساب بنفس الاسم."); return; }
            var acc = existing ?? new Account { Id = Txt.Uid("ac"), CreatedAt = Txt.Now };
            acc.Name = name; acc.Phone = Txt.LatinDigits(tPhone.Text.Trim()); acc.Note = tNote.Text.Trim(); acc.Kind = kind.Value; acc.Discount = (double)nDisc.Value; acc.CreditLimit = (double)nLimit.Value;
            Store.SaveAccount(acc);
            DialogResult = DialogResult.OK;
            Close();
            Store.NotifyChanged();
            Toast.Show("حُفظ الحساب " + name);
        };
        Shown += (s, e) => tName.Focus();
    }

    public static void Open(Account a) { using var d = new AccountDialog(a); d.ShowModal(); }
}

public class AccountPayDialog : DialogShell
{
    readonly Account acc;
    readonly NumericUpDown nAmount = W.Money(200);
    readonly Seg method = Seg.Of(K.PayMethods);
    readonly DateTimePicker dDate = new() { Width = 200, Format = DateTimePickerFormat.Short };
    readonly TextBox tNote = new() { Width = 520, PlaceholderText = "مثل: تسديد شهر أيلول" };

    AccountPayDialog(Account a) : base("تسديد دفعة — " + a.Name, 640, 480, "wallet", Pal.Good)
    {
        acc = a;
        double due = Accounts.Due(a), exp = Accounts.Expected(a);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note($"المستحق: {Txt.Money(due)}{(exp > 0 ? $" — وعلى أجهزة في الورشة: {Txt.Money(exp)}" : "")}\nتُوزَّع الدفعة على الأجهزة غير المسددة من الأقدم.", 560, 48));
        var r = W.Flow();
        r.Controls.Add(W.Labeled("المبلغ *", nAmount));
        r.Controls.Add(W.Labeled("التاريخ", dDate));
        flow.Controls.Add(r);
        method.Margin = new Padding(6, 4, 6, 6);
        flow.Controls.Add(method);
        flow.Controls.Add(W.Labeled("ملاحظة", tNote));
        Body.Controls.Add(flow);
        W.Set(nAmount, due);
        var ok = AddButton("تسجيل الدفعة", DialogResult.None, BtnKind.Success, "check");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            double amount = (double)nAmount.Value, max = Accounts.Due(acc) + Accounts.Expected(acc);
            if (amount <= 0) { nAmount.Focus(); return; }
            if (amount > max + 0.001) { Dialogs.Warn($"المبلغ أكبر من كل ما على الحساب ({Txt.Money(max)})."); return; }
            var res = Accounts.Pay(acc, amount, method.Value, Txt.Iso(dDate.Value), tNote.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
            Store.NotifyChanged();
            Toast.Show($"سُجّلت {Txt.Money(amount)} على {res.Count} جهاز — {acc.Name}");
        };
    }

    public static void Open(Account a)
    {
        if (Accounts.Due(a) + Accounts.Expected(a) <= 0) { Toast.Show("لا يوجد مبلغ مستحق على هذا الحساب", Tone.Info); return; }
        using var d = new AccountPayDialog(a);
        d.ShowModal();
    }
}

public class AccountStatementDialog : DialogShell
{
    readonly Account acc;
    readonly DateTimePicker dFrom = new() { Width = 170, Format = DateTimePickerFormat.Short }, dTo = new() { Width = 170, Format = DateTimePickerFormat.Short };
    readonly Ledger ledger = new() { Dock = DockStyle.Top, Height = 100, MinCell = 160 };
    readonly DataGridView grid = W.Grid();

    AccountStatementDialog(Account a) : base("كشف حساب — " + a.Name, 980, 700, "scroll-text")
    {
        acc = a;
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        dFrom.Value = first; dTo.Value = first.AddMonths(1).AddDays(-1);
        var top = W.Flow(false);
        top.Controls.Add(W.Labeled("من", dFrom));
        top.Controls.Add(W.Labeled("إلى", dTo));
        var bThis = W.Btn("هذا الشهر", null, BtnKind.Soft, 90); bThis.Margin = new Padding(3, 27, 3, 3);
        var bPrev = W.Btn("الشهر الماضي", null, BtnKind.Soft, 100); bPrev.Margin = new Padding(3, 27, 3, 3);
        bThis.Click += (s, e) => { dFrom.Value = first; dTo.Value = first.AddMonths(1).AddDays(-1); };
        bPrev.Click += (s, e) => { dFrom.Value = first.AddMonths(-1); dTo.Value = first.AddDays(-1); };
        top.Controls.Add(bThis);
        top.Controls.Add(bPrev);
        var topHost = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Surface };
        topHost.Controls.Add(top);
        grid.Columns.Add("date", "التاريخ");
        grid.Columns.Add("ref", "المرجع");
        grid.Columns.Add("text", "البيان");
        grid.Columns.Add("charge", "مستحق");
        grid.Columns.Add("paid", "مدفوع");
        grid.Columns.Add("run", "الرصيد");
        grid.Columns["text"].FillWeight = 260;
        Body.Controls.Add(grid);
        Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Surface });
        Body.Controls.Add(ledger);
        Body.Controls.Add(topHost);
        AddButton("تسديد دفعة", DialogResult.None, BtnKind.Success, "wallet").Click += (s, e) => { AccountPayDialog.Open(acc); Render(); };
        AddButton("طباعة الكشف", DialogResult.None, BtnKind.Secondary, "printer").Click += (s, e) => Print();
        AddButton("إرسال بالواتساب", DialogResult.None, BtnKind.Secondary, "message-circle").Click += (s, e) => WhatsApp();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        dFrom.ValueChanged += (s, e) => Render();
        dTo.ValueChanged += (s, e) => Render();
        Render();
    }

    public static void Open(Account a) { using var d = new AccountStatementDialog(a); d.ShowModal(); }

    (string A, string B) Range() => (Txt.Iso(dFrom.Value), Txt.Iso(dTo.Value));

    void Render()
    {
        var (a, b) = Range();
        var st = Accounts.StatementOf(acc, a, b);
        ledger.Set(new[]
        {
            new Ledger.Cell("الرصيد السابق", Txt.Money(st.Opening)),
            new Ledger.Cell("أجهزة الفترة", Txt.Money(st.Charges), $"{st.Lines.Count(l => l.Charge > 0)} جهاز", Pal.Primary),
            new Ledger.Cell("المدفوع في الفترة", Txt.Money(st.Payments), null, Pal.Good),
            new Ledger.Cell("الرصيد الختامي", Txt.Money(Math.Abs(st.Closing)), st.Closing > 0 ? "مستحق على الحساب" : st.Closing < 0 ? "له رصيد عندك" : "مسدد", null, st.Closing > 0 ? -1 : 1),
        });
        grid.Rows.Clear();
        double run = st.Opening;
        foreach (var l in st.Lines)
        {
            run += l.Charge - l.Paid;
            grid.Rows.Add(Txt.FmtShortDate(l.Date), l.RefNo, l.Text, l.Charge > 0 ? Txt.Money(l.Charge) : "", l.Paid != 0 ? Txt.Money(l.Paid) : "", Txt.Money(run));
        }
    }

    void Print()
    {
        var (a, b) = Range();
        var st = Accounts.StatementOf(acc, a, b);
        double run = st.Opening;
        var rows = st.Lines.Select(l => { run += l.Charge - l.Paid; return new[] { Txt.FmtDate(l.Date), l.RefNo, l.Text, l.Charge > 0 ? Txt.Money(l.Charge) : "", l.Paid != 0 ? Txt.Money(l.Paid) : "", Txt.Money(run) }; }).ToList();
        var body = Printer.Header("كشف حساب") +
            $"<div class=\"grid\"><div><span class=\"k\">الحساب: </span><b>{Txt.Esc(acc.Name)}</b></div><div><span class=\"k\">الفترة: </span><b>{Txt.FmtDate(a)} — {Txt.FmtDate(b)}</b></div>" +
            $"<div><span class=\"k\">الرصيد السابق: </span><b>{Txt.Esc(Txt.Money(st.Opening))}</b></div><div><span class=\"k\">الهاتف: </span><b dir=\"ltr\">{Txt.Esc(acc.Phone == "" ? "—" : acc.Phone)}</b></div></div>" +
            Printer.Table(new[] { "التاريخ", "المرجع", "البيان", "مستحق", "مدفوع", "الرصيد" }, rows) +
            Printer.Row("مجموع أجهزة الفترة", Txt.Money(st.Charges)) + Printer.Row("المدفوع في الفترة", Txt.Money(st.Payments), "good") +
            $"<div class=\"row total\"><span>الرصيد الختامي</span><span class=\"{(st.Closing > 0 ? "bad" : "good")}\">{Txt.Esc(Txt.Money(st.Closing))}</span></div>";
        Printer.Doc(body, "كشف " + acc.Name, "820px");
    }

    void WhatsApp()
    {
        var (a, b) = Range();
        var st = Accounts.StatementOf(acc, a, b);
        var lines = st.Lines.Where(l => l.Charge > 0).Select(l => $"- {Txt.FmtShortDate(l.Date)} {l.RefNo}: {l.Text} — {Txt.Money(l.Charge)}");
        var text = $"مرحباً {acc.Name}،\nكشف حسابكم من {Txt.FmtDate(a)} إلى {Txt.FmtDate(b)}:\nالرصيد السابق: {Txt.Money(st.Opening)}\n{string.Join("\n", lines)}\n" +
                   $"مجموع الأجهزة: {Txt.Money(st.Charges)}\nالمدفوع: {Txt.Money(st.Payments)}\nالرصيد المستحق: {Txt.Money(st.Closing)}\n\n{Store.ShopName}{(Store.ShopPhone != "" ? " — " + Store.ShopPhone : "")}";
        using var d = new WaDialog(acc.Name, acc.Phone, new() { new("statement", "كشف الحساب", text) }, 0);
        d.ShowModal();
    }
}
