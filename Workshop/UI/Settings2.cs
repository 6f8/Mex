using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>تبويبات التخصيص في الإعدادات: القوائم، الفنيون وعمولاتهم، وقوالب رسائل واتساب</summary>
public partial class SettingsDialog
{
    // نسخ عمل تُحفظ فقط عند الضغط على «حفظ»
    readonly Dictionary<string, List<string>> listEdits = Lists.All.ToDictionary(d => d.Key, d => Lists.Get(d.Key).ToList());
    readonly List<Tech> techEdits = Techs.All.Select(t => new Tech { Name = t.Name, Basis = t.Basis, Value = t.Value, Skills = t.Skills.ToList() }).ToList();
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
        if (CurList == "statuses" && new[] { K.Check, K.Approval, K.Repair, K.Part, K.Ready, K.Done, K.Cancelled }.Contains(v)) { Toast.Show("هذه حالة أساسية موجودة أصلاً", Tone.Warning); return; }
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
        if (list.Count <= 1 && CurList != "statuses") { Toast.Show("يجب أن يبقى عنصر واحد على الأقل", Tone.Warning); return; }
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
        clSkills.Items.AddRange(K.IssueTypes.Cast<object>().ToArray());
        form.Controls.Add(W.Labeled("يتقن (للتوزيع التلقائي — لا شيء = الكل)", clSkills, "wrench"));
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
            for (int k = 0; k < clSkills.Items.Count; k++) clSkills.SetItemChecked(k, t.Skills.Contains((string)clSkills.Items[k]));
        };
        bNew.Click += (s, e) => { lbTechs.ClearSelected(); tTech.Clear(); cbBasis.SelectedIndex = 0; nTechVal.Value = 0; for (int k = 0; k < clSkills.Items.Count; k++) clSkills.SetItemChecked(k, false); tTech.Focus(); };
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

    readonly CheckedListBox clSkills = new() { Width = 300, Height = 150, CheckOnClick = true, Font = Theme.F(10), BorderStyle = BorderStyle.FixedSingle };
    List<string> Skills() => clSkills.CheckedItems.Cast<string>().ToList();

    void RenderTechs(int select = -1)
    {
        lbTechs.BeginUpdate();
        lbTechs.Items.Clear();
        foreach (var t in techEdits) lbTechs.Items.Add($"{t.Name}   —   {Techs.BasisText(t)}{(t.Skills.Count > 0 ? "   —   " + string.Join("، ", t.Skills) : "")}");
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
            techEdits[sel] = new Tech { Name = name, Basis = basis, Value = v, Skills = Skills() };
            if (Txt.Fold(old) != Txt.Fold(name) && Store.Orders.Any(o => Txt.Fold(o.Technician) == Txt.Fold(old)))
                Toast.Show($"الطلبات السابقة تبقى باسم «{old}»", Tone.Info);
        }
        else { techEdits.Add(new Tech { Name = name, Basis = basis, Value = v, Skills = Skills() }); sel = techEdits.Count - 1; }
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
        var oldCustom = K.Custom;
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
        // الطلبات التي كانت في حالة مخصصة حُذفت تعود إلى «قيد الإصلاح»
        var removed = oldCustom.Except(K.Custom).ToHashSet();
        foreach (var o in Store.Orders.Where(o => removed.Contains(o.Status)).ToList())
        {
            var n = o.Clone();
            Locking.Log(n, $"حُذفت الحالة «{o.Status}» من الإعدادات فانتقل إلى «{K.Repair}»");
            n.Status = K.Repair; n.StatusAt = n.UpdatedAt = Txt.Now;
            Store.SaveOrder(n);
        }
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
    readonly Toggle tgWeekly = new() { Text = "ملخص أسبوعي يوم", Width = 200 }, tgMonthly = new() { Text = "ملخص شهري أول كل شهر (عن الشهر الماضي)", Width = 420 };
    readonly ComboBox cbWeekDay = W.Combo(150, new[] { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" });
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
        var rw = W.Flow(false);
        tgWeekly.Margin = new Padding(6, 8, 6, 0);
        rw.Controls.Add(tgWeekly);
        rw.Controls.Add(W.Wrap(cbWeekDay));
        p.Controls.Add(rw);
        p.Controls.Add(tgMonthly);
        p.Controls.Add(W.Note("الملخص فيه الإيراد والربح والمقارنة بالفترة السابقة، وأكثر الأعطال، وأداء الفنيين، والزبائن الجدد.", 780));

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
        tgWeekly.Checked = Periodic.WeeklyOn; tgMonthly.Checked = Periodic.MonthlyOn;
        cbWeekDay.SelectedIndex = Periodic.WeekDay;
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
        Store.SetFlag("notify_weekly", tgWeekly.Checked);
        Store.SetFlag("notify_monthly", tgMonthly.Checked);
        Store.Set("notify_weekly_day", Math.Max(0, cbWeekDay.SelectedIndex).ToString());
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

/// <summary>تبويب سير العمل: فحص الجودة، الطلبات المعلّقة، الفرع، الشروط الخاصة بكل عطل</summary>
public partial class SettingsDialog
{
    readonly Toggle tgQc = new() { Text = "إلزام فحص الجودة قبل أن يصبح الجهاز «جاهز للاستلام»", Width = 760 };
    readonly NumericUpDown nStale = new() { Width = 120, Minimum = 1, Maximum = 90, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10) };
    readonly TextBox tBranch = new() { Width = 300, PlaceholderText = "مثل: فرع المنصور" };
    readonly Toggle tgDeduct = new() { Text = "خصم أيام الغياب من الراتب (الراتب ÷ 30 لكل يوم)", Width = 760 };
    readonly ComboBox cbTermsType = W.Combo(260, Array.Empty<string>());
    readonly TextBox tIssueTerms = new() { Width = 760, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly Dictionary<string, string> termsEdits = new();
    string curTermsType;

    Control WorkflowTab()
    {
        var p = Page();
        p.Controls.Add(W.Head("فحص الجودة", 780));
        p.Controls.Add(tgQc);
        p.Controls.Add(W.Note("بنود الفحص تُعدَّل من «القوائم ← فحص الجودة قبل التسليم».", 780));
        p.Controls.Add(W.Head("الطلبات المعلّقة", 780));
        p.Controls.Add(W.Labeled("اقترح إلغاء الطلب «بانتظار الموافقة» بعد (يوم)", nStale, "clock"));
        p.Controls.Add(W.Head("الموظفون", 780));
        p.Controls.Add(tgDeduct);
        p.Controls.Add(W.Head("الفرع", 780));
        p.Controls.Add(W.Labeled("اسم هذا الفرع (يُكتب على كل طلب جديد، ويظهر في التقرير المجمّع)", tBranch, "store"));
        p.Controls.Add(W.Head("شروط خاصة لكل نوع عطل", 780));
        p.Controls.Add(W.Note("تُطبع تحت الشروط العامة في فاتورة هذا النوع فقط — مثل: «أجهزة المياه بلا ضمان، وقد تتعطل أجزاء أخرى لاحقاً».", 780, 40));
        p.Controls.Add(W.Labeled("نوع العطل", cbTermsType));
        p.Controls.Add(W.Wrap(tIssueTerms));
        tgQc.Checked = QC.Required;
        tgDeduct.Checked = Staff.DeductAbsence;
        nStale.Value = Stale.Days;
        tBranch.Text = Branches.Current;
        cbTermsType.Items.AddRange(K.IssueTypes.Cast<object>().ToArray());
        foreach (var t in K.IssueTypes) termsEdits[t] = IssueTerms.For(t);
        cbTermsType.SelectedIndexChanged += (s, e) =>
        {
            if (curTermsType != null) termsEdits[curTermsType] = tIssueTerms.Text;
            curTermsType = cbTermsType.Text;
            tIssueTerms.Text = termsEdits.GetValueOrDefault(curTermsType, "");
        };
        tIssueTerms.TextChanged += (s, e) => { if (curTermsType != null) termsEdits[curTermsType] = tIssueTerms.Text; };
        if (cbTermsType.Items.Count > 0) cbTermsType.SelectedIndex = 0;
        return p;
    }

    void SaveWorkflow()
    {
        Store.SetFlag("qc_required", tgQc.Checked);
        Store.SetFlag("hr_deduct_absence", tgDeduct.Checked);
        Store.Set("stale_days", ((int)nStale.Value).ToString());
        Store.Set("branch_name", tBranch.Text.Trim());
        foreach (var (k, v) in termsEdits) IssueTerms.Set(k, v);
    }
}

/// <summary>الهاتف والرسائل: صفحة الفني على الهاتف، مزوّد SMS، تكبير الواجهة، وضع التدريب</summary>
public partial class SettingsDialog
{
    readonly Toggle tgWeb = new() { Text = "تفعيل صفحة الفني على الهاتف (داخل شبكة المحل)", Width = 700 };
    readonly TextBox tWebPin = new() { Width = 160, PlaceholderText = "4 أرقام أو أكثر" }, tWebPort = new() { Width = 100 };
    readonly TextBox tOwnerPin = new() { Width = 160, PlaceholderText = "اختياري — مختلف عن رمز الفني" };
    readonly Label webInfo = W.Note("", 780, 66);
    readonly TextBox tSmsUrl = new() { Width = 760, PlaceholderText = "https://api.provider.com/send?to={phone_intl}&msg={text}&key=..." };
    readonly ComboBox cbSmsMethod = W.Combo(120, new[] { "GET", "POST" });
    readonly TextBox tSmsBody = new() { Width = 760, Height = 60, Multiline = true, PlaceholderText = "للطريقة POST فقط: {\"to\":\"{phone_intl}\",\"text\":\"{text}\"}" };
    readonly TextBox tSmsTest = new() { Width = 200, PlaceholderText = "رقم للتجربة" };
    readonly ComboBox cbZoom = W.Combo(260, new[] { "100% (عادي)", "115% (خط أكبر)", "130% (وضع اللمس)", "150% (لمس / شاشة بعيدة)" });
    static readonly int[] Zooms = { 100, 115, 130, 150 };

    Control PhoneTab()
    {
        var p = Page();
        p.Controls.Add(W.Head("صفحة الفني على الهاتف", 780));
        p.Controls.Add(W.Note("يفتحها الفني من متصفح هاتفه وهو متصل بواي فاي المحل: يرى الأجهزة، يغيّر الحالة، يصوّر الجهاز، يشغّل التوقيت، ويسجّل فحص الجودة. عند أول تشغيل قد يسألك ويندوز عن السماح بالشبكة — اختر «السماح».", 780, 60));
        p.Controls.Add(tgWeb);
        var r = W.Flow(false);
        r.Controls.Add(W.Labeled("رمز الدخول (PIN)", tWebPin, "key-round"));
        r.Controls.Add(W.Labeled("المنفذ", tWebPort));
        r.Controls.Add(W.Labeled("رمز صاحب المحل", tOwnerPin, "lock"));
        p.Controls.Add(r);
        p.Controls.Add(webInfo);
        p.Controls.Add(W.Note("• برمز صاحب المحل تفتح «لوحة صاحب المحل»: المقبوض اليوم، ما في الدرج، الربح، الصناديق والديون — من أي مكان داخل شبكة المحل.\n• «شاشة الزبون»: افتح الصفحة على تابلت عند الاستقبال واضغط «شاشة الزبون» في الأعلى؛ يملأ الزبون بياناته ويظهر الطلب في البرنامج بعلامة «من شاشة الزبون» لتراجعه. الخروج منها يحتاج الرمز.", 780, 80));

        p.Controls.Add(W.Head("رسائل SMS", 780));
        p.Controls.Add(W.Note("بدون مزوّد: زر «SMS» في نافذة الرسالة يفتح تطبيق الرسائل في ويندوز (Phone Link) إن كان مربوطاً بهاتفك.\nمع مزوّد رسائل: الصق رابط الإرسال الذي يعطيك إياه، واكتب فيه {phone} أو {phone_intl} و{text}.", 780, 60));
        p.Controls.Add(W.Labeled("رابط الإرسال", tSmsUrl, "link"));
        var r2 = W.Flow(false);
        r2.Controls.Add(W.Labeled("الطريقة", cbSmsMethod));
        r2.Controls.Add(W.Labeled("رقم التجربة", tSmsTest, "phone"));
        var bTest = W.Btn("إرسال تجربة", "send", BtnKind.Soft, 120); bTest.Margin = new Padding(4, 27, 4, 4);
        r2.Controls.Add(bTest);
        p.Controls.Add(r2);
        p.Controls.Add(W.Labeled("نص الطلب (POST)", tSmsBody));

        tgWeb.Checked = WebApp.Enabled;
        tWebPin.Text = WebApp.Pin;
        tOwnerPin.Text = WebApp.OwnerPin;
        tWebPort.Text = WebApp.Port.ToString();
        tSmsUrl.Text = Sms.Url;
        W.Pick(cbSmsMethod, Sms.Post ? "POST" : "GET");
        tSmsBody.Text = Sms.Body;
        WebInfo();
        bTest.Click += async (s, e) =>
        {
            SavePhone();
            if (!Sms.Configured) { Dialogs.Warn("اكتب رابط الإرسال أولاً."); return; }
            var err = await Sms.Send(tSmsTest.Text.Trim(), "رسالة تجربة من " + Store.ShopName);
            if (err == "") Toast.Show("أُرسلت رسالة التجربة"); else Dialogs.Warn("تعذّر الإرسال: " + err);
        };
        return p;
    }

    void WebInfo()
    {
        if (Training.Active) { webInfo.Text = "غير متاحة في وضع التدريب."; return; }
        var urls = WebApp.Urls();
        webInfo.Text = WebApp.Running
            ? "تعمل الآن. اكتب في متصفح الهاتف: " + (urls.Count > 0 ? string.Join("   أو   ", urls) : $"http://عنوان-الحاسوب:{WebApp.Port}")
            : WebApp.LastError != "" ? "تعذّر التشغيل: " + WebApp.LastError : "متوقفة.";
        webInfo.ForeColor = WebApp.Running ? Pal.Good : WebApp.LastError != "" ? Pal.Bad : Theme.Muted;
    }

    void SavePhone()
    {
        var pin = new string(Txt.LatinDigits(tWebPin.Text).Where(char.IsDigit).ToArray());
        if (tgWeb.Checked && pin.Length < 4) { Toast.Show("رمز الدخول 4 أرقام على الأقل — لم تُفعَّل صفحة الهاتف", Tone.Warning); tgWeb.Checked = false; }
        Store.SetFlag("web_on", tgWeb.Checked);
        Store.Set("web_pin", pin);
        var opin = new string(Txt.LatinDigits(tOwnerPin.Text).Where(char.IsDigit).ToArray());
        if (opin != "" && (opin.Length < 4 || opin == pin)) { Toast.Show("رمز صاحب المحل 4 أرقام على الأقل ومختلف عن رمز الفني — لم يُحفظ", Tone.Warning); opin = WebApp.OwnerPin; }
        Store.Set("web_owner_pin", opin);
        Store.Set("web_port", int.TryParse(Txt.LatinDigits(tWebPort.Text), out var port) && port is > 1024 and < 65535 ? port.ToString() : "8095");
        Store.Set("sms_url", tSmsUrl.Text.Trim());
        Store.Set("sms_method", cbSmsMethod.Text);
        Store.Set("sms_body", tSmsBody.Text.Trim());
        WebApp.Start(SynchronizationContext.Current);
        WebInfo();
    }

    // ---------------- التكبير ----------------
    Control ZoomRow()
    {
        var box = W.Labeled("حجم الواجهة والخط (يُطبّق بعد إعادة التشغيل)", cbZoom, "sun");
        cbZoom.SelectedIndex = Math.Max(0, Array.IndexOf(Zooms, int.TryParse(Store.Get("ui_scale", "100"), out var z) ? z : 100));
        return box;
    }

    bool SaveZoom()
    {
        var v = Zooms[Math.Max(0, cbZoom.SelectedIndex)].ToString();
        if (Store.Get("ui_scale", "100") == v) return false;
        Store.Set("ui_scale", v);
        return true;
    }

    // ---------------- وضع التدريب ----------------
    Control TrainingBox()
    {
        var p = W.Flow(false);
        var b = W.Btn(Training.Active ? "الخروج من وضع التدريب" : "بدء وضع التدريب", "users", Training.Active ? BtnKind.Amber : BtnKind.Secondary, 180);
        b.Click += (s, e) =>
        {
            if (!W.Confirm(Training.Active ? "الخروج من وضع التدريب" : "وضع التدريب",
                Training.Active ? "يعود البرنامج إلى بيانات المحل الحقيقية." : "يُعاد تشغيل البرنامج ببيانات تجريبية منفصلة تماماً يتدرب عليها الموظف الجديد. بيانات المحل لا تتأثر.", "إعادة التشغيل")) return;
            Training.Enable(!Training.Active);
            Program.Restart();
        };
        p.Controls.Add(b);
        if (Training.Active)
        {
            var r = W.Btn("إعادة البيانات التجريبية", "rotate-ccw", BtnKind.Ghost, 180);
            r.Click += (s, e) =>
            {
                if (!W.Confirm("إعادة البيانات التجريبية", "تُحذف كل تجارب التدريب وتبدأ من بيانات تجريبية جديدة.", "إعادة", true)) return;
                SqliteReset();
            };
            p.Controls.Add(r);
        }
        return p;
    }

    static void SqliteReset()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Training.ResetData();
        Program.Restart();
    }
}
