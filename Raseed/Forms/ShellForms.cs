using System.Data;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Raseed;

/// <summary>شعار البرنامج: مربع دائري بتدرج برتقالي/كهرماني وحرف «ر»</summary>
public static class Brand
{
    public static void DrawMark(Graphics g, RectangleF r)
    {
        Gfx.Hq(g);
        using (var p = Gfx.Round(r, r.Width * 0.28f))
        using (var b = new LinearGradientBrush(r, Theme.Amber, Theme.Orange, 60f))
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
        var close = new ModernButton { Kind = BtnKind.Glass, IconName = "x", Size = new Size(36, 36), Location = new Point(16, 16), TabStop = false };
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
            BackColor = ColorTranslator.FromHtml("#E9571F");   // لون التدرج عند زر الإغلاق (الأزرار تُرسم فوق لون الحاوية)
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Hq(g);
            var rect = ClientRectangle;
            using (var bg = new LinearGradientBrush(rect, ColorTranslator.FromHtml("#E8531F"), ColorTranslator.FromHtml("#F6A928"), 70f))
                g.FillRectangle(bg, rect);
            // دوائر زخرفية ناعمة
            using (var b1 = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillEllipse(b1, -120, Height - 260, 380, 380);
            using (var b2 = new SolidBrush(Color.FromArgb(26, 255, 255, 255))) g.FillEllipse(b2, Width - 170, -110, 300, 300);
            using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1)) g.DrawEllipse(pen, Width - 230, -170, 420, 420);

            int right = Width - 48;
            // الشعار بخلفية بيضاء ليتميز عن التدرج
            var mark = new RectangleF(right - 58, 70, 58, 58);
            Gfx.FillRound(g, mark, 16, Color.White);
            TextRenderer.DrawText(g, "ر", FontKit.Get(23, FontStyle.Bold), Rectangle.Round(new RectangleF(mark.X, mark.Y - 3, mark.Width, mark.Height)), Theme.Orange, Gfx.Center);
            TextRenderer.DrawText(g, "رصيد", Theme.FS(30), new Rectangle(40, 142, right - 40, 60), Color.White, Gfx.RtlStart);
            TextRenderer.DrawText(g, "نظام المبيعات والمخازن والحسابات", Theme.F(12), new Rectangle(40, 202, right - 40, 32), ColorTranslator.FromHtml("#FFF1E6"), Gfx.RtlStart);

            var features = new[]
            {
                ("scan-barcode", "فواتير سريعة بالباركود"),
                ("warehouse", "مخازن متعددة بتواريخ الصلاحية"),
                ("users", "حسابات العملاء والأقساط"),
                ("chart-column", "تقارير الأرباح لحظة بلحظة"),
            };
            int y = 272;
            foreach (var (icon, text) in features)
            {
                var ir = new RectangleF(right - 34, y, 34, 34);
                Gfx.FillRound(g, ir, 10, Color.FromArgb(56, 255, 255, 255));
                Icons.Draw(g, icon, ir, Color.White, 17);
                TextRenderer.DrawText(g, text, Theme.F(10.5f), new Rectangle(40, y, right - 34 - 14 - 40, 34), Color.White, Gfx.RtlStart);
                y += 50;
            }
            TextRenderer.DrawText(g, "الإصدار " + Application.ProductVersion.Split('+')[0], Theme.F(9), new Rectangle(40, Height - 50, right - 40, 24), ColorTranslator.FromHtml("#FFE3CC"), Gfx.RtlStart);
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
    record Section(string Name, string Icon, Color Tint);

    const string HomeKey = "الرئيسية";
    const int MaxTabs = 8;

    static readonly Section[] Sections =
    {
        new("المخزن", "warehouse", Theme.Orange),
        new("بيع", "shopping-cart", Theme.Success),
        new("شراء", "truck", Theme.Info),
        new("الحسابات", "wallet", Theme.Purple),
        new("الصيانة والموظفون", "wrench", ColorTranslator.FromHtml("#0E7490")),
        new("الإدارة", "settings", Theme.Gray),
    };

    readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 18), BackColor = Theme.Bg };
    readonly TopBar top;
    readonly DocTabs tabs = new() { Dock = DockStyle.Fill };
    readonly Panel side = new() { Dock = DockStyle.Left, Width = 286, BackColor = Theme.Sidebar };
    readonly ScrollHost nav = new() { Dock = DockStyle.Fill, BackColor = Theme.Sidebar };
    readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Visible = true, Text = "رصيد" };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    readonly Dictionary<string, (Form Form, Page Page)> open = new();
    readonly List<(NavSection Head, List<NavItem> Items)> sections = new();
    readonly List<(NavItem Btn, Page Page)> navItems = new();
    NavSection homeHead;
    string activeKey;
    public bool LoggedOut { get; private set; }
    public static MainForm Instance { get; private set; }

    public List<Page> Pages { get; }

    List<Page> BuildPages() => new()
    {
        new(HomeKey, null, "house", "", "نظرة سريعة على نشاط اليوم والتنبيهات", () => new DashboardForm(this)),

        new("المواد", "items", "package", "المخزن", "تعريف المواد والأسعار والباركود والرصيد الافتتاحي", () => new ItemsForm()),
        new("المخازن", "stock", "warehouse", "المخزن", "أسماء المخازن والفروع", () => new CrudForm(Defs.Warehouses())),
        new("الشركات", "items", "building-2", "المخزن", "الشركات المصنّعة أو الموردة للمواد", () => new CrudForm(Defs.Companies())),
        new("طباعة الباركود", "labels", "barcode", "المخزن", "طباعة ملصقات الأسعار والباركود", () => new LabelsForm()),
        new("إدخال مخزني", "stock", "arrow-down-to-line", "المخزن", "إدخال مواد إلى المخزن بدون مورد (رصيد أول المدة، هدايا، إنتاج)", () => new InvoiceForm("StockIn")),
        new("إخراج مخزني", "stock", "arrow-up-from-line", "المخزن", "إخراج مواد من المخزن لغير البيع (استهلاك داخلي، عينات)", () => new InvoiceForm("StockOut")),
        new("تسوية مخزنية", "stock", "clipboard-check", "المخزن", "جرد الرصيد الفعلي وتسوية الفروقات", () => new StockCountForm()),
        new("نقل بين المخازن", "stock", "arrow-left-right", "المخزن", "نقل مواد من مخزن إلى آخر مع حفظ الصلاحية والكلفة", () => new TransferForm()),
        new("المواد التالفة", "damage", "ban", "المخزن", "إخراج المواد التالفة أو المنتهية من المخزن", () => new InvoiceForm("Damage")),
        new("أرصدة المخازن", "stock", "boxes", "المخزن", "الأرصدة حسب الوجبة وتواريخ انتهاء الصلاحية والنواقص", () => new StockForm(this)),

        new("فاتورة بيع", "sales", "shopping-cart", "بيع", "امسح الباركود أو اكتب اسم المادة — F10 للحفظ", () => new InvoiceForm("Sale")),
        new("إرجاع بيع", "returns", "undo-2", "بيع", "إرجاع مواد من عميل إلى المخزن", () => new InvoiceForm("SaleReturn")),
        new("الأقساط", "installments", "calendar-clock", "بيع", "متابعة الأقساط وتسديدها وتذكير العملاء", () => new InstallmentsForm()),
        new("سجل الفواتير", "reports", "receipt-text", "بيع", "البحث في الفواتير والسندات السابقة وطباعتها وتعديلها", () => new InvoicesListForm()),

        new("فاتورة شراء", "purchases", "truck", "شراء", "إدخال بضاعة من المورد إلى المخزن", () => new InvoiceForm("Purchase")),
        new("إرجاع شراء", "returns", "redo-2", "شراء", "إرجاع مواد إلى المورد", () => new InvoiceForm("PurchaseReturn")),

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

        // ---------- الشعار ----------
        var logo = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Sidebar };
        logo.Paint += (s, e) =>
        {
            var g = e.Graphics;
            Brand.DrawMark(g, new RectangleF(logo.Width - 22 - 50, 22, 50, 50));
            TextRenderer.DrawText(g, "رصيد", Theme.FS(19), new Rectangle(12, 18, logo.Width - 22 - 50 - 26, 34), Theme.Brand, Gfx.RtlStart);
            TextRenderer.DrawText(g, "للمبيعات والمخازن", Theme.FS(9.5f), new Rectangle(12, 50, logo.Width - 22 - 50 - 26, 22), Theme.Orange, Gfx.RtlStart);
            using var pen = new Pen(Theme.BorderStrong);
            g.DrawLine(pen, 0, logo.Height - 1, logo.Width, logo.Height - 1);
        };

        // ---------- المستخدم ----------
        var userBox = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = Theme.Sidebar };
        userBox.Paint += (s, e) =>
        {
            var g = e.Graphics;
            using (var pen = new Pen(Theme.BorderStrong)) g.DrawLine(pen, 0, 0, userBox.Width, 0);
            Avatar.Draw(g, new RectangleF(userBox.Width - 20 - 40, 17, 40, 40), Session.UserName, Theme.Brand);
            TextRenderer.DrawText(g, Session.UserName, Theme.FS(10), new Rectangle(104, 15, userBox.Width - 176, 24), Theme.SidebarText, Gfx.RtlStart);
            TextRenderer.DrawText(g, Session.IsAdmin ? "مدير النظام" : "مستخدم", Theme.F(8.5f), new Rectangle(104, 39, userBox.Width - 176, 20), Theme.SidebarMuted, Gfx.RtlStart);
        };
        var bLogout = new ModernButton { Kind = BtnKind.Ghost, IconName = "log-out", Size = new Size(38, 38), Location = new Point(14, 18), TabStop = false };
        new ToolTip().SetToolTip(bLogout, "تسجيل الخروج");
        bLogout.Click += (s, e) => { if (Ui.Confirm("تسجيل الخروج من البرنامج؟") && CloseAllTabs()) { LoggedOut = true; Close(); } };
        var bPwd = new ModernButton { Kind = BtnKind.Ghost, IconName = "key-round", Size = new Size(38, 38), Location = new Point(56, 18), TabStop = false };
        new ToolTip().SetToolTip(bPwd, "تغيير كلمة المرور");
        bPwd.Click += (s, e) => { using var d = new PasswordDialog(); d.ShowModal(); };
        userBox.Controls.Add(bLogout);
        userBox.Controls.Add(bPwd);

        // ---------- الأقسام (تُفتح وتُطوى) ----------
        var home = Pages.FirstOrDefault(p => p.Text == HomeKey);
        if (home != null)
        {
            homeHead = new NavSection { Text = HomeKey, IconName = home.Icon, Tint = Theme.Brand, HasChildren = false };
            homeHead.Click += (s, e) => Navigate(home);
            nav.Add(homeHead);
        }
        foreach (var sec in Sections)
        {
            var pages = Pages.Where(p => p.Group == sec.Name).ToList();
            if (pages.Count == 0) continue;
            var head = new NavSection { Text = sec.Name, IconName = sec.Icon, Tint = sec.Tint };
            var items = new List<NavItem>();
            nav.Add(head);
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                // تدرّج من البرتقالي إلى الكهرماني على طول القسم
                var item = new NavItem { Text = p.Text, IconName = p.Icon, Fill = Gfx.Mix(Theme.Orange, Theme.Amber, pages.Count == 1 ? 0 : (float)i / (pages.Count - 1)), Visible = false };
                item.Click += (s, e) => Navigate(p);
                items.Add(item);
                navItems.Add((item, p));
                nav.Add(item);
            }
            head.Click += (s, e) => Expand(head.Expanded ? null : sec.Name);
            sections.Add((head, items));
        }
        side.Controls.Add(nav);
        side.Controls.Add(userBox);
        side.Controls.Add(logo);

        // ---------- الشريط العلوي والتبويبات ----------
        top = new TopBar();
        top.Menu.Click += (s, e) => ToggleSidebar();
        top.Search.Click += (s, e) => ShowPalette();
        top.Bell.Click += (s, e) => { if (home != null) Navigate(home); };
        top.Backup.Click += (s, e) => BackupNow();
        top.WhatsApp.Click += (s, e) => Shell("https://web.whatsapp.com/");
        top.Calc.Click += (s, e) => Shell("calc.exe");
        top.Help.Click += (s, e) => ShowHelp();

        var tabStrip = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = DocTabs.Strip, Padding = new Padding(12, 0, 12, 0) };
        tabStrip.Controls.Add(tabs);
        tabs.Selected += Activate;
        tabs.Closed += key => CloseTab(key);

        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        main.Controls.Add(content);
        main.Controls.Add(tabStrip);
        main.Controls.Add(top);
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
            if (!LoggedOut && e.CloseReason == CloseReason.UserClosing && !CloseAllTabs()) { e.Cancel = true; return; }
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
            int caption = 0x00FFFFFF, text = 0x002A1F1B;   // شريط عنوان أبيض ونص داكن (ويندوز 11)
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
        }
        catch { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.K: ShowPalette(); return true;
            case Keys.Control | Keys.W: if (activeKey != null) CloseTab(activeKey); return true;
            case Keys.Control | Keys.Tab: CycleTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: CycleTab(-1); return true;
            case Keys.F1: ShowHelp(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void RefreshAlerts()
    {
        try
        {
            top.Bell.Badge = (int)(Stats.LowStock() + Stats.Expiring(Settings.Int("expiry_days", 30)) +
                                   Stats.DueInstallments(Settings.Int("reminder_days", 3)) + Stats.RepairsReady());
            top.Bell.Invalidate();
        }
        catch { }
    }

    void ShowPalette()
    {
        using var p = new CommandPalette(Pages);
        if (p.ShowDialog(this) == DialogResult.OK && p.Selected != null) Navigate(p.Selected);
    }

    // ---------- القائمة الجانبية ----------
    /// <summary>إخفاء القائمة لمساحة عمل أكبر (مثل شاشة البيع) أو إظهارها</summary>
    public void ToggleSidebar()
    {
        SuspendLayout();
        side.Visible = !side.Visible;
        ResumeLayout(true);
        Invalidate(true);
        Update();
    }

    void Expand(string section)
    {
        nav.SuspendContent();
        foreach (var (head, items) in sections)
        {
            bool exp = head.Text == section;
            head.Expanded = exp;
            foreach (var it in items) it.Visible = exp;
        }
        nav.ResumeContent();
        var target = sections.FirstOrDefault(x => x.Head.Text == section);
        if (target.Head != null) nav.EnsureVisible(target.Items.LastOrDefault() ?? (Control)target.Head, target.Head);
    }

    void Highlight(Page page)
    {
        if (homeHead != null) homeHead.Active = page?.Text == HomeKey;
        foreach (var (btn, p) in navItems) btn.Active = ReferenceEquals(p, page);
        foreach (var (head, items) in sections) head.Active = page != null && head.Text == page.Group;
        if (page != null && page.Group != "" && !sections.Any(x => x.Head.Text == page.Group && x.Head.Expanded)) Expand(page.Group);
    }

    // ---------- التبويبات ----------
    public void Navigate(Page p)
    {
        if (open.ContainsKey(p.Text)) Activate(p.Text);
        else Open(p.Text, p.Make(), p);
    }

    /// <summary>الانتقال إلى شاشة باسمها (إن كانت ضمن صلاحيات المستخدم)</summary>
    public bool Go(string pageText)
    {
        var p = Pages.FirstOrDefault(x => x.Text == pageText);
        if (p == null) return false;
        Navigate(p);
        return true;
    }

    /// <summary>فتح شاشة في تبويب جديد (أو استبدال تبويب بنفس العنوان)</summary>
    public void Open(string title, Form f) => Open(title, f, Pages.FirstOrDefault(x => x.Text == title));

    void Open(string key, Form f, Page page)
    {
        if (open.TryGetValue(key, out var existing))
        {
            if (existing.Form is BaseForm bf && !bf.ConfirmClose()) { f.Dispose(); Activate(key); return; }
            Detach(existing.Form);
            open.Remove(key);
        }
        // حد أقصى للتبويبات: يُغلق أقدم تبويب لا يحتوي عملًا غير محفوظ
        if (open.Count >= MaxTabs)
        {
            var old = tabs.Items.Select(t => t.Key).FirstOrDefault(k => k != HomeKey && k != activeKey && !(open[k].Form is InvoiceForm or TransferForm));
            if (old != null) { Detach(open[old].Form); open.Remove(old); tabs.Remove(old); }
        }
        f.TopLevel = false;
        f.FormBorderStyle = FormBorderStyle.None;
        f.Dock = DockStyle.Fill;
        f.BackColor = Theme.Bg;
        f.Visible = false;
        content.Controls.Add(f);
        open[key] = (f, page);
        tabs.Set(key, key, page?.Icon ?? "square-pen", key != HomeKey);
        // عنوان التبويب يتبع عنوان الشاشة (مثل «تعديل فاتورة» ← «فاتورة بيع» بعد الحفظ)
        if (page == null) f.TextChanged += (s, e) => { if (open.ContainsKey(key) && f.Text != "") tabs.Set(key, f.Text, "square-pen", true); };
        Activate(key, fresh: true);
    }

    void Activate(string key) => Activate(key, false);

    void Activate(string key, bool fresh)
    {
        if (!open.TryGetValue(key, out var entry)) return;
        // لوحة التحكم تُبنى من جديد عند الرجوع إليها لتعرض أحدث الأرقام
        if (!fresh && key == HomeKey && activeKey != HomeKey && entry.Page != null)
        {
            var f = entry.Page.Make();
            f.TopLevel = false; f.FormBorderStyle = FormBorderStyle.None; f.Dock = DockStyle.Fill; f.BackColor = Theme.Bg; f.Visible = false;
            content.Controls.Add(f);
            Detach(entry.Form);
            entry = (f, entry.Page);
            open[key] = entry;
        }
        content.SuspendLayout();
        entry.Form.Show();
        entry.Form.BringToFront();
        foreach (var o in open.Values) if (o.Form != entry.Form && o.Form.Visible) o.Form.Hide();
        content.ResumeLayout();

        bool changed = activeKey != key;
        activeKey = key;
        tabs.Activate(key);
        top.SetTitle(entry.Page?.Text ?? entry.Form.Text, entry.Page?.Desc ?? "", entry.Page?.Icon ?? "square-pen");
        Highlight(entry.Page);
        if (changed && !fresh && entry.Form is BaseForm b) try { b.OnPageActivated(); } catch { }
        if (!fresh) entry.Form.SelectNextControl(entry.Form, true, true, true, true);
    }

    public bool CloseTab(string key)
    {
        if (key == HomeKey || !open.TryGetValue(key, out var entry)) return false;
        if (key != activeKey) Activate(key);
        if (entry.Form is BaseForm bf && !bf.ConfirmClose()) return false;
        var keys = tabs.Items.Select(t => t.Key).ToList();
        int idx = keys.IndexOf(key);
        open.Remove(key);
        tabs.Remove(key);
        Detach(entry.Form);
        if (activeKey == key)
        {
            activeKey = null;
            var rest = tabs.Items.Select(t => t.Key).ToList();
            if (rest.Count > 0) Activate(rest[Math.Clamp(idx - 1, 0, rest.Count - 1)]);
        }
        return true;
    }

    /// <summary>يغلق كل التبويبات (مع التأكيد على غير المحفوظ) — false إذا ألغى المستخدم</summary>
    bool CloseAllTabs()
    {
        foreach (var key in tabs.Items.Select(t => t.Key).Where(k => k != HomeKey).ToList())
            if (!CloseTab(key)) return false;
        return true;
    }

    void CycleTab(int dir)
    {
        var keys = tabs.Items.Select(t => t.Key).ToList();
        if (keys.Count < 2) return;
        int i = keys.IndexOf(activeKey);
        Activate(keys[((i + dir) % keys.Count + keys.Count) % keys.Count]);
    }

    void Detach(Form f)
    {
        content.Controls.Remove(f);
        BeginInvoke(() => f.Dispose());   // التخلص لاحقًا لأن الطلب قد يأتي من زر داخل الشاشة نفسها
    }

    // ---------- أدوات الشريط العلوي ----------
    void BackupNow()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var file = Backup.Run();
            Cursor = Cursors.Default;
            Toast.Show("تم حفظ نسخة احتياطية: " + Path.GetFileName(file));
        }
        catch (Exception ex) { Cursor = Cursors.Default; Dialogs.Error("تعذّر إنشاء النسخة الاحتياطية:\n" + ex.Message); }
    }

    static void Shell(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Warn("تعذّر الفتح: " + ex.Message); }
    }

    void ShowHelp() => Dialogs.Message(
        "اختصارات مفيدة:\n" +
        "•  Ctrl+K  البحث السريع عن أي شاشة\n" +
        "•  Ctrl+W  إغلاق التبويب الحالي،  Ctrl+Tab  التنقل بين التبويبات\n" +
        "•  F2  مربع الباركود في الفاتورة،  F10  حفظ الفاتورة\n\n" +
        "القائمة الجانبية: انقر على اسم القسم (المخزن، بيع، شراء...) لفتحه وإظهار شاشاته.\n" +
        "كل شاشة تُفتح في تبويب مستقل أعلى الصفحة، فيمكنك ترك فاتورة مفتوحة والرجوع إليها.\n\n" +
        $"قاعدة البيانات:\n{Db.DataDir}",
        "الدعم والمساعدة", Tone.Info);

    /// <summary>الشريط العلوي: زر القائمة وعنوان الشاشة، وأدوات سريعة (نسخ احتياطي، واتساب، حاسبة، تنبيهات، مساعدة)</summary>
    sealed class TopBar : Panel
    {
        string title = "", desc = "", icon;
        public ModernButton Menu { get; }
        public ModernButton Search { get; }
        public ModernButton Backup { get; }
        public ModernButton WhatsApp { get; }
        public ModernButton Calc { get; }
        public ModernButton Bell { get; }
        public ModernButton Help { get; }
        readonly ModernButton[] tools;

        public TopBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 68;
            BackColor = Theme.Surface;
            var tip = new ToolTip();
            ModernButton Tool(string iconName, string hint, BtnKind kind = BtnKind.Secondary)
            {
                var b = new ModernButton { Kind = kind, IconName = iconName, Size = new Size(42, 40), TabStop = false, Radius = 10 };
                tip.SetToolTip(b, hint);
                Controls.Add(b);
                return b;
            }
            Menu = Tool("menu", "إظهار / إخفاء القائمة", BtnKind.Ghost);
            Help = new ModernButton { Kind = BtnKind.Dark, IconName = "headset", Text = "الدعم والمساعدة", Font = Theme.FS(9.5f), Height = 40, TabStop = false, Radius = 10 };
            Help.FitWidth(150);
            Controls.Add(Help);
            Bell = Tool("bell", "التنبيهات", BtnKind.Accent);
            Calc = Tool("calculator", "الحاسبة");
            WhatsApp = Tool("message-circle", "واتساب ويب");
            Backup = Tool("cloud-upload", "نسخة احتياطية الآن");
            Search = new ModernButton { Kind = BtnKind.Secondary, IconName = "search", Text = "بحث سريع   Ctrl+K", Font = Theme.F(9.5f), Height = 40, TabStop = false, Radius = 10 };
            Search.FitWidth(190);
            Controls.Add(Search);
            tools = new[] { Help, Bell, Calc, WhatsApp, Backup, Search };
        }

        public void SetTitle(string t, string d, string i) { title = t; desc = d; icon = i; Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tools == null) return;   // يُستدعى من المُنشئ قبل إنشاء الأزرار
            Menu.Location = new Point(Width - 18 - Menu.Width, (Height - Menu.Height) / 2);
            // الأدوات في الجهة اليسرى (نهاية السطر العربي)
            int x = 20;
            foreach (var b in tools)
            {
                b.Location = new Point(x, (Height - b.Height) / 2);
                x = b.Right + (b == Help || b == Backup ? 16 : 8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Gfx.Hq(g);
            int right = Menu.Left - 14;
            if (icon != null && FontKit.HasIcons)
            {
                var ir = new RectangleF(right - 40, (Height - 40) / 2f, 40, 40);
                Gfx.FillRound(g, ir, 11, Theme.OrangeSoft);
                Icons.Draw(g, icon, ir, Theme.Orange, 20);
                right -= 52;
            }
            int left = Search.Right + 20;
            TextRenderer.DrawText(g, title, Theme.FS(14), new Rectangle(left, 10, right - left, 28), Theme.Ink, Gfx.RtlStart);
            TextRenderer.DrawText(g, desc, Theme.F(9.5f), new Rectangle(left, 38, right - left, 22), Theme.Muted, Gfx.RtlStart);
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>حاوية تمرير بعجلة الفأرة بدون شريط تمرير ظاهر (للقائمة الجانبية)؛ العناصر بعرضها الكامل</summary>
public class ScrollHost : Panel
{
    readonly FlowLayoutPanel inner = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Padding.Empty, Margin = Padding.Empty };

    public ScrollHost()
    {
        DoubleBuffered = true;
        inner.BackColor = Theme.Sidebar;
        Controls.Add(inner);
        inner.Location = Point.Empty;
        inner.SizeChanged += (s, e) => Scroll(0);
    }

    public void Add(Control c)
    {
        c.Width = Width;
        c.Margin = Padding.Empty;
        inner.Controls.Add(c);
        c.MouseWheel += (s, e) => Scroll(e.Delta);
    }

    public void SuspendContent() => inner.SuspendLayout();
    public void ResumeContent() => inner.ResumeLayout(true);

    /// <summary>تمرير يُظهر العنصر الأخير من القسم المفتوح مع بقاء رأسه ظاهرًا</summary>
    public void EnsureVisible(Control last, Control first)
    {
        int bottom = last.Bottom + inner.Top, topY = first.Top + inner.Top;
        if (bottom > Height) inner.Top -= bottom - Height;
        if (first.Top + inner.Top < 0 || topY < 0) inner.Top = -first.Top;
        Scroll(0);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        inner.Width = Width;
        foreach (Control c in inner.Controls) c.Width = Width;
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
