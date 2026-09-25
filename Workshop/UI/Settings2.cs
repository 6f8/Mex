using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>تبويبات التخصيص في الإعدادات: القوائم، الفنيون وعمولاتهم، وقوالب رسائل واتساب</summary>
public partial class SettingsDialog
{
    // نسخ عمل تُحفظ فقط عند الضغط على «حفظ»
    readonly Dictionary<string, List<string>> listEdits = Lists.All.ToDictionary(d => d.Key, d => Lists.Get(d.Key).ToList());
    readonly List<Tech> techEdits = Techs.All.Select(t => new Tech { Name = t.Name, Basis = t.Basis, Value = t.Value }).ToList();
    readonly Dictionary<string, string> msgEdits = Msg.All.ToDictionary(t => t.Id, t => Msg.Text(t.Id));

    // ---------------- القوائم ----------------
    readonly ComboBox cbList = W.Combo(420, Lists.All.Select(d => d.Title));
    readonly Label listNote = W.Note("", 700, 44);
    readonly ListBox lbItems = new() { Width = 420, Height = 330, Font = Theme.F(10.5f), IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox tItem = new() { Width = 420, PlaceholderText = "اكتب عنصراً جديداً أو عدّل المحدد" };

    string CurList => Lists.All[Math.Max(0, cbList.SelectedIndex)].Key;

    Control ListsTab()
    {
        var p = Page();
        p.Controls.Add(W.Labeled("القائمة", cbList, "list"));
        p.Controls.Add(listNote);
        var row = W.Flow(false);
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        left.Controls.Add(W.Wrap(tItem));
        lbItems.Margin = new Padding(6);
        left.Controls.Add(lbItems);
        row.Controls.Add(left);
        var btns = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        ModernButton B(string t, string icon, BtnKind k, Action a) { var b = W.Btn(t, icon, k, 150); b.Click += (s, e) => a(); btns.Controls.Add(b); return b; }
        B("إضافة", "plus", BtnKind.Primary, AddItem);
        B("تعديل المحدد", "pencil", BtnKind.Secondary, RenameItem);
        B("حذف", "trash-2", BtnKind.Danger, RemoveItem);
        B("لأعلى", "chevron-up", BtnKind.Ghost, () => MoveItem(-1));
        B("لأسفل", "chevron-down", BtnKind.Ghost, () => MoveItem(1));
        B("القائمة الافتراضية", "rotate-ccw", BtnKind.Ghost, () =>
        {
            if (!W.Confirm("استرجاع القائمة الافتراضية؟", Lists.Find(CurList).Title, "استرجاع")) return;
            listEdits[CurList] = Lists.Find(CurList).Defaults.ToList();
            RenderList();
        });
        row.Controls.Add(btns);
        p.Controls.Add(row);
        p.Controls.Add(W.Note("تغيير اسم عنصر أو حذفه لا يغيّر الطلبات السابقة — تبقى كما سُجّلت.", 700));
        cbList.SelectedIndexChanged += (s, e) => RenderList();
        lbItems.SelectedIndexChanged += (s, e) => { if (lbItems.SelectedItem is string v) tItem.Text = v; };
        tItem.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddItem(); } };
        cbList.SelectedIndex = 0;
        RenderList();
        return p;
    }

    void RenderList(int select = -1)
    {
        var def = Lists.Find(CurList);
        listNote.Text = def.Note;
        lbItems.BeginUpdate();
        lbItems.Items.Clear();
        lbItems.Items.AddRange(listEdits[CurList].Cast<object>().ToArray());
        lbItems.EndUpdate();
        if (select >= 0 && select < lbItems.Items.Count) lbItems.SelectedIndex = select;
    }

    bool Locked(int i) => CurList == "pay_methods" && i == 0;

    void AddItem()
    {
        var v = Txt.Str(tItem.Text);
        var list = listEdits[CurList];
        if (v == "") { tItem.Focus(); return; }
        if (list.Any(x => Txt.Fold(x) == Txt.Fold(v))) { Toast.Show("موجود في القائمة مسبقاً", Tone.Warning); return; }
        if (CurList == "warranties" && !v.Contains("بدون") && Calc.WarrantyDays(v) == 0)
            Toast.Show("اكتب المدة بالأرقام (مثل «10 أيام» أو «2 شهر») ليُحسب تاريخ انتهاء الضمان.", Tone.Warning);
        list.Add(v);
        tItem.Clear();
        RenderList(list.Count - 1);
    }

    void RenameItem()
    {
        int i = lbItems.SelectedIndex;
        var v = Txt.Str(tItem.Text);
        var list = listEdits[CurList];
        if (i < 0 || v == "") return;
        if (Locked(i)) { Toast.Show("«نقد» ثابت ولا يمكن تغييره", Tone.Info); return; }
        if (list.Where((_, k) => k != i).Any(x => Txt.Fold(x) == Txt.Fold(v))) { Toast.Show("موجود في القائمة مسبقاً", Tone.Warning); return; }
        list[i] = v;
        RenderList(i);
    }

    void RemoveItem()
    {
        int i = lbItems.SelectedIndex;
        var list = listEdits[CurList];
        if (i < 0) return;
        if (Locked(i)) { Toast.Show("«نقد» ثابت ولا يمكن حذفه", Tone.Info); return; }
        if (list.Count <= 1) { Toast.Show("يجب أن يبقى عنصر واحد على الأقل", Tone.Warning); return; }
        list.RemoveAt(i);
        tItem.Clear();
        RenderList(Math.Min(i, list.Count - 1));
    }

    void MoveItem(int dir)
    {
        int i = lbItems.SelectedIndex, j = i + dir;
        var list = listEdits[CurList];
        if (i < 0 || j < 0 || j >= list.Count || Locked(i) || Locked(j)) return;
        (list[i], list[j]) = (list[j], list[i]);
        RenderList(j);
    }

    // ---------------- الفنيون ----------------
    readonly ListBox lbTechs = new() { Width = 420, Height = 300, Font = Theme.F(10.5f), IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox tTech = new() { Width = 300 };
    readonly ComboBox cbBasis = W.Combo(220, Techs.Bases.Select(b => b.Title));
    readonly NumericUpDown nTechVal = new() { Width = 160, Minimum = 0, Maximum = 1_000_000_000m, DecimalPlaces = 1, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10) };
    Label techValLabel;

    Control TechsTab()
    {
        var p = Page();
        p.Controls.Add(W.Note("أضف فنيي المحل ليظهر حقل «الفني» في الطلب، وتقرير أداء كل فني وعمولته في التقارير.", 760));
        var row = W.Flow(false);
        lbTechs.Margin = new Padding(6);
        row.Controls.Add(lbTechs);
        var form = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        form.Controls.Add(W.Labeled("اسم الفني", tTech, "user"));
        form.Controls.Add(W.Labeled("طريقة العمولة", cbBasis));
        var valBox = W.Labeled("النسبة %", nTechVal);
        techValLabel = valBox.Controls.OfType<Label>().First();
        form.Controls.Add(valBox);
        var bSave = W.Btn("إضافة / حفظ الفني", "user-plus", BtnKind.Primary, 180);
        var bNew = W.Btn("فني جديد", "plus", BtnKind.Ghost, 180);
        var bDel = W.Btn("حذف الفني", "trash-2", BtnKind.Danger, 180);
        form.Controls.AddRange(new Control[] { bSave, bNew, bDel });
        row.Controls.Add(form);
        p.Controls.Add(row);
        p.Controls.Add(W.Note("العمولة تُحسب على الأجهزة المسلّمة بسعر (طلبات الضمان المجانية بلا عمولة). حذف الفني لا يغيّر الطلبات السابقة.", 760, 40));
        cbBasis.SelectedIndexChanged += (s, e) => techValLabel.Text = cbBasis.SelectedIndex == 2 ? "المبلغ لكل جهاز" : "النسبة %";
        lbTechs.SelectedIndexChanged += (s, e) =>
        {
            int i = lbTechs.SelectedIndex;
            if (i < 0 || i >= techEdits.Count) return;
            var t = techEdits[i];
            tTech.Text = t.Name;
            cbBasis.SelectedIndex = Math.Max(0, Array.FindIndex(Techs.Bases, b => b.Key == t.Basis));
            nTechVal.Value = (decimal)Math.Min((double)nTechVal.Maximum, t.Value);
        };
        bNew.Click += (s, e) => { lbTechs.ClearSelected(); tTech.Clear(); cbBasis.SelectedIndex = 0; nTechVal.Value = 0; tTech.Focus(); };
        bSave.Click += (s, e) => SaveTech();
        bDel.Click += (s, e) =>
        {
            int i = lbTechs.SelectedIndex;
            if (i < 0 || i >= techEdits.Count) return;
            if (!W.Confirm("حذف الفني؟", techEdits[i].Name, "حذف", true)) return;
            techEdits.RemoveAt(i);
            RenderTechs();
            tTech.Clear();
        };
        RenderTechs();
        return p;
    }

    void RenderTechs(int select = -1)
    {
        lbTechs.BeginUpdate();
        lbTechs.Items.Clear();
        foreach (var t in techEdits) lbTechs.Items.Add($"{t.Name}   —   {Techs.BasisText(t)}");
        lbTechs.EndUpdate();
        if (select >= 0 && select < lbTechs.Items.Count) lbTechs.SelectedIndex = select;
    }

    void SaveTech()
    {
        var name = Txt.Str(tTech.Text);
        if (name == "") { tTech.Focus(); Toast.Show("اكتب اسم الفني", Tone.Warning); return; }
        var basis = Techs.Bases[Math.Max(0, cbBasis.SelectedIndex)].Key;
        double v = (double)nTechVal.Value;
        if (basis != "fixed" && v > 100) { nTechVal.Focus(); Toast.Show("النسبة لا تزيد على 100%", Tone.Warning); return; }
        int sel = lbTechs.SelectedIndex;
        int dup = techEdits.FindIndex(t => Txt.Fold(t.Name) == Txt.Fold(name));
        if (dup >= 0 && dup != sel) sel = dup;   // نفس الاسم: تحديث بدل التكرار
        if (sel >= 0 && sel < techEdits.Count)
        {
            var old = techEdits[sel].Name;
            techEdits[sel] = new Tech { Name = name, Basis = basis, Value = v };
            if (Txt.Fold(old) != Txt.Fold(name) && Store.Orders.Any(o => Txt.Fold(o.Technician) == Txt.Fold(old)))
                Toast.Show($"الطلبات السابقة تبقى باسم «{old}»", Tone.Info);
        }
        else { techEdits.Add(new Tech { Name = name, Basis = basis, Value = v }); sel = techEdits.Count - 1; }
        RenderTechs(sel);
        Toast.Show("اضغط «حفظ» أسفل النافذة لاعتماد التغييرات", Tone.Info);
    }

    // ---------------- رسائل واتساب ----------------
    readonly ComboBox cbTpl = W.Combo(420, Msg.All.Select(t => t.Title));
    readonly TextBox tTpl = new() { Width = 820, Height = 230, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Theme.F(10.5f), AcceptsReturn = true };
    readonly TextBox tPreview = new() { Width = 820, Height = 170, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Theme.F(10), BackColor = Theme.SurfaceAlt };
    string curTpl;

    Control MessagesTab()
    {
        var p = Page();
        var top = W.Flow(false);
        top.Controls.Add(W.Labeled("الرسالة", cbTpl, "message-circle"));
        var bDef = W.Btn("النص الافتراضي", "rotate-ccw", BtnKind.Ghost, 150); bDef.Margin = new Padding(4, 27, 4, 4);
        top.Controls.Add(bDef);
        p.Controls.Add(top);
        p.Controls.Add(W.Note("اضغط على متغير لإدراجه في مكان المؤشر. السطر الذي فيه متغير فارغ (مثل موعد غير محدد) يُحذف تلقائياً.", 820));
        var vars = W.Flow();
        vars.MaximumSize = new Size(840, 0);
        var tip = new ToolTip();
        foreach (var (name, hint) in Msg.Vars)
        {
            var b = new ModernButton { Text = "{" + name + "}", Kind = BtnKind.Soft, Height = 30, Font = Theme.F(9), Margin = new Padding(3), TabStop = false };
            b.FitWidth(60);
            tip.SetToolTip(b, hint);
            b.Click += (s, e) => { tTpl.SelectedText = "{" + name + "}"; tTpl.Focus(); };
            vars.Controls.Add(b);
        }
        p.Controls.Add(vars);
        p.Controls.Add(W.Wrap(tTpl));
        p.Controls.Add(W.Note("معاينة على آخر طلب:", 820, 22));
        p.Controls.Add(W.Wrap(tPreview));
        cbTpl.SelectedIndexChanged += (s, e) =>
        {
            if (curTpl != null) msgEdits[curTpl] = tTpl.Text;
            curTpl = Msg.All[Math.Max(0, cbTpl.SelectedIndex)].Id;
            tTpl.Text = msgEdits[curTpl].Replace("\n", "\r\n");
        };
        tTpl.TextChanged += (s, e) => { if (curTpl != null) msgEdits[curTpl] = tTpl.Text; Preview(); };
        bDef.Click += (s, e) => { if (curTpl != null) tTpl.Text = Msg.All.First(t => t.Id == curTpl).Default.Replace("\n", "\r\n"); };
        cbTpl.SelectedIndex = 0;
        return p;
    }

    void Preview()
    {
        if (curTpl == null) return;
        Dictionary<string, string> vars;
        if (curTpl == "debts")
            vars = Calc.GetDebts().FirstOrDefault() is Calc.Customer c ? Msg.VarsFor(c) : null;
        else
            vars = Store.Orders.FirstOrDefault() is Order o ? Msg.VarsFor(o) : null;
        tPreview.Text = vars == null ? "لا توجد بيانات للمعاينة بعد." : Msg.Fill(tTpl.Text, vars).Replace("\n", "\r\n");
    }

    // ---------------- الحفظ ----------------
    bool ValidateCustom()
    {
        var bad = listEdits["warranties"].Where(w => !w.Contains("بدون") && Calc.WarrantyDays(w) == 0).ToList();
        if (bad.Count > 0 && !W.Confirm("مدة ضمان غير مفهومة", $"لن يُحسب تاريخ انتهاء الضمان لـ: {string.Join("، ", bad)}\nاكتب المدة بالأرقام مثل «10 أيام» أو «2 شهر». هل تريد الحفظ على أي حال؟", "حفظ على أي حال"))
            return false;
        if (msgEdits.Any(kv => kv.Value.Trim() == "")) { Dialogs.Warn("لا يمكن ترك نص رسالة فارغاً — استرجع النص الافتراضي."); return false; }
        return true;
    }

    /// <summary>يحفظ القوائم والفنيين والرسائل، ويعيد true إذا تغيّر ما يستلزم إعادة بناء الشاشات</summary>
    bool SaveCustom()
    {
        bool changed = false;
        foreach (var d in Lists.All)
        {
            var before = Lists.Get(d.Key);
            Lists.Set(d.Key, listEdits[d.Key]);
            if (!before.SequenceEqual(Lists.Get(d.Key))) changed = true;
        }
        var oldTechs = string.Join("|", Techs.All.Select(t => t.Name + t.Basis + t.Value));
        Techs.Save(techEdits);
        if (oldTechs != string.Join("|", Techs.All.Select(t => t.Name + t.Basis + t.Value))) changed = true;
        foreach (var (id, text) in msgEdits) Msg.Save(id, text);
        return changed;
    }
}

/// <summary>تبويب التقرير اليومي والتنبيهات (تيليجرام والبريد)</summary>
public partial class SettingsDialog
{
    readonly Toggle tgDaily = new() { Text = "إرسال ملخص اليوم تلقائياً", Width = 360 };
    readonly DateTimePicker dDaily = new() { Width = 140, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
    readonly TextBox tTgToken = new() { Width = 460, UseSystemPasswordChar = true }, tTgChat = new() { Width = 200 };
    readonly TextBox tMailHost = new() { Width = 260, PlaceholderText = "smtp.gmail.com" }, tMailPort = new() { Width = 90 },
                     tMailUser = new() { Width = 300, PlaceholderText = "you@gmail.com" }, tMailPass = new() { Width = 220, UseSystemPasswordChar = true },
                     tMailTo = new() { Width = 300, PlaceholderText = "البريد الذي يصله التقرير" };
    readonly Toggle tgSsl = new() { Text = "اتصال آمن SSL/TLS", Width = 220 };
    readonly List<(string Key, Toggle T)> alertToggles = new();
    readonly Label notifyStatus = W.Note("", 760, 44);

    Control NotifyTab()
    {
        var p = Page();
        p.Controls.Add(W.Note("يصلك ملخص اليوم (المقبوض والمصاريف والأجهزة والديون والتذكيرات) وأنت خارج المحل، وتنبيه فوري عند الأحداث المهمة.", 780, 40));
        var r0 = W.Flow(false);
        tgDaily.Margin = new Padding(6, 30, 6, 0);
        r0.Controls.Add(tgDaily);
        r0.Controls.Add(W.Labeled("وقت الإرسال", dDaily, "clock"));
        p.Controls.Add(r0);
        p.Controls.Add(W.Note("إن كان البرنامج مغلقاً في ذلك الوقت، يُرسل التقرير عند إغلاقه بعد الظهر، أو في اليوم التالي عند فتحه.", 780));

        p.Controls.Add(W.Head("تيليجرام (الأسهل والمجاني)", 780));
        p.Controls.Add(W.Note("1) في تيليجرام افتح ‎@BotFather‎ واكتب ‎/newbot‎ وانسخ الرمز الذي يعطيك.  2) افتح البوت الجديد وأرسل له أي رسالة.  3) الصق الرمز هنا واضغط «جلب رقم المحادثة».", 780, 44));
        var r1 = W.Flow(false);
        r1.Controls.Add(W.Labeled(Store.Get("notify_tg_token") != "" ? "رمز البوت (محفوظ — اتركه فارغاً للإبقاء عليه)" : "رمز البوت", tTgToken, "key-round"));
        r1.Controls.Add(W.Labeled("رقم المحادثة", tTgChat));
        p.Controls.Add(r1);
        var r2 = W.Flow(false);
        var bChat = W.Btn("جلب رقم المحادثة", "search", BtnKind.Secondary, 150);
        var bTg = W.Btn("إرسال رسالة تجربة", "send", BtnKind.Soft, 150);
        var bTgClear = W.Btn("حذف إعدادات تيليجرام", "trash-2", BtnKind.Ghost, 170);
        r2.Controls.AddRange(new Control[] { bChat, bTg, bTgClear });
        p.Controls.Add(r2);

        p.Controls.Add(W.Head("البريد الإلكتروني (اختياري)", 780));
        p.Controls.Add(W.Note("مع Gmail: الخادم smtp.gmail.com والمنفذ 587، وكلمة المرور هي «كلمة مرور التطبيقات» من إعدادات حساب Google (وليست كلمة مرورك العادية).", 780, 40));
        var r3 = W.Flow(false);
        r3.Controls.Add(W.Labeled("خادم SMTP", tMailHost));
        r3.Controls.Add(W.Labeled("المنفذ", tMailPort));
        tgSsl.Margin = new Padding(6, 30, 6, 0);
        r3.Controls.Add(tgSsl);
        p.Controls.Add(r3);
        var r4 = W.Flow(false);
        r4.Controls.Add(W.Labeled("البريد المرسِل", tMailUser));
        r4.Controls.Add(W.Labeled(Store.Get("notify_mail_pass") != "" ? "كلمة المرور (محفوظة)" : "كلمة المرور", tMailPass, "key-round"));
        p.Controls.Add(r4);
        var r5 = W.Flow(false);
        r5.Controls.Add(W.Labeled("يُرسل إلى", tMailTo));
        var bMail = W.Btn("إرسال بريد تجربة", "send", BtnKind.Soft, 150); bMail.Margin = new Padding(4, 27, 4, 4);
        r5.Controls.Add(bMail);
        p.Controls.Add(r5);

        p.Controls.Add(W.Head("تنبيهات فورية عند", 780));
        foreach (var (k, t) in Notify.Alerts)
        {
            var tg = new Toggle { Text = t, Width = 360 };
            alertToggles.Add((k, tg));
            p.Controls.Add(tg);
        }
        var r6 = W.Flow(false);
        var bNow = W.Btn("إرسال تقرير اليوم الآن", "send", BtnKind.Primary, 190);
        r6.Controls.Add(bNow);
        p.Controls.Add(r6);
        p.Controls.Add(notifyStatus);

        tgDaily.Checked = Notify.DailyOn;
        dDaily.Value = DateTime.Today.Add(TimeSpan.Parse(Notify.DailyTime));
        tTgChat.Text = Notify.TgChat;
        tMailHost.Text = Store.Get("notify_mail_host"); tMailPort.Text = Store.Get("notify_mail_port", "587");
        tgSsl.Checked = Store.Flag("notify_mail_ssl", true);
        tMailUser.Text = Store.Get("notify_mail_user"); tMailTo.Text = Store.Get("notify_mail_to");
        foreach (var (k, t) in alertToggles) t.Checked = Notify.AlertOn(k);
        NotifyStatus();

        bChat.Click += async (s, e) =>
        {
            var token = tTgToken.Text.Trim() != "" ? tTgToken.Text.Trim() : Notify.TgToken;
            if (token == "") { tTgToken.Focus(); Toast.Show("الصق رمز البوت أولاً", Tone.Warning); return; }
            try
            {
                var id = await Notify.FindChatId(token);
                if (id == "") { Dialogs.Warn("لم أجد رسائل. افتح البوت في تيليجرام وأرسل له أي رسالة، ثم أعد المحاولة."); return; }
                tTgChat.Text = id;
                Toast.Show("وُجد رقم المحادثة — اضغط «إرسال رسالة تجربة»");
            }
            catch (Exception ex) { Dialogs.Warn("تعذّر الاتصال بتيليجرام: " + ex.Message); }
        };
        bTg.Click += async (s, e) => await TestSend("✅ رسالة تجربة من " + Store.ShopName + "\nالتنبيهات تعمل.");
        bMail.Click += async (s, e) => await TestSend("رسالة تجربة من " + Store.ShopName + " — التنبيهات تعمل.");
        bNow.Click += async (s, e) =>
        {
            SaveNotify();
            if (!Notify.Configured) { Dialogs.Warn("اضبط تيليجرام أو البريد أولاً."); return; }
            bNow.Enabled = false;
            var err = await Notify.Send("تقرير يوم " + Txt.Today + " — " + Store.ShopName, Notify.DailyText(Txt.Today));
            bNow.Enabled = true;
            if (err == "") Toast.Show("أُرسل تقرير اليوم"); else Dialogs.Warn("تعذّر الإرسال:\n" + err);
            NotifyStatus();
        };
        bTgClear.Click += (s, e) =>
        {
            Store.Set("notify_tg_token", ""); Store.Set("notify_tg_chat", "");
            tTgToken.Clear(); tTgChat.Clear();
            Toast.Show("حُذفت إعدادات تيليجرام");
            NotifyStatus();
        };
        return p;
    }

    async Task TestSend(string text)
    {
        SaveNotify();
        if (!Notify.Configured) { Dialogs.Warn("أكمل بيانات تيليجرام أو البريد أولاً."); return; }
        var err = await Notify.Send("رسالة تجربة — " + Store.ShopName, text);
        if (err == "") Toast.Show("أُرسلت رسالة التجربة — تحقق من تيليجرام أو بريدك");
        else Dialogs.Warn("تعذّر الإرسال:\n" + err);
        NotifyStatus();
    }

    void NotifyStatus()
    {
        var ch = new List<string>();
        if (Notify.TelegramOn) ch.Add("تيليجرام");
        if (Notify.EmailOn) ch.Add("البريد");
        var last = Store.Get("notify_daily_last");
        notifyStatus.Text = (ch.Count == 0 ? "لا توجد قناة إرسال مضبوطة." : "القنوات المفعّلة: " + string.Join(" و", ch)) +
            (last != "" ? $"   •   آخر تقرير يومي: {Txt.FmtDate(last)}" : "") +
            (Notify.LastError != "" ? "\nآخر خطأ: " + Notify.LastError : "");
        notifyStatus.ForeColor = Notify.LastError != "" ? Pal.Bad : Theme.Muted;
    }

    void SaveNotify()
    {
        Store.SetFlag("notify_daily", tgDaily.Checked);
        Store.Set("notify_daily_time", dDaily.Value.ToString("HH:mm"));
        if (tTgToken.Text.Trim() != "") Store.Set("notify_tg_token", Secret.Protect(tTgToken.Text.Trim()));
        Store.Set("notify_tg_chat", tTgChat.Text.Trim());
        Store.Set("notify_mail_host", tMailHost.Text.Trim());
        Store.Set("notify_mail_port", int.TryParse(Txt.LatinDigits(tMailPort.Text.Trim()), out var port) ? port.ToString() : "587");
        Store.SetFlag("notify_mail_ssl", tgSsl.Checked);
        Store.Set("notify_mail_user", tMailUser.Text.Trim());
        if (tMailPass.Text != "") Store.Set("notify_mail_pass", Secret.Protect(tMailPass.Text));
        Store.Set("notify_mail_to", tMailTo.Text.Trim());
        foreach (var (k, t) in alertToggles) Store.SetFlag("notify_alert_" + k, t.Checked);
    }
}
