using Raseed;
using static Raseed.Dpi;

namespace Workshop;

// ============================== الصناديق ==============================
public class BoxesPage : Page
{
    public override string Title => "الصناديق والمسحوبات";
    public override string Desc => "رصيد كل صندوق (النقد، البنك، زين كاش...) وحركاته، والتحويل بينها، ومسحوبات صاحب المحل";
    public override string PageIcon => "wallet";

    readonly Ledger ledger = new() { Dock = DockStyle.Top, MinCell = 170 };
    readonly ComboBox cbBox = W.Combo(220, Array.Empty<string>());
    readonly DataGridView grid = W.Grid();

    public BoxesPage()
    {
        var bar = Theme.Bar();
        bar.Controls.Add(W.Labeled("الصندوق", cbBox, "wallet"));
        ModernButton B(string t, string icon, BtnKind k, Action a) { var b = W.Btn(t, icon, k, 120); b.Margin = new Padding(4, 26, 4, 4); b.Click += (s, e) => a(); bar.Controls.Add(b); return b; }
        B("تحويل بين صندوقين", "arrow-left-right", BtnKind.Primary, () => MoneyDialog.Transfer());
        B("مسحوبات صاحب المحل", "log-out", BtnKind.Secondary, () => MoneyDialog.Withdraw());
        B("الرصيد الافتتاحي", "pencil", BtnKind.Ghost, () =>
        {
            var box = cbBox.Text;
            var v = Ask.Reason("الرصيد الافتتاحي — " + box, $"كم كان في «{box}» قبل بدء استعمال البرنامج؟ (الحالي: {Txt.Money(Boxes.OpeningOf(box))})", new[] { "0" }, "حفظ");
            if (v == null) return;
            Boxes.SetOpening(box, Txt.ParseMoney(v));
            Reload();
        });
        grid.Columns.Add("date", "التاريخ");
        grid.Columns.Add("text", "البيان");
        grid.Columns.Add("amount", "المبلغ");
        grid.Columns.Add("run", "الرصيد");
        grid.Columns["text"].FillWeight = 260;
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "amount" || e.Value is not string v) return;
            e.CellStyle.ForeColor = v.StartsWith("-") ? Pal.Bad : Pal.Good;
        };
        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        Controls.Add(ledger);
        cbBox.SelectedIndexChanged += (s, e) => RenderMoves();
    }

    public override void Reload()
    {
        var bal = Boxes.Balances();
        var month = Txt.Today[..7];
        double withdrawn = Boxes.Withdrawals().Where(w => w.Date.StartsWith(month)).Sum(w => w.Amount);
        ledger.Set(bal.Where(kv => Math.Abs(kv.Value) > 0.001 || kv.Key == Lists.Cash).Select(kv => new Ledger.Cell(kv.Key, Txt.Money(kv.Value), null, kv.Key == Lists.Cash ? Pal.Good : Pal.Primary, kv.Value < 0 ? -1 : 0))
            .Append(new Ledger.Cell("مسحوبات هذا الشهر", Txt.Money(withdrawn), "ليست مصروفاً — لا تنقص الربح", Pal.Amber)));
        ledger.Height = ledger.HeightFor(Math.Max(S(400), ledger.Width));
        var keep = cbBox.Text;
        cbBox.Items.Clear();
        cbBox.Items.AddRange(Boxes.Names().Cast<object>().ToArray());
        W.Pick(cbBox, keep != "" ? keep : Lists.Cash);
        RenderMoves();
    }

    void RenderMoves()
    {
        var box = cbBox.Text;
        double run = Boxes.OpeningOf(box);
        var rows = new List<(Boxes.Move M, double Run)>();
        foreach (var m in Boxes.Moves().Where(m => m.Box == box)) { run += m.Amount; rows.Add((m, run)); }
        grid.Rows.Clear();
        if (Boxes.OpeningOf(box) != 0) grid.Rows.Add("—", "رصيد افتتاحي", Txt.Money(Boxes.OpeningOf(box)), Txt.Money(Boxes.OpeningOf(box)));
        foreach (var (m, r) in rows.AsEnumerable().Reverse().Take(300))
            grid.Rows.Add(Txt.FmtShortDate(m.Date), m.Text, (m.Amount < 0 ? "-" : "+") + Txt.Money(Math.Abs(m.Amount)), Txt.Money(r));
    }
}

/// <summary>تحويل بين صندوقين، أو مسحوبات صاحب المحل</summary>
public static class MoneyDialog
{
    public static void Transfer() => Open(true);
    public static void Withdraw() => Open(false);

    static void Open(bool transfer)
    {
        using var d = new DialogShell(transfer ? "تحويل بين صندوقين" : "مسحوبات صاحب المحل", 560, 480, transfer ? "arrow-left-right" : "log-out", Pal.Primary);
        var n = W.Money(200);
        var from = W.Combo(200, Boxes.Names());
        var to = W.Combo(200, Boxes.Names());
        var date = new DateTimePicker { Width = 180, Format = DateTimePickerFormat.Short };
        var note = new TextBox { Width = 460, PlaceholderText = transfer ? "مثل: إيداع في البنك" : "مثل: مصروف البيت" };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        if (!transfer) flow.Controls.Add(W.Note("ما تأخذه لنفسك من المحل: ينقص الصندوق لكنه ليس مصروفاً، فلا يغيّر ربح المحل.", 480, 40));
        var r = W.Flow();
        r.Controls.Add(W.Labeled(transfer ? "من" : "من صندوق", from));
        if (transfer) { r.Controls.Add(W.Labeled("إلى", to)); if (to.Items.Count > 1) to.SelectedIndex = 1; }
        flow.Controls.Add(r);
        var r2 = W.Flow();
        r2.Controls.Add(W.Labeled("المبلغ *", n));
        r2.Controls.Add(W.Labeled("التاريخ", date));
        flow.Controls.Add(r2);
        flow.Controls.Add(W.Labeled("ملاحظة", note));
        d.Body.Controls.Add(flow);
        var ok = d.AddButton("تسجيل", DialogResult.None, BtnKind.Primary, "check");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            double a = (double)n.Value;
            if (a <= 0) { n.Focus(); return; }
            if (transfer && from.Text == to.Text) { Toast.Show("اختر صندوقين مختلفين", Tone.Warning); return; }
            if (transfer) Boxes.AddTransfer(a, from.Text, to.Text, Txt.Iso(date.Value), note.Text.Trim());
            else Boxes.AddWithdrawal(a, from.Text, Txt.Iso(date.Value), note.Text.Trim());
            d.DialogResult = DialogResult.OK;
            d.Close();
            Store.NotifyChanged();
            Toast.Show(transfer ? $"حُوّل {Txt.Money(a)} من {from.Text} إلى {to.Text}" : $"سُجّلت مسحوبات {Txt.Money(a)}");
        };
        d.ShowModal();
    }
}

// ============================== قرار الضمان ==============================
public static class WarrantyDecision
{
    public static readonly string[] RejectReasons = { "كسر أو سقوط", "دخول سوائل", "فتح الجهاز خارج المحل", "سوء استخدام", "عطل مختلف عن الإصلاح السابق", "انتهت مدة الضمان" };

    public static void Decide(Order o, bool covered)
    {
        string reason = covered ? "عطل في الإصلاح أو القطعة السابقة" : Ask.Reason("رفض الضمان", "لماذا لا يشمله الضمان؟ (يُطبع في خطاب الزبون)", RejectReasons, "رفض الضمان");
        if (reason == null) return;
        var n = o.Clone();
        n.X.WarrantyDecision = covered ? "covered" : "rejected";
        n.X.WarrantyReason = reason;
        n.X.WarrantyDecidedAt = Txt.Now;
        Locking.Log(n, (covered ? "قُبل الضمان" : "رُفض الضمان") + " — " + reason);
        n.UpdatedAt = Txt.Now;
        Store.SaveOrder(n);
        Store.NotifyChanged();
        if (W.Confirm(covered ? "قُبل الضمان" : "رُفض الضمان", "طباعة خطاب القرار للزبون؟", "طباعة")) Printer.WarrantyLetter(n);
    }
}

// ============================== عند التسليم وتغيير الحالة ==============================
public static class AfterStatus
{
    /// <summary>بعد تغيير الحالة من البرنامج: رقم الفاتورة ومكافأة الإحالة عند التسليم، اقتراح إضافات، ثم اقتراح رسالة للزبون</summary>
    public static void Run(string orderId, string prev)
    {
        var o = Calc.Find(orderId);
        if (o == null || o.Status == prev) return;
        if (o.Status == K.Done)
        {
            o = InvoiceNumbers.Ensure(o);
            Referral.OnDelivered(o);
            var up = Upsell.For(o);
            if (up.Count > 0) o = UpsellDialog.Offer(o, up) ?? o;
        }
        if (Store.Flag("suggest_message", true) && o.Phone != "" && o.Status is K.Approval or K.Ready or K.Done &&
            W.Confirm("رسالة للزبون", $"{o.CustomerName} — {o.Device}\nأصبح «{o.Status}». إرسال رسالة للزبون؟", "فتح الرسالة"))
            Acts.WhatsApp(o);
        Store.NotifyChanged();
    }
}

public class UpsellDialog : DialogShell
{
    readonly List<(InvItem Item, Toggle T)> picks = new();

    UpsellDialog(Order o, List<InvItem> items) : base("اقتراح للزبون قبل التسليم", 600, 460, "tag", Pal.Good)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note($"{o.Device} — {o.IssueType}. اعرض على الزبون ما يحمي جهازه، وحدد ما وافق عليه ليُضاف للفاتورة:", 540, 44));
        foreach (var i in items)
        {
            var t = new Toggle { Text = $"{i.Name}{(i.Compatible != "" ? " — " + i.Compatible : "")}   {Txt.Money(i.SalePrice)}", Width = 520 };
            picks.Add((i, t));
            flow.Controls.Add(t);
        }
        Body.Controls.Add(flow);
        AddButton("إضافة المحدد", DialogResult.OK, BtnKind.Primary, "plus");
        AddButton("لا شيء", DialogResult.Cancel, BtnKind.Secondary);
    }

    public static Order Offer(Order o, List<InvItem> items)
    {
        using var d = new UpsellDialog(o, items);
        if (d.ShowModal() != DialogResult.OK) return null;
        var n = o;
        foreach (var (i, t) in d.picks.Where(p => p.T.Checked)) n = Upsell.Add(n, i);
        if (n != o) Toast.Show($"أُضيف للطلب — المبلغ الآن {Txt.Money(n.Price)}");
        return n;
    }
}

// ============================== رقم الدور ==============================
public class QueueDialog : DialogShell
{
    readonly Label big = new() { Dock = DockStyle.Fill, Font = Theme.FS(64), ForeColor = Theme.Brand, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label info = new() { Dock = DockStyle.Bottom, Height = 40, Font = Theme.F(11), ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter };
    Form screen;
    Label screenNum;

    QueueDialog() : base("رقم الدور", 560, 460, "list", Pal.Primary)
    {
        Body.Controls.Add(big);
        Body.Controls.Add(info);
        AddButton("رقم جديد (طباعة)", DialogResult.None, BtnKind.Primary, "printer").Click += (s, e) => { int n = Queue.Take(); PrintTicket(n); Render(); };
        AddButton("التالي", DialogResult.None, BtnKind.Success, "arrow-left").Click += (s, e) => { Queue.Next(); Render(); };
        AddButton("شاشة الانتظار", DialogResult.None, BtnKind.Secondary, "smartphone").Click += (s, e) => ShowScreen();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        FormClosed += (s, e) => screen?.Close();
        Render();
    }

    public static void Open() { using var d = new QueueDialog(); d.ShowModal(); }

    void Render()
    {
        big.Text = Queue.Serving == 0 ? "—" : Queue.Serving.ToString();
        info.Text = $"آخر رقم أُعطي: {Queue.Last}   •   بالانتظار: {Math.Max(0, Queue.Last - Queue.Serving)}";
        if (screenNum != null && !screenNum.IsDisposed) screenNum.Text = big.Text;
    }

    /// <summary>نافذة كبيرة تُسحب لشاشة ثانية مقابل الزبائن</summary>
    void ShowScreen()
    {
        if (screen != null && !screen.IsDisposed) { screen.Activate(); return; }
        screen = new Form { Text = "الدور", BackColor = Theme.Brand, Size = new Size(S(700), S(500)), StartPosition = FormStartPosition.CenterScreen, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true };
        var title = new Label { Dock = DockStyle.Top, Height = S(90), Text = "الرقم الحالي", Font = Theme.FS(28), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter };
        screenNum = new Label { Dock = DockStyle.Fill, Font = Theme.FS(140), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Text = big.Text };
        screen.Controls.Add(screenNum);
        screen.Controls.Add(title);
        screen.DoubleClick += (s, e) => screen.WindowState = screen.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        screen.Show();
    }

    static void PrintTicket(int n)
    {
        var body = $"<div style=\"text-align:center\"><div class=\"shop\">{Txt.Esc(Store.ShopName)}</div><div style=\"font-size:14px;margin-top:8px\">رقم دورك</div>" +
                   $"<div style=\"font-size:72px;font-weight:700;color:#2B55C9\">{n}</div><div class=\"k\">{DateTime.Now:yyyy/MM/dd HH:mm}</div></div>";
        Printer.Doc(body, "دور " + n, "280px");
    }
}

// ============================== استيراد أسعار المورد ==============================
public class PriceImportDialog : DialogShell
{
    List<List<string>> rows = new();
    readonly ComboBox cbName = W.Combo(180, Array.Empty<string>()), cbModel = W.Combo(180, Array.Empty<string>()), cbCost = W.Combo(180, Array.Empty<string>());
    readonly TextBox tSup = new() { Width = 220 };
    readonly Toggle tgHeader = new() { Text = "الصف الأول عناوين", Width = 200, Checked = true };
    readonly Toggle tgAddNew = new() { Text = "إضافة غير الموجود كقطع جديدة", Width = 280 };
    readonly DataGridView grid = W.Grid();
    List<PriceImport.Match> matches = new();

    PriceImportDialog() : base("استيراد قائمة أسعار المورد", 1100, 720, "download", Pal.Primary)
    {
        grid.ReadOnly = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "apply", HeaderText = "تطبيق", FillWeight = 40 });
        foreach (var (n, h) in new[] { ("name", "القطعة في الملف"), ("model", "الموديل"), ("item", "القطعة عندك"), ("old", "التكلفة الحالية"), ("new", "التكلفة الجديدة"), ("chg", "التغيير"), ("margin", "هامش الربح") })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = h, ReadOnly = true });
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= matches.Count) return;
            var m = matches[e.RowIndex];
            var c = grid.Columns[e.ColumnIndex].Name;
            if (c == "margin" && m.Item != null && m.Margin < 20) e.CellStyle.ForeColor = Pal.Bad;
            if (c == "chg" && m.Item != null) e.CellStyle.ForeColor = m.NewCost > m.OldCost ? Pal.Bad : Pal.Good;
            if (c == "item" && m.Item == null) e.CellStyle.ForeColor = Theme.Muted;
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, BackColor = Theme.Surface };
        var bFile = W.Btn("اختيار الملف (Excel أو CSV)", "folder-open", BtnKind.Primary, 200); bFile.Margin = new Padding(4, 27, 4, 4);
        top.Controls.Add(bFile);
        top.Controls.Add(W.Labeled("المورد", tSup, "store"));
        top.Controls.Add(W.Labeled("عمود اسم القطعة", cbName));
        top.Controls.Add(W.Labeled("عمود الموديل", cbModel));
        top.Controls.Add(W.Labeled("عمود السعر", cbCost));
        tgHeader.Margin = tgAddNew.Margin = new Padding(6, 8, 6, 0);
        top.Controls.Add(tgHeader);
        top.Controls.Add(tgAddNew);
        Body.Controls.Add(grid);
        Body.Controls.Add(top);
        W.Suggest(tSup, Calc.SupplierBalances().Select(x => x.Name).Concat(Store.Inventory.Select(i => i.Supplier)));
        AddButton("تطبيق المحدد", DialogResult.None, BtnKind.Primary, "check").Click += (s, e) => Apply();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        bFile.Click += (s, e) => LoadFile();
        foreach (var c in new[] { cbName, cbModel, cbCost }) c.SelectedIndexChanged += (s, e) => Match();
        tSup.Leave += (s, e) => Match();
        tgHeader.CheckedChanged += (s, e) => Match();
    }

    public static void Open() { using var d = new PriceImportDialog(); d.ShowModal(); }

    void LoadFile()
    {
        var f = W.OpenFile("Excel أو CSV|*.xlsx;*.csv");
        if (f == null) return;
        try { rows = PriceImport.Read(f); }
        catch (Exception ex) { Dialogs.Warn("تعذّرت قراءة الملف: " + ex.Message); return; }
        if (rows.Count == 0) { Dialogs.Warn("الملف فارغ."); return; }
        int cols = rows.Max(r => r.Count);
        var head = Enumerable.Range(0, cols).Select(i => $"{i + 1}: {(i < rows[0].Count ? rows[0][i] : "")}").ToArray();
        foreach (var c in new[] { cbName, cbModel, cbCost }) { c.Items.Clear(); c.Items.Add("—"); c.Items.AddRange(head); }
        int Guess(params string[] words) { for (int i = 0; i < rows[0].Count; i++) if (words.Any(w => Txt.Fold(rows[0][i]).Contains(w))) return i + 1; return 0; }
        cbName.SelectedIndex = Math.Max(Guess("اسم", "قطعه", "name", "item", "صنف"), 1);
        cbModel.SelectedIndex = Guess("موديل", "model", "جهاز");
        cbCost.SelectedIndex = Math.Max(Guess("سعر", "price", "cost", "تكلف"), Math.Min(2, cols));
        if (tSup.Text.Trim() == "") tSup.Text = Path.GetFileNameWithoutExtension(f);
        Match();
    }

    void Match()
    {
        if (rows.Count == 0) return;
        matches = PriceImport.Matches(rows, cbName.SelectedIndex - 1, cbModel.SelectedIndex - 1, cbCost.SelectedIndex - 1, tSup.Text.Trim(), tgHeader.Checked);
        grid.Rows.Clear();
        foreach (var m in matches)
            grid.Rows.Add(m.Item != null && Math.Abs(m.NewCost - m.OldCost) > 0.001, m.Name, m.Model, m.Item == null ? "غير موجودة" : m.Item.Name + " " + m.Item.Compatible,
                m.Item == null ? "—" : Txt.Money(m.OldCost), Txt.Money(m.NewCost),
                m.Item == null || m.OldCost <= 0 ? "—" : (m.NewCost >= m.OldCost ? "▲ " : "▼ ") + Math.Abs(Math.Round((m.NewCost - m.OldCost) / m.OldCost * 100)) + "%",
                m.Item == null || m.Item.SalePrice <= 0 ? "—" : Math.Round(m.Margin) + "%");
        Text = $"استيراد قائمة أسعار المورد — {matches.Count(m => m.Item != null)} مطابقة من {matches.Count}";
    }

    void Apply()
    {
        grid.EndEdit();
        int changed = 0, added = 0;
        var save = new List<InvItem>();
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.Item != null && grid.Rows[i].Cells["apply"].Value is true) { m.Item.Cost = m.NewCost; m.Item.UpdatedAt = Txt.Now; save.Add(m.Item); changed++; }
            else if (m.Item == null && tgAddNew.Checked)
            {
                var n = new InvItem { Id = Txt.Uid("inv"), Name = m.Name, Compatible = m.Model, Supplier = tSup.Text.Trim(), Cost = m.NewCost, SalePrice = 0, Category = K.InvCats[^1], UpdatedAt = Txt.Now };
                Store.SaveInv(n);
                added++;
            }
        }
        foreach (var it in save) Store.SaveInv(it);
        Store.NotifyChanged();
        Toast.Show($"حُدّثت تكلفة {changed} قطعة" + (added > 0 ? $"، وأُضيفت {added} (بلا سعر بيع — عدّلها)" : ""));
        var low = save.Where(i => i.SalePrice > 0 && (i.SalePrice - i.Cost) / i.SalePrice < 0.2).ToList();
        if (low.Count > 0) Dialogs.Warn("هامش الربح أصبح أقل من 20% في:\n" + string.Join("\n", low.Take(12).Select(i => $"- {i.Name} {i.Compatible}: التكلفة {Txt.Money(i.Cost)} والبيع {Txt.Money(i.SalePrice)}")));
        Match();
    }
}

// ============================== أجهزة القطع ==============================
public class ScrapDialog : DialogShell
{
    readonly DataGridView grid = W.Grid();
    List<ScrapDevice> rows = new();

    ScrapDialog() : base("أجهزة القطع (سكراب)", 980, 620, "boxes", Pal.Slate)
    {
        grid.Columns.Add("date", "التاريخ");
        grid.Columns.Add("device", "الجهاز");
        grid.Columns.Add("source", "المصدر");
        grid.Columns.Add("cost", "التكلفة");
        grid.Columns.Add("parts", "القطع المفكوكة");
        grid.Columns.Add("value", "قيمتها");
        grid.Columns["parts"].FillWeight = 220;
        Body.Controls.Add(grid);
        AddButton("جهاز جديد", DialogResult.None, BtnKind.Primary, "plus").Click += (s, e) => { Edit(null); Render(); };
        AddButton("فكّ قطعة منه", DialogResult.None, BtnKind.Success, "wrench").Click += (s, e) => { if (Sel is ScrapDevice d) { HarvestPart(d); Render(); } };
        AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) =>
        {
            if (Sel is ScrapDevice d && W.Confirm("حذف الجهاز من القائمة؟", d.Device + "\nالقطع التي دخلت المخزون تبقى.", "حذف", true)) { Scrap.Delete(d); Store.NotifyChanged(); Render(); }
        };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        Render();
    }

    public static void Open() { using var d = new ScrapDialog(); d.ShowModal(); }

    /// <summary>تحويل طلب (جهاز تركه صاحبه أو ملغى) إلى جهاز قطع</summary>
    public static void FromOrder(Order o)
    {
        if (!W.Confirm("تحويل إلى جهاز قطع", $"{o.Device} — {o.CustomerName}\nتأكد أن الجهاز لم يعد يُطالب به (بعد انتهاء مدة الحفظ في شروطك).", "تحويل")) return;
        Scrap.Save(new ScrapDevice { Device = o.Device, Imei = o.Imei, Source = "تركه صاحبه", Date = Txt.Today, FromOrder = o.RefNo, Note = $"من الطلب {o.RefNo} — {o.CustomerName}" });
        var n = o.Clone();
        Locking.Log(n, "حُوّل إلى أجهزة القطع");
        Store.SaveOrder(n);
        Store.NotifyChanged();
        Toast.Show("أُضيف إلى أجهزة القطع");
    }

    ScrapDevice Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    void Render()
    {
        rows = Scrap.All();
        grid.Rows.Clear();
        foreach (var d in rows)
            grid.Rows.Add(Txt.FmtShortDate(d.Date), d.Device + (d.Imei != "" ? " — " + d.Imei : ""), d.Source, Txt.Money(d.Cost),
                d.Parts.Count == 0 ? "—" : string.Join("، ", d.Parts.Select(p => p.Name)), Txt.Money(d.Parts.Sum(p => p.Value)));
    }

    static void Edit(ScrapDevice s)
    {
        using var d = new DialogShell("جهاز للقطع", 560, 460, "boxes");
        var tDev = new TextBox { Width = 300, Text = s?.Device ?? "" };
        var tImei = new TextBox { Width = 200, Text = s?.Imei ?? "" };
        var cbSrc = W.Combo(220, Scrap.Sources);
        var nCost = W.Money(160);
        var tNote = new TextBox { Width = 480, Text = s?.Note ?? "" };
        W.Suggest(tDev, Models.All());
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        var r = W.Flow(); r.Controls.Add(W.Labeled("الجهاز *", tDev, "smartphone")); r.Controls.Add(W.Labeled("IMEI", tImei)); flow.Controls.Add(r);
        var r2 = W.Flow(); r2.Controls.Add(W.Labeled("المصدر", cbSrc)); r2.Controls.Add(W.Labeled("ما دفعته فيه", nCost)); flow.Controls.Add(r2);
        flow.Controls.Add(W.Labeled("ملاحظة", tNote));
        d.Body.Controls.Add(flow);
        var ok = d.AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (_, _) =>
        {
            if (tDev.Text.Trim() == "") return;
            var x = s ?? new ScrapDevice { Date = Txt.Today };
            x.Device = tDev.Text.Trim(); x.Imei = tImei.Text.Trim(); x.Source = cbSrc.Text; x.Cost = (double)nCost.Value; x.Note = tNote.Text.Trim();
            Scrap.Save(x);
            if (x.Cost > 0 && s == null && W.Confirm("تسجيله مصروفاً؟", $"دفعت {Txt.Money(x.Cost)} في الجهاز — تسجيله مصروفاً اليوم؟", "تسجيل"))
                Store.AddExpense(new Expense { Id = Txt.Uid("e"), Description = "شراء جهاز للقطع: " + x.Device, Amount = x.Cost, Date = Txt.Today });
            Store.NotifyChanged();
            d.DialogResult = DialogResult.OK;
            d.Close();
        };
        d.ShowModal();
    }

    static void HarvestPart(ScrapDevice s)
    {
        using var d = new DialogShell("فكّ قطعة — " + s.Device, 560, 440, "wrench");
        var tName = new TextBox { Width = 300, PlaceholderText = "مثل: شاشة، بطارية، كاميرا خلفية" };
        var tModel = new TextBox { Width = 220, Text = s.Device };
        var cbCat = W.Combo(220, K.InvCats);
        var nVal = W.Money(160);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        var r = W.Flow(); r.Controls.Add(W.Labeled("القطعة *", tName)); r.Controls.Add(W.Labeled("الموديل", tModel)); flow.Controls.Add(r);
        var r2 = W.Flow(); r2.Controls.Add(W.Labeled("التصنيف", cbCat)); r2.Controls.Add(W.Labeled("قيمتها التقديرية (تكلفتها في المخزون)", nVal)); flow.Controls.Add(r2);
        flow.Controls.Add(W.Note("تُضاف قطعة للمخزون (أو تزيد كمية القطعة الموجودة بنفس الاسم والموديل).", 480));
        d.Body.Controls.Add(flow);
        var ok = d.AddButton("إضافة للمخزون", DialogResult.None, BtnKind.Primary, "plus");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (_, _) =>
        {
            if (tName.Text.Trim() == "") return;
            Scrap.Harvest(s, tName.Text.Trim(), tModel.Text.Trim(), cbCat.Text, (double)nVal.Value);
            Store.NotifyChanged();
            Toast.Show("دخلت المخزون: " + tName.Text.Trim());
            d.DialogResult = DialogResult.OK;
            d.Close();
        };
        d.ShowModal();
    }
}

// ============================== أدوات الورشة ==============================
public class ToolsDialog : DialogShell
{
    readonly DataGridView grid = W.Grid();
    List<ToolItem> rows = new();

    ToolsDialog() : base("أدوات الورشة وصيانتها", 940, 600, "wrench", Pal.Slate)
    {
        foreach (var h in new[] { "الأداة", "الشراء", "السعر", "آخر صيانة", "الصيانة القادمة", "ملاحظة" }) grid.Columns.Add(h, h);
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex != 4) return;
            var n = Tools.NextService(rows[e.RowIndex]);
            if (n != "" && string.CompareOrdinal(n, Txt.Today) <= 0) e.CellStyle.ForeColor = Pal.Bad;
        };
        Body.Controls.Add(grid);
        AddButton("أداة جديدة", DialogResult.None, BtnKind.Primary, "plus").Click += (s, e) => { Edit(null); Render(); };
        AddButton("صيانة الآن", DialogResult.None, BtnKind.Success, "check").Click += (s, e) =>
        {
            if (Sel is not ToolItem t) return;
            var note = Ask.Reason("صيانة " + t.Name, "ماذا عملت؟", new[] { "تنظيف", "تغيير رأس اللحام", "معايرة", "تغيير فلتر" }, "تسجيل");
            if (note == null) return;
            t.LastService = Txt.Today;
            t.Log.Add($"{Txt.Today}: {note}");
            Tools.Save(t);
            Store.NotifyChanged();
            Render();
        };
        AddButton("تعديل", DialogResult.None, BtnKind.Secondary, "pencil").Click += (s, e) => { if (Sel is ToolItem t) { Edit(t); Render(); } };
        AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) => { if (Sel is ToolItem t && W.Confirm("حذف الأداة؟", t.Name, "حذف", true)) { Tools.Delete(t); Store.NotifyChanged(); Render(); } };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        Render();
    }

    public static void Open() { using var d = new ToolsDialog(); d.ShowModal(); }

    ToolItem Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    void Render()
    {
        rows = Tools.All();
        grid.Rows.Clear();
        foreach (var t in rows)
            grid.Rows.Add(t.Name, Txt.FmtShortDate(t.Bought), Txt.Money(t.Price), t.LastService == "" ? "—" : Txt.FmtShortDate(t.LastService),
                Tools.NextService(t) is var n && n != "" ? Txt.FmtDate(n) : "—", t.Log.Count > 0 ? t.Log[^1] : t.Note);
    }

    static void Edit(ToolItem t)
    {
        using var d = new DialogShell(t == null ? "أداة جديدة" : "تعديل الأداة", 560, 480, "wrench");
        var tName = new TextBox { Width = 300, Text = t?.Name ?? "", PlaceholderText = "مثل: محطة لحام، مجهر، ماكينة فصل الشاشات" };
        var dBought = new DateTimePicker { Width = 180, Format = DateTimePickerFormat.Short };
        var nPrice = W.Money(160);
        var nEvery = new NumericUpDown { Width = 120, Minimum = 0, Maximum = 3650, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10) };
        var tNote = new TextBox { Width = 480, Text = t?.Note ?? "" };
        dBought.Value = Txt.ParseDate(t?.Bought) ?? DateTime.Today;
        W.Set(nPrice, t?.Price ?? 0);
        nEvery.Value = t?.IntervalDays ?? 0;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Labeled("الأداة *", tName, "wrench"));
        var r = W.Flow(); r.Controls.Add(W.Labeled("تاريخ الشراء", dBought)); r.Controls.Add(W.Labeled("السعر", nPrice)); flow.Controls.Add(r);
        flow.Controls.Add(W.Labeled("صيانة كل (يوم، 0 = بدون)", nEvery, "clock"));
        flow.Controls.Add(W.Labeled("ملاحظة", tNote));
        d.Body.Controls.Add(flow);
        var ok = d.AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (_, _) =>
        {
            if (tName.Text.Trim() == "") return;
            var x = t ?? new ToolItem();
            x.Name = tName.Text.Trim(); x.Bought = Txt.Iso(dBought.Value); x.Price = (double)nPrice.Value; x.IntervalDays = (int)nEvery.Value; x.Note = tNote.Text.Trim();
            Tools.Save(x);
            Store.NotifyChanged();
            d.DialogResult = DialogResult.OK;
            d.Close();
        };
        d.ShowModal();
    }
}

// ============================== إعدادات المبيعات والولاء ==============================
public partial class SettingsDialog
{
    readonly Toggle tgLoyalty = new() { Text = "تفعيل نقاط الولاء للزبائن", Width = 700 };
    readonly NumericUpDown nPer = W.Money(160), nPtVal = W.Money(160);
    readonly NumericUpDown nMinPts = new() { Width = 120, Minimum = 1, Maximum = 100000, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10) };
    readonly NumericUpDown nRefReward = W.Money(180);
    readonly TextBox tReview = new() { Width = 600, PlaceholderText = "https://g.page/r/..." };
    readonly TextBox tExtW = new() { Width = 420, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    readonly TextBox tInvPrefix = new() { Width = 120 };
    readonly Toggle tgAuto = new() { Text = "توزيع الأجهزة الجديدة تلقائياً على الفنيين (حسب ما يتقنه كل فني وعدد أجهزته المفتوحة)", Width = 760 };
    readonly Toggle tgSuggest = new() { Text = "بعد تغيير الحالة إلى «بانتظار الموافقة» أو «جاهز» أو «تم التسليم»: اقترح إرسال رسالة للزبون", Width = 760 };
    readonly Label lblInvSeq = W.Note("", 400, 22);

    Control SalesTab()
    {
        var p = Page();
        p.Controls.Add(W.Head("نقاط الولاء", 780));
        p.Controls.Add(tgLoyalty);
        var r = W.Flow(false);
        r.Controls.Add(W.Labeled("نقطة لكل (مبلغ مدفوع)", nPer));
        r.Controls.Add(W.Labeled("قيمة النقطة عند الاستبدال", nPtVal));
        r.Controls.Add(W.Labeled("أقل عدد للاستبدال", nMinPts));
        p.Controls.Add(r);
        var lblEx = W.Note("", 780, 22);
        p.Controls.Add(lblEx);
        void Example() => lblEx.Text = nPer.Value > 0 ? $"مثال: زبون دفع {Txt.Money((double)nPer.Value * 100)} يكسب 100 نقطة = خصم {Txt.Money((double)nPtVal.Value * 100)} ({Math.Round((double)(nPtVal.Value / nPer.Value) * 100, 1)}%)" : "";
        nPer.ValueChanged += (s, e) => Example();
        nPtVal.ValueChanged += (s, e) => Example();

        p.Controls.Add(W.Head("مكافأة الإحالة", 780));
        p.Controls.Add(W.Note("عند تسليم أول جهاز لزبون جديد أحاله زبون سابق (حقل «أحاله زبون؟» في الطلب)، يُضاف للمحيل رصيد يستعمله في إصلاحه القادم.", 780, 40));
        p.Controls.Add(W.Labeled("المكافأة (0 = متوقفة)", nRefReward, "gift"));

        p.Controls.Add(W.Head("الضمان الممتد", 780));
        p.Controls.Add(W.Note("خيارات تُباع مع الإصلاح: سطر لكل خيار بالشكل «6 أشهر = 20000». المبلغ يُضاف لسعر الطلب ومدته تُضاف لمدة الضمان.", 780, 40));
        p.Controls.Add(W.Wrap(tExtW));

        p.Controls.Add(W.Head("الفواتير والتقييم", 780));
        var r2 = W.Flow(false);
        r2.Controls.Add(W.Labeled("بادئة رقم الفاتورة", tInvPrefix, "hash"));
        lblInvSeq.Margin = new Padding(6, 32, 6, 0);
        r2.Controls.Add(lblInvSeq);
        p.Controls.Add(r2);
        p.Controls.Add(W.Note("كل جهاز يُسلَّم يأخذ رقم فاتورة متسلسلاً لا يتكرر ولا يُعاد استعماله حتى لو حُذف الطلب.", 780));
        p.Controls.Add(W.Labeled("رابط التقييم على Google (يُطبع في الفاتورة ويُرسل في رسالة الشكر)", tReview, "star"));

        p.Controls.Add(W.Head("سير العمل", 780));
        p.Controls.Add(tgAuto);
        p.Controls.Add(W.Note("ما يتقنه كل فني يُحدَّد من تبويب «الفنيون».", 780));
        p.Controls.Add(tgSuggest);

        tgLoyalty.Checked = Loyalty.On;
        W.Set(nPer, Loyalty.PerPoint);
        W.Set(nPtVal, Loyalty.PointValue);
        nMinPts.Value = Math.Min(nMinPts.Maximum, Loyalty.MinRedeem);
        W.Set(nRefReward, Referral.Reward);
        tExtW.Text = string.Join(Environment.NewLine, ExtWarranty.Options().Select(o => $"{o.Name} = {Txt.Num(o.Price)}"));
        tInvPrefix.Text = Store.Get("invoice_prefix", "INV-");
        lblInvSeq.Text = $"آخر رقم صدر: {Store.Get("invoice_seq", "0")}";
        tReview.Text = Store.Get("google_review_url");
        tgAuto.Checked = AutoAssign.On;
        tgSuggest.Checked = Store.Flag("suggest_message", true);
        Example();
        return p;
    }

    void SaveSales()
    {
        Loyalty.Save(tgLoyalty.Checked, (double)nPer.Value, (double)nPtVal.Value, (int)nMinPts.Value);
        Store.Set("referral_reward", Txt.Num((double)nRefReward.Value));
        var lines = tExtW.Text.Replace("\r\n", "\n").Split('\n').Select(l => l.Split('=')).Where(x => x.Length == 2 && x[0].Trim() != "")
            .Select(x => $"{x[0].Trim()} = {Txt.Num(Txt.ParseMoney(x[1]))}");
        Store.Set("ext_warranties", string.Join("\n", lines));
        Store.Set("invoice_prefix", tInvPrefix.Text.Trim());
        var url = tReview.Text.Trim();
        Store.Set("google_review_url", url == "" || url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url);
        Store.SetFlag("auto_assign", tgAuto.Checked);
        Store.SetFlag("suggest_message", tgSuggest.Checked);
    }
}
