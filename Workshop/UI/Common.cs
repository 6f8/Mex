using System.Data;
using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>صفحة في النافذة الرئيسية (الرئيسية، الطلبات...) تُحدَّث عند تغيّر البيانات</summary>
public abstract class Page : BaseForm
{
    public abstract string Title { get; }
    public abstract string Desc { get; }
    public abstract string PageIcon { get; }
    /// <summary>إعادة عرض البيانات (بعد أي تعديل أو عند فتح الصفحة)</summary>
    public abstract void Reload();
}

/// <summary>أدوات بناء الحقول (بنفس أسلوب رصيد)</summary>
public static class W
{
    public static NumericUpDown Money(int width = 200)
    {
        var n = new NumericUpDown
        {
            Width = width, Minimum = 0, Maximum = 1_000_000_000_000m, DecimalPlaces = Store.Currency == "د.ع" ? 0 : 2,
            ThousandsSeparator = true, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10)
        };
        n.Enter += (s, e) => n.BeginInvoke(() => n.Select(0, n.Text.Length));
        return n;
    }

    public static void Set(NumericUpDown n, double v)
    {
        if (!double.IsFinite(v)) v = 0;
        decimal d = v >= (double)n.Maximum ? n.Maximum : v <= (double)n.Minimum ? n.Minimum : (decimal)v;
        if (n.Value != d) n.Value = d;
    }

    public static ComboBox Combo(int width, IEnumerable<string> items, bool editable = false)
    {
        var cb = new ComboBox { Width = width, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, Font = Theme.F(10) };
        cb.Items.AddRange(items.Cast<object>().ToArray());
        if (cb.Items.Count > 0 && !editable) cb.SelectedIndex = 0;
        return cb;
    }

    public static void Pick(ComboBox cb, string v)
    {
        int i = cb.Items.IndexOf(v);
        if (i >= 0) cb.SelectedIndex = i;
        else if (cb.DropDownStyle != ComboBoxStyle.DropDownList) cb.Text = v ?? "";
        else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    /// <summary>إكمال تلقائي من قائمة (أسماء الأجهزة، الموردين، الزبائن)</summary>
    public static void Suggest(TextBox t, IEnumerable<string> values)
    {
        var src = new AutoCompleteStringCollection();
        src.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().Take(3000).ToArray());
        t.AutoCompleteCustomSource = src;
        t.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        t.AutoCompleteSource = AutoCompleteSource.CustomSource;
    }

    public static Control Wrap(Control c, string icon = null) =>
        c is TextBox or NumericUpDown or ComboBox or DateTimePicker ? new InputBox(c, c.Width, icon) : c;

    /// <summary>حقل بعنوان صغير فوقه</summary>
    public static Control Labeled(string caption, Control c, string icon = null)
    {
        var field = Wrap(c, icon);
        var p = new Panel { Width = field.Width + 4, Height = field.Height + 23, Margin = new Padding(6, 1, 6, 3), BackColor = Theme.Surface };
        var l = new Label { Text = caption, Dock = DockStyle.Top, Height = 23, ForeColor = Theme.Text2, Font = Theme.F(9), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 2, 0) };
        field.Dock = DockStyle.Bottom;
        p.Controls.Add(field);
        p.Controls.Add(l);
        return p;
    }

    public static Label Head(string text, int width = 600) => new()
    {
        Text = text, AutoSize = false, Width = width, Height = 34, Font = Theme.FS(11), ForeColor = Theme.BrandDark,
        TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(6, 12, 6, 4), BackColor = Theme.Surface
    };

    public static Label Note(string text, int width = 600, int height = 24) => new()
    {
        Text = text, AutoSize = false, Width = width, Height = height, Font = Theme.F(9), ForeColor = Theme.Muted,
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 2, 6, 2), BackColor = Theme.Surface
    };

    public static ModernButton Btn(string text, string icon, BtnKind kind = BtnKind.Secondary, int min = 100)
    {
        var b = new ModernButton { Text = text, IconName = icon, Kind = kind, Height = 40, Margin = new Padding(4, 4, 4, 4) };
        b.FitWidth(min);
        return b;
    }

    public static ModernButton IconBtn(string icon, string tip, BtnKind kind = BtnKind.Secondary)
    {
        var b = new ModernButton { IconName = icon, Kind = kind, Size = new Size(40, 40), Margin = new Padding(3, 4, 3, 4) };
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    public static FlowLayoutPanel Flow(bool wrap = true) => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = wrap, BackColor = Theme.Surface, Margin = new Padding(0) };

    public static DataGridView Grid()
    {
        var g = new DataGridView { Dock = DockStyle.Fill };
        Theme.Grid(g, true);
        if (Store.Compact) g.RowTemplate.Height = S(32);
        return g;
    }

    public static string SaveFile(string filter, string name)
    {
        using var d = new SaveFileDialog { Filter = filter, FileName = name };
        return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
    }

    public static string OpenFile(string filter)
    {
        using var d = new OpenFileDialog { Filter = filter };
        return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
    }

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Warn("تعذّر الفتح: " + ex.Message); }
    }

    public static bool Copy(string text)
    {
        try { Clipboard.SetText(text ?? ""); Toast.Show("تم نسخ النص"); return true; }
        catch { Toast.Show("تعذّر النسخ", Tone.Warning); return false; }
    }

    public static bool Confirm(string title, string body, string ok = "تأكيد", bool danger = false) =>
        Dialogs.Confirm(body, title, ok, danger);

    /// <summary>سؤال بثلاثة خيارات: true / false / null (الخيار البديل)</summary>
    public static bool? Ask3(string title, string body, string ok, string alt, bool danger = false)
    {
        var r = Dialogs.Message(body, title, danger ? Tone.Danger : Tone.Info,
            (ok, DialogResult.Yes, danger ? BtnKind.Danger : BtnKind.Primary), (alt, DialogResult.Retry, BtnKind.Secondary), ("إلغاء", DialogResult.No, BtnKind.Ghost));
        return r == DialogResult.Yes ? true : r == DialogResult.Retry ? null : false;
    }
}

/// <summary>
/// جدول الطلبات الموحّد: الأعمدة حسب الإعدادات، شارات الحالة والدفع، الزبون والجهاز بسطرين،
/// ومدة العمل الحية. النقر المزدوج يفتح الطلب، والقائمة المختصرة فيها كل الإجراءات.
/// </summary>
public class OrdersGrid : DataGridView
{
    readonly HashSet<string> hidden;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 30_000 };
    List<Order> rows = new();
    public event Action<Order> OpenOrder;

    public OrdersGrid(params string[] hide)
    {
        hidden = hide.ToHashSet();
        Dock = DockStyle.Fill;
        Theme.Grid(this, true);
        if (Store.Compact) RowTemplate.Height = S(34); else RowTemplate.Height = S(46);
        BuildColumns();
        CellPainting += Paint2;
        CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            if (Columns[e.ColumnIndex].Name == "duration") { e.Value = Calc.DurationText(rows[e.RowIndex]); e.FormattingApplied = true; }
        };
        CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.RowIndex < rows.Count) OpenOrder?.Invoke(rows[e.RowIndex]); };
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && Selected != null) { e.Handled = true; OpenOrder?.Invoke(Selected); } };
        CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) CurrentCell = Rows[e.RowIndex].Cells[FirstVisible()]; };
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = Theme.F(10) };
        menu.Opening += (s, e) =>
        {
            menu.Items.Clear();
            var o = Selected;
            if (o == null) { e.Cancel = true; return; }
            menu.Items.Add("عرض الطلب", null, (_, _) => OpenOrder?.Invoke(o));
            menu.Items.Add("تعديل", null, (_, _) => Acts.Edit(o));
            var st = new ToolStripMenuItem("تغيير الحالة");
            foreach (var s2 in K.Statuses) { var item = new ToolStripMenuItem(s2, null, (_, _) => Acts.SetStatus(o, s2)) { Checked = o.Status == s2 }; st.DropDownItems.Add(item); }
            menu.Items.Add(st);
            if (Calc.RemainingOf(o) > 0) menu.Items.Add("تسجيل دفعة", null, (_, _) => QuickPayDialog.ForOrder(o));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("واتساب", null, (_, _) => Acts.WhatsApp(o));
            menu.Items.Add("طباعة الفاتورة", null, (_, _) => Printer.Invoice(o));
            menu.Items.Add("طباعة الملصق", null, (_, _) => Printer.Label(o));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("حذف (نقل إلى المحذوفات)", null, (_, _) => Acts.Delete(o));
        };
        ContextMenuStrip = menu;
        tick.Tick += (s, e) => { if (Visible && Columns.Contains("duration") && Columns["duration"].Visible) InvalidateColumn(Columns["duration"].Index); };
        tick.Start();
        Disposed += (s, e) => tick.Dispose();
    }

    int FirstVisible() => Columns.Cast<DataGridViewColumn>().First(c => c.Visible).Index;

    void Col(string name, string header, int weight, int min, bool right = false)
    {
        var c = new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = header, FillWeight = weight, MinimumWidth = S(min), SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleRight }
        };
        Columns.Add(c);
    }

    void BuildColumns()
    {
        Col("ref", "المرجع", 70, 86);
        Col("customer", "الزبون", 150, 130);
        Col("device", "الجهاز", 150, 120);
        Col("issue", "نوع العطل", 80, 90);
        Col("status", "الحالة", 100, 120);
        Col("tech", "الفني", 80, 90);
        Col("price", "السعر", 80, 90, true);
        Col("profit", "الربح", 80, 90, true);
        Col("payment", "الدفع", 110, 120);
        Col("received", "الاستلام", 70, 84);
        Col("estimated", "التسليم المتوقع", 80, 110);
        Col("duration", "مدة العمل", 80, 96);
        ApplyColumns();
    }

    public void ApplyColumns()
    {
        foreach (var (k, _) in K.TableCols)
            if (Columns.Contains(k)) Columns[k].Visible = Store.Col(k) && !hidden.Contains(k) && (k != "tech" || Techs.All.Count > 0);
    }

    public Order Selected => CurrentRow != null && CurrentRow.Index < rows.Count ? rows[CurrentRow.Index] : null;

    public void Fill(IEnumerable<Order> list)
    {
        string keep = Selected?.Id;
        rows = list.ToList();
        SuspendLayout();
        Rows.Clear();
        if (rows.Count > 0)
        {
            var arr = rows.Select(o => new DataGridViewRow()).ToArray();
            for (int i = 0; i < rows.Count; i++)
            {
                var o = rows[i];
                arr[i].CreateCells(this, o.RefNo, o.CustomerName, o.Device, o.IssueType, o.Status, o.Technician, Txt.Num(o.Price), Txt.Num(Calc.ProfitOf(o)),
                    o.PaymentStatus, Txt.FmtShortDate(o.DateReceived), Txt.FmtShortDate(o.DateEstimated), "");
                arr[i].Height = RowTemplate.Height;
            }
            Rows.AddRange(arr);
        }
        ResumeLayout();
        if (keep != null)
        {
            int i = rows.FindIndex(o => o.Id == keep);
            if (i >= 0) CurrentCell = Rows[i].Cells[FirstVisible()];
        }
        Invalidate();
    }

    void Paint2(object sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= rows.Count) return;
        var o = rows[e.RowIndex];
        var name = Columns[e.ColumnIndex].Name;
        if (name is not ("ref" or "customer" or "device" or "status" or "payment" or "profit")) return;
        e.PaintBackground(e.CellBounds, true);
        var g = e.Graphics;
        Gfx.Hq(g);
        var r = Rectangle.Inflate(e.CellBounds, -S(8), 0);
        bool two = RowTemplate.Height >= S(40);
        switch (name)
        {
            case "ref":
                Pal.Tag(g, o.RefNo, r.Right, r.Y + (r.Height - S(22)) / 2, S(22));
                break;
            case "customer":
            case "device":
                {
                    string main = name == "customer" ? o.CustomerName : o.Device;
                    string sub = name == "customer" ? (o.Phone == "" ? "—" : o.Phone) : Calc.IsLate(o) ? $"متأخر {Calc.LateDays(o)} يوم" : "";
                    var subColor = name == "device" ? Pal.Bad : Theme.Muted;
                    if (two && sub != "")
                    {
                        TextRenderer.DrawText(g, main, Theme.FS(9.5f), new Rectangle(r.X, r.Y + S(3), r.Width, r.Height / 2), Theme.Ink, Gfx.RtlStart | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(g, sub, Theme.F(8.5f), new Rectangle(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2 - S(3)), subColor, Gfx.RtlStart | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
                    }
                    else
                        TextRenderer.DrawText(g, sub != "" && name == "device" ? main + " • " + sub : main, Theme.F(9.5f), r, name == "device" && sub != "" ? Pal.Bad : Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
                    break;
                }
            case "status":
                Pal.Status(g, o.Status, r);
                break;
            case "payment":
                {
                    double rem = Calc.RemainingOf(o);
                    var (fg, bg) = K.StatusColors(o.PaymentStatus);
                    if (two && rem > 0)
                    {
                        Pal.Pill(g, o.PaymentStatus, new Rectangle(r.X, r.Y + S(2), r.Width, r.Height / 2), fg, bg);
                        TextRenderer.DrawText(g, (o.Status == K.Cancelled ? "أجرة فحص " : "باقي ") + Txt.Money(rem), Theme.F(8), new Rectangle(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2 - S(2)), Theme.Muted, Gfx.RtlStart);
                    }
                    else Pal.Pill(g, o.PaymentStatus, r, fg, bg);
                    break;
                }
            case "profit":
                {
                    double p = Calc.ProfitOf(o);
                    TextRenderer.DrawText(g, Txt.Num(p), Theme.F(9.5f), r, p >= 0 ? Pal.Good : Pal.Bad, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    break;
                }
        }
        e.Handled = true;
    }
}

/// <summary>
/// لوحة الطلبات (كانبان): عمود لكل حالة، والبطاقة تُسحب بالفأرة إلى عمود آخر لتغيير حالتها.
/// </summary>
public class KanbanBoard : Control
{
    List<Order> list = new();
    public event Action<Order> OpenOrder;
    public event Action<Order, string> Drop;
    Order drag; Point downAt; Point cur; bool dragging; int overCol = -1;
    const int MaxCards = 60;

    public KanbanBoard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    int ColW => S(236);
    int Gap => S(12);
    int CardH => S(98);
    int HeadH => S(46);

    public void Set(IEnumerable<Order> l)
    {
        list = l.ToList();
        int maxCards = K.Statuses.Max(s => Math.Min(MaxCards, list.Count(o => o.Status == s)) + (list.Count(o => o.Status == s) > MaxCards ? 1 : 0));
        var size = new Size(K.Statuses.Length * (ColW + Gap), HeadH + Math.Max(1, maxCards) * (CardH + S(8)) + S(20));
        if (Size != size) Size = size;
        Invalidate();
    }

    Rectangle ColRect(int c) => new(Width - (c + 1) * (ColW + Gap) + Gap / 2, 0, ColW, Height);
    Rectangle CardRect(int c, int i) { var col = ColRect(c); return new Rectangle(col.X + S(8), HeadH + i * (CardH + S(8)), col.Width - S(16), CardH); }

    (int Col, Order O) Hit(Point p)
    {
        for (int c = 0; c < K.Statuses.Length; c++)
        {
            if (!ColRect(c).Contains(p)) continue;
            var items = list.Where(o => o.Status == K.Statuses[c]).Take(MaxCards).ToList();
            for (int i = 0; i < items.Count; i++) if (CardRect(c, i).Contains(p)) return (c, items[i]);
            return (c, null);
        }
        return (-1, null);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var (_, o) = Hit(e.Location);
        drag = o; downAt = e.Location; dragging = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        cur = e.Location;
        if (drag != null && e.Button == MouseButtons.Left)
        {
            if (!dragging && (Math.Abs(e.X - downAt.X) > S(6) || Math.Abs(e.Y - downAt.Y) > S(6))) { dragging = true; Capture = true; }
            if (dragging) { overCol = Hit(e.Location).Col; Invalidate(); }
        }
        Cursor = Hit(e.Location).O != null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var o = drag;
        bool was = dragging;
        drag = null; dragging = false; Capture = false;
        int col = overCol; overCol = -1;
        Invalidate();
        if (o == null) return;
        if (!was) { OpenOrder?.Invoke(o); return; }
        if (col >= 0 && K.Statuses[col] != o.Status) Drop?.Invoke(o, K.Statuses[col]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        Gfx.Hq(g);
        for (int c = 0; c < K.Statuses.Length; c++)
        {
            var s = K.Statuses[c];
            var col = ColRect(c);
            Gfx.FillRound(g, col, S(12f), c == overCol && dragging ? Theme.BrandSoft : Theme.SurfaceAlt);
            Gfx.DrawRound(g, col, S(12f), c == overCol && dragging ? Theme.Brand : Theme.Border);
            var items = list.Where(o => o.Status == s).ToList();
            Pal.Status(g, s, new Rectangle(col.X + S(40), S(10), col.Width - S(52), S(26)));
            TextRenderer.DrawText(g, items.Count.ToString(), Theme.FS(9.5f), new Rectangle(col.X + S(10), S(10), S(30), S(26)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, "لا طلبات", Theme.F(9), new Rectangle(col.X, HeadH, col.Width, S(40)), Theme.Subtle, Gfx.Center);
                continue;
            }
            int i = 0;
            foreach (var o in items.Take(MaxCards))
            {
                var r = CardRect(c, i++);
                bool isDrag = dragging && o == drag;
                Gfx.FillRound(g, r, S(10f), isDrag ? Theme.GraySoft : Theme.Surface);
                Gfx.DrawRound(g, r, S(10f), Calc.IsLate(o) ? Gfx.Mix(Pal.Bad, Color.White, 0.45f) : Theme.Border);
                int pad = S(10);
                int tw = Pal.Tag(g, o.RefNo, r.X + S(10) + TextRenderer.MeasureText(g, o.RefNo, Theme.FS(8.5f), Size.Empty, TextFormatFlags.NoPadding).Width + S(14), r.Y + S(8), S(22));
                TextRenderer.DrawText(g, o.CustomerName, Theme.FS(9.5f), new Rectangle(r.X + pad + tw + S(6), r.Y + S(6), r.Width - 2 * pad - tw - S(6), S(26)), Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, o.Device, Theme.F(9), new Rectangle(r.X + pad, r.Y + S(34), r.Width - 2 * pad, S(22)), Theme.Muted, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, o.Technician != "" ? o.IssueType + " • " + o.Technician : o.IssueType, Theme.F(8.5f), new Rectangle(r.X + r.Width / 2, r.Y + S(60), r.Width / 2 - pad, S(24)), Theme.Text2, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, Calc.IsLate(o) ? $"متأخر {Calc.LateDays(o)} يوم" : Txt.Money(o.Price), Theme.FS(9), new Rectangle(r.X + pad, r.Y + S(60), r.Width / 2 - pad, S(24)),
                    Calc.IsLate(o) ? Pal.Bad : Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            if (items.Count > MaxCards)
                TextRenderer.DrawText(g, $"و {items.Count - MaxCards} طلب آخر — استخدم البحث", Theme.F(8.5f), new Rectangle(col.X, HeadH + MaxCards * (CardH + S(8)), col.Width, S(30)), Theme.Subtle, Gfx.Center);
        }
        if (dragging && drag != null)
        {
            var ghost = new Rectangle(cur.X - ColW / 2 + S(8), cur.Y - CardH / 2, ColW - S(16), CardH);
            Gfx.Shadow(g, ghost, S(10f));
            Gfx.FillRound(g, ghost, S(10f), Gfx.Alpha(Theme.Surface, 235));
            Gfx.DrawRound(g, ghost, S(10f), Theme.Brand, S(1.5f));
            TextRenderer.DrawText(g, drag.CustomerName, Theme.FS(9.5f), new Rectangle(ghost.X + S(10), ghost.Y + S(8), ghost.Width - S(20), S(24)), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g, drag.Device, Theme.F(9), new Rectangle(ghost.X + S(10), ghost.Y + S(34), ghost.Width - S(20), S(22)), Theme.Muted, Gfx.RtlStart);
        }
    }
}
