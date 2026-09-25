using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>
/// رسم الجهاز (الوجه الأمامي والخلفي): النقر يضيف علامة خدش أو كسر مرقّمة، والنقر بالزر الأيمن على علامة يحذفها.
/// </summary>
public class DamageMap : Control
{
    public List<Mark> Marks { get; set; } = new();
    public bool ReadOnly { get; set; }
    public event Action Changed;
    readonly ToolTip tip = new();

    public DamageMap()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(420, 300);
        Cursor = Cursors.Cross;
    }

    RectangleF Body(string side)
    {
        float h = Height - S(40), w = h * 0.5f, gap = S(40);
        float total = w * 2 + gap, x0 = (Width - total) / 2f;
        // الأمامي يميناً (قراءة عربية) والخلفي يساراً
        return side == "front" ? new RectangleF(x0 + w + gap, S(24), w, h) : new RectangleF(x0, S(24), w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        foreach (var side in new[] { "front", "back" })
        {
            var r = Body(side);
            Gfx.FillRound(g, r, S(18f), Theme.Surface);
            Gfx.DrawRound(g, r, S(18f), Theme.BorderStrong, S(2f));
            if (side == "front")
            {
                var scr = RectangleF.Inflate(r, -S(8), -S(18));
                Gfx.FillRound(g, scr, S(8f), Theme.SurfaceAlt);
                using var b = new SolidBrush(Theme.BorderStrong);
                g.FillEllipse(b, r.X + r.Width / 2 - S(4), r.Y + S(6), S(8), S(8));
            }
            else
            {
                using var b = new SolidBrush(Theme.BorderStrong);
                g.FillEllipse(b, r.X + S(10), r.Y + S(12), S(22), S(22));
                g.FillEllipse(b, r.X + S(10), r.Y + S(38), S(22), S(22));
            }
            TextRenderer.DrawText(g, side == "front" ? "الأمام" : "الخلف", Theme.F(8.5f), new Rectangle((int)r.X, 0, (int)r.Width, S(22)), Theme.Muted, Gfx.Center);
        }
        for (int i = 0; i < Marks.Count; i++)
        {
            var m = Marks[i];
            var r = Body(m.Side);
            float x = r.X + (float)m.X * r.Width, y = r.Y + (float)m.Y * r.Height, d = S(20);
            using (var b = new SolidBrush(Pal.Bad)) g.FillEllipse(b, x - d / 2, y - d / 2, d, d);
            TextRenderer.DrawText(g, (i + 1).ToString(), Theme.FS(8), new Rectangle((int)(x - d / 2), (int)(y - d / 2), (int)d, (int)d), Color.White, Gfx.Center);
        }
        if (!ReadOnly && Marks.Count == 0)
            TextRenderer.DrawText(g, "انقر مكان الخدش أو الكسر", Theme.F(8.5f), new Rectangle(0, Height - S(18), Width, S(18)), Theme.Subtle, Gfx.Center);
    }

    int HitMark(Point p)
    {
        for (int i = Marks.Count - 1; i >= 0; i--)
        {
            var r = Body(Marks[i].Side);
            float x = r.X + (float)Marks[i].X * r.Width, y = r.Y + (float)Marks[i].Y * r.Height;
            if (Math.Abs(p.X - x) <= S(12) && Math.Abs(p.Y - y) <= S(12)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitMark(e.Location);
        var t = i >= 0 ? $"{i + 1}. {(Marks[i].Note == "" ? "خدش / كسر" : Marks[i].Note)}" : null;
        if (tip.GetToolTip(this) != (t ?? "")) tip.SetToolTip(this, t);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (ReadOnly) return;
        int hit = HitMark(e.Location);
        if (e.Button == MouseButtons.Right) { if (hit >= 0) { Marks.RemoveAt(hit); Changed?.Invoke(); Invalidate(); } return; }
        if (hit >= 0) return;
        foreach (var side in new[] { "front", "back" })
        {
            var r = Body(side);
            if (!r.Contains(e.Location)) continue;
            var note = Ask.Reason("وصف العلامة", "ماذا يوجد هنا؟", new[] { "خدش", "كسر", "شعر في الشاشة", "انبعاج", "ضربة في الإطار", "كسر في الزجاج الخلفي" }, "إضافة");
            if (note == null) return;
            Marks.Add(new Mark { Side = side, X = Math.Round((e.X - r.X) / r.Width, 3), Y = Math.Round((e.Y - r.Y) / r.Height, 3), Note = note });
            Changed?.Invoke();
            Invalidate();
            return;
        }
    }

    /// <summary>نص العلامات: «1. خدش (الأمام)، 2. كسر (الخلف)»</summary>
    public static string Describe(List<Mark> marks) => string.Join("، ", marks.Select((m, i) => $"{i + 1}. {m.Note} ({(m.Side == "front" ? "الأمام" : "الخلف")})"));

    /// <summary>رسم للطباعة (SVG)</summary>
    public static string Svg(List<Mark> marks)
    {
        if (marks.Count == 0) return "";
        var sb = new System.Text.StringBuilder("<svg width=\"260\" height=\"170\" viewBox=\"0 0 260 170\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"Tahoma\">");
        void Phone(double x, string label)
        {
            sb.Append($"<rect x=\"{x}\" y=\"18\" width=\"75\" height=\"145\" rx=\"12\" fill=\"#fff\" stroke=\"#8A93A6\" stroke-width=\"2\"/>");
            sb.Append($"<text x=\"{x + 37.5}\" y=\"12\" font-size=\"10\" text-anchor=\"middle\" fill=\"#5A6478\">{label}</text>");
        }
        Phone(155, "الأمام"); Phone(30, "الخلف");
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            double x0 = m.Side == "front" ? 155 : 30, cx = x0 + m.X * 75, cy = 18 + m.Y * 145;
            sb.Append($"<circle cx=\"{cx:0.#}\" cy=\"{cy:0.#}\" r=\"8\" fill=\"#C43F2C\"/><text x=\"{cx:0.#}\" y=\"{cy + 3.5:0.#}\" font-size=\"9\" text-anchor=\"middle\" fill=\"#fff\" font-weight=\"700\">{i + 1}</text>");
        }
        return sb.Append("</svg>").ToString();
    }
}

/// <summary>فحص الجودة قبل التسليم: كل بند يعمل / لا يعمل</summary>
public class QcDialog : DialogShell
{
    readonly List<TriChip> chips = new();
    readonly ComboBox cbBy = W.Combo(220, Array.Empty<string>(), true);

    QcDialog(Order o) : base("فحص الجودة قبل التسليم", 760, 560, "shield-check", Pal.Good)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note($"{o.RefNo} — {o.Device} — {o.CustomerName}\nاضغط على البند للتبديل: لم يُفحص ← يعمل ← لا يعمل", 700, 44));
        var box = W.Flow();
        box.MaximumSize = new Size(720, 0);
        foreach (var item in QC.Items)
        {
            var c = new TriChip { Key = item, Text = item, State = o.X.QC.GetValueOrDefault(item, "") };
            chips.Add(c);
            box.Controls.Add(c);
        }
        flow.Controls.Add(box);
        cbBy.Items.AddRange(Techs.All.Select(t => (object)t.Name).ToArray());
        cbBy.Text = o.X.QcBy ?? (o.Technician != "" ? o.Technician : "");
        flow.Controls.Add(W.Labeled("فحصه", cbBy, "user"));
        Body.Controls.Add(flow);
        var ok = AddButton("اعتماد الفحص", DialogResult.None, BtnKind.Success, "check");
        AddButton("كل البنود تعمل", DialogResult.None, BtnKind.Soft, "check").Click += (s, e) => { foreach (var c in chips) { c.State = "ok"; c.Invalidate(); } };
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var missing = chips.Where(c => c.State == "").Select(c => c.Text).ToList();
            if (missing.Count > 0) { Dialogs.Warn("لم تُفحص بعد: " + string.Join("، ", missing)); return; }
            var bad = chips.Where(c => c.State == "bad").Select(c => c.Text).ToList();
            if (bad.Count > 0 && !W.Confirm("بنود لا تعمل", "لا يعمل: " + string.Join("، ", bad) + "\nاعتماد الفحص على أي حال؟ (سيظهر ذلك في الفاتورة)", "اعتماد")) return;
            o.X.QC = chips.ToDictionary(c => c.Key, c => c.State);
            o.X.QcAt = Txt.Now;
            o.X.QcBy = cbBy.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    /// <summary>يعدّل الطلب الممرَّر (مسودة) ويعيد true عند الاعتماد</summary>
    public static bool Run(Order o)
    {
        using var d = new QcDialog(o);
        return d.ShowModal() == DialogResult.OK;
    }
}

/// <summary>خطة تقسيط: عدد الأقساط وأول موعد والمدة بينها</summary>
public class PlanDialog : DialogShell
{
    readonly NumericUpDown nCount = new() { Width = 120, Minimum = 2, Maximum = 36, Value = 3, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10) };
    readonly NumericUpDown nTotal = W.Money(200);
    readonly DateTimePicker dFirst = new() { Width = 180, Format = DateTimePickerFormat.Short };
    readonly Seg step = new(("30", "شهرياً"), ("14", "كل أسبوعين"), ("7", "أسبوعياً"));
    readonly ListBox preview = new() { Width = 520, Height = 200, Font = Theme.F(10), BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    public List<Inst> Plan { get; private set; } = new();

    public PlanDialog(double remaining, List<Inst> current) : base("خطة تقسيط", 620, 620, "calendar", Pal.Primary)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note("المبلغ المتبقي يُقسَّم على دفعات بمواعيد. تظهر الأقساط المتأخرة في الرئيسية والتقرير اليومي.", 560, 40));
        var r = W.Flow(false);
        r.Controls.Add(W.Labeled("المبلغ المقسَّط", nTotal));
        r.Controls.Add(W.Labeled("عدد الأقساط", nCount));
        flow.Controls.Add(r);
        var r2 = W.Flow(false);
        r2.Controls.Add(W.Labeled("أول قسط", dFirst, "calendar"));
        step.Margin = new Padding(6, 27, 6, 4);
        r2.Controls.Add(step);
        flow.Controls.Add(r2);
        preview.Margin = new Padding(6);
        flow.Controls.Add(preview);
        Body.Controls.Add(flow);
        W.Set(nTotal, remaining);
        dFirst.Value = DateTime.Today.AddMonths(1);
        if (current.Count > 0) { nCount.Value = Math.Clamp(current.Count, 2, 36); W.Set(nTotal, current.Sum(i => i.Amount)); dFirst.Value = Txt.ParseDate(current[0].Date) ?? dFirst.Value; }
        step.Value = "30";
        nCount.ValueChanged += (s, e) => Render();
        nTotal.ValueChanged += (s, e) => Render();
        dFirst.ValueChanged += (s, e) => Render();
        step.Changed += _ => Render();
        Render();
        var ok = AddButton("اعتماد الخطة", DialogResult.None, BtnKind.Primary, "check");
        if (current.Count > 0) AddButton("إلغاء التقسيط", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) => { Plan = new(); DialogResult = DialogResult.OK; Close(); };
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) => { if (Plan.Count == 0) return; DialogResult = DialogResult.OK; Close(); };
    }

    void Render()
    {
        Plan = Installments.Make((double)nTotal.Value, (int)nCount.Value, dFirst.Value.Date, int.Parse(step.Value));
        preview.BeginUpdate();
        preview.Items.Clear();
        for (int i = 0; i < Plan.Count; i++) preview.Items.Add($"القسط {i + 1}:   {Txt.FmtDate(Plan[i].Date)}   —   {Txt.Money(Plan[i].Amount)}");
        preview.EndUpdate();
    }
}

/// <summary>قائمة الطباعة الموحدة (فاتورة، ملصق، وصل، بطاقة عمل، بطاقة ضمان، ملف الزبون)</summary>
public static class PrintMenu
{
    public static void Show(Control anchor, Order o)
    {
        var m = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = Theme.F(10) };
        m.Items.Add("فاتورة الصيانة", null, (_, _) => Printer.Invoice(o));
        m.Items.Add("ملصق الجهاز", null, (_, _) => Printer.Label(o));
        m.Items.Add("بطاقة عمل للفني (بدون أسعار)", null, (_, _) => Printer.JobCard(o));
        if (o.Status == K.Done && Calc.WarrantyEnd(o) != "") m.Items.Add("بطاقة الضمان", null, (_, _) => Printer.WarrantyCard(o));
        if (o.PaymentStatus == K.PayFull && Calc.ChargeOf(o) > 0) m.Items.Add("وصل استلام المبلغ", null, (_, _) => Printer.Receipt(o));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("ملف الزبون الكامل", null, (_, _) => Printer.CustomerFile(Calc.CustomerKey(o)));
        m.Show(anchor, new Point(anchor.Width, anchor.Height), ToolStripDropDownDirection.AboveLeft);
    }
}

/// <summary>الطلبات المعلّقة بانتظار موافقة الزبون: إلغاؤها مع رسالة، أو فتحها</summary>
public class StaleDialog : DialogShell
{
    readonly DataGridView grid = W.Grid();
    List<Order> rows = new();

    StaleDialog() : base("طلبات بانتظار الموافقة منذ مدة", 900, 600, "clock", Pal.Amber)
    {
        grid.ReadOnly = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "pick", HeaderText = "", FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ref", HeaderText = "المرجع", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "الزبون", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "device", HeaderText = "الجهاز", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "days", HeaderText = "منذ", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "price", HeaderText = "السعر المعروض", ReadOnly = true });
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.RowIndex < rows.Count && e.ColumnIndex > 0) Acts.View(rows[e.RowIndex]); };
        Body.Controls.Add(grid);
        Body.Controls.Add(W.Note($"طلبات «بانتظار الموافقة» منذ {Stale.Days} أيام أو أكثر. حدّد ما تريد إلغاءه: يُلغى الطلب (تبقى أجرة الفحص إن وُجدت) وتُفتح رسالة للزبون ليستلم جهازه.", 840, 44));
        AddButton("إلغاء المحدد مع رسالة", DialogResult.None, BtnKind.Danger, "ban").Click += (s, e) => CancelPicked();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        Render();
    }

    public static void Open() { using var d = new StaleDialog(); d.ShowModal(); }

    void Render()
    {
        rows = Stale.List();
        grid.Rows.Clear();
        foreach (var o in rows) grid.Rows.Add(true, o.RefNo, o.CustomerName, o.Device, Calc.StatusDays(o) + " يوم", Txt.Money(o.Price));
    }

    void CancelPicked()
    {
        grid.EndEdit();
        var picked = rows.Where((o, i) => grid.Rows[i].Cells["pick"].Value is true).ToList();
        if (picked.Count == 0) return;
        if (!W.Confirm($"إلغاء {picked.Count} طلب؟", "سيُلغى كل طلب محدد وتُفتح رسالة واتساب لكل زبون (يمكنك تخطي أي رسالة).", "إلغاء الطلبات", true)) return;
        foreach (var o in picked)
        {
            Acts.SetStatus(o, K.Cancelled);
            if (Calc.Find(o.Id) is Order n && n.Phone != "")
            {
                using var d = new WaDialog(n.CustomerName, n.Phone, new() { new("stale", "إلغاء لعدم الرد", Msg.For("stale", n)) }, 0);
                d.ShowModal();
            }
        }
        Render();
        if (rows.Count == 0) Close();
    }
}
