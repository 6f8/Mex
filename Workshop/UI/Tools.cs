using Raseed;
using static Raseed.Dpi;

namespace Workshop;

// ============================== تقفيل اليوم ==============================
public class CloseDayDialog : DialogShell
{
    readonly DateTimePicker date = new() { Width = 200, Format = DateTimePickerFormat.Long };
    Control content;

    CloseDayDialog() : base("تقفيل اليوم", 900, 760, "receipt")
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Surface, WrapContents = false };
        top.Controls.Add(W.Labeled("اليوم", date, "calendar"));
        date.Value = DateTime.Today;
        content = Build();
        Body.Controls.Add(content);
        Body.Controls.Add(top);
        date.ValueChanged += (s, e) =>
        {
            Body.SuspendLayout();
            Body.Controls.Remove(content);
            content.Dispose();
            content = Dpi.Fit(Body, Build());
            Body.Controls.Add(content);
            content.BringToFront();
            Body.ResumeLayout();
        };
        AddButton("طباعة", DialogResult.None, BtnKind.Primary, "printer").Click += (s, e) => Print();
        AddButton("إرسال لنفسي بالواتساب", DialogResult.None, BtnKind.Success, "message-circle").Click += (s, e) =>
        {
            var c = Calc.Close(Txt.Iso(date.Value));
            if (Store.ShopPhone == "") Toast.Show("أضف هاتف المحل من الإعدادات ليُرسل التقفيل إلى رقمك مباشرة.", Tone.Info);
            using var d = new WaDialog(Store.ShopName, Store.ShopPhone, new() { new("close", "التقفيل", Calc.CloseText(c)) }, 0);
            d.ShowModal();
        };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
    }

    public static void Open()
    {
        using var d = new CloseDayDialog();
        d.ShowModal();
    }

    Control Build()
    {
        var c = Calc.Close(Txt.Iso(date.Value));
        Text = "تقفيل يوم " + Txt.FmtLong(date.Value);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        var ledger = new Ledger { Width = 820, Height = 100, MinCell = 180, Margin = new Padding(4) };
        ledger.Set(new[]
        {
            new Ledger.Cell("المقبوض", Txt.Money(c.CashIn), c.RefundTotal > 0 ? $"{c.Payments.Count} دفعة — بعد إرجاع {Txt.Money(c.RefundTotal)}" : $"{c.Payments.Count} دفعة", Pal.Good),
            new Ledger.Cell("المصاريف ودفعات الموردين", Txt.Money(c.ExpTotal + c.SupPaid), $"مصاريف {Txt.Money(c.ExpTotal)} — موردون {Txt.Money(c.SupPaid)}", Pal.Bad),
            new Ledger.Cell("صافي حركة النقد", Txt.Money(c.Drawer), "النقد المقبوض ناقص ما دُفع", null, c.Drawer >= 0 ? 1 : -1),
            new Ledger.Cell("صافي ربح اليوم", Txt.Money(c.Profit), "إيراد " + Txt.Money(c.Revenue), null, c.Profit >= 0 ? 1 : -1),
        });
        flow.Controls.Add(ledger);
        Label Line(string text, Color? col = null, bool bold = false) => new()
        {
            Text = text, AutoSize = false, Width = 820, Height = 30, Font = bold ? Theme.FS(10) : Theme.F(10), ForeColor = col ?? Theme.Text2,
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface, Margin = new Padding(8, 0, 8, 0), AutoEllipsis = true
        };
        void Section(string t) => flow.Controls.Add(W.Head(t, 820));
        Section("المقبوض حسب طريقة الدفع");
        if (c.Payments.Count == 0 && c.Refunds.Count == 0) flow.Controls.Add(Line("لا توجد دفعات في هذا اليوم", Theme.Subtle));
        foreach (var (k, v) in c.Methods.Where(x => x.Value != 0)) flow.Controls.Add(Line($"{k}:   {Txt.Money(v)}" + (c.CashIn > 0 ? $"   ({Math.Round(v * 100 / c.CashIn)}%)" : "")));
        Section("حركة الأجهزة");
        flow.Controls.Add(Line($"استُلم: {c.Received.Count}      سُلّم: {c.Delivered.Count}      أُلغي: {c.Cancelled.Count}"));
        flow.Controls.Add(Line("ديون جديدة من تسليمات اليوم: " + Txt.Money(c.NewDebt), c.NewDebt > 0 ? Pal.Bad : null));
        Section("الدفعات");
        if (c.Payments.Count == 0) flow.Controls.Add(Line("لا شيء", Theme.Subtle));
        foreach (var (o, p) in c.Payments) flow.Controls.Add(Link(Line($"{o.RefNo}   {o.CustomerName}   ·  {p.Method}   —   {Txt.Money(p.Amount)}"), o));
        if (c.Refunds.Count > 0)
        {
            Section("مبالغ أُرجعت للزبائن");
            foreach (var (o, p) in c.Refunds) flow.Controls.Add(Link(Line($"{o.RefNo}   {o.CustomerName}   ·  {p.Method}   —   {Txt.Money(-p.Amount)}   ({p.Note.Replace("استرجاع: ", "")})", Pal.Bad), o));
        }
        Section("المصاريف");
        if (c.Exps.Count == 0) flow.Controls.Add(Line("لا شيء", Theme.Subtle));
        foreach (var e in c.Exps) flow.Controls.Add(Line($"{e.Description}   —   {Txt.Money(e.Amount)}"));
        Section("المُسلّمة");
        if (c.Delivered.Count == 0) flow.Controls.Add(Line("لا شيء", Theme.Subtle));
        foreach (var o in c.Delivered) flow.Controls.Add(Link(Line($"{o.RefNo}   {o.CustomerName} — {o.Device}   —   {Txt.Money(o.Price)}", Calc.RemainingOf(o) > 0 ? Pal.Bad : null), o));
        return flow;
    }

    static Label Link(Label l, Order o)
    {
        l.Cursor = Cursors.Hand;
        l.Click += (s, e) => Acts.View(o);
        return l;
    }

    void Print()
    {
        var c = Calc.Close(Txt.Iso(date.Value));
        var sb = new System.Text.StringBuilder(Printer.Header("تقفيل يوم " + Txt.FmtDate(c.D)));
        sb.Append(Printer.Row("المقبوض", Txt.Money(c.CashIn + c.RefundTotal)));
        if (c.RefundTotal > 0) sb.Append(Printer.Row("مُرجَع للزبائن", Txt.Money(c.RefundTotal), "bad"));
        foreach (var (k, v) in c.Methods.Where(x => x.Value != 0)) sb.Append(Printer.Row("&nbsp;&nbsp;— " + Txt.Esc(k), Txt.Money(v)));
        sb.Append(Printer.Row("المصاريف", Txt.Money(c.ExpTotal)));
        if (c.SupPaid > 0) sb.Append(Printer.Row("دفعات الموردين", Txt.Money(c.SupPaid)));
        sb.Append(Printer.Row("صافي حركة النقد", Txt.Money(c.Drawer), c.Drawer >= 0 ? "good" : "bad"));
        sb.Append(Printer.Row("أجهزة مستلمة / مسلّمة / ملغاة", $"{c.Received.Count} / {c.Delivered.Count} / {c.Cancelled.Count}"));
        sb.Append(Printer.Row("الإيراد", Txt.Money(c.Revenue)));
        sb.Append(Printer.Row("تكلفة القطع", Txt.Money(c.Parts)));
        if (c.Loss > 0) sb.Append(Printer.Row("خسائر الملغاة", Txt.Money(c.Loss)));
        sb.Append($"<div class=\"row total\"><span>صافي ربح اليوم</span><span class=\"{(c.Profit >= 0 ? "good" : "bad")}\">{Txt.Esc(Txt.Money(c.Profit))}</span></div>");
        if (c.Payments.Count > 0)
        {
            sb.Append("<h3 style=\"font-size:14px;margin-top:16px\">الدفعات</h3>");
            foreach (var (o, p) in c.Payments) sb.Append(Printer.Row($"{Txt.Esc(o.RefNo)} {Txt.Esc(o.CustomerName)} — {Txt.Esc(p.Method)}", Txt.Money(p.Amount)));
        }
        Printer.Doc(sb.ToString(), "تقفيل " + c.D, "560px");
    }
}

// ============================== المحذوفات ==============================
public class TrashDialog : DialogShell
{
    readonly DataGridView grid = W.Grid();
    List<Order> rows = new();

    TrashDialog() : base("الطلبات المحذوفة", 860, 600, "trash-2", Pal.Bad)
    {
        grid.Columns.Add("ref", "المرجع");
        grid.Columns.Add("name", "الزبون");
        grid.Columns.Add("device", "الجهاز");
        grid.Columns.Add("at", "حُذف في");
        Body.Controls.Add(grid);
        Body.Controls.Add(W.Note("الطلبات المحذوفة تبقى هنا حتى تحذفها نهائياً. الاستعادة تعيد الطلب وقطعه إلى المخزون.", 800, 30));
        AddButton("استعادة", DialogResult.None, BtnKind.Primary, "rotate-ccw").Click += (s, e) => { if (Sel is Order o) { Acts.Restore(o.Id); Render(); } };
        AddButton("حذف نهائي", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) =>
        {
            if (Sel is not Order o) return;
            if (!W.Confirm("حذف نهائي؟", $"{o.RefNo} — {o.CustomerName}\nلا يمكن التراجع عن هذا.", "حذف نهائياً", true)) return;
            Store.PurgeTrash(o.Id);
            Store.NotifyChanged();
            Render();
        };
        AddButton("إفراغ السلة", DialogResult.None, BtnKind.Ghost, "ban").Click += (s, e) =>
        {
            if (Store.Trash.Count == 0) return;
            if (!W.Confirm($"حذف {Store.Trash.Count} طلب نهائياً؟", "لا يمكن التراجع عن هذا.", "إفراغ السلة", true)) return;
            foreach (var o in Store.Trash.ToList()) Store.PurgeTrash(o.Id);
            Store.NotifyChanged();
            Render();
        };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        Render();
    }

    public static void Open() { using var d = new TrashDialog(); d.ShowModal(); }

    Order Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    void Render()
    {
        rows = Store.Trash.ToList();
        grid.Rows.Clear();
        foreach (var o in rows) grid.Rows.Add(o.RefNo, o.CustomerName, o.Device, Txt.FmtDate(Txt.Cut10(o.DeletedAt ?? "")));
    }
}

// ============================== أعمدة جدول الطلبات ==============================
public static class ColumnsDialog
{
    public static void Open()
    {
        using var d = new DialogShell("الأعمدة الظاهرة في الجدول", 460, 560, "layout-list");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        var toggles = new List<(string Key, Toggle T)>();
        foreach (var (k, title) in K.TableCols)
        {
            var t = new Toggle { Text = title, Width = 380, Height = 34, Checked = Store.Col(k), Margin = new Padding(6, 3, 6, 3) };
            toggles.Add((k, t));
            flow.Controls.Add(t);
        }
        d.Body.Controls.Add(flow);
        d.AddButton("تم", DialogResult.OK, BtnKind.Primary, "check");
        d.AddButton("إظهار الكل", DialogResult.None, BtnKind.Secondary).Click += (s, e) => { foreach (var (_, t) in toggles) t.Checked = true; };
        if (d.ShowModal() != DialogResult.OK) return;
        foreach (var (k, t) in toggles) Store.SetFlag("col_" + k, t.Checked);
        Store.NotifyChanged();
    }
}

// ============================== تعريفات الطابعات ==============================
public class DriversDialog : DialogShell
{
    record Brand(string Name, string[] Keys, string[] Sites, string Note = null);
    static readonly Brand[] Brands =
    {
        new("Canon", new[] { "canon", "كانون" }, new[] { "canon-me.com", "canon-europe.com", "usa.canon.com" }),
        new("HP", new[] { "hp", "hb", "hewlett", "packard", "اتش", "hpe" }, new[] { "support.hp.com" }),
        new("Epson", new[] { "epson", "ابسون", "إبسون" }, new[] { "epson.com", "epson.eu" }),
        new("Brother", new[] { "brother", "براذر" }, new[] { "support.brother.com" }),
        new("Samsung", new[] { "samsung", "سامسونج" }, new[] { "support.hp.com" }, "تعريفات طابعات سامسونج انتقلت إلى موقع HP."),
        new("Xerox", new[] { "xerox", "زيروكس" }, new[] { "support.xerox.com" }),
        new("Ricoh", new[] { "ricoh", "ريكو" }, new[] { "ricoh.com" }),
        new("Kyocera", new[] { "kyocera", "كيوسيرا" }, new[] { "kyoceradocumentsolutions.com" }),
        new("Lexmark", new[] { "lexmark", "لكسمارك" }, new[] { "lexmark.com" }),
        new("Pantum", new[] { "pantum", "بانتوم" }, new[] { "pantum.com" }),
        new("Konica Minolta", new[] { "konica", "minolta", "كونيكا" }, new[] { "konicaminolta.com" }),
        new("Sharp", new[] { "sharp", "شارب" }, new[] { "sharp.com" }),
        new("OKI", new[] { "oki" }, new[] { "oki.com" }),
        new("Toshiba", new[] { "toshiba", "توشيبا" }, new[] { "toshiba.com" }),
        new("Dell", new[] { "dell", "ديل" }, new[] { "dell.com" }),
    };
    static readonly (string Label, string Value)[] Oses = { ("Windows 11 / 10", "Windows 11"), ("Windows 7", "Windows 7"), ("macOS", "macOS"), ("Linux", "Linux") };

    readonly TextBox q = new() { Width = 420, PlaceholderText = "اسم الطابعة والموديل، مثل: Canon GX6040" };
    readonly ComboBox os = W.Combo(180, Oses.Select(o => o.Label));
    readonly FlowLayoutPanel result = new() { Width = 820, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
    readonly DataGridView lib = W.Grid();
    readonly Label note = W.Note("", 820, 30);
    readonly List<LinkLabel> links = new();
    List<Driver> rows = new();

    DriversDialog() : base("تعريفات الطابعات", 900, 760, "printer")
    {
        var top = W.Flow();
        top.Controls.Add(W.Labeled("الطابعة", q, "search"));
        top.Controls.Add(W.Labeled("نظام التشغيل", os));
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        flow.Controls.Add(top);
        flow.Controls.Add(note);
        for (int i = 0; i < 4; i++)
        {
            var l = new LinkLabel { AutoSize = false, Width = 820, Height = 30, Font = Theme.F(10), TextAlign = ContentAlignment.MiddleLeft, LinkColor = Theme.Brand, Visible = false, Margin = new Padding(8, 0, 8, 0) };
            l.LinkClicked += (s, e) => { if (l.Tag is string u) W.OpenUrl(u); };
            links.Add(l);
            flow.Controls.Add(l);
        }
        flow.Controls.Add(W.Head("مكتبة التعريفات المحفوظة", 820));
        var host = new Panel { Width = 830, Height = 300, BackColor = Theme.Surface };
        lib.Columns.Add("printer", "الطابعة");
        lib.Columns.Add("info", "الشركة — النظام — ملاحظة");
        lib.Columns.Add("url", "الرابط");
        host.Controls.Add(lib);
        flow.Controls.Add(host);
        Body.Controls.Add(flow);
        AddButton("حفظ رابط تعريف", DialogResult.None, BtnKind.Primary, "plus").Click += (s, e) => EditDriver(null);
        AddButton("تحميل", DialogResult.None, BtnKind.Success, "download").Click += (s, e) => { if (Sel?.Url is string u && u != "") W.OpenUrl(u); };
        AddButton("تعديل", DialogResult.None, BtnKind.Secondary, "pencil").Click += (s, e) => { if (Sel != null) EditDriver(Sel); };
        AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) =>
        {
            if (Sel is not Driver d || !W.Confirm("حذف هذا التعريف من المكتبة؟", d.Printer, "حذف", true)) return;
            Store.DeleteDriver(d);
            Render();
            Toast.Show("حُذف التعريف");
        };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        lib.CellDoubleClick += (s, e) => { if (Sel?.Url is string u && u != "") W.OpenUrl(u); };
        Ui2.OnIdle(q, Render, 200);
        os.SelectedIndexChanged += (s, e) => Render();
        Render();
        Shown += (s, e) => q.Focus();
    }

    public static void Open() { using var d = new DriversDialog(); d.ShowModal(); }

    Driver Sel => lib.CurrentRow != null && lib.CurrentRow.Index < rows.Count ? rows[lib.CurrentRow.Index] : null;

    static int Lev(string a, string b)
    {
        int m = a.Length, n = b.Length;
        if (Math.Abs(m - n) > 2) return 9;
        var prev = Enumerable.Range(0, n + 1).ToArray();
        for (int i = 1; i <= m; i++)
        {
            var cur = new int[n + 1]; cur[0] = i;
            for (int j = 1; j <= n; j++) cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[n];
    }

    /// <summary>يتعرف على الشركة حتى مع الخطأ الإملائي («canan gx6040» ← Canon GX6040)</summary>
    static (Brand Brand, string Full, bool Fixed) Detect(string query)
    {
        var raw = query.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var tokens = Txt.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            foreach (var b in Brands)
            {
                bool exact = b.Keys.Contains(t);
                bool near = !exact && t.Length >= 4 && b.Keys.Any(k => k.Length >= 4 && Lev(t, k) <= (k.Length >= 6 ? 2 : 1));
                if (exact || near)
                {
                    var model = string.Join(" ", raw.Where((_, j) => j != i)).ToUpperInvariant();
                    return (b, (b.Name + " " + model).Trim(), near);
                }
            }
        }
        return (null, query.Trim(), false);
    }

    void Render()
    {
        var text = q.Text.Trim();
        var osv = Oses[Math.Max(0, os.SelectedIndex)].Value;
        var det = Detect(text);
        foreach (var l in links) l.Visible = false;
        string G(string s) => "https://www.google.com/search?q=" + Uri.EscapeDataString(s);
        void Link(int i, string url, string title) { links[i].Text = title; links[i].Tag = url; links[i].Visible = true; }
        if (text == "") { note.Text = "اكتب اسم الطابعة لتظهر روابط التعريف من الموقع الرسمي."; note.ForeColor = Theme.Muted; }
        else if (det.Brand != null)
        {
            var sites = string.Join(" OR ", det.Brand.Sites.Select(x => "site:" + x));
            note.Text = (det.Fixed ? $"هل تقصد {det.Full}؟   " : "") + "الشركة: " + det.Brand.Name + (det.Brand.Note != null ? " — " + det.Brand.Note : "");
            note.ForeColor = Theme.BrandDark;
            Link(0, G($"{det.Full} driver {osv} {sites}"), "🔎  الموقع الرسمي للشركة — بحث داخل موقع " + det.Brand.Name + " فقط");
            Link(1, G($"{det.Full} full driver download {osv}"), "🔎  بحث عام عن التعريف — الحزمة الكاملة مع السكانر");
            Link(2, "https://www.bing.com/search?q=" + Uri.EscapeDataString($"{det.Full} driver {osv} {sites}"), "🔎  بحث بديل (Bing) — إذا لم تظهر نتيجة في Google");
            Link(3, "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString($"{det.Full} driver install"), "▶  فيديو طريقة التعريف — YouTube");
        }
        else
        {
            note.Text = "لم أتعرف على الشركة. اكتب اسمها قبل الموديل، مثل: Canon GX6040 أو HP LaserJet M111w.";
            note.ForeColor = Pal.AmberInk;
            Link(0, G($"{text} printer driver {osv}"), "🔎  بحث عام عن التعريف — " + text);
        }
        var words = Txt.Fold(det.Full).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        rows = Store.Drivers.Where(d => words.Length == 0 || words.All(w => Txt.Fold(string.Join(" ", d.Printer, d.Brand, d.Os, d.Note)).Contains(w)))
            .OrderBy(d => d.Printer, StringComparer.CurrentCulture).ToList();
        lib.Rows.Clear();
        foreach (var d in rows) lib.Rows.Add(d.Printer, string.Join(" — ", new[] { d.Brand, d.Os, d.Note }.Where(x => x != "")), d.Url);
    }

    void EditDriver(Driver d)
    {
        var det = Detect(q.Text);
        using var dlg = new DialogShell(d == null ? "حفظ رابط تعريف" : "تعديل التعريف", 540, 560, "printer");
        var tPrinter = new TextBox { Width = 460, Text = d?.Printer ?? det.Full };
        var tBrand = new TextBox { Width = 220, Text = d?.Brand ?? det.Brand?.Name ?? "" };
        var tOs = new TextBox { Width = 220, Text = d?.Os ?? Oses[Math.Max(0, os.SelectedIndex)].Label };
        var tUrl = new TextBox { Width = 460, Text = d?.Url ?? "", PlaceholderText = "https://..." };
        var tNote = new TextBox { Width = 460, Text = d?.Note ?? "" };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Labeled("الطابعة *", tPrinter));
        var r = W.Flow();
        r.Controls.Add(W.Labeled("الشركة", tBrand));
        r.Controls.Add(W.Labeled("النظام", tOs));
        flow.Controls.Add(r);
        flow.Controls.Add(W.Labeled("رابط التحميل *", tUrl, "link"));
        flow.Controls.Add(W.Labeled("ملاحظة", tNote));
        dlg.Body.Controls.Add(flow);
        var ok = dlg.AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        dlg.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var url = tUrl.Text.Trim();
            bool okUrl = Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
            if (tPrinter.Text.Trim() == "" || !okUrl) { Dialogs.Warn("اكتب اسم الطابعة ورابطاً يبدأ بـ https://"); return; }
            Store.SaveDriver(new Driver { Id = d?.Id ?? Txt.Uid("drv"), Printer = tPrinter.Text.Trim(), Brand = tBrand.Text.Trim(), Os = tOs.Text.Trim(), Url = url, Note = tNote.Text.Trim(), UpdatedAt = Txt.Now });
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
            Toast.Show("حُفظ التعريف في المكتبة");
        };
        if (dlg.ShowModal() == DialogResult.OK) Render();
    }
}

// ============================== البحث السريع (Ctrl+K) ==============================
public class PaletteDialog : BaseForm
{
    record Item(string Group, string Title, string Sub, string Icon, Action Run);
    readonly TextBox q = new() { Width = 560, PlaceholderText = "ابحث عن طلب أو زبون أو أمر..." };
    readonly ListBox list = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Theme.Surface };
    List<Item> items = new();
    public Action Chosen { get; private set; }

    public PaletteDialog()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 520);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
        KeyPreview = true;
        list.ItemHeight = S(50);
        list.DrawItem += DrawItem;
        var box = new InputBox(q, 600, "search") { Dock = DockStyle.Top, Height = 48 };
        Controls.Add(list);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Surface });
        Controls.Add(box);
        Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 28, Text = "↑ ↓ للتنقل   •   Enter للفتح   •   Esc للإغلاق", ForeColor = Theme.Subtle, Font = Theme.F(8.5f), TextAlign = ContentAlignment.MiddleCenter });
        q.TextChanged += (s, e) => Render();
        list.DoubleClick += (s, e) => Accept();
        list.MouseClick += (s, e) => Accept();
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { if (list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { if (list.SelectedIndex > 0) list.SelectedIndex--; e.Handled = true; }
        };
        Deactivate += (s, e) => { if (Chosen == null) Close(); };
        Render();
        Shown += (s, e) => q.Focus();
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var p = new Pen(Theme.BorderStrong); e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }

    void Render()
    {
        var f = Txt.Fold(q.Text);
        var main = MainForm.Instance;
        var cmds = new List<Item>
        {
            new("أوامر", "طلب صيانة جديد", "Ctrl+N", "plus", () => Acts.New()),
            new("أوامر", "الرئيسية", null, "house", () => main?.Go("dashboard")),
            new("أوامر", "الطلبات", null, "clipboard-list", () => main?.Go("orders")),
            new("أوامر", "الديون المستحقة", null, "wallet", () => main?.Go("debts")),
            new("أوامر", "الزبائن", null, "users", () => main?.Go("customers")),
            new("أوامر", "قطع الغيار والأسعار", null, "package", () => main?.Go("inventory")),
            new("أوامر", "التقارير والمصاريف", null, "chart-column", () => main?.Go("reports")),
            new("أوامر", "حسابات الموردين", null, "store", () => main?.Go("suppliers")),
            new("أوامر", "تقفيل اليوم", null, "receipt", CloseDayDialog.Open),
            new("أوامر", "تعريفات الطابعات", null, "printer", DriversDialog.Open),
            new("أوامر", "المحذوفات", null, "trash-2", TrashDialog.Open),
            new("أوامر", "القطع المعيبة ومرتجعات الموردين", null, "triangle-alert", DefectsDialog.Open),
            new("أوامر", "التراجع عن آخر عملية", "Ctrl+Z", "rotate-ccw", UndoUi.Run),
            new("أوامر", "تقرير الفروع المجمّع", null, "store", BranchReportDialog.Open),
            new("أوامر", "التذكيرات", null, "bell", RemindersDialog.Open),
            new("أوامر", "شاشة الفني", null, "wrench", () => main?.Go("tech")),
            new("أوامر", "الموظفون والرواتب", null, "id-card", () => main?.Go("staff")),
            new("أوامر", "تسجيل حضور الموظفين", null, "check", AttendanceDialog.Open),
            new("أوامر", "اقتراح كميات الشراء", null, "package", ReorderDialog.Open),
            new("أوامر", "طلبات بانتظار الموافقة منذ مدة", null, "clock", StaleDialog.Open),
            new("أوامر", "حسابات التجار والشركات", null, "briefcase", () => main?.Go("accounts")),
            new("أوامر", "التقرير اليومي والتنبيهات", null, "send", () => SettingsDialog.Open("notify")),
            new("أوامر", "الفنيون والعمولات", null, "wrench", () => SettingsDialog.Open("techs")),
            new("أوامر", "تعديل القوائم (الأعطال، الملحقات، الضمان...)", null, "list", () => SettingsDialog.Open("lists")),
            new("أوامر", "تعديل رسائل واتساب", null, "message-circle", () => SettingsDialog.Open("messages")),
            new("أوامر", "نسخة احتياطية الآن", null, "database", () => main?.BackupNow()),
            new("أوامر", "الصناديق والمسحوبات", null, "wallet-cards", () => main?.Go("boxes")),
            new("أوامر", "تحويل بين صندوقين", null, "arrow-left-right", MoneyDialog.Transfer),
            new("أوامر", "مسحوبات صاحب المحل", null, "log-out", MoneyDialog.Withdraw),
            new("أوامر", "رقم الدور وشاشة الانتظار", null, "list-ordered", QueueDialog.Open),
            new("أوامر", "استيراد قائمة أسعار المورد", null, "download", PriceImportDialog.Open),
            new("أوامر", "أجهزة القطع (سكراب)", null, "boxes", ScrapDialog.Open),
            new("أوامر", "أدوات الورشة وصيانتها", null, "wrench", ToolsDialog.Open),
            new("أوامر", "نقاط الولاء والإحالات والضمان الممتد", null, "gift", () => SettingsDialog.Open("sales")),
            new("أوامر", "الهاتف: لوحة صاحب المحل وشاشة الزبون", null, "smartphone", () => SettingsDialog.Open("web")),
            new("أوامر", "الإعدادات", null, "settings", SettingsDialog.Open),
        }.Where(c => f == "" || Txt.Fold(c.Title).Contains(f)).ToList();
        var res = new List<Item>();
        if (f != "")
        {
            res.AddRange(Store.Orders.Where(o => Txt.Matches(Calc.Haystack(o), f)).Take(7)
                .Select(o => new Item("طلبات", $"{o.CustomerName} — {o.Device}", $"{o.RefNo} · {o.Status}", "clipboard-list", () => Acts.View(o))));
            res.AddRange(Calc.GetCustomers().Where(c => Txt.Fold(c.Name + " " + c.Phone).Contains(f)).Take(4)
                .Select(c => new Item("زبائن", c.Name, c.Phone, "user", () => CustomerDialog.Open(c.Key))));
        }
        res.AddRange(cmds.Take(f != "" ? 4 : 27));
        items = res;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var i in items) list.Items.Add(i.Title);
        if (items.Count > 0) list.SelectedIndex = 0;
        list.EndUpdate();
    }

    void DrawItem(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        var it = items[e.Index];
        var g = e.Graphics;
        Gfx.Hq(g);
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, e.Bounds);
        var r = Rectangle.Inflate(e.Bounds, -S(2), -S(2));
        if (sel) Gfx.FillRound(g, r, S(10f), Theme.BrandSoft);
        var ir = new RectangleF(r.Right - S(44), r.Y + (r.Height - S(34)) / 2f, S(34), S(34));
        Gfx.FillRound(g, ir, S(9f), sel ? Theme.Surface : Theme.SurfaceAlt);
        Icons.Draw(g, it.Icon, ir, sel ? Theme.Brand : Theme.Muted, 17);
        var gs = TextRenderer.MeasureText(it.Group, Theme.F(8)).Width + S(14);
        var gr = new Rectangle(r.X + S(8), r.Y + (r.Height - S(20)) / 2, gs, S(20));
        Gfx.FillRound(g, gr, S(10f), sel ? Theme.Surface : Theme.GraySoft);
        TextRenderer.DrawText(g, it.Group, Theme.F(8), gr, Theme.Muted, Gfx.Center);
        int tx = gr.Right + S(10), tw = (int)ir.X - S(10) - tx;
        if (it.Sub != null)
        {
            TextRenderer.DrawText(g, it.Title, Theme.FS(10), new Rectangle(tx, r.Y + S(2), tw, S(24)), Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, it.Sub, Theme.F(8.5f), new Rectangle(tx, r.Y + S(24), tw, S(20)), Theme.Muted, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
        }
        else TextRenderer.DrawText(g, it.Title, Theme.FS(10), new Rectangle(tx, r.Y, tw, r.Height), Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
    }

    void Accept()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= items.Count) return;
        Chosen = items[list.SelectedIndex].Run;
        DialogResult = DialogResult.OK;
        Close();
    }
}
