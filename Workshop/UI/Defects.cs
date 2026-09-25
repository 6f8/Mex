using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>تسجيل قطعة معيبة أو تعديلها: من قطع طلب (رجع بالضمان) أو من قائمة القطع (وصلت معيبة)</summary>
public class DefectDialog : DialogShell
{
    record Candidate(string Title, Part Part, Order From);

    readonly Defect existing;
    readonly Order order;
    readonly List<Candidate> candidates = new();
    readonly ComboBox cbPick = W.Combo(560, Array.Empty<string>());
    readonly ComboBox cbInv = W.Combo(560, Array.Empty<string>());
    readonly TextBox tName = new() { Width = 360 }, tSup = new() { Width = 260 }, tNote = new() { Width = 640 };
    readonly NumericUpDown nCost = W.Money(200);
    readonly DateTimePicker dDate = new() { Width = 200, Format = DateTimePickerFormat.Short };
    readonly Toggle tgStock = new() { Text = "خصم قطعة من المخزون (لم تعد صالحة للاستعمال)", Width = 560, Height = 34, Visible = false };
    readonly Label lblWarranty = W.Note("", 640, 26);
    string supEnd = "";
    readonly List<InvItem> invItems = new();
    string invId, fromOrderTech = "";

    DefectDialog(Order o, Defect d) : base(d == null ? "تسجيل قطعة معيبة" : "تعديل القطعة المعيبة", 720, 600, "triangle-alert", Pal.Amber)
    {
        order = o;
        existing = d;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        if (o != null && d == null)
        {
            flow.Controls.Add(W.Note($"الطلب {o.RefNo} — {o.CustomerName} — {o.Device}", 640));
            AddParts(o, "");
            if (Calc.Find(o.WarrantyOf) is Order src) AddParts(src, $" — من الطلب الأصلي {src.RefNo}");
            cbPick.Items.AddRange(candidates.Select(c => (object)c.Title).ToArray());
            cbPick.Items.Add("قطعة أخرى (أكتبها يدوياً)");
            flow.Controls.Add(W.Labeled("أي قطعة تعطلت؟", cbPick, "package"));
            cbPick.SelectedIndexChanged += (s, e) => FromCandidate();
        }
        else if (d == null)
        {
            invItems.AddRange(Store.Inventory.OrderBy(i => i.Name, StringComparer.CurrentCulture));
            cbInv.Items.Add("— ليست من قائمة القطع —");
            cbInv.Items.AddRange(invItems.Select(i => (object)(i.Name + (i.Compatible != "" ? $" ({i.Compatible})" : "") + (i.Supplier != "" ? " — " + i.Supplier : ""))).ToArray());
            cbInv.SelectedIndex = 0;
            flow.Controls.Add(W.Note("قطعة وصلتك معيبة من المورد، أو تعطلت قبل تركيبها.", 640));
            flow.Controls.Add(W.Labeled("من قائمة القطع (اختياري)", cbInv, "package-search"));
            cbInv.SelectedIndexChanged += (s, e) => FromInventory();
        }
        var r1 = W.Flow();
        r1.Controls.Add(W.Labeled("اسم القطعة *", tName));
        r1.Controls.Add(W.Labeled("المورد", tSup, "store"));
        flow.Controls.Add(r1);
        var r2 = W.Flow();
        r2.Controls.Add(W.Labeled("التكلفة", nCost));
        r2.Controls.Add(W.Labeled("التاريخ", dDate));
        flow.Controls.Add(r2);
        lblWarranty.Font = Theme.FS(9.5f);
        flow.Controls.Add(lblWarranty);
        flow.Controls.Add(W.Labeled("ملاحظة (سبب العطل)", tNote));
        tgStock.Margin = new Padding(6, 6, 6, 2);
        flow.Controls.Add(tgStock);
        Body.Controls.Add(flow);
        W.Suggest(tSup, Calc.SupplierBalances().Select(x => x.Name).Concat(Store.Inventory.Select(i => i.Supplier)).Concat(Store.Orders.SelectMany(x => x.Parts.Select(p => p.Supplier))));

        if (d != null)
        {
            tName.Text = d.PartName; tSup.Text = d.Supplier; W.Set(nCost, d.Cost); tNote.Text = d.Note;
            dDate.Value = Txt.ParseDate(d.Date) ?? DateTime.Today;
            invId = d.InventoryItemId;
            supEnd = d.SupWarrantyEnd;
            ShowWarranty();
        }
        else if (cbPick.Items.Count > 0) cbPick.SelectedIndex = 0;

        dDate.ValueChanged += (s, e) => ShowWarranty();
        if (d == null && cbPick.Items.Count == 0) ShowWarranty();
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) => Save();
        Shown += (s, e) => tName.Focus();
    }

    void AddParts(Order o, string suffix)
    {
        foreach (var p in o.Parts.Where(p => p.Name != "" || p.Cost > 0))
            candidates.Add(new($"{(p.Name == "" ? "قطعة" : p.Name)}{(p.Supplier != "" ? " — " + p.Supplier : "")} — {Txt.Money(p.Cost)}{suffix}", p, o));
    }

    void FromCandidate()
    {
        int i = cbPick.SelectedIndex;
        if (i < 0 || i >= candidates.Count) { invId = null; fromOrderTech = order?.Technician ?? ""; supEnd = ""; ShowWarranty(); return; }
        var c = candidates[i];
        tName.Text = c.Part.Name; tSup.Text = c.Part.Supplier; W.Set(nCost, c.Part.Cost);
        invId = c.Part.InventoryItemId;
        fromOrderTech = c.From.Technician;
        supEnd = SupWarranty.EndFor(c.Part, c.From);
        ShowWarranty();
    }

    void FromInventory()
    {
        int i = cbInv.SelectedIndex - 1;
        if (i < 0 || i >= invItems.Count) { invId = null; tgStock.Visible = false; supEnd = ""; ShowWarranty(); return; }
        var it = invItems[i];
        // وصلت معيبة: من يوم تسجيلها
        var days = it.SupWarranty ?? SupWarranty.ForSupplier(it.Supplier);
        supEnd = days is int dd ? Txt.Iso(dDate.Value.Date.AddDays(dd)) : "";
        ShowWarranty();
        tName.Text = it.Compatible != "" ? $"{it.Name} ({it.Compatible})" : it.Name;
        tSup.Text = it.Supplier;
        W.Set(nCost, it.Cost);
        invId = it.Id;
        tgStock.Visible = it.Qty != null;
        tgStock.Checked = it.Qty != null && it.Qty > 0;
    }

    void ShowWarranty()
    {
        var on = Txt.Iso(dDate.Value);
        lblWarranty.Text = "🛡 " + SupWarranty.Text(supEnd, on) + (SupWarranty.Valid(supEnd, on) ? " — من حقك إرجاعها" : "");
        lblWarranty.ForeColor = supEnd == "" ? Theme.Muted : SupWarranty.Valid(supEnd, on) ? Pal.Good : Pal.Bad;
    }

    public static void ForOrder(Order o)
    {
        using var d = new DefectDialog(o, null);
        d.ShowModal();
    }

    public static void New()
    {
        using var d = new DefectDialog(null, null);
        d.ShowModal();
    }

    public static void Edit(Defect x)
    {
        using var d = new DefectDialog(null, x);
        d.ShowModal();
    }

    void Save()
    {
        var name = tName.Text.Trim();
        if (name == "") { tName.Focus(); Dialogs.Warn("اكتب اسم القطعة."); return; }
        var d = existing ?? new Defect { Id = Txt.Uid("df") };
        d.PartName = name;
        d.Supplier = tSup.Text.Trim();
        d.Cost = (double)nCost.Value;
        d.Note = tNote.Text.Trim();
        d.Date = Txt.Iso(dDate.Value);
        d.SupWarrantyEnd = supEnd ?? "";
        if (existing == null)
        {
            d.InventoryItemId = invId;
            if (order != null)
            {
                d.OrderId = order.Id; d.RefNo = order.RefNo; d.Device = order.Device;
                d.Technician = fromOrderTech != "" ? fromOrderTech : order.Technician;
            }
        }
        // الكتابة المسجلة سابقاً لنفس المورد
        if (d.Supplier != "" && Calc.SupplierBalances().FirstOrDefault(x => x.Key == Txt.Fold(d.Supplier)) is Calc.SupplierBalance known) d.Supplier = known.Name;
        Store.SaveDefect(d);
        if (existing == null && tgStock.Visible && tgStock.Checked && Store.Inventory.FirstOrDefault(i => i.Id == invId) is InvItem it && it.Qty != null)
        {
            it.Qty--;
            it.UpdatedAt = Txt.Now;
            Store.SaveInv(it);
        }
        DialogResult = DialogResult.OK;
        Close();
        Store.NotifyChanged();
        Toast.Show(existing == null ? $"سُجّلت القطعة المعيبة «{name}» — أرجعها للمورد من «القطع المعيبة»" : "حُفظ التعديل");
    }
}

/// <summary>إغلاق قطعة معيبة: خصم من حساب المورد، أو استبدال، أو رفض (خسارة)</summary>
public class ResolveDefectDialog : DialogShell
{
    readonly Defect d;
    readonly Seg how = new(Defects.Resolutions.Select(r => (r.Key, r.Key switch { "credit" => "خصم من الحساب", "replaced" => "استبدلها", _ => "رفض الإرجاع" })).ToArray());
    readonly TextBox tSup = new() { Width = 300 };
    readonly NumericUpDown nCredit = W.Money(200);
    readonly Label explain = W.Note("", 600, 44);
    readonly Control creditBox;

    ResolveDefectDialog(Defect x) : base("إرجاع القطعة للمورد", 680, 510, "rotate-ccw", Pal.Amber)
    {
        d = x;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        flow.Controls.Add(W.Head($"{d.PartName} — {Txt.Money(d.Cost)}", 600));
        if (d.RefNo != "") flow.Controls.Add(W.Note($"من الطلب {d.RefNo} — {d.Device}", 600));
        if (d.SupWarrantyEnd != "")
        {
            var wl = W.Note("🛡 " + SupWarranty.Text(d.SupWarrantyEnd, d.Date), 600);
            wl.ForeColor = SupWarranty.Valid(d.SupWarrantyEnd, d.Date) ? Pal.Good : Pal.Bad;
            flow.Controls.Add(wl);
        }
        flow.Controls.Add(W.Note("ماذا فعل المورد؟", 600, 22));
        how.Margin = new Padding(6, 2, 6, 6);
        flow.Controls.Add(how);
        var r = W.Flow();
        r.Controls.Add(W.Labeled("المورد", tSup, "store"));
        creditBox = W.Labeled("المبلغ المخصوم من حسابه", nCredit);
        r.Controls.Add(creditBox);
        flow.Controls.Add(r);
        flow.Controls.Add(explain);
        Body.Controls.Add(flow);
        tSup.Text = d.Supplier;
        W.Set(nCredit, d.Credited > 0 ? d.Credited : d.Cost);
        W.Suggest(tSup, Calc.SupplierBalances().Select(s => s.Name).Concat(Store.Inventory.Select(i => i.Supplier)));
        how.Value = d.Resolution != "" ? d.Resolution : "credit";
        how.Changed += _ => Explain();
        Explain();
        var ok = AddButton("تأكيد", DialogResult.None, BtnKind.Primary, "check");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) => Save();
    }

    public static bool Open(Defect x)
    {
        using var dlg = new ResolveDefectDialog(x);
        return dlg.ShowModal() == DialogResult.OK;
    }

    void Explain()
    {
        creditBox.Visible = how.Value == "credit";
        bool stock = Store.Inventory.FirstOrDefault(i => i.Id == d.InventoryItemId)?.Qty != null;
        explain.Text = how.Value switch
        {
            "credit" => "تُسجَّل حركة «مرتجع» في حساب المورد فينقص ما تدين له به.",
            "replaced" => stock ? "أعطاك قطعة سليمة بدلها — تُضاف قطعة للمخزون." : "أعطاك قطعة سليمة بدلها — لا يتغير حسابه.",
            _ => "رفض المورد الإرجاع — تُحسب القطعة خسارة في تقرير جودة الموردين.",
        };
    }

    void Save()
    {
        if (how.Value == "credit" && tSup.Text.Trim() == "") { tSup.Focus(); Dialogs.Warn("اكتب اسم المورد ليُخصم المبلغ من حسابه."); return; }
        if (how.Value == "credit" && nCredit.Value <= 0) { nCredit.Focus(); Dialogs.Warn("اكتب المبلغ المخصوم."); return; }
        Defects.Resolve(d, how.Value, (double)nCredit.Value, tSup.Text);
        DialogResult = DialogResult.OK;
        Close();
        Store.NotifyChanged();
        Toast.Show(how.Value == "credit" ? $"خُصم {Txt.Money((double)nCredit.Value)} من حساب {d.Supplier}" : "أُغلقت القطعة المعيبة");
    }
}

/// <summary>القطع المعيبة بانتظار الإرجاع، وسجلها، وجودة كل مورد</summary>
public class DefectsDialog : DialogShell
{
    readonly Seg filter = new(("pending", "بانتظار الإرجاع"), ("all", "الكل"));
    readonly DataGridView grid = W.Grid(), quality = W.Grid();
    readonly Label sum = W.Note("", 800, 28);
    List<Defect> rows = new();

    DefectsDialog() : base("القطع المعيبة ومرتجعات الموردين", 1040, 720, "triangle-alert", Pal.Amber)
    {
        var tabs = new ModernTabs { Dock = DockStyle.Fill };
        // ---------- القائمة ----------
        var list = new Panel { BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        grid.Columns.Add("date", "التاريخ");
        grid.Columns.Add("part", "القطعة");
        grid.Columns.Add("sup", "المورد");
        grid.Columns.Add("cost", "التكلفة");
        grid.Columns.Add("ref", "الطلب");
        grid.Columns.Add("tech", "الفني");
        grid.Columns.Add("warranty", "ضمان المورد");
        grid.Columns.Add("state", "الحالة");
        grid.Columns["part"].FillWeight = 200;
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || grid.Columns[e.ColumnIndex].Name != "state") return;
            var d = rows[e.RowIndex];
            e.CellStyle.ForeColor = d.Status == "pending" ? Pal.AmberInk : d.Resolution == "rejected" ? Pal.Bad : Pal.Good;
        };
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || grid.Columns[e.ColumnIndex].Name != "warranty") return;
            var d = rows[e.RowIndex];
            if (d.SupWarrantyEnd != "") e.CellStyle.ForeColor = SupWarranty.Valid(d.SupWarrantyEnd, d.Date) ? Pal.Good : Pal.Bad;
        };
        grid.CellDoubleClick += (s, e) => Resolve();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Surface, WrapContents = false };
        filter.Margin = new Padding(4, 6, 12, 4);
        top.Controls.Add(filter);
        sum.Margin = new Padding(6, 10, 6, 0);
        top.Controls.Add(sum);
        list.Controls.Add(grid);
        list.Controls.Add(top);
        tabs.Add("القطع المعيبة", list, "triangle-alert");

        // ---------- جودة الموردين ----------
        var q = new Panel { BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        quality.Columns.Add("sup", "المورد");
        quality.Columns.Add("parts", "قطع رُكّبت");
        quality.Columns.Add("defects", "تعطلت");
        quality.Columns.Add("rate", "نسبة العطل");
        quality.Columns.Add("returns", "أجهزة رجعت بالضمان");
        quality.Columns.Add("value", "قيمة المعيب");
        quality.Columns.Add("recovered", "المسترد");
        quality.Columns.Add("lost", "الخسارة");
        q.Controls.Add(quality);
        q.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 44, BackColor = Theme.Surface, ForeColor = Theme.Muted, Font = Theme.F(9),
            Text = "كل الفترة. «تعطلت» من القطع المعيبة المسجلة، و«رجعت بالضمان» أجهزة عادت بعد تركيب قطعة من هذا المورد. المورد الأعلى نسبة في الأعلى.",
            TextAlign = ContentAlignment.MiddleLeft
        });
        tabs.Add("جودة الموردين", q, "store");
        tabs.SelectedIndex = 0;
        Body.Controls.Add(tabs);

        AddButton("إرجاع / إغلاق", DialogResult.None, BtnKind.Primary, "rotate-ccw").Click += (s, e) => Resolve();
        AddButton("قطعة معيبة جديدة", DialogResult.None, BtnKind.Secondary, "plus").Click += (s, e) => { DefectDialog.New(); Render(); };
        AddButton("تعديل", DialogResult.None, BtnKind.Secondary, "pencil").Click += (s, e) => { if (Sel is Defect d) { DefectDialog.Edit(d); Render(); } };
        AddButton("طباعة قائمة الإرجاع", DialogResult.None, BtnKind.Secondary, "printer").Click += (s, e) => PrintReturn();
        AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2").Click += (s, e) => Delete();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        filter.Changed += _ => Render();
        Render();
    }

    public static void Open()
    {
        using var d = new DefectsDialog();
        d.ShowModal();
    }

    Defect Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    void Render()
    {
        rows = Store.Defects.Where(d => filter.Value == "all" || d.Status == "pending")
            .OrderBy(d => d.Status == "pending" ? 0 : 1).ThenByDescending(d => d.Date, StringComparer.Ordinal).ToList();
        grid.Rows.Clear();
        foreach (var d in rows)
            grid.Rows.Add(Txt.FmtShortDate(d.Date), d.PartName + (d.Note != "" ? "  — " + d.Note : ""), d.Supplier == "" ? "—" : d.Supplier, Txt.Money(d.Cost),
                d.RefNo == "" ? "—" : d.RefNo, d.Technician == "" ? "—" : d.Technician,
                d.SupWarrantyEnd == "" ? "—" : SupWarranty.Valid(d.SupWarrantyEnd, d.Date) ? "ساري ✓" : "منتهٍ",
                Defects.StateText(d) + (d.Resolution == "credit" ? " " + Txt.Money(d.Credited) : ""));
        var pending = Store.Defects.Where(d => d.Status == "pending").ToList();
        sum.Text = pending.Count == 0 ? "لا توجد قطع بانتظار الإرجاع" : $"{pending.Count} قطعة بانتظار الإرجاع بقيمة {Txt.Money(pending.Sum(d => d.Cost))} — انقر مرتين على القطعة لإرجاعها";

        quality.Rows.Clear();
        foreach (var x in Defects.SupplierQuality().OrderByDescending(x => x.DefectRate + x.ReturnRate).ThenByDescending(x => x.Parts))
        {
            int i = quality.Rows.Add(x.Name, x.Parts, x.DefectCount, x.Parts > 0 ? Math.Round(x.DefectRate, 1) + "%" : "—",
                x.Orders > 0 ? $"{x.WarrantyReturns}  ({Math.Round(x.ReturnRate)}%)" : x.WarrantyReturns.ToString(),
                Txt.Money(x.DefectCost), Txt.Money(x.Recovered), Txt.Money(x.Lost));
            if (x.DefectRate > 10 || x.ReturnRate > 10) quality.Rows[i].DefaultCellStyle.ForeColor = Pal.Bad;
        }
    }

    void Resolve()
    {
        if (Sel is not Defect d) return;
        if (d.Status != "pending")
        {
            var r = W.Ask3("القطعة مغلقة", $"{d.PartName} — {Defects.StateText(d)}\nهل تريد تغيير ما فعله المورد، أو إعادتها إلى «بانتظار الإرجاع»؟", "تغيير", "إعادتها بانتظار الإرجاع");
            if (r == false) return;
            if (r == null) { Defects.Reopen(d); Store.NotifyChanged(); Render(); return; }
        }
        if (ResolveDefectDialog.Open(d)) Render();
    }

    void Delete()
    {
        if (Sel is not Defect d) return;
        var extra = d.Resolution == "credit" ? "\nسيُحذف المرتجع من حساب المورد أيضاً." : d.Resolution == "replaced" && Store.Inventory.Any(i => i.Id == d.InventoryItemId) ? "\nستُخصم القطعة البديلة من المخزون." : "";
        if (!W.Confirm("حذف القطعة المعيبة؟", d.PartName + extra, "حذف", true)) return;
        Defects.Delete(d);
        Store.NotifyChanged();
        Render();
    }

    /// <summary>ورقة تُسلَّم مع القطع عند إرجاعها للمورد</summary>
    void PrintReturn()
    {
        var sup = Sel?.Supplier ?? "";
        var list = Store.Defects.Where(d => d.Status == "pending" && (sup == "" || Txt.Fold(d.Supplier) == Txt.Fold(sup))).OrderBy(d => d.Date, StringComparer.Ordinal).ToList();
        if (list.Count == 0) { Toast.Show(sup == "" ? "لا توجد قطع بانتظار الإرجاع" : $"لا توجد قطع بانتظار الإرجاع لـ {sup}", Tone.Info); return; }
        var body = Printer.Header("قائمة قطع معيبة للإرجاع") +
            (sup != "" ? $"<div class=\"grid\"><div><span class=\"k\">المورد: </span><b>{Txt.Esc(sup)}</b></div><div><span class=\"k\">التاريخ: </span><b>{Txt.FmtDate(Txt.Today)}</b></div></div>" : "") +
            Printer.Table(new[] { "التاريخ", "القطعة", "المورد", "الجهاز", "ملاحظة", "التكلفة" },
                list.Select(d => new[] { Txt.FmtDate(d.Date), d.PartName, d.Supplier, d.Device, d.Note, Txt.Money(d.Cost) })) +
            $"<div class=\"row total\"><span>المجموع ({list.Count} قطعة)</span><span>{Txt.Esc(Txt.Money(list.Sum(d => d.Cost)))}</span></div>" +
            "<div class=\"foot\">توقيع المستلم: ..............................</div>";
        Printer.Doc(body, "مرتجعات " + sup, "760px");
    }
}
