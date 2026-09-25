using System.Runtime.InteropServices;
using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>زر في القائمة الجانبية مع عدّاد (الطلبات المفتوحة، الديون...)</summary>
public class NavBtn : NavSection
{
    int count;
    public int Count { get => count; set { if (count != value) { count = value; Invalidate(); } } }
    public Color BadgeColor { get; set; } = Theme.Brand;

    public NavBtn() { Expandable = false; Height = 44; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (count <= 0 || Rail) return;
        var g = e.Graphics;
        Gfx.Hq(g);
        var text = count > 999 ? "999+" : count.ToString();
        var f = Theme.FS(8.5f);
        int w = Math.Max(S(24), TextRenderer.MeasureText(text, f).Width + S(10)), h = S(22);
        var r = new Rectangle(S(20), (Height - h) / 2, w, h);
        Gfx.FillRound(g, r, h / 2f, BadgeColor);
        TextRenderer.DrawText(g, text, f, r, Color.White, Gfx.Center);
    }
}

/// <summary>
/// النافذة الرئيسية: قائمة جانبية بالصفحات وعدّاداتها، شريط علوي بالعنوان والأزرار السريعة،
/// وتُحدَّث الصفحة المفتوحة تلقائياً عند أي تعديل في البيانات.
/// </summary>
public class MainForm : BaseForm
{
    public static MainForm Instance { get; private set; }

    record Def(string Key, string Title, string Icon, Color Tint, Func<Page> Make);
    static readonly Color Blue = ColorTranslator.FromHtml("#2B55C9"), Red = ColorTranslator.FromHtml("#C43F2C"), Green = ColorTranslator.FromHtml("#1D8657"),
        Amber = ColorTranslator.FromHtml("#E2952B"), Violet = ColorTranslator.FromHtml("#6A4CC2"), Teal = ColorTranslator.FromHtml("#0D7C86"), Slate = ColorTranslator.FromHtml("#5E6A7E");

    readonly List<Def> defs = new()
    {
        new("dashboard", "الرئيسية", "house", Blue, () => new DashboardPage()),
        new("orders", "الطلبات", "clipboard-list", Blue, () => new OrdersPage()),
        new("tech", "شاشة الفني", "wrench", Teal, () => new TechPage()),
        new("debts", "الديون المستحقة", "wallet", Red, () => new DebtsPage()),
        new("customers", "الزبائن", "users", Violet, () => new CustomersPage()),
        new("inventory", "قطع الغيار والأسعار", "package", Amber, () => new InventoryPage()),
        new("suppliers", "حسابات الموردين", "store", Teal, () => new SuppliersPage()),
        new("accounts", "التجار والشركات", "briefcase", Violet, () => new AccountsPage()),
        new("reports", "التقارير والمصاريف", "chart-column", Green, () => new ReportsPage()),
        new("staff", "الموظفون والرواتب", "id-card", Slate, () => new StaffPage()),
    };

    readonly Panel side = new() { Dock = DockStyle.Left, Width = 250, BackColor = Theme.Sidebar };
    readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    readonly Dictionary<string, NavBtn> nav = new();
    readonly Dictionary<string, Page> pages = new();
    readonly HashSet<string> dirty = new();
    readonly TopBar top;
    readonly Panel logo;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    NavBtn trashBtn, defectsBtn, remindersBtn;
    string current;
    bool reloadQueued;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm()
    {
        Instance = this;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 640);
        KeyPreview = true;
        UpdateShop();

        // ---------- الشعار ----------
        logo = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Sidebar };
        logo.Paint += (s, e) =>
        {
            var g = e.Graphics;
            Gfx.Hq(g);
            int box = S(40);
            var mark = new RectangleF(logo.Width - S(20) - box, (logo.Height - box) / 2f, box, box);
            Gfx.FillRound(g, mark, S(11f), Theme.Brand);
            Icons.Draw(g, "wrench", mark, Color.White, 20);
            int tx = (int)mark.X - S(12);
            TextRenderer.DrawText(g, Store.ShopName, Theme.FS(13), new Rectangle(S(12), (int)mark.Y - S(3), tx - S(12), S(26)), Theme.SidebarText, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "إدارة طلبات الصيانة", Theme.F(8.5f), new Rectangle(S(12), (int)mark.Y + S(22), tx - S(12), S(20)), Theme.SidebarMuted, Gfx.RtlStart);
            using var pen = new Pen(Theme.SidebarBorder);
            g.DrawLine(pen, S(14), logo.Height - 1, logo.Width - S(14), logo.Height - 1);
        };

        // ---------- القائمة ----------
        var list = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Sidebar };
        var items = new List<Control> { new Panel { Height = 8, BackColor = Theme.Sidebar } };
        foreach (var d in defs)
        {
            var b = new NavBtn { Text = d.Title, IconName = d.Icon, Tint = d.Tint, BadgeColor = d.Key == "debts" ? Red : d.Key == "suppliers" ? Teal : d.Key == "inventory" ? Amber : Blue };
            b.Click += (s, e) => Go(d.Key);
            nav[d.Key] = b;
            items.Add(b);
            if (d.Key == "dashboard") items.Add(new NavLabel { Text = "العمل اليومي" });
            if (d.Key == "customers") items.Add(new NavLabel { Text = "الحسابات" });
        }
        items.Add(new NavLabel { Text = "الأدوات" });
        NavBtn Tool(string text, string icon, Color tint, Action run)
        {
            var b = new NavBtn { Text = text, IconName = icon, Tint = tint, BadgeColor = Slate };
            b.Click += (s, e) => run();
            items.Add(b);
            return b;
        }
        Tool("تقفيل اليوم", "receipt", Green, CloseDayDialog.Open);
        remindersBtn = Tool("التذكيرات", "bell", Amber, RemindersDialog.Open);
        remindersBtn.BadgeColor = Red;
        defectsBtn = Tool("القطع المعيبة", "triangle-alert", Amber, DefectsDialog.Open);
        defectsBtn.BadgeColor = Amber;
        Tool("تعريفات الطابعات", "printer", Slate, DriversDialog.Open);
        trashBtn = Tool("المحذوفات", "trash-2", Red, TrashDialog.Open);
        Tool("الإعدادات", "settings", Slate, SettingsDialog.Open);
        items.Add(new Panel { Height = 12, BackColor = Theme.Sidebar });
        // الإضافة بترتيب عكسي لأن الالتصاق بالأعلى يضع الأحدث فوق
        for (int i = items.Count - 1; i >= 0; i--) { items[i].Dock = DockStyle.Top; list.Controls.Add(items[i]); }

        var foot = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Sidebar, Padding = new Padding(14, 12, 14, 12) };
        var bNew = new ModernButton { Text = "طلب جديد", IconName = "plus", Kind = BtnKind.Primary, Dock = DockStyle.Fill, TabStop = false };
        bNew.Click += (s, e) => Acts.New();
        foot.Controls.Add(bNew);
        side.Controls.Add(list);
        side.Controls.Add(foot);
        side.Controls.Add(logo);
        side.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.SidebarBorder });

        // ---------- الشريط العلوي ----------
        top = new TopBar();
        top.Search.Click += (s, e) => ShowPalette();
        top.New.Click += (s, e) => Acts.New();
        top.Backup.Click += (s, e) => BackupNow();
        top.CloseDay.Click += (s, e) => CloseDayDialog.Open();

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        main.Controls.Add(content);
        main.Controls.Add(top);
        Controls.Add(main);
        Controls.Add(side);

        Store.Changed += QueueReload;
        timer.Tick += (s, e) => { top.Invalidate(); UpdateBadges(); Notify.Tick(); };
        Shown += (s, e) =>
        {
            Go("dashboard");
            UpdateBadges();
            timer.Start();
            AutoBackup.Schedule();
            Notify.Tick();
            if (Reminders.DueCount > 0) Toast.Show($"لديك {Reminders.DueCount} تذكير مستحق اليوم — من «التذكيرات»", Tone.Info);
        };
        FormClosing += (s, e) =>
        {
            timer.Stop();
            Store.Changed -= QueueReload;
            if (AutoBackup.Dir != "") AutoBackup.Run();
            Notify.OnClosing();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int caption = 0x00FFFFFF, text = 0x002A1F1B;
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
        }
        catch { }
    }

    public void UpdateShop()
    {
        Text = $"ورشة الصيانة — {Store.ShopName}";
        logo?.Invalidate();
    }

    // ---------------- الصفحات ----------------
    /// <summary>فتح صفحة (تُنشأ مرة واحدة وتبقى)، ثم تنفيذ إعداد اختياري عليها مثل فلتر معيّن</summary>
    public void Go(string key, Action<Page> setup = null)
    {
        var def = defs.FirstOrDefault(d => d.Key == key);
        if (def == null) return;
        bool fresh = false;
        if (!pages.TryGetValue(key, out var page) || page.IsDisposed)
        {
            page = def.Make();
            page.TopLevel = false;
            page.FormBorderStyle = FormBorderStyle.None;
            page.BackColor = Theme.Bg;
            page.Visible = false;
            Dpi.ScaleTree(page);
            page.Dock = DockStyle.Fill;
            content.Controls.Add(page);
            pages[key] = page;
            fresh = true;
        }
        content.SuspendLayout();
        page.Show();
        page.BringToFront();
        foreach (var (k, p) in pages) if (k != key && p.Visible) p.Hide();
        content.ResumeLayout();
        current = key;
        foreach (var (k, b) in nav) b.Active = k == key;
        if (fresh || dirty.Remove(key)) Safe(page.Reload);
        dirty.Remove(key);
        if (setup != null) Safe(() => setup(page));
        UpdateTitle(page);
        page.OnPageActivated();
    }

    /// <summary>بعد تعديل القوائم أو الفنيين: تُبنى الشاشات من جديد لتظهر القيم الجديدة في الحقول والفلاتر</summary>
    public void ResetPages()
    {
        var key = current ?? "dashboard";
        content.SuspendLayout();
        foreach (var p in pages.Values) { content.Controls.Remove(p); p.Dispose(); }
        pages.Clear();
        dirty.Clear();
        content.ResumeLayout();
        current = null;
        Go(key);
    }

    public void UpdateTitle(Page p)
    {
        if (p == null || current == null || !pages.TryGetValue(current, out var cur) || cur != p) return;
        top.SetTitle(p.Title, p.Desc, p.PageIcon);
    }

    Page Current => current != null && pages.TryGetValue(current, out var p) ? p : null;

    static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Program.Log(ex); Toast.Show("تعذّر عرض الصفحة: " + ex.Message, Tone.Warning); }
    }

    /// <summary>تغيّرت البيانات: تُحدَّث الصفحة الظاهرة مرة واحدة بعد انتهاء الحدث، والبقية عند فتحها</summary>
    void QueueReload()
    {
        foreach (var k in pages.Keys) dirty.Add(k);
        if (reloadQueued || !IsHandleCreated) return;
        reloadQueued = true;
        BeginInvoke(() =>
        {
            reloadQueued = false;
            if (IsDisposed) return;
            if (Current is Page p && current != null && dirty.Remove(current)) { Safe(p.Reload); UpdateTitle(p); }
            UpdateBadges();
        });
    }

    void UpdateBadges()
    {
        nav["orders"].Count = Store.Orders.Count(Calc.IsOpen);
        nav["debts"].Count = Calc.GetDebts().Count;
        nav["inventory"].Count = Store.Inventory.Count(i => Calc.StockState(i) is "low" or "out");
        nav["suppliers"].Count = SupplierDues.Alerts().Select(d => d.Supplier).Distinct().Count();
        nav["suppliers"].BadgeColor = nav["suppliers"].Count > 0 ? Red : Teal;
        trashBtn.Count = Store.Trash.Count;
        defectsBtn.Count = Defects.PendingCount;
        remindersBtn.Count = Reminders.DueCount;
        nav["accounts"].Count = Store.Accounts.Count(a => Accounts.Due(a) > 0);
    }

    // ---------------- أدوات ----------------
    public void BackupNow()
    {
        try
        {
            var file = Backup.Run();
            if (AutoBackup.Dir != "") AutoBackup.Run();
            Toast.Show("حُفظت نسخة احتياطية: " + Path.GetFileName(file));
            QueueReload();
        }
        catch (Exception ex) { Dialogs.Error("تعذّر حفظ النسخة الاحتياطية: " + ex.Message); }
    }

    void ShowPalette()
    {
        using var d = new PaletteDialog();
        d.ShowDialog(this);
        if (d.Chosen != null) Ui2.Later(d.Chosen);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.K: ShowPalette(); return true;
            case Keys.Control | Keys.N: Acts.New(); return true;
            case Keys.F5: if (Current is Page p) Safe(p.Reload); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------- قارئ الباركود ----------------
    // القارئ يكتب الأحرف بسرعة كبيرة ثم Enter: إذا طابق مرجع طلب أو IMEI يُفتح الطلب مباشرة
    readonly System.Text.StringBuilder scan = new();
    DateTime lastKey;

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (ActiveControl is TextBoxBase || ActiveControl is ComboBox || ActiveControl is NumericUpDown || ActiveControl is ContainerControl { ActiveControl: TextBoxBase }) { scan.Clear(); return; }
        var now = DateTime.Now;
        if ((now - lastKey).TotalMilliseconds > 80) scan.Clear();
        lastKey = now;
        if (e.KeyChar == '\r')
        {
            var code = scan.ToString().Trim();
            scan.Clear();
            if (code.Length < 4) return;
            var o = Store.Orders.FirstOrDefault(x => string.Equals(x.RefNo, code, StringComparison.OrdinalIgnoreCase)) ??
                    Store.Orders.FirstOrDefault(x => x.Imei != "" && x.Imei == code);
            if (o != null) { e.Handled = true; Acts.View(o); }
            else Toast.Show("لا يوجد طلب بهذا الرمز: " + code, Tone.Warning);
        }
        else if (!char.IsControl(e.KeyChar)) scan.Append(e.KeyChar);
    }

    // ---------------- الشريط العلوي ----------------
    sealed class TopBar : Panel
    {
        string title = "", desc = "", icon;
        public ModernButton Search { get; }
        public ModernButton New { get; }
        public ModernButton Backup { get; }
        public ModernButton CloseDay { get; }
        readonly ModernButton[] tools;

        public TopBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 68;
            BackColor = Theme.Surface;
            var tip = new ToolTip();
            ModernButton Make(string text, string ic, BtnKind kind, int min, string hint)
            {
                var b = new ModernButton { Kind = kind, IconName = ic, Text = text, Font = Theme.F(9.5f), Height = 40, TabStop = false, Radius = 10 };
                b.FitWidth(min);
                tip.SetToolTip(b, hint);
                Controls.Add(b);
                return b;
            }
            New = Make("طلب جديد", "plus", BtnKind.Primary, 120, "طلب صيانة جديد (Ctrl+N)");
            Search = Make("بحث سريع   Ctrl+K", "search", BtnKind.Secondary, 180, "ابحث عن طلب أو زبون أو أمر (Ctrl+K)");
            CloseDay = Make("", "receipt", BtnKind.Secondary, 40, "تقفيل اليوم");
            Backup = Make("", "cloud-upload", BtnKind.Secondary, 40, "نسخة احتياطية الآن");
            tools = new[] { New, Search, CloseDay, Backup };
        }

        public void SetTitle(string t, string d, string i) { title = t; desc = d; icon = i; Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tools == null) return;
            bool narrow = Width < S(900);
            Search.Text = narrow ? "" : "بحث سريع   Ctrl+K";
            Search.FitWidth(narrow ? 40 : 180);
            int x = S(16);
            foreach (var b in tools)
            {
                b.Location = new Point(x, (Height - b.Height) / 2);
                x = b.Right + S(8);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            int right = Width - S(20);
            if (Icons.Has(icon))
            {
                int box = S(40);
                var ir = new RectangleF(right - box, (Height - box) / 2f, box, box);
                Gfx.FillRound(g, ir, S(10f), Theme.BrandSoft);
                Icons.Draw(g, icon, ir, Theme.Brand, 20);
                right -= box + S(12);
            }
            int left = tools[^1].Right + S(16);
            var clock = Txt.FmtClock(DateTime.Now);
            int cw = TextRenderer.MeasureText(clock, Theme.F(9)).Width;
            if (right - left > cw + S(300))
            {
                TextRenderer.DrawText(g, clock, Theme.F(9), new Rectangle(left, 0, cw + S(4), Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                left += cw + S(20);
            }
            if (right - left > S(60))
            {
                TextRenderer.DrawText(g, title, Theme.FS(13.5f), new Rectangle(left, S(10), right - left, S(28)), Theme.Ink, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, desc, Theme.F(9), new Rectangle(left, S(38), right - left, S(20)), Theme.Muted, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
            }
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}
