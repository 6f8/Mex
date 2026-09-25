using System.Drawing.Drawing2D;

namespace Raseed;

/// <summary>
/// شريط الصفحة الرئيسية الكحلي: الترحيب والتاريخ والساعة، و«الوصول السريع» — بطاقات لأكثر الشاشات استخدامًا
/// يختارها المستخدم من زر الإعدادات (تُحفظ في الإعدادات باسم quick_access).
/// </summary>
public class HomeHero : Panel
{
    public const string DefaultQuick = "المواد|قائمة بيع|قائمة شراء|سند قبض|الأقساط|حساب الزبائن";
    static readonly Color Navy = ColorTranslator.FromHtml("#2B3A8F"), NavyDark = ColorTranslator.FromHtml("#1E2A6E");
    readonly MainForm main;
    readonly FlowLayoutPanel tiles = new() { WrapContents = true, BackColor = ColorTranslator.FromHtml("#2B3A8F") };
    readonly ModernButton gear = new() { Kind = BtnKind.Glass, IconName = "settings", Size = new Size(36, 36), TabStop = false };
    readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    readonly System.Globalization.CultureInfo ar = new("ar-IQ");
    const int GreetW = 380;

    public HomeHero(MainForm owner)
    {
        main = owner;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Navy;
        ar.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
        new ToolTip().SetToolTip(gear, "اختيار شاشات الوصول السريع");
        Controls.Add(tiles);
        Controls.Add(gear);
        gear.Click += (s, e) =>
        {
            using var d = new QuickAccessDialog(main);
            if (d.ShowModal() == DialogResult.OK) BuildTiles();
        };
        clock.Tick += (s, e) => Invalidate(new Rectangle(0, 0, GreetW + 40, Height));
        clock.Start();
        Disposed += (s, e) => clock.Dispose();
        BuildTiles();
    }

    public static List<string> Chosen() => Settings.Get("quick_access", DefaultQuick).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    void BuildTiles()
    {
        tiles.SuspendLayout();
        foreach (Control c in tiles.Controls.Cast<Control>().ToList()) c.Dispose();
        tiles.Controls.Clear();
        foreach (var name in Chosen())
        {
            var p = main?.Pages.FirstOrDefault(x => x.Text == name);
            if (p == null) continue;
            var t = new QuickTile { Title = p.Text, Group = p.Group == "" ? "الرئيسية" : p.Group, IconName = p.Icon, Tint = MainForm.TintOf(p.Group), Margin = new Padding(0, 0, 12, 12) };
            t.Click += (s, e) => main.Navigate(p);
            tiles.Controls.Add(t);
        }
        tiles.ResumeLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // البطاقات يسار الترحيب، والإعدادات بجانب عنوان «الوصول السريع»
        tiles.SetBounds(24, 62, Math.Max(200, Width - GreetW - 48), Height - 74);
        gear.Location = new Point(24, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new LinearGradientBrush(ClientRectangle, Navy, NavyDark, 0f)) g.FillRectangle(bg, ClientRectangle);
        Gfx.Hq(g);
        using (var b1 = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillEllipse(b1, Width - 220, -120, 300, 300);
        using (var b2 = new SolidBrush(Color.FromArgb(14, 247, 181, 44))) g.FillEllipse(b2, Width - GreetW - 90, Height - 110, 200, 200);

        // الوصول السريع
        int qw = Width - GreetW - 48;
        TextRenderer.DrawText(g, "الوصول السريع", Theme.FS(12), new Rectangle(70, 18, qw - 46, 30), Color.White, Gfx.RtlStart);
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawLine(pen, 24, 56, 24 + qw, 56);

        // الترحيب والساعة
        var now = DateTime.Now;
        int x = Width - GreetW - 10, w = GreetW - 20;
        string greet = now.Hour < 12 ? "صباح الخير" : "مساء الخير";
        TextRenderer.DrawText(g, Settings.Get("shop_name"), Theme.F(11), new Rectangle(x, 22, w, 26), ColorTranslator.FromHtml("#C7CEF0"), Gfx.RtlStart);
        TextRenderer.DrawText(g, greet, Theme.FS(26), new Rectangle(x, 50, w, 52), Theme.Amber, Gfx.RtlStart);
        TextRenderer.DrawText(g, now.ToString("dddd، d MMMM yyyy", ar), Theme.F(11), new Rectangle(x, 108, w, 26), Color.White, Gfx.RtlStart);
        TextRenderer.DrawText(g, now.ToString("hh:mm:ss tt", ar), Theme.FS(20), new Rectangle(x, 136, w, 40), Color.White, Gfx.RtlStart);
        TextRenderer.DrawText(g, $"مرحبًا {Session.UserName}", Theme.F(10), new Rectangle(x, 186, w, 24), Theme.Amber, Gfx.RtlStart);
        TextRenderer.DrawText(g, "الإصدار " + Application.ProductVersion.Split('+')[0], Theme.F(8.5f), new Rectangle(x, 212, w, 20), ColorTranslator.FromHtml("#8E98CF"), Gfx.RtlStart);
    }
}

/// <summary>بطاقة وصول سريع: اسم القسم، اسم الشاشة، وأيقونة بلون القسم</summary>
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
        Size = new Size(150, 150);
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(1, hover ? 1 : 4, Width - 3, Height - 6);
        Gfx.FillRound(g, r, 10, Color.White);
        if (hover) Gfx.DrawRound(g, r, 10, Theme.Amber, 2);
        TextRenderer.DrawText(g, Group, Theme.F(8.5f), new Rectangle(12, (int)r.Y + 12, Width - 26, 20), Theme.Subtle, Gfx.RtlStart);
        TextRenderer.DrawText(g, Title, Theme.FS(11), new Rectangle(8, (int)r.Y + 32, Width - 22, 50), Theme.Ink,
            TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        var ir = new RectangleF(14, r.Bottom - 54, 42, 42);
        Gfx.FillRound(g, ir, 10, Gfx.Mix(Tint, Color.White, 0.84f));
        Icons.Draw(g, IconName, ir, Tint, 22);
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
