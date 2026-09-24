using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Raseed;

/// <summary>هوية البرنامج البصرية: الألوان والخطوط وأنماط الجداول والأشرطة</summary>
public static class Theme
{
    static Color C(string hex) => ColorTranslator.FromHtml(hex);

    // ---- الهوية ----
    public static readonly Color Brand = C("#0F8F83");          // أخضر مزرق (لون «الرصيد»)
    public static readonly Color BrandDark = C("#0B7168");
    public static readonly Color BrandSoft = C("#E7F5F3");
    public static readonly Color BrandSoft2 = C("#CBEBE6");

    // ---- الشريط الجانبي ----
    public static readonly Color Sidebar = C("#0B1324");
    public static readonly Color SidebarHover = C("#18233A");
    public static readonly Color SidebarText = C("#B4BFD2");
    public static readonly Color SidebarMuted = C("#5F6B84");

    // ---- الأسطح والنصوص ----
    public static readonly Color Bg = C("#F3F5F9");
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = C("#F8FAFC");
    public static readonly Color Border = C("#E3E8EF");
    public static readonly Color BorderStrong = C("#D3DAE4");
    public static readonly Color Ink = C("#0F172A");
    public static readonly Color Text2 = C("#334155");
    public static readonly Color Muted = C("#64748B");
    public static readonly Color Subtle = C("#94A3B8");

    // ---- حالات ----
    public static readonly Color Success = C("#16A34A"), SuccessSoft = C("#E8F7EE");
    public static readonly Color Danger = C("#DC2626"), DangerSoft = C("#FDECEC");
    public static readonly Color Warning = C("#D97706"), WarningSoft = C("#FEF3E2");
    public static readonly Color Info = C("#2563EB"), InfoSoft = C("#EAF1FE");
    public static readonly Color Purple = C("#7C3AED"), PurpleSoft = C("#F2ECFE");
    public static readonly Color Gray = C("#64748B"), GraySoft = C("#EEF2F6");

    // أسماء قديمة ما زالت مستخدمة في الشاشات
    public static readonly Color Accent = Brand;
    public static readonly Color Primary = Sidebar;
    public static readonly Color PrimaryHover = SidebarHover;

    public static Font F(float size = 10f, FontStyle style = FontStyle.Regular) => FontKit.Get(size, style);
    /// <summary>وزن نصف عريض للعناوين والأزرار</summary>
    public static Font FS(float size = 10f) => FontKit.Get(size, FontStyle.Regular, true);

    /// <summary>زر حديث؛ اللون القديم يحدد نوعه، والأيقونة تُختار من نص الزر تلقائيًا</summary>
    public static ModernButton Btn(string text, Color? color = null, int width = 130, string icon = null)
    {
        var kind = color is Color c ? KindFor(c) : BtnKind.Primary;
        var b = new ModernButton { Text = text, Kind = kind, IconName = icon ?? AutoIcon(text), Margin = new Padding(4, 6, 4, 4) };
        b.FitWidth(width);
        return b;
    }

    static BtnKind KindFor(Color c)
    {
        if (c == Success || c == Brand) return c == Brand ? BtnKind.Soft : BtnKind.Primary;
        if (c == Danger) return BtnKind.Danger;
        if (c == Warning) return BtnKind.Warning;
        if (c == Gray || c == Purple) return BtnKind.Secondary;
        return BtnKind.Primary;
    }

    static readonly (string Key, string Icon)[] iconWords =
    {
        ("حذف السطر", "x"), ("حذف", "trash-2"), ("إرجاع قطعة", "package-plus"), ("صرف قطعة", "package-minus"),
        ("طباعة تجريبية", "printer"), ("طباع", "printer"), ("Excel", "file-spreadsheet"), ("واتساب", "message-circle"),
        ("تذكير", "send"), ("إشعار", "send"), ("تعديل", "pencil"), ("حفظ", "save"), ("جديد", "plus"), ("إضافة", "plus"),
        ("استلام", "inbox"), ("تأكيد التسليم", "check"), ("تسليم", "handshake"), ("تم التسليم", "check"), ("راجع", "undo-2"),
        ("تغيير الحالة", "repeat"), ("مناقلة", "arrow-left-right"), ("إتلاف", "ban"), ("توليد", "barcode"),
        ("العدد = الرصيد", "boxes"), ("تصفير", "rotate-ccw"), ("معاينة", "eye"), ("تحميل", "download"), ("اعتماد", "badge-check"),
        ("احتساب", "calculator"), ("الكل حاضر", "list-checks"), ("نسخ احتياطي", "cloud-upload"), ("استعادة", "rotate-ccw"),
        ("فتح مجلد", "folder-open"), ("اختبار", "send"), ("رابط تطبيق", "smartphone"), ("حصص", "hand-coins"),
        ("راتب", "banknote"), ("رواتب", "banknote"), ("تسديد", "wallet"), ("صرف", "banknote"), ("عرض", "eye"), ("تحديث", "refresh-cw"),
        ("بحث", "search"), ("موافق", "check"), ("إلغاء", "x"), ("دخول", "log-in"), ("فاتورة", "receipt-text"),
    };

    public static string AutoIcon(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var (k, i) in iconWords) if (text.Contains(k)) return i;
        return null;
    }

    /// <summary>كان عنوانًا كبيرًا مكررًا؛ العنوان الآن في رأس النافذة الرئيسية، فيبقى فاصل صغير فقط</summary>
    public static Control Title(string text) => new Panel { Dock = DockStyle.Top, Height = 2, Tag = text };

    /// <summary>شريط أدوات بشكل بطاقة بيضاء بزوايا دائرية</summary>
    public static FlowLayoutPanel Bar() => new ToolbarCard { Dock = DockStyle.Top };

    /// <summary>بطاقة مؤشر (رقم كبير مع أيقونة)</summary>
    public static Control Card(string title, string value, Color color, string icon = null, string hint = null) =>
        new KpiCard { Title = title, Value = value, Accent = color, IconName = icon, Hint = hint };

    // ================= الجداول =================
    public static void Grid(DataGridView g, bool readOnly = true)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
        g.ColumnHeadersDefaultCellStyle.Font = FS(9.5f);
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 44;
        g.RowTemplate.Height = 40;
        g.DefaultCellStyle.Font = F(10);
        g.DefaultCellStyle.ForeColor = Ink;
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        g.DefaultCellStyle.SelectionBackColor = BrandSoft;
        g.DefaultCellStyle.SelectionForeColor = Ink;
        g.AlternatingRowsDefaultCellStyle.BackColor = Surface;
        g.GridColor = C("#EDF1F5");
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToResizeRows = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.ReadOnly = readOnly;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.Font = F(10);
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);

        g.DataBindingComplete += (s, e) =>
        {
            // نسخة من القائمة: تغيير العرض قد يعيد ربط جدول آخر أثناء المرور على الأعمدة
            foreach (var c in g.Columns.Cast<DataGridViewColumn>().ToList())
            {
                if (c.DataGridView == null) continue;
                if (c.Name == "id") c.Visible = false;
                // حد أدنى للعرض حسب العنوان: شريط تمرير أفقي بدل «...» في الشاشات الصغيرة
                int head = TextRenderer.MeasureText(c.HeaderText ?? "", FS(9.5f)).Width + 28;
                // عرض أطول قيمة في أول الصفوف (التواريخ مثلاً) حتى لا تُقص
                int content = 0;
                if (c.Visible)
                    for (int i = 0; i < Math.Min(g.Rows.Count, 25); i++)
                    {
                        var v = g.Rows[i].Cells[c.Index].FormattedValue as string;
                        if (!string.IsNullOrEmpty(v)) content = Math.Max(content, TextRenderer.MeasureText(v, F(10)).Width + 28);
                    }
                int min = Math.Max(c.ValueType == typeof(double) || c.ValueType == typeof(long) ? 80 : 100, Math.Min(Math.Max(head, content), 240));
                try { if (c.DataGridView != null && c.MinimumWidth != min) c.MinimumWidth = min; } catch { /* العمود يُعاد بناؤه أثناء الربط */ }
                if (c.ValueType == typeof(double) || c.ValueType == typeof(long))
                {
                    if (c.ValueType == typeof(double)) c.DefaultCellStyle.Format = "#,0.##";
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            }
        };

        // تمييز الصف تحت المؤشر
        int hover = -1;
        g.CellMouseEnter += (s, e) => { if (e.RowIndex != hover) { int old = hover; hover = e.RowIndex; InvalidateRow(g, old); InvalidateRow(g, hover); } };
        g.MouseLeave += (s, e) => { int old = hover; hover = -1; InvalidateRow(g, old); };
        var hoverColor = C("#F5F8FB");
        g.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (e.RowIndex == hover) e.CellStyle.BackColor = hoverColor;
            // اتجاه صحيح للقيم اللاتينية داخل جدول عربي: «2026-09-24 14:00» لا «14:00 2026-09-24»، و«-45,000» لا «45,000-»
            const string LRM = "\u200E";
            if (e.Value is double d && d < 0)
            {
                e.Value = LRM + d.ToString(string.IsNullOrEmpty(e.CellStyle.Format) ? "#,0.##" : e.CellStyle.Format);
                e.FormattingApplied = true;
            }
            else if (e.Value is string str && str.Length > 4 && char.IsDigit(str[0]) && (str.Contains(' ') || str.Contains(':')))
            {
                e.Value = LRM + str;
                e.FormattingApplied = true;
            }
        };

        // شارات ملونة لأعمدة الحالة
        g.CellPainting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var col = g.Columns[e.ColumnIndex];
            if (col.Name is not ("الحالة" or "التنبيه" or "النوع")) return;
            var text = Convert.ToString(e.FormattedValue);
            if (string.IsNullOrEmpty(text)) return;
            var (fg, bg) = StatusColors(text);
            if (fg == Color.Empty) return;
            e.PaintBackground(e.CellBounds, true);
            var gr = e.Graphics;
            Gfx.Hq(gr);
            var font = FS(9);
            var sz = TextRenderer.MeasureText(gr, text, font, Size.Empty, TextFormatFlags.NoPadding);
            int w = Math.Min(e.CellBounds.Width - 12, sz.Width + 22), h = 26;
            var r = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - w) / 2, e.CellBounds.Y + (e.CellBounds.Height - h) / 2, w, h);
            Gfx.FillRound(gr, r, h / 2f, bg);
            TextRenderer.DrawText(gr, text, font, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.RightToLeft);
            e.Handled = true;
        };

        // رسالة «لا توجد بيانات»
        g.Paint += (s, e) =>
        {
            if (g.Rows.Count > 0 || g.Columns.Count == 0) return;
            int top = g.ColumnHeadersVisible ? g.ColumnHeadersHeight : 0;
            var area = new Rectangle(0, top, g.Width, g.Height - top);
            if (area.Height < 90) return;
            Gfx.Hq(e.Graphics);
            var circle = new RectangleF(area.X + area.Width / 2f - 28, area.Y + area.Height / 2f - 50, 56, 56);
            e.Graphics.FillEllipse(new SolidBrush(GraySoft), circle);
            Icons.Draw(e.Graphics, "inbox", circle, Subtle, 26);
            TextRenderer.DrawText(e.Graphics, "لا توجد بيانات لعرضها", FS(10.5f), new Rectangle(area.X, (int)circle.Bottom + 10, area.Width, 26), Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.RightToLeft);
        };
    }

    static void InvalidateRow(DataGridView g, int i)
    {
        if (i >= 0 && i < g.Rows.Count) g.InvalidateRow(i);
    }

    /// <summary>ألوان الشارة حسب نص الحالة</summary>
    public static (Color Fg, Color Bg) StatusColors(string s)
    {
        if (s is "جاهز" or "مسدد" or "فعّال" or "فعّالة" or "على الملاك" or "حاضر" or "تم التسليم" or "بيع" or "قبض" or "جهاز جاهز للتسليم") return (C("#15803D"), SuccessSoft);
        if (s is "متأخر" or "لا يصلح" or "ملغي" or "موقوف" or "متوقفة" or "منفك" or "غائب" or "منتهية الصلاحية" or "راجع" or "إتلاف" or "صرف" or "مصروف") return (C("#B91C1C"), DangerSoft);
        if (s is "بانتظار قطعة" or "قيد الفحص" or "قيد التصليح" or "قاربت على الانتهاء" or "قسط مستحق" or "قيد التوصيل" or "إجازة" or "مخزون منخفض" or "إرجاع بيع" or "إرجاع شراء") return (C("#B45309"), WarningSoft);
        if (s is "مستلم" or "قائم" or "شراء" or "مدير" or "تحويل" or "صيرفة" or "راتب" or "سلفة" or "دفعة فاتورة") return (C("#1D4ED8"), InfoSoft);
        if (s is "صيانة" or "عربون صيانة" or "توزيع أرباح") return (C("#6D28D9"), PurpleSoft);
        if (s is "مكافأة" or "تسديد قسط") return (C("#15803D"), SuccessSoft);
        if (s is "خصم") return (C("#B91C1C"), DangerSoft);
        if (s is "مستخدم" or "عطلة") return (Text2, GraySoft);
        return (Color.Empty, Color.Empty);
    }
}

/// <summary>أدوات الرسم المشتركة</summary>
public static class Gfx
{
    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Max(0, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
        if (rad < 0.5f) { p.AddRectangle(r); return p; }
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
    {
        using var p = Round(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, RectangleF r, float rad, Color c, float w = 1f)
    {
        using var p = Round(r, rad);
        using var pen = new Pen(c, w);
        g.DrawPath(pen, p);
    }

    /// <summary>ظل ناعم أسفل البطاقة</summary>
    public static void Shadow(Graphics g, RectangleF r, float rad)
    {
        for (int i = 1; i <= 3; i++)
            FillRound(g, new RectangleF(r.X - i + 1, r.Y + i, r.Width + 2 * i - 2, r.Height + i - 1), rad + i, Color.FromArgb(7 - i, 15, 23, 42));
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);

    /// <summary>أول لون خلفية معتم لدى الآباء (للزوايا الدائرية)</summary>
    public static Color OpaqueBack(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.Surface;
    }

    public const TextFormatFlags RtlStart = TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter |
                                           TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
    public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter |
                                         TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
}
