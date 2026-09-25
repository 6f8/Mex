using System.Drawing.Drawing2D;
using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>عنصر يحسب ارتفاعه من العرض المتاح (للترتيب العمودي المتجاوب)</summary>
public interface IAutoHeight { int HeightFor(int width); }

/// <summary>ألوان الورشة (الحالات والمبالغ)</summary>
public static class Pal
{
    static Color C(string h) => ColorTranslator.FromHtml(h);
    public static readonly Color Good = C("#1D8657"), GoodSoft = C("#E1F3EA"), Bad = C("#C43F2C"), BadSoft = C("#FBE8E4");
    public static readonly Color Amber = C("#E2952B"), AmberSoft = C("#FCF0DC"), AmberInk = C("#8A5610");
    public static readonly Color Wait = C("#94700F"), WaitSoft = C("#FAF1D6"), Slate = C("#5E6A7E");
    public static readonly Color Primary = C("#2B55C9"), Violet = C("#6A4CC2"), Teal = C("#0D7C86");

    /// <summary>شارة ملونة (حالة أو دفع)</summary>
    public static void Pill(Graphics g, string text, Rectangle area, Color fg, Color bg, Font font = null, bool center = false)
    {
        font ??= Theme.FS(8.5f);
        var sz = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        int w = Math.Min(area.Width, sz.Width + S(18)), h = Math.Min(area.Height, S(24));
        var r = new Rectangle(center ? area.X + (area.Width - w) / 2 : area.Right - w, area.Y + (area.Height - h) / 2, w, h);
        Gfx.FillRound(g, r, h / 2f, bg);
        TextRenderer.DrawText(g, text, font, r, fg, Gfx.Center | TextFormatFlags.EndEllipsis);
    }

    public static void Status(Graphics g, string status, Rectangle area, bool center = false)
    {
        var (fg, bg) = K.StatusColors(status);
        if (fg.IsEmpty) (fg, bg) = (Theme.Text2, Theme.GraySoft);
        Pill(g, status, area, fg, bg, null, center);
    }

    /// <summary>الرقم المرجعي بشكل بطاقة كهرمانية</summary>
    public static int Tag(Graphics g, string refNo, int right, int y, int h)
    {
        if (string.IsNullOrEmpty(refNo)) return 0;
        var f = Theme.FS(8.5f);
        int w = TextRenderer.MeasureText(g, refNo, f, Size.Empty, TextFormatFlags.NoPadding).Width + S(14);
        var r = new Rectangle(right - w, y, w, h);
        Gfx.FillRound(g, r, S(5f), AmberSoft);
        Gfx.DrawRound(g, r, S(5f), Gfx.Mix(Amber, Color.White, 0.5f));
        TextRenderer.DrawText(g, refNo, f, r, AmberInk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return w;
    }
}

/// <summary>ترتيب عمودي: كل عنصر بعرض الحاوية وارتفاعه المحسوب (يُستعمل داخل صفحة قابلة للتمرير)</summary>
public class VStack : Panel, IAutoHeight
{
    bool busy;
    public int Gap { get; set; } = 14;
    public VStack() { BackColor = Theme.Bg; DoubleBuffered = true; }

    public int HeightFor(int width) => Arrange(width, false);

    int Arrange(int width, bool apply)
    {
        int y = Padding.Top, w = Math.Max(S(40), width - Padding.Horizontal);
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            int h = c is IAutoHeight a ? a.HeightFor(w) : c.Height;
            if (apply) c.SetBounds(Padding.Left, y, w, h);
            y += h + S(Gap);
        }
        return y - (y > Padding.Top ? S(Gap) : 0) + Padding.Bottom;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (busy) return;
        busy = true;
        try
        {
            int h = Arrange(ClientSize.Width, true);
            if (Dock == DockStyle.Top && Height != h) Height = h;
        }
        finally { busy = false; }
    }

    /// <summary>بعد تغيّر المحتوى (ارتفاع عنصر داخلي)</summary>
    public void Relayout() => PerformLayout();
}

/// <summary>أعمدة متجاورة بعرض متساوٍ، تنزل تحت بعضها إذا ضاقت المساحة</summary>
public class Cols : Panel, IAutoHeight
{
    bool busy;
    public int MinCol { get; set; } = 380;
    public int Gap { get; set; } = 14;
    public Cols() { BackColor = Theme.Bg; DoubleBuffered = true; }

    List<Control> Vis => Controls.Cast<Control>().Where(c => c.Visible).ToList();
    bool Side(int width, int n) => width >= S(MinCol) * n + S(Gap) * (n - 1);

    public int HeightFor(int width) => Arrange(width, false);

    int Arrange(int width, bool apply)
    {
        var cols = Vis;
        if (cols.Count == 0) return 0;
        int gap = S(Gap);
        bool side = Side(width, cols.Count);
        int colW = side ? (width - gap * (cols.Count - 1)) / cols.Count : width;
        int x = width, y = 0, h = 0;
        var heights = cols.Select(c => c is IAutoHeight a ? a.HeightFor(colW) : c.Height).ToList();
        int rowH = side ? heights.Max() : 0;
        for (int i = 0; i < cols.Count; i++)
        {
            if (side)
            {
                if (apply) cols[i].SetBounds(x - colW, 0, colW, rowH);
                x -= colW + gap;
                h = rowH;
            }
            else
            {
                if (apply) cols[i].SetBounds(0, y, colW, heights[i]);
                y += heights[i] + gap;
                h = y - gap;
            }
        }
        return h;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (busy) return;
        busy = true;
        try { Arrange(ClientSize.Width, true); } finally { busy = false; }
    }
}

/// <summary>بطاقة بعنوان تحتوي عنصرًا واحدًا؛ ارتفاعها = العنوان + ارتفاع المحتوى</summary>
public class Box : CardPanel, IAutoHeight
{
    public Control Content { get; }
    /// <summary>أدوات صغيرة في رأس البطاقة (جهة اليسار)</summary>
    public FlowLayoutPanel Tools { get; }

    int desired;
    /// <summary>ارتفاع المحتوى المطلوب (بالبكسل الفعلي بعد التكبير) لمحتوى لا يحسب ارتفاعه بنفسه</summary>
    public int ContentHeight { get => desired; set { desired = value; Parent?.PerformLayout(); } }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        desired = (int)Math.Round(desired * factor.Height);
    }

    public Box(string title, Control content, string icon = null, string subtitle = null)
    {
        desired = content.Height;
        Title = title;
        Subtitle = subtitle;
        IconName = icon;
        Content = content;
        content.Dock = DockStyle.Fill;
        Controls.Add(content);
        Tools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Anchor = AnchorStyles.Top | AnchorStyles.Left, FlowDirection = FlowDirection.LeftToRight };
        Controls.Add(Tools);
        Tools.BringToFront();
        Resize += (s, e) => PlaceTools();
        Tools.SizeChanged += (s, e) => PlaceTools();
    }

    void PlaceTools() => Tools.Location = new Point(S(14), Math.Max(S(8), (HeaderHeight - Tools.Height) / 2 + S(4)));

    public int HeightFor(int width) => Padding.Vertical + (Content is IAutoHeight a ? a.HeightFor(width - Padding.Horizontal) : desired) + S(4);
}

/// <summary>صف مؤشرات مالية (الإيراد، المقبوض، الربح...) بخلايا متساوية تلتف إلى صفين عند الضيق</summary>
public class Ledger : Control, IAutoHeight
{
    public record Cell(string Label, string Value, string Foot = null, Color? Dot = null, int Tone = 0);
    List<Cell> cells = new();
    public int MinCell { get; set; } = 190;

    public Ledger()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 100;
    }

    public void Set(IEnumerable<Cell> c) { cells = c.ToList(); FitHeight(); Invalidate(); Parent?.PerformLayout(); }

    /// <summary>عند الالتصاق بالأعلى: الارتفاع حسب عدد الصفوف</summary>
    void FitHeight() { if (Dock == DockStyle.Top && Width > 0) { int h = HeightFor(Width); if (Height != h) Height = h; } }
    protected override void OnResize(EventArgs e) { base.OnResize(e); FitHeight(); }

    int ColsFor(int w) => Math.Max(1, Math.Min(Math.Max(1, cells.Count), w / S(MinCell)));
    public int HeightFor(int width) => (int)Math.Ceiling(Math.Max(1, cells.Count) / (double)ColsFor(width)) * S(94) + S(4);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var card = new RectangleF(S(1.5f), 0.5f, Width - S(4f), Height - S(4f));
        Gfx.Shadow(g, card, S(12f));
        Gfx.FillRound(g, card, S(12f), Theme.Surface);
        Gfx.DrawRound(g, card, S(12f), Theme.Border);
        if (cells.Count == 0) return;
        int cols = ColsFor(Width), rowH = S(94);
        float cw = (card.Width) / cols;
        using var pen = new Pen(Theme.Border);
        for (int i = 0; i < cells.Count; i++)
        {
            int r = i / cols, c = i % cols;
            float right = card.Right - c * cw, top = card.Y + r * rowH;
            if (c > 0) g.DrawLine(pen, right, top + S(14), right, top + rowH - S(14));
            if (r > 0 && c == 0) g.DrawLine(pen, card.X + S(14), top, card.Right - S(14), top);
            var cell = cells[i];
            int x = (int)(right - cw) + S(16), w = (int)cw - S(32);
            int textRight = x + w;
            if (cell.Dot is Color dot)
            {
                using var b = new SolidBrush(dot);
                g.FillEllipse(b, textRight - S(9), top + S(20), S(9), S(9));
                textRight -= S(15);
            }
            TextRenderer.DrawText(g, cell.Label, Theme.F(9), new Rectangle(x, (int)top + S(12), textRight - x, S(24)), Theme.Muted, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            var vc = cell.Tone > 0 ? Pal.Good : cell.Tone < 0 ? Pal.Bad : Theme.Ink;
            var vf = Theme.FS(cell.Value != null && cell.Value.Length > 14 ? 13 : 15.5f);
            TextRenderer.DrawText(g, cell.Value, vf, new Rectangle(x, (int)top + S(36), w, S(32)), vc, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(cell.Foot))
                TextRenderer.DrawText(g, cell.Foot, Theme.F(8), new Rectangle(x, (int)top + S(66), w, S(20)), Theme.Subtle, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>أزرار متلاصقة لاختيار واحد (اليوم / الأسبوع / الشهر...)</summary>
public class Seg : Control
{
    readonly List<(string V, string T)> items;
    string value;
    int hover = -1;
    public event Action<string> Changed;
    public string Value { get => value; set { this.value = value; Invalidate(); } }

    public Seg(params (string V, string T)[] opts)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        items = opts.ToList();
        value = items.Count > 0 ? items[0].V : null;
        Height = 38;
        Cursor = Cursors.Hand;
        Margin = new Padding(4, 4, 4, 4);
        Width = Measure();
    }

    public static Seg Of(params string[] opts) => new(opts.Select(o => (o, o)).ToArray());

    Font Fnt => Theme.FS(9.5f);
    int ItemW(string t) => TextRenderer.MeasureText(t, Fnt).Width + S(22);
    int Measure() => items.Sum(i => ItemW(i.T)) + S(8);

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        Width = Measure();
    }

    List<Rectangle> Rects()
    {
        var list = new List<Rectangle>();
        int x = Width - S(4);
        foreach (var i in items) { int w = ItemW(i.T); list.Add(new Rectangle(x - w, S(4), w, Height - S(8))); x -= w; }
        return list;
    }

    protected override void OnMouseMove(MouseEventArgs e) { int h = Rects().FindIndex(r => r.Contains(e.Location)); if (h != hover) { hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = Rects().FindIndex(r => r.Contains(e.Location));
        if (i < 0 || items[i].V == value) return;
        value = items[i].V;
        Invalidate();
        Changed?.Invoke(value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        Gfx.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(10f), Theme.SurfaceAlt);
        Gfx.DrawRound(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(10f), Theme.Border);
        var rs = Rects();
        for (int i = 0; i < items.Count; i++)
        {
            bool sel = items[i].V == value;
            if (sel)
            {
                Gfx.FillRound(g, rs[i], S(8f), Theme.Surface);
                Gfx.DrawRound(g, rs[i], S(8f), Theme.BorderStrong);
            }
            else if (i == hover) Gfx.FillRound(g, rs[i], S(8f), Theme.GraySoft);
            TextRenderer.DrawText(g, items[i].T, Fnt, rs[i], sel ? Theme.BrandDark : Theme.Muted, Gfx.Center);
        }
    }
}

/// <summary>قائمة بأشرطة نسبية (توزيع الحالات، أنواع الأعطال، طرق الدفع)</summary>
public class MeterList : Control, IAutoHeight
{
    public record Row(string Label, double Value, string Text, bool StatusBadge = false);
    List<Row> rows = new();
    public string EmptyText { get; set; } = "لا توجد بيانات في هذه الفترة";

    public MeterList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    public void Set(IEnumerable<Row> r) { rows = r.ToList(); Invalidate(); }
    public int HeightFor(int width) => Math.Max(1, rows.Count) * S(32) + S(6);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        if (rows.Count == 0) { TextRenderer.DrawText(g, EmptyText, Theme.F(9.5f), new Rectangle(0, 0, Width, S(30)), Theme.Subtle, Gfx.RtlStart); return; }
        double max = Math.Max(1e-9, rows.Max(r => r.Value));
        int labW = Math.Min(S(150), Width / 3), txtW = Math.Min(S(150), Width / 3);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int y = i * S(32) + S(3);
            var lr = new Rectangle(Width - labW, y, labW, S(28));
            if (r.StatusBadge) Pal.Status(g, r.Label, lr);
            else TextRenderer.DrawText(g, r.Label, Theme.F(9.5f), lr, Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            var track = new RectangleF(txtW + S(8), y + S(10), Width - labW - txtW - S(16), S(8));
            if (track.Width > S(10))
            {
                Gfx.FillRound(g, track, S(4f), Theme.GraySoft);
                float w = Math.Max(S(4f), (float)(track.Width * r.Value / max));
                Gfx.FillRound(g, new RectangleF(track.Right - w, track.Y, w, track.Height), S(4f), Theme.Brand);
            }
            TextRenderer.DrawText(g, r.Text, Theme.F(9), new Rectangle(0, y, txtW, S(28)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>مخطط أعمدة مجمّعة (الإيراد والربح) مع قيم سالبة وتلميح عند المرور</summary>
public class DualBarChart : Control, IAutoHeight
{
    public List<string> Labels { get; } = new();
    public List<(string Name, double[] Values, Color Color)> Series { get; } = new();
    public bool AllLabels { get; set; }
    int hover = -1;
    public string EmptyText { get; set; } = "لا توجد بيانات بعد";

    public DualBarChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    public int HeightFor(int width) => S(250);
    bool HasData => Series.Any(s => s.Values.Any(v => Math.Abs(v) > 0.0001));

    RectangleF Plot => new(S(8), S(30), Width - S(16), Height - S(30) - S(26));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int n = Labels.Count;
        int h = -1;
        if (n > 0 && Plot.Contains(e.Location))
        {
            float slot = Plot.Width / n;
            h = n - 1 - (int)((e.X - Plot.X) / slot);   // من اليمين
            if (h < 0 || h >= n) h = -1;
        }
        if (h != hover) { hover = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        if (!HasData || Labels.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Theme.F(10), ClientRectangle, Theme.Subtle, Gfx.Center);
            return;
        }
        // مفتاح الألوان
        int lx = S(8);
        foreach (var s in Series)
        {
            var tw = TextRenderer.MeasureText(s.Name, Theme.F(8.5f)).Width;
            using (var b = new SolidBrush(s.Color)) g.FillRectangle(b, lx, S(8), S(10), S(10));
            TextRenderer.DrawText(g, s.Name, Theme.F(8.5f), new Point(lx + S(14), S(4)), Theme.Muted);
            lx += tw + S(28);
        }
        var plot = Plot;
        var all = Series.SelectMany(s => s.Values).ToList();
        double maxV = Math.Max(1, all.Max()), minV = Math.Min(0, all.Min()), range = maxV - minV;
        float Y(double v) => plot.Y + (float)((maxV - v) / range * plot.Height);
        using (var grid = new Pen(Theme.Border) { DashStyle = DashStyle.Dash })
            foreach (var t in new[] { 0, 0.5, 1 }) { float y = Y(minV + range * t); g.DrawLine(grid, plot.X, y, plot.Right, y); }
        int n = Labels.Count, sc = Series.Count;
        float slot = plot.Width / n, barW = Math.Min(S(22f), (slot - S(8)) / sc);
        for (int i = 0; i < n; i++)
        {
            float cx = plot.Right - slot * i - slot / 2;
            float start = cx - (barW * sc + S(3) * (sc - 1)) / 2;
            if (i == hover) Gfx.FillRound(g, new RectangleF(cx - slot / 2 + S(2), plot.Y, slot - S(4), plot.Height), S(6f), Theme.SurfaceAlt);
            for (int si = 0; si < sc; si++)
            {
                double v = i < Series[si].Values.Length ? Series[si].Values[i] : 0;
                float top = Y(Math.Max(v, 0)), bottom = Y(Math.Min(v, 0));
                float h = Math.Max(v == 0 ? 0 : S(2f), bottom - top);
                if (h <= 0) continue;
                var col = v < 0 ? Pal.Bad : Series[si].Color;
                Gfx.FillRound(g, new RectangleF(start + si * (barW + S(3)), top, barW, h), Math.Min(S(3f), barW / 2), col);
            }
            int every = AllLabels ? 1 : slot < S(34) ? 2 : 1;
            if (i % every == 0 || i == hover)
                TextRenderer.DrawText(g, Labels[i], Theme.F(8), new Rectangle((int)(cx - slot / 2) - S(8), (int)plot.Bottom + S(4), (int)slot + S(16), S(20)),
                    i == hover ? Theme.Ink : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        if (hover >= 0)
        {
            var lines = new[] { Labels[hover] }.Concat(Series.Select(s => $"{s.Name}: {Txt.Money(hover < s.Values.Length ? s.Values[hover] : 0)}")).ToList();
            var f = Theme.F(9);
            int tw = lines.Max(l => TextRenderer.MeasureText(l, f).Width) + S(20), th = lines.Count * S(20) + S(10);
            float cx = plot.Right - slot * hover - slot / 2;
            var tip = new RectangleF(Math.Clamp(cx - tw / 2f, 0, Width - tw), plot.Y, tw, th);
            Gfx.FillRound(g, tip, S(7f), Theme.Ink);
            for (int k = 0; k < lines.Count; k++)
                TextRenderer.DrawText(g, lines[k], k == 0 ? Theme.FS(9) : f, new Rectangle((int)tip.X + S(10), (int)tip.Y + S(5) + k * S(20), tw - S(20), S(20)), Color.White, Gfx.RtlStart);
        }
    }
}

/// <summary>شريط مراحل الطلب (قيد الفحص ← ... ← تم التسليم)</summary>
public class StepsBar : Control
{
    public string Status { get; set; }
    public StepsBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 52;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var flow = K.Statuses.Where(s => s != K.Cancelled).ToList();
        int pos = flow.IndexOf(Status);
        bool cancel = Status == K.Cancelled;
        float w = (Width - S(4) * (flow.Count - 1f)) / flow.Count;
        for (int i = 0; i < flow.Count; i++)
        {
            float x = Width - (i + 1) * w - i * S(4);
            var col = cancel ? Pal.BadSoft : i <= pos ? Theme.Brand : Theme.GraySoft;
            Gfx.FillRound(g, new RectangleF(x, S(6), w, S(6)), S(3f), col);
            TextRenderer.DrawText(g, flow[i], i == pos ? Theme.FS(8.5f) : Theme.F(8.5f), new Rectangle((int)x, S(16), (int)w, S(30)),
                cancel ? Pal.Bad : i <= pos ? Theme.BrandDark : Theme.Subtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft);
        }
    }
}

/// <summary>أجهزة على طاولة العمل الآن: بطاقة لكل جهاز مفتوح (المتأخر أولًا)</summary>
public class BenchStrip : Control, IAutoHeight
{
    List<Order> items = new();
    int hover = -1;
    public event Action<Order> OpenOrder;
    public string EmptyText { get; set; } = "لا توجد أجهزة قيد العمل. كل الطلبات مسلّمة أو ملغاة.";

    public BenchStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
    }

    public void Set(IEnumerable<Order> list) { items = list.Take(14).ToList(); Invalidate(); }

    int ColsFor(int w) => Math.Max(1, (w + S(10)) / (S(214) + S(10)));
    public int HeightFor(int width) => items.Count == 0 ? S(40) : (int)Math.Ceiling(items.Count / (double)ColsFor(width)) * (S(112) + S(10));

    Rectangle CardRect(int i)
    {
        int cols = ColsFor(Width), gap = S(10);
        int cw = (Width - gap * (cols - 1)) / cols, ch = S(112);
        int c = i % cols, r = i / cols;
        return new Rectangle(Width - (c + 1) * cw - c * gap, r * (ch + gap), cw, ch);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Enumerable.Range(0, items.Count).FirstOrDefault(i => CardRect(i).Contains(e.Location), -1);
        if (h != hover) { hover = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = Enumerable.Range(0, items.Count).FirstOrDefault(k => CardRect(k).Contains(e.Location), -1);
        if (i >= 0) OpenOrder?.Invoke(items[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        if (items.Count == 0) { TextRenderer.DrawText(g, EmptyText, Theme.F(9.5f), new Rectangle(0, 0, Width, S(36)), Theme.Subtle, Gfx.RtlStart); return; }
        for (int i = 0; i < items.Count; i++)
        {
            var o = items[i];
            var r = CardRect(i);
            bool late = Calc.IsLate(o);
            Gfx.FillRound(g, r, S(10f), i == hover ? Theme.SurfaceAlt : Theme.Surface);
            Gfx.DrawRound(g, r, S(10f), late ? Gfx.Mix(Pal.Bad, Color.White, 0.45f) : Theme.Border, late ? S(1.5f) : 1f);
            int pad = S(12), right = r.Right - pad;
            Pal.Tag(g, o.RefNo, right, r.Y + S(10), S(22));
            Pal.Status(g, o.Status, new Rectangle(r.X + pad, r.Y + S(9), r.Width / 2, S(24)));
            TextRenderer.DrawText(g, o.Device, Theme.FS(10), new Rectangle(r.X + pad, r.Y + S(38), r.Width - 2 * pad, S(24)), Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, $"{o.CustomerName} — {o.IssueType}", Theme.F(8.5f), new Rectangle(r.X + pad, r.Y + S(60), r.Width - 2 * pad, S(20)), Theme.Muted, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "⏱ " + Calc.DurationText(o), Theme.F(8.5f), new Rectangle(r.X + r.Width / 2, r.Y + S(84), r.Width / 2 - pad, S(20)), Theme.Text2, Gfx.RtlStart);
            string right2 = late ? $"متأخر {Calc.LateDays(o)} يوم" : o.DateEstimated != "" ? "موعده " + Txt.FmtShortDate(o.DateEstimated) : "";
            TextRenderer.DrawText(g, right2, late ? Theme.FS(8.5f) : Theme.F(8.5f), new Rectangle(r.X + pad, r.Y + S(84), r.Width / 2 - pad, S(20)), late ? Pal.Bad : Theme.Subtle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>التنبيهات: صناديق ملونة بعنوان وعناصر قابلة للنقر وزر اختياري</summary>
public class AlertsPanel : Control, IAutoHeight
{
    public record Item(string Text, Action Run);
    public record Alert(int Tone, string Icon, string Title, List<Item> Items, string Note = null, string Button = null, Action ButtonRun = null);
    List<Alert> alerts = new();
    readonly List<(Rectangle R, Action Run)> hits = new();
    int hoverHit = -1;

    public AlertsPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    public void Set(IEnumerable<Alert> a) { alerts = a.ToList(); Visible = alerts.Count > 0; Invalidate(); Parent?.PerformLayout(); }

    // الترتيب: يُحسب في القياس والرسم بنفس الدالة
    List<(Alert A, Rectangle Box, List<(Rectangle R, Item I)> Chips, Rectangle Btn)> Arrange(int width, Graphics g = null)
    {
        var res = new List<(Alert, Rectangle, List<(Rectangle, Item)>, Rectangle)>();
        int y = 0;
        var f = Theme.F(9);
        foreach (var a in alerts)
        {
            int pad = S(14), iconW = S(34);
            int btnW = a.Button != null ? TextRenderer.MeasureText(a.Button, Theme.FS(9)).Width + S(28) : 0;
            int innerR = width - pad - iconW, innerL = pad + (btnW > 0 ? btnW + S(10) : 0);
            int cy = y + S(12) + S(24) + (a.Note != null ? S(20) : 0);
            var chips = new List<(Rectangle, Item)>();
            int x = innerR;
            int lineH = S(28);
            foreach (var it in a.Items)
            {
                int w = Math.Min(innerR - innerL, TextRenderer.MeasureText(it.Text, f).Width + S(20));
                if (x - w < innerL && x != innerR) { x = innerR; cy += lineH + S(4); }
                chips.Add((new Rectangle(x - w, cy, w, lineH), it));
                x -= w + S(6);
            }
            int bottom = a.Items.Count > 0 ? cy + lineH + S(12) : cy + S(4);
            var box = new Rectangle(0, y, width, Math.Max(bottom - y, S(58)));
            var btn = btnW > 0 ? new Rectangle(pad, y + (box.Height - S(34)) / 2, btnW, S(34)) : Rectangle.Empty;
            res.Add((a, box, chips, btn));
            y += box.Height + S(10);
        }
        return res;
    }

    public int HeightFor(int width) => alerts.Count == 0 ? 0 : Arrange(width).Sum(x => x.Box.Height + S(10)) - S(10);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = hits.FindIndex(x => x.R.Contains(e.Location));
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        if (h != hoverHit) { hoverHit = h; Invalidate(); }
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        var h = hits.FirstOrDefault(x => x.R.Contains(e.Location));
        h.Run?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        hits.Clear();
        foreach (var (a, box, chips, btn) in Arrange(Width, g))
        {
            var (bg, border, fg) = a.Tone switch
            {
                2 => (Pal.BadSoft, Gfx.Mix(Pal.Bad, Color.White, 0.6f), Pal.Bad),
                1 => (Gfx.Mix(Pal.Primary, Color.White, 0.9f), Gfx.Mix(Pal.Primary, Color.White, 0.6f), Pal.Primary),
                _ => (Pal.WaitSoft, Gfx.Mix(Pal.Wait, Color.White, 0.6f), Pal.Wait),
            };
            Gfx.FillRound(g, box, S(12f), bg);
            Gfx.DrawRound(g, box, S(12f), border);
            Icons.Draw(g, a.Icon, new RectangleF(box.Right - S(34), box.Y + S(14), S(20), S(20)), fg, 18);
            TextRenderer.DrawText(g, a.Title, Theme.FS(10), new Rectangle(box.X + S(14) + (btn.Width > 0 ? btn.Width + S(10) : 0), box.Y + S(12), box.Width - S(48) - (btn.Width > 0 ? btn.Width + S(10) : 0), S(24)), Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            if (a.Note != null)
                TextRenderer.DrawText(g, a.Note, Theme.F(8.5f), new Rectangle(box.X + S(14) + (btn.Width > 0 ? btn.Width + S(10) : 0), box.Y + S(36), box.Width - S(48) - (btn.Width > 0 ? btn.Width + S(10) : 0), S(20)), Theme.Muted, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            foreach (var (r, it) in chips)
            {
                int idx = hits.Count;
                hits.Add((r, it.Run));
                Gfx.FillRound(g, r, S(8f), idx == hoverHit ? Theme.Surface : Gfx.Alpha(Color.White, 170));
                Gfx.DrawRound(g, r, S(8f), border);
                TextRenderer.DrawText(g, it.Text, Theme.F(9), r, Theme.Ink, Gfx.Center | TextFormatFlags.EndEllipsis);
            }
            if (btn.Width > 0)
            {
                int idx = hits.Count;
                hits.Add((btn, a.ButtonRun));
                Gfx.FillRound(g, btn, S(8f), idx == hoverHit ? Theme.BrandDark : Theme.Brand);
                TextRenderer.DrawText(g, a.Button, Theme.FS(9), btn, Color.White, Gfx.Center);
            }
        }
    }
}
