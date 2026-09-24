using System.Data;

namespace Raseed;

public class LoginForm : BaseForm
{
    readonly TextBox user = new() { Width = 290, Text = "admin" };
    readonly TextBox pass = new() { Width = 290, UseSystemPasswordChar = true };

    public LoginForm()
    {
        Text = "تسجيل الدخول — رصيد";
        Width = 420; Height = 400;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;

        var head = new Label { Text = "رصيد", Dock = DockStyle.Top, Height = 95, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(28, FontStyle.Bold), ForeColor = Color.White, BackColor = Theme.Primary };
        var sub = new Label { Text = "نظام المبيعات والمخازن والحسابات", Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(50, 10, 50, 10) };
        flow.Controls.Add(Ui.Labeled("اسم المستخدم", user));
        flow.Controls.Add(Ui.Labeled("كلمة المرور", pass));
        var b = Theme.Btn("دخول", Theme.Accent, 298);
        b.Height = 42;
        b.Click += (s, e) =>
        {
            if (Session.Login(user.Text, pass.Text)) { DialogResult = DialogResult.OK; Close(); }
            else Ui.Warn("اسم المستخدم أو كلمة المرور غير صحيحة.");
        };
        flow.Controls.Add(b);
        AcceptButton = b;
        Controls.Add(flow);
        Controls.Add(sub);
        Controls.Add(head);
        Shown += (s, e) => pass.Focus();
    }
}

public class MainForm : BaseForm
{
    readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
    readonly Label pageTitle = new() { Dock = DockStyle.Fill, Font = Theme.F(15, FontStyle.Bold), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 16, 0) };
    readonly NotifyIcon tray = new() { Icon = SystemIcons.Information, Visible = true, Text = "رصيد" };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    readonly List<Button> navButtons = new();
    Form current;

    (string Text, string Perm, Func<Form> Make)[] Pages() => new (string, string, Func<Form>)[]
    {
        ("الرئيسية", null, () => new DashboardForm(this)),
        ("فاتورة بيع", "sales", () => new InvoiceForm("Sale")),
        ("فاتورة شراء", "purchases", () => new InvoiceForm("Purchase")),
        ("إرجاع بيع", "returns", () => new InvoiceForm("SaleReturn")),
        ("إرجاع شراء", "returns", () => new InvoiceForm("PurchaseReturn")),
        ("إتلاف مواد", "damage", () => new InvoiceForm("Damage")),
        ("الصيانة", "repairs", () => new RepairsForm()),
        ("سجل الفواتير", "reports", () => new InvoicesListForm()),
        ("المواد", "items", () => new CrudForm(Defs.Items())),
        ("العملاء والموردون", "parties", () => new CrudForm(Defs.Parties())),
        ("المخازن والصلاحيات", "stock", () => new StockForm(this)),
        ("جرد المخزون", "stock", () => new StockCountForm()),
        ("ملصقات الباركود", "labels", () => new LabelsForm()),
        ("السندات والصيرفة", "vouchers", () => new VoucherForm()),
        ("الأقساط", "installments", () => new InstallmentsForm()),
        ("الموارد البشرية", "hr", () => new HrForm()),
        ("التقارير والأرباح", "reports", () => new ReportsForm()),
        ("مدير المهام", "tasks", () => new CrudForm(Defs.Tasks())),
        ("التعريفات", "settings", () => new DefsHubForm()),
        ("المستخدمون والصلاحيات", "users", () => new UsersForm()),
        ("الإعدادات والنسخ", "settings", () => new SettingsForm()),
    };

    public MainForm()
    {
        Text = $"رصيد — {Settings.Get("shop_name")}";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1150, 700);

        var side = new Panel { Dock = DockStyle.Left, Width = 235, BackColor = Theme.Primary };
        var logo = new Label { Text = "رصيد", Dock = DockStyle.Top, Height = 78, Font = Theme.F(24, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 4, 8, 4) };
        foreach (var (text, perm, make) in Pages())
        {
            if (!Session.Can(perm)) continue;
            var b = NavButton(text);
            b.Click += (s, e) => { Highlight(b); Open(text, make()); };
            nav.Controls.Add(b);
        }
        side.Controls.Add(nav);
        side.Controls.Add(logo);

        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White };
        var userLbl = new Label { Dock = DockStyle.Right, Width = 340, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Padding = new Padding(16, 0, 16, 0),
            Text = $"{Session.UserName}   |   {DateTime.Now:yyyy/MM/dd}" };
        var pwd = new LinkLabel { Text = "تغيير كلمة المرور", Dock = DockStyle.Right, Width = 130, TextAlign = ContentAlignment.MiddleCenter, LinkColor = Theme.Accent };
        pwd.LinkClicked += (s, e) => { using var d = new PasswordDialog(); d.ShowDialog(this); };
        header.Controls.Add(pageTitle);
        header.Controls.Add(pwd);
        header.Controls.Add(userLbl);

        Controls.Add(content);
        Controls.Add(header);
        Controls.Add(side);

        Scheduler.Notify = (t, m) => BeginInvoke(() => tray.ShowBalloonTip(8000, t, m, ToolTipIcon.Info));
        Scheduler.Alert = (t, m) => BeginInvoke(() => MessageBox.Show(this, m, "تذكير: " + t, MessageBoxButtons.OK, MessageBoxIcon.Information));
        tray.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Maximized; Activate(); };

        timer.Tick += (s, e) => Scheduler.Tick();
        Shown += (s, e) =>
        {
            if (navButtons.Count > 0) Highlight(navButtons[0]);
            Open("الرئيسية", new DashboardForm(this));
            MobileApi.Start();
            timer.Start();
            Scheduler.Tick();
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

    Button NavButton(string text)
    {
        var b = new Button
        {
            Text = "   " + text, Width = 216, Height = 42, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Theme.Primary,
            TextAlign = ContentAlignment.MiddleLeft, Font = Theme.F(10.5f), Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 2)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Theme.PrimaryHover;
        navButtons.Add(b);
        return b;
    }

    void Highlight(Button active)
    {
        foreach (var b in navButtons) b.BackColor = b == active ? Theme.Accent : Theme.Primary;
    }

    public void Open(string title, Form f)
    {
        if (current != null)
        {
            var old = current;
            content.Controls.Remove(old);
            BeginInvoke(() => old.Dispose());   // التخلص لاحقاً لأن الطلب قد يأتي من زر داخل الشاشة نفسها
        }
        f.TopLevel = false;
        f.FormBorderStyle = FormBorderStyle.None;
        f.Dock = DockStyle.Fill;
        content.Controls.Add(f);
        pageTitle.Text = title;
        f.Show();
        current = f;
    }
}

/// <summary>التعريفات: المخازن، الصناديق والخزائن، مراكز الكلفة، شركات التوصيل، الشركاء</summary>
public class DefsHubForm : BaseForm
{
    public DefsHubForm()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, RightToLeftLayout = true, Font = Theme.F(10, FontStyle.Bold) };
        foreach (var def in new[] { Defs.Warehouses(), Defs.Cashboxes(), Defs.CostCenters(), Defs.Delivery(), Defs.Partners() })
        {
            var page = new TabPage(def.Title) { BackColor = Theme.Bg };
            var f = new CrudForm(def) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
            page.Controls.Add(f);
            f.Show();
            tabs.TabPages.Add(page);
        }
        Controls.Add(tabs);
    }
}

/// <summary>المستخدمون والصلاحيات المتقدمة</summary>
public class UsersForm : BaseForm
{
    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox tUser = new() { Width = 260 }, tPass = new() { Width = 260, UseSystemPasswordChar = true }, tName = new() { Width = 260 };
    readonly CheckBox cAdmin = new() { Text = "مدير (كل الصلاحيات)", Width = 260 }, cActive = new() { Text = "حساب فعّال", Width = 260, Checked = true };
    readonly CheckedListBox perms = new() { Width = 260, Height = 360, CheckOnClick = true };
    long id;
    string loadedUser = "";

    public UsersForm()
    {
        foreach (var p in Session.AllPerms) perms.Items.Add(p.Title);
        var editor = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(10) };
        editor.Controls.Add(Ui.Labeled("اسم الدخول", tUser));
        editor.Controls.Add(Ui.Labeled("كلمة المرور (اتركها فارغة لعدم التغيير)", tPass));
        editor.Controls.Add(Ui.Labeled("الاسم الكامل", tName));
        editor.Controls.Add(cAdmin);
        editor.Controls.Add(cActive);
        editor.Controls.Add(Ui.Labeled("الصلاحيات", perms));
        var bNew = Theme.Btn("جديد", Theme.Gray, 120);
        var bSave = Theme.Btn("حفظ", Theme.Success, 120);
        editor.Controls.Add(bNew);
        editor.Controls.Add(bSave);

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10 });
        Controls.Add(editor);
        Controls.Add(Theme.Title("المستخدمون والصلاحيات"));

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
        id = 0; loadedUser = "";
        tUser.Clear(); tPass.Clear(); tName.Clear();
        cAdmin.Checked = false; cActive.Checked = true;
        for (int i = 0; i < perms.Items.Count; i++) perms.SetItemChecked(i, false);
    }

    void LoadUser()
    {
        if (grid.CurrentRow == null) return;
        id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var r = Db.Query("SELECT * FROM users WHERE id=@p0", id).Rows[0];
        tUser.Text = loadedUser = Db.S(r["username"]);
        tName.Text = Db.S(r["full_name"]);
        tPass.Clear();
        cAdmin.Checked = Db.L(r["is_admin"]) == 1;
        cActive.Checked = Db.L(r["active"]) == 1;
        var set = Db.Query("SELECT perm FROM user_perms WHERE user_id=@p0", id).Rows.Cast<DataRow>().Select(x => Db.S(x["perm"])).ToHashSet();
        for (int i = 0; i < perms.Items.Count; i++) perms.SetItemChecked(i, set.Contains(Session.AllPerms[i].Key));
    }

    void Save()
    {
        if (!Session.Guard("users")) return;
        string u = tUser.Text.Trim(), p = tPass.Text;
        if (u == "") { Ui.Warn("أدخل اسم الدخول."); return; }
        if ((id == 0 || u != loadedUser) && p == "") { Ui.Warn("أدخل كلمة المرور."); return; }
        if (id == Session.UserId && (!cActive.Checked || (Session.IsAdmin && !cAdmin.Checked))) { Ui.Warn("لا يمكنك إيقاف حسابك أو إزالة صلاحية المدير عن نفسك."); return; }
        try
        {
            using var tx = new Tx();
            if (id == 0)
                id = tx.Insert("INSERT INTO users(username,pass_hash,full_name,is_admin,active) VALUES(@p0,@p1,@p2,@p3,@p4)",
                    u, Session.Hash(u, p), tName.Text.Trim(), cAdmin.Checked ? 1 : 0, cActive.Checked ? 1 : 0);
            else
            {
                tx.Exec("UPDATE users SET username=@p0, full_name=@p1, is_admin=@p2, active=@p3 WHERE id=@p4",
                    u, tName.Text.Trim(), cAdmin.Checked ? 1 : 0, cActive.Checked ? 1 : 0, id);
                if (p != "") tx.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.Hash(u, p), id);
            }
            tx.Exec("DELETE FROM user_perms WHERE user_id=@p0", id);
            foreach (int i in perms.CheckedIndices)
                tx.Exec("INSERT INTO user_perms(user_id,perm) VALUES(@p0,@p1)", id, Session.AllPerms[i].Key);
            tx.Commit();
            loadedUser = u;
            tPass.Clear();
            LoadGrid();
            Ui.Info("تم حفظ المستخدم.");
        }
        catch (Exception ex) { Ui.Warn("تعذر الحفظ: " + ex.Message); }
    }
}

/// <summary>الإعدادات، النسخ الاحتياطي والاستعادة، واتساب، ربط الهاتف</summary>
public class SettingsForm : BaseForm
{
    readonly Dictionary<string, Control> boxes = new();

    static readonly Dictionary<string, string[]> Choices = new()
    {
        ["print_mode"] = new[] { "A4", "80mm" },
        ["print_preview"] = new[] { "1", "0" },
        ["print_after_save"] = new[] { "2", "1", "0" },
        ["label_mode"] = new[] { "roll", "A4" },
        ["label_price"] = new[] { "1", "0" },
        ["backup_on_exit"] = new[] { "1", "0" },
        ["wa_mode"] = new[] { "link", "cloud" },
        ["api_enabled"] = new[] { "0", "1" },
    };

    public SettingsForm()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), BackColor = Color.White };
        foreach (var (key, caption, _) in Settings.All)
        {
            Control t;
            if (key is "printer_name" or "label_printer")
            {
                var cb = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDown };
                try { foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) cb.Items.Add(p); } catch { }
                cb.Text = Settings.Get(key);
                t = cb;
            }
            else if (Choices.TryGetValue(key, out var ch))
            {
                var cb = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
                cb.Items.AddRange(ch);
                int i = Array.IndexOf(ch, Settings.Get(key));
                cb.SelectedIndex = i >= 0 ? i : 0;
                t = cb;
            }
            else
            {
                var tb = new TextBox { Width = 440, Text = Settings.Get(key) };
                if (key is "wa_template" or "repair_terms" or "repair_ready_msg") { tb.Multiline = true; tb.Height = 70; }
                if (key == "wa_token") tb.UseSystemPasswordChar = true;
                t = tb;
            }
            boxes[key] = t;
            flow.Controls.Add(Ui.Labeled(caption, t));
        }

        var bar = Theme.Bar();
        var bSave = Theme.Btn("حفظ الإعدادات", Theme.Success, 150);
        var bBackup = Theme.Btn("نسخ احتياطي الآن", Theme.Accent, 160);
        var bRestore = Theme.Btn("استعادة نسخة", Theme.Danger, 140);
        var bFolder = Theme.Btn("فتح مجلد النسخ", Theme.Gray, 150);
        var bWa = Theme.Btn("اختبار واتساب", Theme.Purple, 140);
        var bPrint = Theme.Btn("طباعة تجريبية", Theme.Gray, 130);
        var bMobile = Theme.Btn("رابط تطبيق الهاتف", Theme.Accent, 160);
        bar.Controls.AddRange(new Control[] { bSave, bBackup, bRestore, bFolder, bWa, bPrint, bMobile });
        bPrint.Click += (s, e) =>
        {
            var d = PrintDoc.Header("صفحة تجريبية");
            d.Pair("حجم الورق", Settings.Get("print_mode"), "الطابعة", Settings.Get("printer_name") == "" ? "الافتراضية" : Settings.Get("printer_name"));
            d.Table(new[] { "#", "المادة", "الكمية", "السعر", "المجموع" }, new[] { 6f, 44, 14, 17, 19 },
                new List<string[]> { new[] { "1", "شاشة آيفون 13 أصلية", "1", "85,000", "85,000" }, new[] { "2", "بطارية سامسونج A52", "2", "25,000", "50,000" } });
            d.Footer();
            d.Barcode("TEST123");
            d.Print();
        };
        bMobile.Click += (s, e) =>
        {
            var urls = MobileApi.Urls();
            if (urls.Count == 0) { Ui.Warn("ربط الهاتف غير مفعّل. فعّله من الإعدادات (api_enabled = 1) ثم أعد تشغيل البرنامج."); return; }
            var text = string.Join("\r\n", urls);
            Clipboard.SetText(text);
            Ui.Info("افتح أحد الروابط التالية من متصفح الهاتف (على نفس شبكة الواي فاي)، ثم اختر «إضافة إلى الشاشة الرئيسية»:\n\n" + text + "\n\n(تم نسخ الروابط)");
        };
        bar.Controls.Add(new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(10, 16, 10, 0), Text = "ربط الهاتف: " + MobileApi.Status });

        Controls.Add(flow);
        Controls.Add(bar);
        Controls.Add(Theme.Title("الإعدادات"));

        bSave.Click += (s, e) =>
        {
            if (!Session.Guard("settings")) return;
            foreach (var kv in boxes) Settings.Set(kv.Key, kv.Value.Text.Trim());
            Ui.Info("تم الحفظ. بعض الإعدادات (مثل ربط الهاتف) تُطبّق عند إعادة تشغيل البرنامج.");
        };
        bBackup.Click += (s, e) =>
        {
            if (!Session.Guard("backup")) return;
            try { Ui.Info("تم حفظ النسخة الاحتياطية:\n" + Backup.Run()); }
            catch (Exception ex) { Ui.Warn("فشل النسخ: " + ex.Message); }
        };
        bRestore.Click += (s, e) =>
        {
            if (!Session.Guard("backup")) return;
            using var ofd = new OpenFileDialog { Filter = "نسخة رصيد (*.db)|*.db", InitialDirectory = Backup.LocalDir };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            if (!Ui.Confirm("سيتم استبدال جميع البيانات الحالية بالنسخة المختارة (تُحفظ نسخة أمان تلقائياً قبل ذلك). متابعة؟")) return;
            Backup.Restore(ofd.FileName);
            Ui.Info("تمت الاستعادة. سيُعاد تشغيل البرنامج.");
            Application.Restart();
            Environment.Exit(0);
        };
        bFolder.Click += (s, e) =>
        {
            Directory.CreateDirectory(Backup.LocalDir);
            System.Diagnostics.Process.Start("explorer.exe", Backup.LocalDir);
        };
        bWa.Click += async (s, e) =>
        {
            var ph = Settings.Get("shop_phone");
            if (ph == "") { Ui.Warn("أدخل هاتف المحل ثم احفظ الإعدادات."); return; }
            bool ok = await WhatsApp.Send(ph, "رسالة اختبار من برنامج رصيد ✓");
            if (WhatsApp.IsCloud) Ui.Info(ok ? "تم الإرسال بنجاح." : "فشل الإرسال — تحقق من Token و Phone Number ID.");
        };
    }
}
