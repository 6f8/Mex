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
