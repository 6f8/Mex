using System.Data;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Raseed;

/// <summary>شعار البرنامج: مربع دائري بتدرج لون الهوية وحرف «ر»</summary>
public static class Brand
{
    public static void DrawMark(Graphics g, RectangleF r)
    {
        Gfx.Hq(g);
        using (var p = Gfx.Round(r, r.Width * 0.28f))
        using (var b = new LinearGradientBrush(r, ColorTranslator.FromHtml("#14B8A6"), Theme.BrandDark, 45f))
            g.FillPath(b, p);
        using (var p = Gfx.Round(RectangleF.Inflate(r, -1, -1), r.Width * 0.27f))
        using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1)) g.DrawPath(pen, p);
        TextRenderer.DrawText(g, "ر", FontKit.Get(r.Height * 0.40f, FontStyle.Bold), Rectangle.Round(new RectangleF(r.X, r.Y - r.Height * 0.06f, r.Width, r.Height)), Color.White, Gfx.Center);
    }
}

// ============================== تسجيل الدخول ==============================
public class LoginForm : BaseForm
{
    readonly TextBox user = new() { Width = 340 };
    readonly TextBox pass = new() { Width = 340, UseSystemPasswordChar = true };
    readonly Label err = new() { AutoSize = false, Width = 340, Height = 26, ForeColor = Theme.Danger, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft };
    Point drag;

    public LoginForm()
    {
        Text = "تسجيل الدخول — رصيد";
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(940, 580);
        BackColor = Theme.Surface;
        KeyPreview = true;

        var brand = new BrandPanel { Dock = DockStyle.Right, Width = 420 };
        var close = new ModernButton { Kind = BtnKind.Dark, IconName = "x", Size = new Size(36, 36), Location = new Point(16, 16), TabStop = false };
        close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        brand.Controls.Add(close);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(70, 70, 70, 30) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(new Label { Text = "تسجيل الدخول", AutoSize = false, Width = 360, Height = 46, Font = Theme.FS(20), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft });
        flow.Controls.Add(new Label { Text = "مرحبًا بعودتك، أدخل بياناتك للمتابعة", AutoSize = false, Width = 360, Height = 34, Font = Theme.F(10.5f), ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopLeft, Margin = new Padding(3, 0, 3, 18) });

        flow.Controls.Add(Caption("اسم المستخدم"));
        var userBox = new InputBox(user, 348, "user") { Height = 46, Margin = new Padding(3, 0, 3, 14) };
        flow.Controls.Add(userBox);
        flow.Controls.Add(Caption("كلمة المرور"));
        var passBox = new InputBox(pass, 348, "lock") { Height = 46, Margin = new Padding(3, 0, 3, 4) };
        var eye = new ModernButton { Kind = BtnKind.Ghost, IconName = "eye", Size = new Size(32, 32), TabStop = false };
        eye.Click += (s, e) => { pass.UseSystemPasswordChar = !pass.UseSystemPasswordChar; eye.IconName = pass.UseSystemPasswordChar ? "eye" : "eye-off"; eye.Invalidate(); pass.Focus(); };
        passBox.Trailing = eye;
        flow.Controls.Add(passBox);
        flow.Controls.Add(err);

        var b = new ModernButton { Text = "دخول", IconName = "log-in", Width = 348, Height = 46, Font = Theme.FS(11), Margin = new Padding(3, 8, 3, 10) };
        b.Click += (s, e) => TryLogin();
        flow.Controls.Add(b);
        AcceptButton = b;

        if (DefaultAdminActive())
            flow.Controls.Add(new Label
            {
                Text = "أول مرة؟ اسم المستخدم admin وكلمة المرور admin — ستُطلب منك كلمة مرور جديدة بعد الدخول.",
                AutoSize = false, Width = 348, Height = 44, Font = Theme.F(9), ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopLeft
            });

        host.Controls.Add(flow);
        Controls.Add(host);
        Controls.Add(brand);

        foreach (var c in new Control[] { brand, host, flow })
        {
            c.MouseDown += (s, e) => drag = e.Location;
            c.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - drag.X, Location.Y + e.Y - drag.Y); };
        }
        pass.KeyUp += (s, e) => CapsHint();
        user.TextChanged += (s, e) => err.Text = "";
        pass.TextChanged += (s, e) => err.Text = "";
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (s, e) => { if (user.Text == "") user.Focus(); else pass.Focus(); };
    }

    static Label Caption(string t) => new()
    {
        Text = t, AutoSize = false, Width = 348, Height = 26, Font = Theme.FS(9.5f), ForeColor = Theme.Text2,
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 0, 3, 2)
    };

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    static bool DefaultAdminActive()
    {
        try
        {
            var dt = Db.Query("SELECT username, pass_hash FROM users WHERE username='admin' AND active=1");
            return dt.Rows.Count == 1 && Session.Verify("admin", "admin", Db.S(dt.Rows[0]["pass_hash"]), out _);
        }
        catch { return false; }
    }

    void CapsHint()
    {
        if (Control.IsKeyLocked(Keys.CapsLock)) { err.ForeColor = Theme.Warning; err.Text = "تنبيه: زر Caps Lock مفعّل"; }
        else if (err.ForeColor == Theme.Warning) err.Text = "";
    }

    void TryLogin()
    {
        if (user.Text.Trim() == "") { err.ForeColor = Theme.Danger; err.Text = "أدخل اسم المستخدم."; user.Focus(); return; }
        Cursor = Cursors.WaitCursor;
        bool ok = Session.Login(user.Text, pass.Text);
        Cursor = Cursors.Default;
        if (ok) { DialogResult = DialogResult.OK; Close(); return; }
        err.ForeColor = Theme.Danger;
        err.Text = "اسم المستخدم أو كلمة المرور غير صحيحة.";
        pass.SelectAll();
        pass.Focus();
    }

    /// <summary>لوحة الهوية الجانبية في شاشة الدخول</summary>
    sealed class BrandPanel : Panel
    {
        public BrandPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Hq(g);
            var rect = ClientRectangle;
            using (var bg = new LinearGradientBrush(rect, ColorTranslator.FromHtml("#0B1324"), ColorTranslator.FromHtml("#0C3B3C"), 60f))
                g.FillRectangle(bg, rect);
            // دوائر زخرفية ناعمة
            using (var b1 = new SolidBrush(Color.FromArgb(22, 20, 184, 166))) g.FillEllipse(b1, -120, Height - 260, 380, 380);
            using (var b2 = new SolidBrush(Color.FromArgb(16, 255, 255, 255))) g.FillEllipse(b2, Width - 170, -110, 300, 300);
            using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255), 1)) g.DrawEllipse(pen, Width - 230, -170, 420, 420);

            int right = Width - 48;
            Brand.DrawMark(g, new RectangleF(right - 56, 70, 56, 56));
            TextRenderer.DrawText(g, "رصيد", Theme.FS(30), new Rectangle(40, 140, right - 40, 60), Color.White, Gfx.RtlStart);
            TextRenderer.DrawText(g, "نظام المبيعات والمخازن والحسابات", Theme.F(12), new Rectangle(40, 200, right - 40, 32), ColorTranslator.FromHtml("#9FB3C8"), Gfx.RtlStart);

            var features = new[]
            {
                ("scan-barcode", "فواتير سريعة بالباركود"),
                ("package", "مخزون بتواريخ الصلاحية"),
                ("users", "حسابات العملاء والأقساط"),
                ("chart-column", "تقارير الأرباح لحظة بلحظة"),
            };
            int y = 270;
            foreach (var (icon, text) in features)
            {
                var ir = new RectangleF(right - 34, y, 34, 34);
                Gfx.FillRound(g, ir, 10, Color.FromArgb(30, 255, 255, 255));
                Icons.Draw(g, icon, ir, ColorTranslator.FromHtml("#5EEAD4"), 17);
                TextRenderer.DrawText(g, text, Theme.F(10.5f), new Rectangle(40, y, right - 34 - 14 - 40, 34), ColorTranslator.FromHtml("#E2E8F0"), Gfx.RtlStart);
                y += 50;
            }
            TextRenderer.DrawText(g, "الإصدار " + Application.ProductVersion.Split('+')[0], Theme.F(9), new Rectangle(40, Height - 50, right - 40, 24), ColorTranslator.FromHtml("#64748B"), Gfx.RtlStart);
        }
    }
}

// ============================== أول تشغيل ==============================
/// <summary>ترحيب لمرة واحدة: بيانات المحل وتغيير كلمة المرور الافتراضية</summary>
public class SetupDialog : DialogShell
{
    public SetupDialog() : base("مرحبًا بك في رصيد", 600, 560, "sparkles")
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        flow.Controls.Add(new Label
        {
            Text = "خطوة واحدة قبل البدء: أدخل بيانات محلك لتظهر في الفواتير المطبوعة، واختر كلمة مرور جديدة بدل الافتراضية لحماية بياناتك.",
            AutoSize = false, Width = 530, Height = 52, ForeColor = Theme.Text2, Font = Theme.F(10), TextAlign = ContentAlignment.TopLeft
        });
        var shop = new TextBox { Width = 522, Text = Settings.Get("shop_name") == "محلي" ? "" : Settings.Get("shop_name"), PlaceholderText = "مثال: صيدلية النور" };
        var phone = new TextBox { Width = 253, Text = Settings.Get("shop_phone"), PlaceholderText = "07xx xxx xxxx" };
        var addr = new TextBox { Width = 253, Text = Settings.Get("shop_address"), PlaceholderText = "المدينة — الشارع" };
        var p1 = new TextBox { Width = 253, UseSystemPasswordChar = true };
        var p2 = new TextBox { Width = 253, UseSystemPasswordChar = true };
        // بيانات المحل يدخلها المدير فقط؛ المستخدم العادي يغيّر كلمة مروره فقط
        if (Session.IsAdmin)
        {
            flow.Controls.Add(Ui.Labeled("اسم المحل", shop));
            flow.Controls.Add(Ui.Labeled("الهاتف", phone));
            flow.Controls.Add(Ui.Labeled("العنوان", addr));
        }
        else Height = 440;
        flow.Controls.Add(new Label { Text = "كلمة المرور الجديدة (4 أحرف على الأقل)", AutoSize = false, Width = 522, Height = 34, Font = Theme.FS(10.5f), ForeColor = Theme.Ink, TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(6, 10, 6, 0) });
        flow.Controls.Add(Ui.Labeled("كلمة المرور", p1));
        flow.Controls.Add(Ui.Labeled("تأكيد كلمة المرور", p2));
        Body.Controls.Add(flow);

        AddButton("لاحقًا", DialogResult.Cancel, BtnKind.Secondary, "clock");
        var ok = AddButton("حفظ والبدء", DialogResult.None, BtnKind.Primary, "check");
        AcceptButton = ok;
        ok.Click += (s, e) =>
        {
            if (p1.Text.Length < 4) { Dialogs.Warn("كلمة المرور قصيرة جدًا (4 أحرف على الأقل)."); p1.Focus(); return; }
            if (p1.Text != p2.Text) { Dialogs.Warn("تأكيد كلمة المرور غير مطابق."); p2.Focus(); return; }
            if (p1.Text == "admin") { Dialogs.Warn("اختر كلمة مرور مختلفة عن الافتراضية."); p1.Focus(); return; }
            if (Session.IsAdmin)
            {
                if (shop.Text.Trim() != "") Settings.Set("shop_name", shop.Text.Trim());
                Settings.Set("shop_phone", phone.Text.Trim());
                Settings.Set("shop_address", addr.Text.Trim());
            }
            Db.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.HashPassword(p1.Text), Session.UserId);
            Session.UsingDefaultPassword = false;
            DialogResult = DialogResult.OK;
            Close();
        };
        Shown += (s, e) => { if (Session.IsAdmin) shop.Focus(); else p1.Focus(); };
    }
}

// ============================== النافذة الرئيسية ==============================
public class MainForm : BaseForm
{
    public record Page(string Text, string Perm, string Icon, string Group, string Desc, Func<Form> Make);

    readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 18), BackColor = Theme.Bg };
    readonly HeaderBar header;
    readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Visible = true, Text = "رصيد" };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    readonly List<NavButton> navButtons = new();
    Form current;
    public bool LoggedOut { get; private set; }
    public static MainForm Instance { get; private set; }

    public List<Page> Pages { get; }

    List<Page> BuildPages() => new()
    {
        new("الرئيسية", null, "layout-dashboard", "", "نظرة سريعة على نشاط اليوم والتنبيهات", () => new DashboardForm(this)),
        new("فاتورة بيع", "sales", "shopping-cart", "المبيعات", "امسح الباركود أو اكتب اسم المادة — F10 للحفظ", () => new InvoiceForm("Sale")),
        new("إرجاع بيع", "returns", "undo-2", "المبيعات", "إرجاع مواد من عميل إلى المخزن", () => new InvoiceForm("SaleReturn")),
        new("سجل الفواتير", "reports", "receipt-text", "المبيعات", "البحث في الفواتير السابقة وطباعتها وتعديلها", () => new InvoicesListForm()),
        new("الأقساط", "installments", "calendar-clock", "المبيعات", "متابعة الأقساط وتسديدها وتذكير العملاء", () => new InstallmentsForm()),
        new("فاتورة شراء", "purchases", "truck", "المشتريات والمخزون", "إدخال بضاعة من المورد إلى المخزن", () => new InvoiceForm("Purchase")),
        new("إرجاع شراء", "returns", "redo-2", "المشتريات والمخزون", "إرجاع مواد إلى المورد", () => new InvoiceForm("PurchaseReturn")),
        new("المواد", "items", "package", "المشتريات والمخزون", "تعريف المواد والأسعار والباركود", () => new CrudForm(Defs.Items())),
        new("المخازن والصلاحيات", "stock", "warehouse", "المشتريات والمخزون", "الأرصدة حسب الوجبة وتواريخ انتهاء الصلاحية", () => new StockForm(this)),
        new("جرد المخزون", "stock", "clipboard-check", "المشتريات والمخزون", "مطابقة الرصيد الفعلي وتسوية الفروقات", () => new StockCountForm()),
        new("إتلاف مواد", "damage", "ban", "المشتريات والمخزون", "إخراج المواد التالفة أو المنتهية من المخزن", () => new InvoiceForm("Damage")),
        new("ملصقات الباركود", "labels", "barcode", "المشتريات والمخزون", "طباعة ملصقات الأسعار والباركود", () => new LabelsForm()),
        new("العملاء والموردون", "parties", "users", "الحسابات", "الحسابات والأرصدة وسقوف الذمة", () => new CrudForm(Defs.Parties())),
        new("السندات والصيرفة", "vouchers", "wallet", "الحسابات", "قبض وصرف ومصروفات وتحويل بين الصناديق", () => new VoucherForm()),
        new("التقارير والأرباح", "reports", "chart-column", "الحسابات", "كشوف الحساب والأرباح وحركة الصناديق", () => new ReportsForm()),
        new("الصيانة", "repairs", "wrench", "الصيانة والموظفون", "استلام الأجهزة ومتابعتها وتسليمها", () => new RepairsForm()),
        new("الموارد البشرية", "hr", "id-card", "الصيانة والموظفون", "الموظفون والحضور والسلف والرواتب", () => new HrForm()),
        new("مدير المهام", "tasks", "list-todo", "الإدارة", "تذكيرات ومهام تلقائية مثل النسخ الاحتياطي", () => new CrudForm(Defs.Tasks())),
        new("التعريفات", "settings", "layers", "الإدارة", "المخازن والصناديق ومراكز الكلفة والتوصيل والشركاء", () => new DefsHubForm()),
        new("المستخدمون والصلاحيات", "users", "shield-check", "الإدارة", "حسابات الدخول وصلاحيات كل مستخدم", () => new UsersForm()),
        new("الإعدادات", "settings", "settings", "الإدارة", "بيانات المحل والطباعة والنسخ الاحتياطي وواتساب", () => new SettingsForm()),
    };

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm()
    {
        Instance = this;
        Text = $"رصيد — {Settings.Get("shop_name")}";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1180, 720);
        KeyPreview = true;
        Pages = BuildPages().Where(p => Session.Can(p.Perm)).ToList();

        // ---------- الشريط الجانبي ----------
        var side = new Panel { Dock = DockStyle.Left, Width = 262, BackColor = Theme.Sidebar };
        var logo = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Theme.Sidebar };
        logo.Paint += (s, e) =>
        {
            var g = e.Graphics;
            Brand.DrawMark(g, new RectangleF(logo.Width - 20 - 42, 22, 42, 42));
            TextRenderer.DrawText(g, "رصيد", Theme.FS(16), new Rectangle(12, 18, logo.Width - 90, 30), Color.White, Gfx.RtlStart);
            TextRenderer.DrawText(g, Settings.Get("shop_name"), Theme.F(9), new Rectangle(12, 46, logo.Width - 90, 22), Theme.SidebarMuted, Gfx.RtlStart);
            using var pen = new Pen(Color.FromArgb(24, 255, 255, 255));
            g.DrawLine(pen, 18, logo.Height - 1, logo.Width - 18, logo.Height - 1);
        };

        var userBox = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Theme.Sidebar };
        userBox.Paint += (s, e) =>
        {
            var g = e.Graphics;
            using (var pen = new Pen(Color.FromArgb(24, 255, 255, 255))) g.DrawLine(pen, 18, 0, userBox.Width - 18, 0);
            Avatar.Draw(g, new RectangleF(userBox.Width - 20 - 40, 18, 40, 40), Session.UserName, Theme.Brand);
            TextRenderer.DrawText(g, Session.UserName, Theme.FS(10), new Rectangle(104, 16, userBox.Width - 176, 24), Color.White, Gfx.RtlStart);
            TextRenderer.DrawText(g, Session.IsAdmin ? "مدير النظام" : "مستخدم", Theme.F(8.5f), new Rectangle(104, 40, userBox.Width - 176, 20), Theme.SidebarMuted, Gfx.RtlStart);
        };
        var bLogout = new ModernButton { Kind = BtnKind.Dark, IconName = "log-out", Size = new Size(38, 38), Location = new Point(16, 19), TabStop = false };
        new ToolTip().SetToolTip(bLogout, "تسجيل الخروج");
        bLogout.Click += (s, e) => { if (Ui.Confirm("تسجيل الخروج من البرنامج؟")) { LoggedOut = true; Close(); } };
        var bPwd = new ModernButton { Kind = BtnKind.Dark, IconName = "key-round", Size = new Size(38, 38), Location = new Point(58, 19), TabStop = false };
        new ToolTip().SetToolTip(bPwd, "تغيير كلمة المرور");
        bPwd.Click += (s, e) => { using var d = new PasswordDialog(); d.ShowModal(); };
        userBox.Controls.Add(bLogout);
        userBox.Controls.Add(bPwd);

        var nav = new ScrollHost { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
        string group = null;
        foreach (var p in Pages)
        {
            if (p.Group != group && p.Group != "")
                nav.Add(new Label
                {
                    Text = p.Group, AutoSize = false, Height = 34, ForeColor = Theme.SidebarMuted, Font = Theme.FS(8.5f),
                    TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(10, 0, 10, 4), BackColor = Theme.Sidebar
                });
            group = p.Group;
            var b = new NavButton { Text = p.Text, IconName = p.Icon, Group = p.Group, Tag = p };
            b.Click += (s, e) => Navigate(p);
            navButtons.Add(b);
            nav.Add(b);
        }
        side.Controls.Add(nav);
        side.Controls.Add(userBox);
        side.Controls.Add(logo);

        // ---------- الرأس ----------
        header = new HeaderBar();
        header.Search.Click += (s, e) => ShowPalette();
        header.Bell.Click += (s, e) => Navigate(Pages[0]);

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        main.Controls.Add(content);
        main.Controls.Add(header);
        Controls.Add(main);
        Controls.Add(side);

        Scheduler.Notify = (t, m) => { if (IsHandleCreated) BeginInvoke(() => tray.ShowBalloonTip(8000, t, m, ToolTipIcon.Info)); };
        Scheduler.Alert = (t, m) => { if (IsHandleCreated) BeginInvoke(() => Dialogs.Message(m, "تذكير: " + t, Tone.Info)); };
        tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Maximized; Activate(); };

        timer.Tick += (s, e) => { Scheduler.Tick(); RefreshAlerts(); };
        Shown += (s, e) =>
        {
            Navigate(Pages[0]);
            MobileApi.Start();
            timer.Start();
            Scheduler.Tick();
            RefreshAlerts();
        };
        FormClosing += (s, e) =>
        {
            timer.Stop();
            MobileApi.Stop();
            if (Settings.Get("backup_on_exit") == "1") try { Backup.Run(); } catch { }
            tray.Visible = false;
            tray.Dispose();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int caption = 0x00FFFFFF, text = 0x002A170F;   // شريط عنوان أبيض ونص داكن (ويندوز 11)
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
        }
        catch { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K)) { ShowPalette(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void RefreshAlerts()
    {
        try
        {
            header.Bell.Badge = (int)(Stats.LowStock() + Stats.Expiring(Settings.Int("expiry_days", 30)) +
                                      Stats.DueInstallments(Settings.Int("reminder_days", 3)) + Stats.RepairsReady());
            header.Bell.Invalidate();
        }
        catch { }
    }

    void ShowPalette()
    {
        using var p = new CommandPalette(Pages);
        if (p.ShowDialog(this) == DialogResult.OK && p.Selected != null) Navigate(p.Selected);
    }

    public void Navigate(Page p) => Open(p.Text, p.Make(), p);

    /// <summary>الانتقال إلى شاشة باسمها (إن كانت ضمن صلاحيات المستخدم)</summary>
    public bool Go(string pageText)
    {
        var p = Pages.FirstOrDefault(x => x.Text == pageText);
        if (p == null) return false;
        Navigate(p);
        return true;
    }

    public void Open(string title, Form f) => Open(title, f, Pages.FirstOrDefault(x => x.Text == title));

    void Open(string title, Form f, Page page)
    {
        if (current != null)
        {
            var old = current;
            content.Controls.Remove(old);
            BeginInvoke(() => old.Dispose());   // التخلص لاحقًا لأن الطلب قد يأتي من زر داخل الشاشة نفسها
        }
        foreach (var b in navButtons) b.Active = page != null && ReferenceEquals(b.Tag, page);
        f.TopLevel = false;
        f.FormBorderStyle = FormBorderStyle.None;
        f.Dock = DockStyle.Fill;
        f.BackColor = Theme.Bg;
        content.Controls.Add(f);
        header.SetTitle(title, page?.Desc ?? "", page?.Icon);
        f.Show();
        current = f;
    }

    /// <summary>رأس الصفحة: العنوان والوصف، البحث السريع والتنبيهات والتاريخ</summary>
    sealed class HeaderBar : Panel
    {
        string title = "", desc = "", icon;
        public ModernButton Search { get; }
        public ModernButton Bell { get; }

        public HeaderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 76;
            BackColor = Theme.Surface;
            Search = new ModernButton { Kind = BtnKind.Secondary, IconName = "search", Text = "بحث سريع   Ctrl+K", Font = Theme.F(9.5f), Height = 40 };
            Search.FitWidth(210);
            Search.TabStop = false;
            Bell = new ModernButton { Kind = BtnKind.Secondary, IconName = "bell", Size = new Size(42, 40), TabStop = false };
            new ToolTip().SetToolTip(Bell, "التنبيهات");
            Controls.Add(Search);
            Controls.Add(Bell);
        }

        public void SetTitle(string t, string d, string i) { title = t; desc = d; icon = i; Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Bell == null || Search == null) return;   // يُستدعى من المُنشئ قبل إنشاء الأزرار
            // أدوات الرأس في الجهة اليسرى (نهاية السطر العربي)
            Bell.Location = new Point(22, (Height - Bell.Height) / 2);
            Search.Location = new Point(Bell.Right + 10, (Height - Search.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            int right = Width - 26;
            if (icon != null && FontKit.HasIcons)
            {
                var ir = new RectangleF(right - 44, (Height - 44) / 2f, 44, 44);
                Gfx.FillRound(g, ir, 12, Theme.BrandSoft);
                Icons.Draw(g, icon, ir, Theme.Brand, 21);
                right -= 58;
            }
            int left = Search.Right + 20;
            TextRenderer.DrawText(g, title, Theme.FS(15), new Rectangle(left, 12, right - left, 30), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g, desc, Theme.F(9.5f), new Rectangle(left, 42, right - left, 22), Theme.Muted, Gfx.RtlStart);
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>حاوية تمرير بعجلة الفأرة بدون شريط تمرير ظاهر (للقائمة الجانبية)</summary>
public class ScrollHost : Panel
{
    readonly FlowLayoutPanel inner = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 6, 12, 12) };

    public ScrollHost()
    {
        DoubleBuffered = true;
        inner.BackColor = Theme.Sidebar;
        Controls.Add(inner);
        inner.Location = Point.Empty;
    }

    public void Add(Control c)
    {
        c.Width = Width - 24;
        c.Margin = new Padding(0, 1, 0, 1);
        inner.Controls.Add(c);
        c.MouseWheel += (s, e) => Scroll(e.Delta);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        inner.Width = Width;
        foreach (Control c in inner.Controls) c.Width = Width - 24;
        Scroll(0);
    }

    protected override void OnMouseWheel(MouseEventArgs e) { Scroll(e.Delta); base.OnMouseWheel(e); }

    new void Scroll(int delta)
    {
        int min = Math.Min(0, Height - inner.Height);
        inner.Top = Math.Max(min, Math.Min(0, inner.Top + delta / 3));
    }
}

/// <summary>البحث السريع (Ctrl+K): اكتب اسم الشاشة وانتقل إليها مباشرة</summary>
public class CommandPalette : BaseForm
{
    readonly TextBox q = new() { Width = 520, PlaceholderText = "اكتب ما تبحث عنه... مثل: فاتورة، أقساط، مواد" };
    readonly ListBox list = new() { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 50, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly List<MainForm.Page> all;
    public MainForm.Page Selected { get; private set; }

    public CommandPalette(List<MainForm.Page> pages)
    {
        all = pages;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(600, 470);
        BackColor = Theme.Surface;
        Padding = new Padding(16);
        KeyPreview = true;

        var box = new InputBox(q, 566, "search") { Dock = DockStyle.Top, Height = 48 };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "↑ ↓ للتنقل   •   Enter للفتح   •   Esc للإغلاق", ForeColor = Theme.Subtle, Font = Theme.F(8.5f), TextAlign = ContentAlignment.MiddleCenter };
        list.Dock = DockStyle.Fill;
        list.BackColor = Theme.Surface;
        list.DrawItem += DrawItem;
        list.DoubleClick += (s, e) => Accept();
        list.MouseClick += (s, e) => Accept();
        Controls.Add(list);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Surface });
        Controls.Add(box);
        Controls.Add(hint);

        q.TextChanged += (s, e) => Filter();
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
        };
        Deactivate += (s, e) => { if (DialogResult == DialogResult.None) Close(); };
        Filter();
        Shown += (s, e) => q.Focus();
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    void Filter()
    {
        var t = q.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var p in all.Where(p => t == "" || p.Text.Contains(t) || p.Desc.Contains(t) || p.Group.Contains(t)))
            list.Items.Add(p);
        list.EndUpdate();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    void Accept()
    {
        if (list.SelectedItem is not MainForm.Page p) return;
        Selected = p;
        DialogResult = DialogResult.OK;
        Close();
    }

    void DrawItem(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var p = (MainForm.Page)list.Items[e.Index];
        var g = e.Graphics;
        Gfx.Hq(g);
        g.FillRectangle(new SolidBrush(Theme.Surface), e.Bounds);
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var r = new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
        if (sel) Gfx.FillRound(g, r, 10, Theme.BrandSoft);
        var ir = new RectangleF(r.Right - 44, r.Y + 5, 36, 36);
        Gfx.FillRound(g, ir, 9, sel ? Theme.Surface : Theme.SurfaceAlt);
        Icons.Draw(g, p.Icon, ir, sel ? Theme.Brand : Theme.Muted, 18);
        int tw = (int)r.Width - 64;
        TextRenderer.DrawText(g, p.Text, Theme.FS(10.5f), new Rectangle((int)r.X + 8, (int)r.Y + 3, tw, 24), Theme.Ink, Gfx.RtlStart);
        TextRenderer.DrawText(g, p.Desc, Theme.F(8.5f), new Rectangle((int)r.X + 8, (int)r.Y + 25, tw, 20), Theme.Muted, Gfx.RtlStart);
        if (p.Group != "")
        {
            var gs = TextRenderer.MeasureText(p.Group, Theme.F(8.5f));
            var gr = new Rectangle((int)r.X + 10, (int)r.Y + 12, gs.Width + 14, 22);
            Gfx.FillRound(g, gr, 11, sel ? Theme.Surface : Theme.GraySoft);
            TextRenderer.DrawText(g, p.Group, Theme.F(8.5f), gr, Theme.Muted, Gfx.Center);
        }
    }
}

/// <summary>التعريفات: المخازن، الصناديق والخزائن، مراكز الكلفة، شركات التوصيل، الشركاء</summary>
public class DefsHubForm : BaseForm
{
    public DefsHubForm()
    {
        var tabs = new ModernTabs { Dock = DockStyle.Fill };
        var icons = new[] { "warehouse", "wallet", "layers", "truck", "handshake" };
        var defs = new[] { Defs.Warehouses(), Defs.Cashboxes(), Defs.CostCenters(), Defs.Delivery(), Defs.Partners() };
        for (int i = 0; i < defs.Length; i++)
        {
            var f = new CrudForm(defs[i]) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
            var host = new Panel { BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 0) };
            host.Controls.Add(f);
            f.Show();
            tabs.Add(defs[i].Title, host, icons[i]);
        }
        Controls.Add(tabs);
    }
}

/// <summary>المستخدمون والصلاحيات المتقدمة</summary>
public class UsersForm : BaseForm
{
    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox tUser = new() { Width = 290 }, tPass = new() { Width = 290, UseSystemPasswordChar = true }, tName = new() { Width = 290 };
    readonly Toggle cAdmin = new() { Text = "مدير (كل الصلاحيات)", Width = 290 }, cActive = new() { Text = "حساب فعّال", Width = 290, Checked = true };
    readonly CheckedListBox perms = new() { Width = 286, Height = 330, CheckOnClick = true, BorderStyle = BorderStyle.None, Font = Theme.F(10), BackColor = Theme.Surface };
    readonly Label lblMode = new() { AutoSize = false, Width = 290, Height = 26, ForeColor = Theme.Brand, Font = Theme.FS(9.5f), TextAlign = ContentAlignment.MiddleLeft };
    long id;

    public UsersForm()
    {
        foreach (var p in Session.AllPerms) perms.Items.Add(p.Title);
        var editor = new CardPanel { Dock = DockStyle.Right, Width = 362, Title = "بيانات المستخدم", Subtitle = "اختر مستخدمًا من الجدول أو أنشئ جديدًا", IconName = "user-cog" };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        flow.Controls.Add(lblMode);
        flow.Controls.Add(Ui.Labeled("اسم الدخول", tUser));
        tPass.PlaceholderText = "اتركها فارغة لعدم التغيير";
        flow.Controls.Add(Ui.Labeled("كلمة المرور", tPass));
        flow.Controls.Add(Ui.Labeled("الاسم الكامل", tName));
        flow.Controls.Add(cAdmin);
        flow.Controls.Add(cActive);
        var permBox = new Panel { Width = 294, Height = 340, Padding = new Padding(4), BackColor = Theme.Surface, Margin = new Padding(6, 4, 6, 8) };
        permBox.Paint += (s, e) => { Gfx.Hq(e.Graphics); Gfx.DrawRound(e.Graphics, new RectangleF(0.5f, 0.5f, permBox.Width - 2, permBox.Height - 2), 8, Theme.BorderStrong); };
        perms.Dock = DockStyle.Fill;
        permBox.Controls.Add(perms);
        flow.Controls.Add(new Label { Text = "الصلاحيات", AutoSize = false, Width = 290, Height = 26, Font = Theme.F(9), ForeColor = Theme.Text2, TextAlign = ContentAlignment.BottomLeft });
        flow.Controls.Add(permBox);
        editor.Controls.Add(flow);

        var bar = Theme.Bar();
        var bNew = Theme.Btn("مستخدم جديد", Theme.Gray, 130);
        var bSave = Theme.Btn("حفظ", Theme.Success, 110);
        bar.Controls.AddRange(new Control[] { bSave, bNew });

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14, BackColor = Theme.Bg });
        Controls.Add(editor);
        Controls.Add(bar);

        bNew.Click += (s, e) => New();
        bSave.Click += (s, e) => Save();
        grid.CellClick += (s, e) => LoadUser();
        cAdmin.CheckedChanged += (s, e) => perms.Enabled = !cAdmin.Checked;
        LoadGrid();
        New();
    }

    void LoadGrid() => grid.DataSource = Db.Query(@"SELECT id, username AS [اسم الدخول], full_name AS [الاسم],
        CASE is_admin WHEN 1 THEN 'مدير' ELSE 'مستخدم' END AS [النوع], CASE active WHEN 1 THEN 'فعّال' ELSE 'موقوف' END AS [الحالة] FROM users");

    void New()
    {
        id = 0;
        tUser.Clear(); tPass.Clear(); tName.Clear();
        tPass.PlaceholderText = "كلمة مرور المستخدم الجديد";
        cAdmin.Checked = false; cActive.Checked = true;
        for (int i = 0; i < perms.Items.Count; i++) perms.SetItemChecked(i, false);
        lblMode.Text = "مستخدم جديد";
        tUser.Focus();
    }

    void LoadUser()
    {
        if (grid.CurrentRow == null) return;
        id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var r = Db.Query("SELECT * FROM users WHERE id=@p0", id).Rows[0];
        tUser.Text = Db.S(r["username"]);
        tName.Text = Db.S(r["full_name"]);
        tPass.Clear();
        tPass.PlaceholderText = "اتركها فارغة لعدم التغيير";
        cAdmin.Checked = Db.L(r["is_admin"]) == 1;
        cActive.Checked = Db.L(r["active"]) == 1;
        var set = Db.Query("SELECT perm FROM user_perms WHERE user_id=@p0", id).Rows.Cast<DataRow>().Select(x => Db.S(x["perm"])).ToHashSet();
        for (int i = 0; i < perms.Items.Count; i++) perms.SetItemChecked(i, set.Contains(Session.AllPerms[i].Key));
        lblMode.Text = "تعديل: " + tUser.Text;
    }

    void Save()
    {
        if (!Session.Guard("users")) return;
        string u = tUser.Text.Trim(), p = tPass.Text;
        if (u == "") { Ui.Warn("أدخل اسم الدخول."); return; }
        if (id == 0 && p == "") { Ui.Warn("أدخل كلمة المرور."); return; }
        if (p != "" && p.Length < 4) { Ui.Warn("كلمة المرور قصيرة جدًا (4 أحرف على الأقل)."); return; }
        if (id == Session.UserId && (!cActive.Checked || (Session.IsAdmin && !cAdmin.Checked))) { Ui.Warn("لا يمكنك إيقاف حسابك أو إزالة صلاحية المدير عن نفسك."); return; }
        if (Db.L(Db.Scalar("SELECT COUNT(*) FROM users WHERE username=@p0 COLLATE NOCASE AND id<>@p1", u, id)) > 0) { Ui.Warn("اسم الدخول مستخدم مسبقًا."); return; }
        try
        {
            using var tx = new Tx();
            if (id == 0)
                id = tx.Insert("INSERT INTO users(username,pass_hash,full_name,is_admin,active) VALUES(@p0,@p1,@p2,@p3,@p4)",
                    u, Session.HashPassword(p), tName.Text.Trim(), cAdmin.Checked ? 1 : 0, cActive.Checked ? 1 : 0);
            else
            {
                tx.Exec("UPDATE users SET username=@p0, full_name=@p1, is_admin=@p2, active=@p3 WHERE id=@p4",
                    u, tName.Text.Trim(), cAdmin.Checked ? 1 : 0, cActive.Checked ? 1 : 0, id);
                if (p != "") tx.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.HashPassword(p), id);
            }
            tx.Exec("DELETE FROM user_perms WHERE user_id=@p0", id);
            foreach (int i in perms.CheckedIndices)
                tx.Exec("INSERT INTO user_perms(user_id,perm) VALUES(@p0,@p1)", id, Session.AllPerms[i].Key);
            tx.Commit();
            tPass.Clear();
            LoadGrid();
            Toast.Show("تم حفظ المستخدم " + u);
            lblMode.Text = "تعديل: " + u;
        }
        catch (Exception ex) { Ui.Warn("تعذر الحفظ: " + ex.Message); }
    }
}
