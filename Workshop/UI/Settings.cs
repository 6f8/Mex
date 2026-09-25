using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>الإعدادات: بيانات المحل، المظهر، الخصوصية، والنسخ الاحتياطي ونقل البيانات</summary>
public class SettingsDialog : DialogShell
{
    readonly TextBox shopName = new() { Width = 360 }, shopPhone = new() { Width = 220 }, shopAddress = new() { Width = 600 };
    readonly TextBox terms = new() { Width = 600, Height = 150, Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly ComboBox currency = W.Combo(180, new[] { "د.ع", "$", "ر.س", "د.إ", "ج.م", "د.أ", "ل.س" }, true);
    readonly TextBox country = new() { Width = 120 };
    readonly ComboBox palette = W.Combo(420, Theme.Palettes.Select(p => p.Name));
    readonly Toggle compact = new() { Text = "جداول مضغوطة (صفوف أقصر لعرض طلبات أكثر)", Width = 600 };
    readonly Toggle labelAfterSave = new() { Text = "عرض طباعة ملصق الجهاز بعد حفظ طلب جديد", Width = 600 };
    readonly Toggle labelPass = new() { Text = "طباعة رمز القفل على ملصق الجهاز", Width = 600 };
    readonly Toggle clearPass = new() { Text = "مسح رمز القفل تلقائياً عند تسليم الجهاز", Width = 600 };
    readonly List<(string Key, Toggle T)> dash = new();
    readonly Label backupInfo = W.Note("", 640, 48);
    readonly Label autoInfo = W.Note("", 640, 48);
    bool themeChanged;

    SettingsDialog() : base("الإعدادات", 860, 720, "settings")
    {
        var tabs = new ModernTabs { Dock = DockStyle.Fill };
        tabs.Add("المحل", ShopTab(), "store");
        tabs.Add("المظهر", LookTab(), "sun");
        tabs.Add("الخصوصية", PrivacyTab(), "shield-check");
        tabs.Add("النسخ الاحتياطي والبيانات", DataTab(), "database", "البيانات");
        tabs.SelectedIndex = 0;
        Body.Controls.Add(tabs);

        AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save").Click += (s, e) => Save();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        LoadValues();
    }

    public static void Open()
    {
        using var d = new SettingsDialog();
        d.ShowModal();
    }

    static FlowLayoutPanel Page() => new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(8) };

    Control ShopTab()
    {
        var p = Page();
        p.Controls.Add(W.Note("تظهر هذه البيانات في الفواتير والملصقات ورسائل واتساب.", 640));
        var r = W.Flow();
        r.Controls.Add(W.Labeled("اسم المحل", shopName, "store"));
        r.Controls.Add(W.Labeled("هاتف المحل", shopPhone, "phone"));
        p.Controls.Add(r);
        p.Controls.Add(W.Labeled("العنوان", shopAddress, "pin"));
        var r2 = W.Flow();
        r2.Controls.Add(W.Labeled("العملة", currency));
        r2.Controls.Add(W.Labeled("رمز الدولة للواتساب", country));
        p.Controls.Add(r2);
        p.Controls.Add(W.Labeled("شروط الصيانة (تُطبع أسفل الفاتورة ووصل الاستلام)", terms));
        var reset = W.Btn("استرجاع الشروط الافتراضية", "rotate-ccw", BtnKind.Ghost, 200);
        reset.Click += (s, e) => terms.Text = Store.DefaultTerms;
        p.Controls.Add(reset);
        return p;
    }

    Control LookTab()
    {
        var p = Page();
        p.Controls.Add(W.Labeled("ألوان البرنامج (تُطبّق بعد إعادة التشغيل)", palette, "sun"));
        palette.SelectedIndexChanged += (s, e) => themeChanged = true;
        p.Controls.Add(compact);
        p.Controls.Add(labelAfterSave);
        p.Controls.Add(W.Head("بطاقات الأرقام في الرئيسية", 640));
        foreach (var (k, t) in K.DashCells)
        {
            var tg = new Toggle { Text = t, Width = 600 };
            dash.Add((k, tg));
            p.Controls.Add(tg);
        }
        return p;
    }

    Control PrivacyTab()
    {
        var p = Page();
        p.Controls.Add(W.Note("رمز قفل الجهاز يُحفظ ليتمكن الفني من فحصه؛ يمكنك إخفاؤه من الملصق أو مسحه بعد التسليم.", 640, 40));
        p.Controls.Add(labelPass);
        p.Controls.Add(clearPass);
        return p;
    }

    Control DataTab()
    {
        var p = Page();
        p.Controls.Add(W.Head("النسخ الاحتياطي", 640));
        p.Controls.Add(backupInfo);
        var r = W.Flow();
        var bNow = W.Btn("نسخة احتياطية الآن", "database", BtnKind.Primary, 170);
        bNow.Click += (s, e) => { MainForm.Instance?.BackupNow(); RefreshInfo(); };
        var bSave = W.Btn("حفظ نسخة في...", "download", BtnKind.Secondary, 150);
        bSave.Click += (s, e) =>
        {
            var f = W.SaveFile("نسخة الورشة (*.db)|*.db", $"workshop-{Txt.Today}.db");
            if (f == null) return;
            try { Backup.CopyTo(f); Store.Set("last_backup", Txt.Now); Toast.Show("حُفظت النسخة"); RefreshInfo(); }
            catch (Exception ex) { Dialogs.Error("تعذّر الحفظ: " + ex.Message); }
        };
        var bRestore = W.Btn("استعادة نسخة", "rotate-ccw", BtnKind.Danger, 150);
        bRestore.Click += (s, e) => RestoreDb();
        var bFolder = W.Btn("فتح مجلد النسخ", "folder-open", BtnKind.Ghost, 150);
        bFolder.Click += (s, e) => { Directory.CreateDirectory(Backup.DefaultDir); W.OpenUrl(Backup.DefaultDir); };
        r.Controls.AddRange(new Control[] { bNow, bSave, bRestore, bFolder });
        p.Controls.Add(r);

        p.Controls.Add(W.Head("النسخ التلقائي", 640));
        p.Controls.Add(autoInfo);
        var r2 = W.Flow();
        var bPick = W.Btn("اختيار مجلد", "folder-open", BtnKind.Secondary, 140);
        bPick.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog { Description = "اختر مجلداً (مثل Google Drive أو فلاشة) لحفظ نسخة يومية تلقائياً" };
            if (d.ShowDialog() != DialogResult.OK) return;
            Store.Set("auto_backup_dir", d.SelectedPath);
            if (AutoBackup.Run()) Toast.Show("فُعّل النسخ التلقائي");
            else Dialogs.Warn("تعذّر الحفظ في المجلد: " + AutoBackup.Error);
            RefreshInfo();
        };
        var bOff = W.Btn("إيقاف", "ban", BtnKind.Ghost, 100);
        bOff.Click += (s, e) => { Store.Set("auto_backup_dir", ""); RefreshInfo(); };
        r2.Controls.AddRange(new Control[] { bPick, bOff });
        p.Controls.Add(r2);

        p.Controls.Add(W.Head("نقل البيانات", 640));
        p.Controls.Add(W.Note("ملف JSON متوافق مع نسخة المتصفح السابقة من البرنامج (مع الصور).", 640));
        var r3 = W.Flow();
        var bImp = W.Btn("استيراد من نسخة المتصفح", "arrow-up-from-line", BtnKind.Secondary, 200);
        bImp.Click += (s, e) => ImportWeb();
        var bExp = W.Btn("تصدير JSON", "download", BtnKind.Secondary, 140);
        bExp.Click += (s, e) =>
        {
            var f = W.SaveFile("JSON (*.json)|*.json", $"workshop-backup-{Txt.Today}.json");
            if (f == null) return;
            try { WebBackup.Export(f); Toast.Show("صُدّرت البيانات"); } catch (Exception ex) { Dialogs.Error("تعذّر التصدير: " + ex.Message); }
        };
        var bCsv = W.Btn("تصدير الطلبات CSV", "file-text", BtnKind.Secondary, 170);
        bCsv.Click += (s, e) =>
        {
            var f = W.SaveFile("CSV (*.csv)|*.csv", $"orders-{Txt.Today}.csv");
            if (f == null) return;
            try { Csv.ExportOrders(f); Toast.Show("صُدّرت الطلبات"); } catch (Exception ex) { Dialogs.Error("تعذّر التصدير: " + ex.Message); }
        };
        r3.Controls.AddRange(new Control[] { bImp, bExp, bCsv });
        p.Controls.Add(r3);

        p.Controls.Add(W.Head("مسح البيانات", 640));
        var r4 = W.Flow();
        var bReset = W.Btn("مسح كل البيانات", "trash-2", BtnKind.Danger, 160);
        bReset.Click += (s, e) => ResetAll();
        r4.Controls.Add(bReset);
        if (Backup.HasSnapshot)
        {
            var bUndo = W.Btn("التراجع عن آخر مسح", "rotate-ccw", BtnKind.Secondary, 180);
            bUndo.Click += (s, e) =>
            {
                if (!W.Confirm("استرجاع البيانات قبل آخر مسح؟", "ستُستبدل البيانات الحالية بالنسخة المحفوظة قبل المسح.", "استرجاع")) return;
                try { Backup.RestoreSnapshot(); Store.NotifyChanged(); Toast.Show("استُرجعت البيانات"); bUndo.Visible = false; }
                catch (Exception ex) { Dialogs.Error("تعذّر الاسترجاع: " + ex.Message); }
            };
            r4.Controls.Add(bUndo);
        }
        p.Controls.Add(r4);
        p.Controls.Add(W.Note("مجلد البيانات: " + Store.DataDir, 640));
        return p;
    }

    void LoadValues()
    {
        shopName.Text = Store.Get("shop_name", "ورشة الصيانة");
        shopPhone.Text = Store.ShopPhone;
        shopAddress.Text = Store.ShopAddress;
        terms.Text = Store.Terms;
        W.Pick(currency, Store.Currency);
        country.Text = Store.CountryCode;
        int pi = Array.FindIndex(Theme.Palettes, x => x.Key == Store.Get("ui_theme", "corporate"));
        palette.SelectedIndex = Math.Max(0, pi);
        themeChanged = false;
        compact.Checked = Store.Compact;
        labelAfterSave.Checked = Store.Flag("label_after_save", true);
        labelPass.Checked = Store.LabelPasscode;
        clearPass.Checked = Store.ClearPasscodeOnDelivery;
        foreach (var (k, t) in dash) t.Checked = Store.DashCell(k);
        RefreshInfo();
    }

    void RefreshInfo()
    {
        int d = Backup.DaysSinceLast();
        backupInfo.Text = d < 0 ? "لم تُحفظ أي نسخة احتياطية بعد." : "آخر نسخة: " + Txt.FmtDate(Txt.Cut10(Store.Get("last_backup"))) + (d == 0 ? " (اليوم)" : $" (قبل {d} يوم)");
        backupInfo.ForeColor = d < 0 || d >= 7 ? Pal.Bad : Theme.Muted;
        autoInfo.Text = AutoBackup.Dir == ""
            ? "غير مفعّل — اختر مجلداً ليُحفظ فيه ملف لكل يوم تلقائياً بعد التعديلات (يُحتفظ بآخر 30 يوماً)."
            : $"المجلد: {AutoBackup.Dir}" + (AutoBackup.LastAt != "" ? "\nآخر نسخة تلقائية: " + Txt.FmtDate(Txt.Cut10(AutoBackup.LastAt)) : "") + (AutoBackup.Error != "" ? "\nخطأ: " + AutoBackup.Error : "");
    }

    void Save()
    {
        var cc = new string(Txt.LatinDigits(country.Text).Where(char.IsDigit).ToArray());
        Store.Set("shop_name", shopName.Text.Trim());
        Store.Set("shop_phone", shopPhone.Text.Trim());
        Store.Set("shop_address", shopAddress.Text.Trim());
        Store.Set("shop_terms", terms.Text.Trim());
        Store.Set("currency", currency.Text.Trim());
        Store.Set("country_code", cc == "" ? "964" : cc);
        Store.SetFlag("compact", compact.Checked);
        Store.SetFlag("label_after_save", labelAfterSave.Checked);
        Store.SetFlag("privacy_label_passcode", labelPass.Checked);
        Store.SetFlag("privacy_clear_passcode", clearPass.Checked);
        foreach (var (k, t) in dash) Store.SetFlag("dash_" + k, t.Checked);
        if (themeChanged && palette.SelectedIndex >= 0)
        {
            Store.Set("ui_theme", Theme.Palettes[palette.SelectedIndex].Key);
            if (W.Confirm("تغيير الألوان", "تُطبّق الألوان الجديدة بعد إعادة تشغيل البرنامج. إعادة التشغيل الآن؟", "إعادة التشغيل"))
            {
                Program.Restart();
                return;
            }
        }
        themeChanged = false;
        Store.NotifyChanged();
        MainForm.Instance?.UpdateShop();
        Toast.Show("حُفظت الإعدادات");
        DialogResult = DialogResult.OK;
        Close();
    }

    void RestoreDb()
    {
        var f = W.OpenFile("نسخة الورشة (*.db)|*.db");
        if (f == null) return;
        if (!Backup.IsValid(f, out var err)) { Dialogs.Warn(err); return; }
        if (!W.Confirm("استعادة هذه النسخة؟", $"{Path.GetFileName(f)}\nستُستبدل كل البيانات الحالية (تُحفظ نسخة أمان منها أولاً).", "استعادة", true)) return;
        try
        {
            Backup.Restore(f);
            Store.NotifyChanged();
            MainForm.Instance?.UpdateShop();
            LoadValues();
            Toast.Show("استُعيدت النسخة");
        }
        catch (Exception ex) { Dialogs.Error("تعذّرت الاستعادة: " + ex.Message); }
    }

    void ImportWeb()
    {
        var f = W.OpenFile("نسخة المتصفح (*.json)|*.json");
        if (f == null) return;
        WebBackup.Pending p;
        try { p = WebBackup.Read(f); }
        catch (Exception ex) { Dialogs.Error("الملف غير صالح: " + ex.Message); return; }
        var summary = $"طلبات: {p.Orders.Count}   محذوفات: {p.Trash.Count}   قطع: {p.Inventory.Count}   مصاريف: {p.Expenses.Count}   حركات موردين: {p.SupplierTx.Count}   تعريفات: {p.Drivers.Count}   صور: {p.Photos.Count}";
        var ans = W.Ask3("استيراد البيانات", summary + "\n\nدمج: يضيف ما ليس موجوداً ويبقي بياناتك.\nاستبدال: يحذف البيانات الحالية ويضع بيانات الملف (تُحفظ نسخة أمان أولاً).", "دمج", "استبدال");
        if (ans == false) return;
        bool replace = ans == null;
        if (replace && !W.Confirm("استبدال كل البيانات؟", "ستُحذف البيانات الحالية وتحل محلها بيانات الملف.", "استبدال", true)) return;
        try
        {
            if (replace) try { Backup.Run(); } catch { }
            int n = WebBackup.Apply(p, replace);
            Store.NotifyChanged();
            MainForm.Instance?.UpdateShop();
            LoadValues();
            Toast.Show(replace ? $"استُبدلت البيانات ({n} طلب)" : $"أُضيف {n} طلب جديد");
        }
        catch (Exception ex) { Dialogs.Error("تعذّر الاستيراد: " + ex.Message); }
    }

    void ResetAll()
    {
        using var d = new DialogShell("مسح كل البيانات", 500, 300, "trash-2", Pal.Bad);
        var t = new TextBox { Width = 400 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note("ستُحذف كل الطلبات والقطع والمصاريف وحسابات الموردين. للتأكيد اكتب كلمة «مسح»:", 420, 44));
        flow.Controls.Add(W.Wrap(t));
        d.Body.Controls.Add(flow);
        var ok = d.AddButton("مسح نهائي", DialogResult.None, BtnKind.Danger, "trash-2");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            if (t.Text.Trim() != "مسح") { Toast.Show("اكتب «مسح» للتأكيد", Tone.Warning); return; }
            d.DialogResult = DialogResult.OK;
            d.Close();
        };
        if (d.ShowModal() != DialogResult.OK) return;
        try
        {
            Backup.ResetAll();
            Store.NotifyChanged();
            Toast.Show("مُسحت البيانات — يمكنك التراجع من الإعدادات");
        }
        catch (Exception ex) { Dialogs.Error("تعذّر المسح: " + ex.Message); }
    }
}
