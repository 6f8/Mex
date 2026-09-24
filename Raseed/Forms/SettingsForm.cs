namespace Raseed;

/// <summary>الإعدادات مقسّمة إلى أقسام واضحة بعناصر مفهومة (مفاتيح تشغيل، قوائم، اختيار مجلدات)</summary>
public class SettingsForm : BaseForm
{
    readonly Dictionary<string, Func<string>> getters = new();
    readonly ModernTabs tabs = new(vertical: true) { Dock = DockStyle.Fill };
    FlowLayoutPanel cur;

    public SettingsForm()
    {
        Section("بيانات المحل", "store", "تظهر في رأس الفواتير والوصولات المطبوعة");
        TextField("shop_name", 360);
        TextField("shop_phone", 360);
        TextField("shop_address", 736);
        TextField("invoice_footer", 736);

        Section("الطباعة", "printer", "طريقة طباعة الفواتير والسندات");
        Choice("print_mode", ("A4", "ورق A4 عادي"), ("80mm", "طابعة إيصالات حرارية 80 ملم"));
        Choice("print_after_save", ("2", "اسألني كل مرة"), ("1", "اطبع تلقائيًا"), ("0", "لا تطبع"));
        Printer("printer_name");
        Switch("print_preview", "عرض معاينة قبل الطباعة");
        Act("طباعة صفحة تجريبية", "printer", TestPrint);

        Section("ملصقات الباركود", "barcode", "مقاس الملصق ونوع الطابعة");
        Printer("label_printer");
        Choice("label_mode", ("roll", "طابعة ملصقات (رول)"), ("A4", "ورقة A4 مقسّمة"));
        Number("label_w", 175);
        Number("label_h", 175);
        Switch("label_price", "إظهار السعر على الملصق");

        Section("العملة والتنبيهات", "coins", "سعر الصرف ومواعيد التنبيه");
        Number("usd_rate", 240);
        Number("expiry_days", 240);
        Number("reminder_days", 240);

        Section("الصيانة", "wrench", "نصوص وصل الصيانة ورسالة الجاهزية");
        Memo("repair_terms");
        Memo("repair_ready_msg");
        Hint("يمكن استخدام: {name} اسم الزبون، {device} الجهاز، {id} رقم الوصل، {price} المبلغ، {shop} اسم المحل");

        Section("النسخ الاحتياطي", "cloud-upload", "حماية بياناتك من الضياع");
        Folder("backup_dir", "افتراضيًا داخل مجلد البرنامج");
        Folder("cloud_dir", "مجلد Google Drive أو OneDrive (اختياري)");
        Number("backup_keep", 175);
        Switch("backup_on_exit", "نسخة احتياطية تلقائية عند إغلاق البرنامج");
        Act("نسخ احتياطي الآن", "cloud-upload", BackupNow, BtnKind.Soft);
        Act("استعادة نسخة", "rotate-ccw", RestoreBackup, BtnKind.Danger);
        Act("فتح مجلد النسخ", "folder-open", () =>
        {
            Directory.CreateDirectory(Backup.LocalDir);
            System.Diagnostics.Process.Start("explorer.exe", Backup.LocalDir);
        }, BtnKind.Secondary);

        Section("واتساب", "message-circle", "إرسال الفواتير والتذكيرات للعملاء");
        Choice("wa_mode", ("link", "فتح واتساب والإرسال بضغطة (مجاني)"), ("cloud", "إرسال تلقائي عبر WhatsApp Cloud API"));
        Secret("wa_token");
        TextField("wa_phone_id", 360);
        Memo("wa_template");
        Hint("يمكن استخدام: {name} اسم العميل، {seq} رقم القسط، {amount} المبلغ، {due} تاريخ الاستحقاق، {shop} اسم المحل");
        Act("إرسال رسالة تجريبية", "send", TestWhatsApp, BtnKind.Soft);

        Section("ربط الهاتف", "smartphone", "متابعة المبيعات والمخزون من متصفح الهاتف");
        Switch("api_enabled", "تفعيل ربط تطبيق الهاتف (يُطبّق بعد إعادة التشغيل)");
        Number("api_port", 175);
        Token();
        Hint("الحالة: " + MobileApi.Status);
        Act("عرض روابط الهاتف", "link", ShowMobileLinks, BtnKind.Soft);

        Section("حول البرنامج", "info", "الإصدار ومكان حفظ البيانات");
        Hint($"رصيد — الإصدار {Application.ProductVersion.Split('+')[0]}");
        Hint("قاعدة البيانات: " + Db.FilePath);
        Hint("الخطوط: IBM Plex Sans Arabic (رخصة OFL) — الأيقونات: Lucide (رخصة ISC)");
        Act("فتح مجلد البيانات", "folder-open", () => System.Diagnostics.Process.Start("explorer.exe", Db.DataDir), BtnKind.Secondary);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };
        var bSave = new ModernButton { Text = "حفظ الإعدادات", IconName = "save", Dock = DockStyle.Right, Width = 170, Height = 44 };
        bSave.Click += (s, e) => Save();
        // الترتيب مهم: عنصر الملء أولاً ثم العنصر الملتصق بالحافة
        bar.Controls.Add(new Label
        {
            Text = "التغييرات لا تُحفظ حتى تضغط «حفظ الإعدادات»", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.F(9.5f),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0)
        });
        bar.Controls.Add(bSave);

        Controls.Add(tabs);
        Controls.Add(bar);
    }

    // ---------------- بناء الأقسام ----------------
    void Section(string title, string icon, string subtitle)
    {
        var card = new CardPanel { Title = title, Subtitle = subtitle, IconName = icon, Dock = DockStyle.Top };
        cur = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, AutoScroll = true, Padding = new Padding(0, 4, 0, 4) };
        var flow = cur;
        // الحقول العريضة تتبع عرض البطاقة (لا شريط تمرير أفقي في الشاشات الصغيرة)
        flow.Resize += (s, e) =>
        {
            int w = Math.Max(300, flow.ClientSize.Width - 24);
            foreach (Control c in flow.Controls)
                if (c.Tag as string == "wide") c.Width = Math.Min(740, w);
                else if (c.Width > w) c.Width = w;
        };
        card.Controls.Add(cur);
        var page = new Panel { BackColor = Theme.Bg };
        card.Dock = DockStyle.Fill;
        page.Controls.Add(card);
        tabs.Add(title, page, icon);
    }

    static string Caption(string key) => Settings.All.First(x => x.Key == key).Caption;

    void Add(Control c) { if (c.Width >= 700) c.Tag = "wide"; cur.Controls.Add(c); }

    void TextField(string key, int width)
    {
        var t = new TextBox { Width = width, Text = Settings.Get(key) };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Secret(string key)
    {
        var t = new TextBox { Width = 360, Text = Settings.Get(key), UseSystemPasswordChar = true };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Memo(string key)
    {
        var t = new TextBox { Width = 736, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = Settings.Get(key) };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Number(string key, int width)
    {
        var t = new TextBox { Width = width, Text = Settings.Get(key), TextAlign = HorizontalAlignment.Center };
        t.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.') e.Handled = true; };
        Add(Ui.Labeled(Caption(key), t));
        getters[key] = () => t.Text.Trim();
    }

    void Choice(string key, params (string Value, string Text)[] options)
    {
        var cb = Ui.Combo(360);
        foreach (var o in options) cb.Items.Add(o.Text);
        int i = Array.FindIndex(options, o => o.Value == Settings.Get(key));
        cb.SelectedIndex = i >= 0 ? i : 0;
        Add(Ui.Labeled(Caption(key), cb));
        getters[key] = () => options[Math.Max(0, cb.SelectedIndex)].Value;
    }

    void Printer(string key)
    {
        var cb = Ui.Combo(360);
        cb.Items.Add("الطابعة الافتراضية لويندوز");
        try { foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) cb.Items.Add(p); } catch { }
        var saved = Settings.Get(key);
        if (saved != "" && !cb.Items.Contains(saved)) cb.Items.Add(saved);
        cb.SelectedIndex = saved == "" ? 0 : cb.Items.IndexOf(saved);
        Add(Ui.Labeled(Caption(key), cb));
        getters[key] = () => cb.SelectedIndex <= 0 ? "" : Convert.ToString(cb.SelectedItem);
    }

    void Switch(string key, string text)
    {
        var t = new Toggle { Text = text, Width = 736, Checked = Settings.Get(key) == "1", Margin = new Padding(6, 10, 6, 6) };
        Add(t);
        getters[key] = () => t.Checked ? "1" : "0";
    }

    void Folder(string key, string placeholder)
    {
        var t = new TextBox { Width = 600, Text = Settings.Get(key), PlaceholderText = placeholder };
        Add(Ui.Labeled(Caption(key), t));
        var b = new ModernButton { Text = "اختيار...", Kind = BtnKind.Secondary, IconName = "folder-open", Margin = new Padding(6, 31, 6, 4), Height = 40 };
        b.FitWidth(120);
        b.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = t.Text, UseDescriptionForTitle = true, Description = Caption(key) };
            if (d.ShowDialog() == DialogResult.OK) t.Text = d.SelectedPath;
        };
        Add(b);
        getters[key] = () => t.Text.Trim();
    }

    void Token()
    {
        var t = new TextBox { Width = 360, Text = Settings.Get("api_token"), ReadOnly = true };
        Add(Ui.Labeled(Caption("api_token"), t));
        var b = new ModernButton { Text = "رمز جديد", Kind = BtnKind.Secondary, IconName = "refresh-cw", Margin = new Padding(6, 31, 6, 4), Height = 40 };
        b.FitWidth(120);
        b.Click += (s, e) =>
        {
            if (!Ui.Confirm("إنشاء رمز ربط جديد؟ الهواتف المرتبطة حاليًا ستحتاج الرابط الجديد.")) return;
            t.Text = Guid.NewGuid().ToString("N")[..12];
        };
        Add(b);
        getters["api_token"] = () => t.Text.Trim();
    }

    void Hint(string text) => Add(new Label
    {
        Text = text, AutoSize = false, Width = 736, Height = 30, ForeColor = Theme.Muted, Font = Theme.F(9),
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 2, 6, 2)
    });

    void Act(string text, string icon, Action act, BtnKind kind = BtnKind.Secondary)
    {
        if (cur.Controls.Count > 0 && cur.Controls[^1] is not ModernButton) cur.SetFlowBreak(cur.Controls[^1], true);
        var b = new ModernButton { Text = text, IconName = icon, Kind = kind, Margin = new Padding(6, 14, 6, 4) };
        b.FitWidth(150);
        b.Click += (s, e) => act();
        Add(b);
    }

    // ---------------- الإجراءات ----------------
    void Save()
    {
        if (!Session.Guard("settings")) return;
        foreach (var kv in getters) Settings.Set(kv.Key, kv.Value());
        Toast.Show("تم حفظ الإعدادات");
    }

    static void TestPrint()
    {
        var d = PrintDoc.Header("صفحة تجريبية");
        d.Pair("حجم الورق", Settings.Get("print_mode"), "الطابعة", Settings.Get("printer_name") == "" ? "الافتراضية" : Settings.Get("printer_name"));
        d.Table(new[] { "#", "المادة", "الكمية", "السعر", "المجموع" }, new[] { 6f, 44, 14, 17, 19 },
            new List<string[]> { new[] { "1", "شاشة آيفون 13 أصلية", "1", "85,000", "85,000" }, new[] { "2", "بطارية سامسونج A52", "2", "25,000", "50,000" } });
        d.Footer();
        d.Barcode("TEST123");
        d.Print();
    }

    static void BackupNow()
    {
        if (!Session.Guard("backup")) return;
        try { Dialogs.Info("تم حفظ النسخة الاحتياطية في:\n" + Backup.Run(), "نسخ احتياطي"); }
        catch (Exception ex) { Ui.Warn("فشل النسخ: " + ex.Message); }
    }

    static void RestoreBackup()
    {
        if (!Session.Guard("backup")) return;
        using var ofd = new OpenFileDialog { Filter = "نسخة رصيد (*.db)|*.db", InitialDirectory = Backup.LocalDir };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        if (!Backup.IsValidBackup(ofd.FileName, out var err)) { Ui.Warn(err); return; }
        if (!Dialogs.Confirm("سيتم استبدال جميع البيانات الحالية بالنسخة المختارة.\nتُحفظ نسخة أمان من البيانات الحالية تلقائيًا قبل الاستعادة.", "استعادة نسخة", "نعم، استعد النسخة", danger: true)) return;
        try { Backup.Restore(ofd.FileName); }
        catch (Exception ex) { Ui.Warn("تعذرت الاستعادة: " + ex.Message); return; }
        Dialogs.Info("تمت الاستعادة بنجاح. سيُعاد تشغيل البرنامج الآن.", "استعادة نسخة");
        Application.Restart();
        Environment.Exit(0);
    }

    static async void TestWhatsApp()
    {
        var ph = Settings.Get("shop_phone");
        if (ph == "") { Ui.Warn("أدخل هاتف المحل في «بيانات المحل» ثم احفظ الإعدادات."); return; }
        bool ok = await WhatsApp.Send(ph, "رسالة اختبار من برنامج رصيد ✓");
        if (WhatsApp.IsCloud)
        {
            if (ok) Toast.Show("تم الإرسال بنجاح");
            else Ui.Warn("فشل الإرسال — تحقق من رمز الوصول ومعرّف الرقم.");
        }
    }

    static void ShowMobileLinks()
    {
        var urls = MobileApi.Urls();
        if (urls.Count == 0) { Ui.Warn("ربط الهاتف غير مفعّل. فعّله من هذا القسم، احفظ الإعدادات، ثم أعد تشغيل البرنامج."); return; }
        var text = string.Join("\r\n", urls);
        Clipboard.SetText(text);
        Dialogs.Info("افتح أحد الروابط التالية من متصفح الهاتف (على نفس شبكة الواي فاي)، ثم اختر «إضافة إلى الشاشة الرئيسية»:\n\n" + text + "\n\n(تم نسخ الروابط)", "ربط الهاتف");
    }
}
