using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Raseed;

public enum BtnKind { Primary, Secondary, Soft, Danger, Warning, Ghost, Dark }

/// <summary>زر حديث: زوايا دائرية، أيقونة، حالات مرور وضغط وتركيز</summary>
public class ModernButton : Button
{
    BtnKind kind = BtnKind.Primary;
    bool hover, down;

    [DefaultValue(BtnKind.Primary)]
    public BtnKind Kind { get => kind; set { kind = value; Invalidate(); } }
    [DefaultValue(null)] public string IconName { get; set; }
    [DefaultValue(8)] public int Radius { get; set; } = 8;
    /// <summary>عدد يظهر كشارة صغيرة فوق الزر (0 = مخفي)</summary>
    [DefaultValue(0)] public int Badge { get; set; }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = Theme.FS(10);
        Height = 38;
    }

    /// <summary>يوسّع الزر ليتسع للنص والأيقونة</summary>
    public void FitWidth(int min)
    {
        int text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        int icon = Icons.Get(IconName) != null && FontKit.HasIcons ? 18 + (text > 0 ? 8 : 0) : 0;
        Width = Math.Max(min, text + icon + (text > 0 ? 32 : 20));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    (Color Bg, Color Fg, Color Border) Colors()
    {
        float t = down ? 1f : hover ? 0.55f : 0f;
        return kind switch
        {
            BtnKind.Primary => (Gfx.Mix(Theme.Brand, Theme.BrandDark, t), Color.White, Color.Empty),
            BtnKind.Secondary => (Gfx.Mix(Theme.Surface, Theme.GraySoft, t), Theme.Ink, Theme.BorderStrong),
            BtnKind.Soft => (Gfx.Mix(Theme.BrandSoft, Theme.BrandSoft2, t), Theme.BrandDark, Color.Empty),
            BtnKind.Danger => (Gfx.Mix(Theme.DangerSoft, ColorTranslator.FromHtml("#F9D0D0"), t), ColorTranslator.FromHtml("#C0262D"), Color.Empty),
            BtnKind.Warning => (Gfx.Mix(Theme.WarningSoft, ColorTranslator.FromHtml("#FBE0B5"), t), ColorTranslator.FromHtml("#B45309"), Color.Empty),
            BtnKind.Ghost => (Gfx.Alpha(Theme.GraySoft, (int)(255 * t)), Theme.Text2, Color.Empty),
            BtnKind.Dark => (Gfx.Mix(Theme.SidebarHover, ColorTranslator.FromHtml("#243150"), t), Color.White, Color.Empty),
            _ => (Theme.Brand, Color.White, Color.Empty)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // الأزرار في WinForms «معتمة» فلا تُرسم خلفيتها تلقائيًا؛ نرسمها بلون الحاوية
        // (الشفافية المتداخلة ترسم بإزاحة خاطئة، وبدون مسح تظهر بقايا من المخزن المؤقت)
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var (bg, fg, bd) = Colors();
        if (!Enabled)
        {
            bg = bg.A == 0 ? bg : Gfx.Mix(bg, Theme.GraySoft, 0.6f);
            fg = Theme.Subtle;
        }
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (bg.A > 0) Gfx.FillRound(g, r, Radius, bg);
        if (!bd.IsEmpty) Gfx.DrawRound(g, r, Radius, bd);
        if (Focused && ShowFocusCues)
            Gfx.DrawRound(g, RectangleF.Inflate(r, -2.5f, -2.5f), Radius - 2, kind == BtnKind.Primary ? Gfx.Alpha(Color.White, 170) : Gfx.Alpha(Theme.Brand, 150), 1.5f);

        bool rtl = RightToLeft == RightToLeft.Yes;
        bool hasIcon = Icons.Get(IconName) != null && FontKit.HasIcons;
        int iconPx = 18, gap = string.IsNullOrEmpty(Text) || !hasIcon ? 0 : 8;
        int tw = string.IsNullOrEmpty(Text) ? 0 : Math.Min(Width - 16 - (hasIcon ? iconPx + gap : 0),
            TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
        int total = tw + (hasIcon ? iconPx + gap : 0);
        int x0 = (Width - total) / 2;
        Rectangle textRect, iconRect;
        if (rtl) { textRect = new Rectangle(x0, 0, tw, Height); iconRect = new Rectangle(x0 + tw + gap, (Height - iconPx) / 2, iconPx, iconPx); }
        else { iconRect = new Rectangle(x0, (Height - iconPx) / 2, iconPx, iconPx); textRect = new Rectangle(x0 + iconPx + gap, 0, tw, Height); }
        if (hasIcon) Icons.Draw(g, IconName, iconRect, fg, 17);
        if (tw > 0) TextRenderer.DrawText(g, Text, Font, textRect, fg, Gfx.Center);

        if (Badge > 0)
        {
            var txt = Badge > 99 ? "99+" : Badge.ToString();
            int bw = Math.Max(18, TextRenderer.MeasureText(txt, Theme.FS(8)).Width + 6);
            var br = new Rectangle(rtl ? 2 : Width - bw - 2, 2, bw, 18);
            Gfx.FillRound(g, br, 9, Theme.Danger);
            TextRenderer.DrawText(g, txt, Theme.FS(8), br, Color.White, Gfx.Center);
        }
    }
}

/// <summary>غلاف حديث لحقول الإدخال: إطار دائري بلون هادئ يتحول للون الهوية عند التركيز</summary>
public class InputBox : Panel
{
    public Control Inner { get; }
    public string LeadingIcon { get; set; }
    Control trailing;
    /// <summary>زر صغير داخل الحقل في نهايته (مثل إظهار كلمة المرور)</summary>
    public Control Trailing
    {
        get => trailing;
        set { if (trailing != null) Controls.Remove(trailing); trailing = value; if (value != null) { Controls.Add(value); value.BringToFront(); } Arrange(); }
    }
    readonly Panel clip;
    readonly Control arrow;
    bool focused, hover;
    public const int StdHeight = 40;

    public InputBox(Control c, int width, string icon = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Inner = c;
        LeadingIcon = icon;
        Width = Math.Max(60, width);
        Margin = new Padding(0);
        bool multi = c is TextBox { Multiline: true };
        Height = multi ? Math.Max(c.Height + 16, 64) : StdHeight;

        c.Font = c.Font == null || c.Font == Control.DefaultFont ? Theme.F(10) : c.Font;
        switch (c)
        {
            case TextBox t:
                t.BorderStyle = BorderStyle.None; t.BackColor = Theme.Surface;
                if (t.Multiline) t.ScrollBars = ScrollBars.None;   // التمرير بالعجلة والأسهم يكفي، وشريط التمرير القديم يشوّه الشكل
                break;
            case DateTimePicker d when d.Format == DateTimePickerFormat.Short:
                d.Format = DateTimePickerFormat.Custom; d.CustomFormat = "yyyy-MM-dd";   // صيغة تاريخ موحدة في كل البرنامج
                break;
            case NumericUpDown n:
                n.BorderStyle = BorderStyle.None; n.BackColor = Theme.Surface;
                if (n.Controls.Count > 0) n.Controls[0].Visible = false;   // إخفاء أسهم الزيادة (العجلة والأسهم تعمل)
                break;
            case ComboBox cb: cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Theme.Surface; break;
        }
        clip = new Panel { BackColor = Theme.Surface };
        clip.Controls.Add(c);
        Controls.Add(clip);
        if (c is ComboBox combo)
        {
            arrow = new ChevronBox(combo);
            clip.Controls.Add(arrow);
            arrow.BringToFront();
            combo.HandleCreated += (s, e) => Arrange();
        }
        c.Dock = DockStyle.None;
        c.Enter += (s, e) => { focused = true; Invalidate(); };
        c.Leave += (s, e) => { focused = false; Invalidate(); };
        c.MouseEnter += (s, e) => { hover = true; Invalidate(); };
        c.MouseLeave += (s, e) => { hover = false; Invalidate(); };
        c.EnabledChanged += (s, e) => SyncBack();
        c.SizeChanged += (s, e) => { if (!arranging) Arrange(); };
        Click += (s, e) => c.Focus();
        clip.Click += (s, e) => c.Focus();
    }

    void SyncBack()
    {
        var bg = Inner.Enabled ? Theme.Surface : Theme.SurfaceAlt;
        clip.BackColor = bg;
        if (Inner is not DateTimePicker) Inner.BackColor = bg;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Gfx.OpaqueBack(this));
    protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
    protected override void OnRightToLeftChanged(EventArgs e) { base.OnRightToLeftChanged(e); Arrange(); }

    bool arranging;
    void Arrange()
    {
        if (arranging || clip == null) return;
        arranging = true;
        try
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            int iconW = LeadingIcon != null && FontKit.HasIcons ? 28 : 0;
            int trailW = trailing != null ? trailing.Width + 4 : 0;
            int padS = 11 + iconW, padE = 11 + trailW;    // بداية (يمين) ونهاية (يسار)
            if (trailing != null) trailing.Location = new Point(rtl ? 6 : Width - 6 - trailing.Width, (Height - trailing.Height) / 2);
            int x = rtl ? padE : padS, w = Math.Max(10, Width - padS - padE);
            switch (Inner)
            {
                case TextBox { Multiline: true } t:
                    clip.Bounds = new Rectangle(x, 8, w, Height - 16);
                    t.Bounds = new Rectangle(0, 0, clip.Width, clip.Height);
                    break;
                case TextBox t:
                    {
                        int h = t.PreferredHeight;
                        clip.Bounds = new Rectangle(x, (Height - h) / 2, w, h);
                        t.Bounds = new Rectangle(0, 0, clip.Width, h);
                        break;
                    }
                case NumericUpDown n:
                    {
                        int h = n.PreferredHeight;
                        int spin = n.Controls.Count > 0 ? n.Controls[0].Width : 0;
                        clip.Bounds = new Rectangle(x, (Height - h) / 2, w, h);
                        // مساحة الأسهم المخفية تُدفع خارج المنطقة الظاهرة
                        bool spinLeft = rtl ^ (n.UpDownAlign == LeftRightAlignment.Left);
                        n.Bounds = new Rectangle(spinLeft ? -spin : 0, 0, clip.Width + spin, h);
                        break;
                    }
                case ComboBox cb:
                    {
                        int h = cb.Height;
                        // نقص 3 بكسل من كل جهة لإخفاء إطار القائمة الأصلي
                        clip.Bounds = new Rectangle(x - 3, (Height - (h - 6)) / 2, w + 6, h - 6);
                        cb.Bounds = new Rectangle(-3, -3, clip.Width + 6, h);
                        // موضع سهم القائمة الأصلي كما يرسمه النظام (يمين أو يسار حسب الاتجاه)
                        int aw = SystemInformation.VerticalScrollBarWidth + 4;
                        int ax = rtl ? 0 : clip.Width - aw;
                        if (cb.IsHandleCreated && ComboButton(cb, out var btn))
                        {
                            aw = btn.Width + 6;
                            ax = btn.X + cb.Left - 3 < clip.Width / 2 ? 0 : clip.Width - aw;
                        }
                        arrow.Bounds = new Rectangle(ax, 0, aw, clip.Height);
                        break;
                    }
                case DateTimePicker d:
                    {
                        int h = d.Height;
                        clip.Bounds = new Rectangle(x - 4, (Height - (h - 4)) / 2, w + 8, h - 4);
                        d.Bounds = new Rectangle(-2, -2, clip.Width + 4, h);
                        break;
                    }
                default:
                    clip.Bounds = new Rectangle(x, 4, w, Height - 8);
                    Inner.Bounds = new Rectangle(0, 0, clip.Width, clip.Height);
                    break;
            }
        }
        finally { arranging = false; }
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct COMBOBOXINFO { public int cbSize; public RECT rcItem, rcButton; public int stateButton; public IntPtr hwndCombo, hwndItem, hwndList; }
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

    static bool ComboButton(ComboBox cb, out Rectangle r)
    {
        r = Rectangle.Empty;
        try
        {
            var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };
            if (!GetComboBoxInfo(cb.Handle, ref info) || info.rcButton.R <= info.rcButton.L) return false;
            r = Rectangle.FromLTRB(info.rcButton.L, info.rcButton.T, info.rcButton.R, info.rcButton.B);
            return true;
        }
        catch { return false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Gfx.Hq(g);
        var r = new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f);
        if (focused) Gfx.DrawRound(g, RectangleF.Inflate(r, 1f, 1f), 10, Theme.BrandSoft2, 3f);
        Gfx.FillRound(g, r, 8, Inner.Enabled ? Theme.Surface : Theme.SurfaceAlt);
        Gfx.DrawRound(g, r, 8, focused ? Theme.Brand : hover ? ColorTranslator.FromHtml("#B8C2D0") : Theme.BorderStrong, focused ? 1.4f : 1f);
        if (LeadingIcon != null && FontKit.HasIcons)
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            var ir = new RectangleF(rtl ? Width - 36 : 10, (Height - 18) / 2f, 18, 18);
            Icons.Draw(g, LeadingIcon, ir, focused ? Theme.Brand : Theme.Subtle, 17);
        }
    }

    /// <summary>سهم القائمة المنسدلة بشكل موحد</summary>
    sealed class ChevronBox : Control
    {
        readonly ComboBox cb;
        public ChevronBox(ComboBox c)
        {
            cb = c;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            c.EnabledChanged += (s, e) => { BackColor = c.Enabled ? Theme.Surface : Theme.SurfaceAlt; Invalidate(); };
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!cb.Enabled) return;
            cb.Focus();
            cb.DroppedDown = !cb.DroppedDown;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (FontKit.HasIcons) Icons.Draw(e.Graphics, "chevron-down", new RectangleF(0, 0, Width, Height), Theme.Muted, 16);
            else
            {
                Gfx.Hq(e.Graphics);
                float cx = Width / 2f, cy = Height / 2f;
                using var pen = new Pen(Theme.Muted, 1.6f);
                e.Graphics.DrawLines(pen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
            }
        }
    }
}

/// <summary>مفتاح تشغيل/إيقاف حديث بدل مربع الاختيار</summary>
public class Toggle : CheckBox
{
    bool hover;
    public Toggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 34;
        Cursor = Cursors.Hand;
        Font = Theme.F(10);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        bool rtl = RightToLeft == RightToLeft.Yes;
        const int tw = 40, th = 22;
        var track = new RectangleF(rtl ? Width - tw - 2 : 2, (Height - th) / 2f, tw, th);
        var on = Checked;
        var col = !Enabled ? Theme.BorderStrong : on ? (hover ? Theme.BrandDark : Theme.Brand) : (hover ? ColorTranslator.FromHtml("#B8C2D0") : Theme.BorderStrong);
        Gfx.FillRound(g, track, th / 2f, col);
        float k = th - 6;
        float kx = on ^ rtl ? track.Right - k - 3 : track.X + 3;
        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, track.Y + 3, k, k);
        if (Focused && ShowFocusCues) Gfx.DrawRound(g, RectangleF.Inflate(track, 2, 2), th / 2f + 2, Theme.BrandSoft2, 2);
        var tr = rtl ? new Rectangle(0, 0, (int)track.X - 10, Height) : new Rectangle((int)track.Right + 10, 0, Width - (int)track.Right - 10, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Ink : Theme.Subtle, rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>بطاقة بيضاء بزوايا دائرية وظل خفيف، مع عنوان وأيقونة اختيارية</summary>
public class CardPanel : Panel
{
    string title, subtitle;
    public string IconName { get; set; }
    public Color IconColor { get; set; } = Theme.Brand;
    public int Radius { get; set; } = 12;
    public int HeaderHeight => string.IsNullOrEmpty(title) ? 0 : (string.IsNullOrEmpty(subtitle) ? 52 : 64);

    public string Title { get => title; set { title = value; UpdatePadding(); Invalidate(); } }
    public string Subtitle { get => subtitle; set { subtitle = value; UpdatePadding(); Invalidate(); } }

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
    }

    void UpdatePadding() => Padding = new Padding(16, 12 + HeaderHeight, 16, 16);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(1.5f, 0.5f, Width - 4f, Height - 4f);
        Gfx.Shadow(g, r, Radius);
        Gfx.FillRound(g, r, Radius, Theme.Surface);
        Gfx.DrawRound(g, r, Radius, Theme.Border);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (string.IsNullOrEmpty(title)) return;
        var g = e.Graphics;
        Gfx.Hq(g);
        bool rtl = RightToLeft == RightToLeft.Yes;
        int x = 18, iconBox = 0;
        if (IconName != null && FontKit.HasIcons)
        {
            iconBox = 36;
            var ir = new RectangleF(rtl ? Width - x - iconBox - 2 : x, 14, iconBox, iconBox);
            Gfx.FillRound(g, ir, 10, Gfx.Mix(IconColor, Color.White, 0.88f));
            Icons.Draw(g, IconName, ir, IconColor, 18);
            iconBox += 12;
        }
        var tr = rtl ? new Rectangle(18, 12, Width - 38 - iconBox, 26) : new Rectangle(x + iconBox, 12, Width - 38 - iconBox, 26);
        if (string.IsNullOrEmpty(subtitle)) tr.Y = 18;
        TextRenderer.DrawText(g, title, Theme.FS(11.5f), tr, Theme.Ink, rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sr = tr; sr.Y += 24; sr.Height = 22;
            TextRenderer.DrawText(g, subtitle, Theme.F(9), sr, Theme.Muted, rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>شريط أدوات على شكل بطاقة (يُستخدم عبر Theme.Bar)</summary>
public class ToolbarCard : FlowLayoutPanel
{
    public const int Gap = 12;
    public ToolbarCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        BackColor = Theme.Surface;
        Padding = new Padding(10, 8, 10, 8 + Gap);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(1.5f, 0.5f, Width - 4f, Height - Gap - 2f);
        Gfx.Shadow(g, r, 12);
        Gfx.FillRound(g, r, 12, Theme.Surface);
        Gfx.DrawRound(g, r, 12, Theme.Border);
    }
}

/// <summary>بطاقة مؤشر رقمي: أيقونة ملونة، عنوان، قيمة كبيرة، وملاحظة</summary>
public class KpiCard : Control
{
    public string Title { get; set; }
    public string Value { get; set; }
    public string Hint { get; set; }
    public string IconName { get; set; }
    public Color Accent { get; set; } = Theme.Brand;
    bool hover;

    public KpiCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(236, 118);
        Margin = new Padding(8);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(1.5f, 0.5f, Width - 4f, Height - 4f);
        Gfx.Shadow(g, r, 14);
        Gfx.FillRound(g, r, 14, Theme.Surface);
        Gfx.DrawRound(g, r, 14, hover && Cursor == Cursors.Hand ? Theme.BrandSoft2 : Theme.Border);

        bool rtl = RightToLeft == RightToLeft.Yes;
        var ir = new RectangleF(rtl ? 18 : Width - 60, 18, 40, 40);
        Gfx.FillRound(g, ir, 12, Gfx.Mix(Accent, Color.White, 0.88f));
        if (IconName != null) Icons.Draw(g, IconName, ir, Accent, 20);

        var flags = rtl ? Gfx.RtlStart : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        int tx = rtl ? 62 : 18, tw = Width - 18 - 62;
        TextRenderer.DrawText(g, Title, Theme.F(9.5f), new Rectangle(tx, 20, tw, 22), Theme.Muted, flags);
        var vf = Theme.FS(Value != null && Value.Length > 11 ? 15 : 18);
        TextRenderer.DrawText(g, Value, vf, new Rectangle(rtl ? 18 : 18, 46, Width - 36, 36), Theme.Ink, flags);
        if (!string.IsNullOrEmpty(Hint))
            TextRenderer.DrawText(g, Hint, Theme.F(8.5f), new Rectangle(18, Height - 32, Width - 36, 20), Accent, flags);
    }
}

/// <summary>تبويبات حديثة: أفقية (خط سفلي) أو عمودية (قائمة جانبية)</summary>
public class ModernTabs : Panel
{
    readonly TabStrip strip;
    readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly List<(string Title, string Icon, Control Page)> pages = new();
    int selected = -1;

    public event EventHandler SelectedIndexChanged;
    public bool Vertical { get; }

    public ModernTabs(bool vertical = false)
    {
        Vertical = vertical;
        BackColor = Theme.Bg;
        strip = new TabStrip(this) { Dock = vertical ? DockStyle.Right : DockStyle.Top };
        if (vertical) strip.Width = 214;
        Controls.Add(body);
        if (vertical) Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Bg });
        Controls.Add(strip);
    }

    public int Count => pages.Count;
    internal IReadOnlyList<(string Title, string Icon, Control Page)> Pages => pages;

    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value < 0 || value >= pages.Count || value == selected) return;
            selected = value;
            for (int i = 0; i < pages.Count; i++) pages[i].Page.Visible = i == value;
            strip.Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>إضافة صفحة جاهزة من نوع TabPage (تُنقل عناصرها إلى الصفحة الجديدة)</summary>
    public void Add(TabPage tp, string icon = null)
    {
        var panel = new Panel { BackColor = tp.BackColor == Color.Transparent ? Theme.Bg : tp.BackColor, Font = tp.Font };
        var ctrls = tp.Controls.Cast<Control>().ToArray();
        tp.Controls.Clear();
        panel.Controls.AddRange(ctrls);
        Add(tp.Text, panel, icon);
    }

    public void Add(string title, Control page, string icon = null)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        body.Controls.Add(page);
        pages.Add((title, icon, page));
        strip.Relayout();
        if (selected < 0) SelectedIndex = 0;
    }

    sealed class TabStrip : Control
    {
        readonly ModernTabs owner;
        readonly List<Rectangle> rects = new();
        int hover = -1;
        const int ItemH = 44;

        public TabStrip(ModernTabs o)
        {
            owner = o;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = ItemH + 12;
            Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        public void Relayout()
        {
            rects.Clear();
            var font = Theme.FS(10);
            if (owner.Vertical)
            {
                int y = 12;
                foreach (var p in owner.pages) { rects.Add(new Rectangle(8, y, Width - 16, 42)); y += 46; }
            }
            else
            {
                int x = Width - 4, y = 4, rows = 1;
                foreach (var p in owner.pages)
                {
                    int w = TextRenderer.MeasureText(p.Title, font).Width + 28 + (p.Icon != null && FontKit.HasIcons ? 24 : 0);
                    if (x - w < 4 && x < Width - 4) { x = Width - 4; y += ItemH; rows++; }
                    rects.Add(new Rectangle(x - w, y, w, ItemH));
                    x -= w + 4;
                }
                int h = rows * ItemH + 16;
                if (Height != h) Height = h;
            }
            Invalidate();
        }

        int HitTest(Point p) { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = HitTest(e.Location); if (h >= 0) owner.SelectedIndex = h; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            if (owner.Vertical)
            {
                var card = new RectangleF(1.5f, 0.5f, Width - 4f, Math.Min(Height - 4f, rects.Count * 46 + 24));
                Gfx.Shadow(g, card, 12);
                Gfx.FillRound(g, card, 12, Theme.Surface);
                Gfx.DrawRound(g, card, 12, Theme.Border);
            }
            else
            {
                using var pen = new Pen(Theme.Border);
                g.DrawLine(pen, 0, Height - 13, Width, Height - 13);
            }
            for (int i = 0; i < rects.Count && i < owner.pages.Count; i++)
            {
                var r = rects[i];
                var (title, icon, _) = owner.pages[i];
                bool sel = i == owner.selected, hov = i == hover;
                Color fg = sel ? Theme.BrandDark : hov ? Theme.Ink : Theme.Muted;
                if (owner.Vertical)
                {
                    if (sel) Gfx.FillRound(g, r, 8, Theme.BrandSoft);
                    else if (hov) Gfx.FillRound(g, r, 8, Theme.SurfaceAlt);
                    if (sel) Gfx.FillRound(g, new RectangleF(r.Right - 4, r.Y + 10, 4, r.Height - 20), 2, Theme.Brand);
                }
                else
                {
                    if (hov && !sel) Gfx.FillRound(g, new RectangleF(r.X, r.Y + 4, r.Width, r.Height - 10), 8, ColorTranslator.FromHtml("#E9EEF4"));
                    if (sel) Gfx.FillRound(g, new RectangleF(r.X + 10, r.Bottom - 5, r.Width - 20, 3), 1.5f, Theme.Brand);
                }
                int iconW = icon != null && FontKit.HasIcons ? 24 : 0;
                if (iconW > 0) Icons.Draw(g, icon, new RectangleF(r.Right - 14 - 18, r.Y + (r.Height - 18) / 2f - (owner.Vertical ? 0 : 2), 18, 18), sel ? Theme.Brand : fg, 17);
                var tr = new Rectangle(r.X + 8, r.Y - (owner.Vertical ? 0 : 2), r.Width - 22 - iconW, r.Height);
                TextRenderer.DrawText(g, title, sel ? Theme.FS(10) : Theme.F(10), tr, fg, owner.Vertical ? Gfx.RtlStart : Gfx.Center);
            }
        }
    }
}

/// <summary>عنصر في القائمة الجانبية</summary>
public class NavButton : Control
{
    bool hover, active;
    public string IconName { get; set; }
    public string Group { get; set; }
    public bool Active { get => active; set { active = value; Invalidate(); } }

    public NavButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Sidebar;
        Height = 38;
        Cursor = Cursors.Hand;
        Font = Theme.F(10);
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.Hq(g);
        var r = new RectangleF(0.5f, 1f, Width - 1.5f, Height - 2f);
        if (active)
        {
            using var br = new LinearGradientBrush(r, Theme.Brand, ColorTranslator.FromHtml("#12A594"), LinearGradientMode.Horizontal);
            using var p = Gfx.Round(r, 9);
            g.FillPath(br, p);
        }
        else if (hover) Gfx.FillRound(g, r, 9, Theme.SidebarHover);
        if (Focused && ShowFocusCues && !active) Gfx.DrawRound(g, r, 9, Theme.SidebarMuted);
        var fg = active ? Color.White : hover ? Color.White : Theme.SidebarText;
        Icons.Draw(g, IconName, new RectangleF(Width - 38, (Height - 20) / 2f, 20, 20), active ? Color.White : hover ? Color.White : Theme.SidebarMuted.IsEmpty ? fg : Gfx.Mix(Theme.SidebarText, Theme.SidebarMuted, 0.35f), 18);
        TextRenderer.DrawText(g, Text, active ? Theme.FS(10) : Theme.F(10), new Rectangle(8, 0, Width - 54, Height), fg, Gfx.RtlStart);
    }
}

/// <summary>مؤشر صغير: أيقونة ملونة، عدد، وعنوان — يُنقر للانتقال</summary>
public class StatChip : Control
{
    public long Count { get; set; }
    public string IconName { get; set; }
    public Color Accent { get; set; } = Theme.Brand;
    bool hover;

    public StatChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var active = Count > 0;
        Gfx.FillRound(g, r, 12, hover ? Gfx.Mix(Accent, Color.White, 0.9f) : Theme.SurfaceAlt);
        Gfx.DrawRound(g, r, 12, hover ? Gfx.Mix(Accent, Color.White, 0.6f) : Theme.Border);
        var ir = new RectangleF(Width - 50, (Height - 38) / 2f, 38, 38);
        Gfx.FillRound(g, ir, 10, active ? Gfx.Mix(Accent, Color.White, 0.85f) : Theme.GraySoft);
        Icons.Draw(g, IconName, ir, active ? Accent : Theme.Subtle, 18);
        TextRenderer.DrawText(g, Count.ToString("#,0"), Theme.FS(15), new Rectangle(10, 6, Width - 70, 28), active ? Theme.Ink : Theme.Subtle, Gfx.RtlStart);
        TextRenderer.DrawText(g, Text, Theme.F(8.5f), new Rectangle(10, 33, Width - 70, 22), Theme.Muted, Gfx.RtlStart);
    }
}

/// <summary>مخطط أعمدة بسيط وأنيق (مبيعات الأيام الأخيرة)</summary>
public class BarChart : Control
{
    public List<(string Label, double Value)> Data { get; set; } = new();
    public Color BarColor { get; set; } = Theme.Brand;
    int hover = -1;
    readonly List<RectangleF> bars = new();

    public BarChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1;
        for (int i = 0; i < bars.Count; i++) if (e.X >= bars[i].X - 4 && e.X <= bars[i].Right + 4) h = i;
        if (h != hover) { hover = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Gfx.Hq(g);
        bars.Clear();
        if (Data.Count == 0) return;
        double max = Math.Max(1, Data.Max(d => d.Value));
        // تقريب الحد الأعلى لرقم جميل
        double mag = Math.Pow(10, Math.Floor(Math.Log10(max)));
        double top = Math.Ceiling(max / mag) * mag;
        int left = 8, right = 64, topPad = 26, bottom = 30;
        var plot = new RectangleF(left, topPad, Width - left - right, Height - topPad - bottom);
        using var grid = new Pen(Theme.Border) { DashStyle = DashStyle.Dash };
        var lf = Theme.F(8.5f);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(grid, plot.X, y, plot.Right, y);
            TextRenderer.DrawText(g, Short(top * i / 4), lf, new Rectangle((int)plot.Right + 6, (int)y - 10, right - 8, 20), Theme.Subtle,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        int n = Data.Count;
        float slot = plot.Width / n, bw = Math.Min(34, slot * 0.56f);
        for (int i = 0; i < n; i++)
        {
            // من اليمين إلى اليسار: أقدم يوم على اليمين
            float cx = plot.Right - slot * i - slot / 2f;
            float h = (float)(plot.Height * Data[i].Value / top);
            var br = new RectangleF(cx - bw / 2, plot.Bottom - Math.Max(h, 2), bw, Math.Max(h, 2));
            bars.Add(br);
            var col = i == hover ? Theme.BrandDark : i == n - 1 ? Theme.Brand : Gfx.Mix(Theme.Brand, Color.White, 0.45f);
            using (var path = TopRound(br, Math.Min(6, bw / 2)))
            using (var b = new SolidBrush(col)) g.FillPath(b, path);
            int every = slot < 30 ? 3 : slot < 46 ? 2 : 1;
            if (i % every == (n - 1) % every || i == hover)
                TextRenderer.DrawText(g, Data[i].Label, lf, new Rectangle((int)(cx - slot / 2) - 10, (int)plot.Bottom + 6, (int)slot + 20, 20),
                    i == hover ? Theme.Ink : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        if (hover >= 0 && hover < bars.Count)
        {
            var b = bars[hover];
            var txt = Ui.M(Data[hover].Value);
            var sz = TextRenderer.MeasureText(txt, Theme.FS(9));
            var tip = new RectangleF(b.X + b.Width / 2 - sz.Width / 2f - 10, Math.Max(0, b.Y - 32), sz.Width + 20, 26);
            tip.X = Math.Max(0, Math.Min(Width - tip.Width, tip.X));
            Gfx.FillRound(g, tip, 7, Theme.Ink);
            TextRenderer.DrawText(g, txt, Theme.FS(9), Rectangle.Round(tip), Color.White, Gfx.Center);
        }
    }

    static GraphicsPath TopRound(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        if (r.Height < d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    static string Short(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.#") + " م" : v >= 1000 ? (v / 1000).ToString("0.#") + " ألف" : v.ToString("0");
}

/// <summary>صورة رمزية دائرية بالأحرف الأولى</summary>
public static class Avatar
{
    public static void Draw(Graphics g, RectangleF r, string name, Color bg)
    {
        Gfx.Hq(g);
        using (var b = new SolidBrush(bg)) g.FillEllipse(b, r);
        var initials = Initials(name);
        TextRenderer.DrawText(g, initials, Theme.FS(r.Height > 36 ? 12 : 10), Rectangle.Round(r), Color.White, Gfx.Center);
    }

    public static string Initials(string name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "؟";
        return parts.Length == 1 ? parts[0][..1] : parts[0][..1] + " " + parts[1][..1];
    }
}

/// <summary>رقم بارز بعنوان صغير فوقه (يُرسم ذاتياً لضمان المحاذاة من اليمين)</summary>
public class StatLabel : Control
{
    string caption = "", value = "";
    Color valueColor = Theme.Ink;
    public string Caption { get => caption; set { caption = value; Invalidate(); } }
    public string Value { get => value; set { this.value = value; Invalidate(); } }
    public Color ValueColor { get => valueColor; set { valueColor = value; Invalidate(); } }
    public float ValueSize { get; set; } = 15f;

    public StatLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(150, 64);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        TextRenderer.DrawText(g, caption, Theme.F(9), new Rectangle(2, 2, Width - 4, 22), Theme.Muted, Gfx.RtlStart);
        TextRenderer.DrawText(g, value, Theme.FS(ValueSize), new Rectangle(2, 24, Width - 4, Height - 26), valueColor, Gfx.RtlStart);
    }
}
