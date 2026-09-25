using System.Drawing.Drawing2D;

namespace Raseed;

/// <summary>
/// شريط الصفحة الرئيسية الكحلي: الترحيب والتاريخ والساعة، و«الوصول السريع» — بطاقات لأكثر الشاشات استخدامًا
/// يختارها المستخدم من زر الإعدادات (تُحفظ في الإعدادات باسم quick_access).
/// </summary>
public class HomeHero : Panel
{
    public const string DefaultQuick = "المواد|قائمة بيع|قائمة شراء|سند قبض|الأقساط|حساب الزبائن";
    readonly MainForm main;
    readonly List<QuickTile> tiles = new();
    readonly ModernButton gear = new() { Kind = BtnKind.Glass, IconName = "settings", Size = new Size(34, 34), TabStop = false };
    readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    readonly System.Globalization.CultureInfo ar = new("ar-IQ");

    /// <summary>عرض عمود الترحيب بالبكسل الفعلي (أضيق في الشاشات الصغيرة)</summary>
    int GreetW => Width < Dpi.S(900) ? Dpi.S(250) : Dpi.S(330);

    public HomeHero(MainForm owner)
    {
        main = owner;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Hero1;
        ar.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
        new ToolTip().SetToolTip(gear, "اختيار شاشات الوصول السريع");
        Controls.Add(gear);
        gear.Click += (s, e) =>
        {
            using var d = new QuickAccessDialog(main);
            if (d.ShowModal() == DialogResult.OK) BuildTiles();
        };
        clock.Tick += (s, e) => Invalidate(new Rectangle(Width - GreetW - Dpi.S(20), 0, GreetW + Dpi.S(20), Height));
        clock.Start();
        Disposed += (s, e) => clock.Dispose();
        BuildTiles();
    }

    public static List<string> Chosen() => Settings.Get("quick_access", DefaultQuick).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    void BuildTiles()
    {
        SuspendLayout();
        foreach (var t in tiles) { Controls.Remove(t); t.Dispose(); }
        tiles.Clear();
        foreach (var name in Chosen())
        {
            var p = main?.Pages.FirstOrDefault(x => x.Text == name);
            if (p == null) continue;
            var t = new QuickTile { Title = p.Text, Group = p.Group == "" ? "الرئيسية" : p.Group, IconName = p.Icon, Tint = MainForm.TintOf(p.Group) };
            t.Click += (s, e) => main.Navigate(p);
            tiles.Add(t);
            Controls.Add(t);
        }
        ResumeLayout();
        Arrange();
    }

    /// <summary>البطاقات ارتفاعها المناسب لعددها: أعمدة حسب العرض، وسطر أو سطران</summary>
    public int PreferredHeight(int width)
    {
        int qw = width - GreetWFor(width) - Dpi.S(48);
        int cols = Cols(qw);
        int rows = Math.Max(1, (tiles.Count + cols - 1) / cols);
        return Dpi.S(66) + rows * Dpi.S(72) + (rows - 1) * Dpi.S(10) + Dpi.S(18);
    }

    int GreetWFor(int width) => width < Dpi.S(900) ? Dpi.S(250) : Dpi.S(330);
    int Cols(int qw) => Math.Max(1, Math.Min(Math.Max(1, tiles.Count), (qw + Dpi.S(10)) / (Dpi.S(190) + Dpi.S(10))));

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Arrange();
    }

    void Arrange()
    {
        if (tiles.Count == 0 && gear == null) return;
        int qw = Width - GreetW - Dpi.S(48), gap = Dpi.S(10);
        int cols = Cols(qw);
        int tw = (qw - gap * (cols - 1)) / cols, th = Dpi.S(72);
        int left = Dpi.S(24), top = Dpi.S(66);
        for (int i = 0; i < tiles.Count; i++)
        {
            int c = i % cols, r = i / cols;
            // الترتيب من اليمين (بداية السطر العربي)
            int x = left + qw - (c + 1) * tw - c * gap;
            tiles[i].SetBounds(x, top + r * (th + gap), tw, th);
        }
        gear.Location = new Point(Dpi.S(24), Dpi.S(16));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new LinearGradientBrush(ClientRectangle, Theme.Hero1, Theme.Hero2, 0f)) g.FillRectangle(bg, ClientRectangle);
        Gfx.Hq(g);
        using (var b1 = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillEllipse(b1, Width - Dpi.S(220), -Dpi.S(120), Dpi.S(300), Dpi.S(300));
        using (var b2 = new SolidBrush(Color.FromArgb(14, 247, 181, 44))) g.FillEllipse(b2, Width - GreetW - Dpi.S(90), Height - Dpi.S(110), Dpi.S(200), Dpi.S(200));

        // الوصول السريع
        int qw = Width - GreetW - Dpi.S(48);
        TextRenderer.DrawText(g, "الوصول السريع", Theme.FS(12), new Rectangle(Dpi.S(70), Dpi.S(16), qw - Dpi.S(46), Dpi.S(30)), Color.White, Gfx.RtlStart);
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawLine(pen, Dpi.S(24), Dpi.S(56), Dpi.S(24) + qw, Dpi.S(56));

        // الترحيب والساعة
        var now = DateTime.Now;
        int x = Width - GreetW - Dpi.S(10), w = GreetW - Dpi.S(20);
        string greet = now.Hour < 12 ? "صباح الخير" : "مساء الخير";
        bool small = Height < Dpi.S(220);
        int y = Dpi.S(small ? 12 : 20);
        TextRenderer.DrawText(g, Settings.Get("shop_name"), Theme.F(10.5f), new Rectangle(x, y, w, Dpi.S(24)), Gfx.Mix(Color.White, Theme.Hero1, 0.22f), Gfx.RtlStart);
        y += Dpi.S(24);
        TextRenderer.DrawText(g, greet, Theme.FS(small ? 20 : 24), new Rectangle(x, y, w, Dpi.S(small ? 40 : 48)), Theme.HeroAccent, Gfx.RtlStart);
        y += Dpi.S(small ? 42 : 52);
        TextRenderer.DrawText(g, now.ToString("dddd، d MMMM yyyy", ar), Theme.F(10.5f), new Rectangle(x, y, w, Dpi.S(24)), Color.White, Gfx.RtlStart);
        y += Dpi.S(26);
        TextRenderer.DrawText(g, now.ToString("hh:mm:ss tt", ar), Theme.FS(small ? 16 : 19), new Rectangle(x, y, w, Dpi.S(36)), Color.White, Gfx.RtlStart);
        y += Dpi.S(40);
        if (y + Dpi.S(22) < Height)
            TextRenderer.DrawText(g, $"مرحبًا {Session.UserName}  •  الإصدار {Application.ProductVersion.Split('+')[0]}", Theme.F(8.5f), new Rectangle(x, y, w, Dpi.S(22)), Gfx.Mix(Color.White, Theme.Hero1, 0.36f), Gfx.RtlStart);
    }
}

/// <summary>بطاقة وصول سريع أفقية: أيقونة بلون القسم، واسم الشاشة واسم القسم</summary>
public class QuickTile : Control
{
    bool hover;
    public string Title { get; set; }
    public string Group { get; set; }
    public string IconName { get; set; }
    public Color Tint { get; set; } = Theme.Orange;

    public QuickTile()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(190, 72);
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Gfx.FillRound(g, r, Dpi.S(12f), hover ? Color.White : Color.FromArgb(250, 255, 255, 255));
        if (hover) Gfx.DrawRound(g, RectangleF.Inflate(r, -1, -1), Dpi.S(11f), Theme.Amber, Dpi.S(2f));
        int box = Math.Min(Dpi.S(44), Height - Dpi.S(20));
        var ir = new RectangleF(Width - Dpi.S(14) - box, (Height - box) / 2f, box, box);
        Gfx.FillRound(g, ir, Dpi.S(11f), Gfx.Mix(Tint, Color.White, 0.84f));
        Icons.Draw(g, IconName, ir, Tint, 22);
        int tx = Dpi.S(10), tw = (int)ir.X - Dpi.S(10) - tx;
        TextRenderer.DrawText(g, Title, Theme.FS(10.5f), new Rectangle(tx, Height / 2 - Dpi.S(24), tw, Dpi.S(26)), Theme.Ink, Gfx.RtlStart);
        TextRenderer.DrawText(g, Group, Theme.F(8.5f), new Rectangle(tx, Height / 2 + Dpi.S(1), tw, Dpi.S(22)), Theme.Subtle, Gfx.RtlStart);
    }
}

/// <summary>اختيار شاشات الوصول السريع</summary>
public class QuickAccessDialog : DialogShell
{
    public QuickAccessDialog(MainForm main) : base("الوصول السريع", 480, 600, "settings")
    {
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, Font = Theme.F(10.5f), IntegralHeight = false };
        var chosen = HomeHero.Chosen();
        var pages = main.Pages.Where(p => p.Group != "").ToList();
        foreach (var p in pages) list.Items.Add($"{p.Text}   —   {p.Group}", chosen.Contains(p.Text));
        Body.Controls.Add(list);
        Body.Controls.Add(new Label { Text = "اختر الشاشات التي تظهر كبطاقات في الصفحة الرئيسية (8 كحد أعلى):", Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Text2, Font = Theme.F(10) });
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var sel = list.CheckedIndices.Cast<int>().Select(i => pages[i].Text).ToList();
            if (sel.Count > 8) { Ui.Warn("اختر 8 شاشات كحد أعلى."); return; }
            Settings.Set("quick_access", string.Join("|", sel));
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}
