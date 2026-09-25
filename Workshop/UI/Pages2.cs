using Raseed;
using static Raseed.Dpi;

namespace Workshop;

// ============================== قطع الغيار والأسعار ==============================
public class InventoryPage : Page
{
    public override string Title => "قطع الغيار والأسعار";
    public override string Desc => "قائمة أسعار الشراء والبيع لكل موديل ومورد، مع الكميات";
    public override string PageIcon => "package";

    record RowItem(bool Header, string Model, List<InvItem> Group, InvItem Item);

    readonly Ledger ledger = new() { Dock = DockStyle.Top };
    readonly TextBox search = new() { Width = 260, PlaceholderText = "اسم القطعة أو الموديل أو المورد" };
    readonly Seg cat = new(new[] { ("", "الكل") }.Concat(K.InvCats.Select(c => (c, c))).ToArray());
    readonly Seg mode = new(("models", "حسب الموديل"), ("table", "جدول"));
    readonly ComboBox sort = W.Combo(190, new[] { "الاسم", "آخر تعديل", "تكلفة الشراء: الأقل", "تكلفة الشراء: الأعلى", "سعر البيع: الأعلى", "الربح: الأعلى", "هامش الربح: الأعلى", "المورد" });
    readonly ModernButton bLow;
    readonly DataGridView grid = W.Grid();
    readonly HashSet<string> open = new();
    List<RowItem> rows = new();
    bool lowOnly;

    public InventoryPage()
    {
        var bar = Theme.Bar();
        bar.Controls.Add(W.Labeled("بحث", search, "search"));
        bar.Controls.Add(W.Labeled("الترتيب", sort));
        Panel Wrap(Control c) { var p = new Panel { Width = c.Width + 8, Height = 64, BackColor = Theme.Surface }; c.Location = new Point(0, 24); p.Controls.Add(c); return p; }
        bar.Controls.Add(Wrap(mode));
        bLow = W.Btn("الناقصة فقط", "package-minus", BtnKind.Secondary, 110); bLow.Margin = new Padding(4, 26, 4, 4);
        var bImport = W.Btn("استيراد CSV", "download", BtnKind.Secondary, 110); bImport.Margin = new Padding(4, 26, 4, 4);
        var bExport = W.Btn("تصدير CSV", "file-spreadsheet", BtnKind.Secondary, 110); bExport.Margin = new Padding(4, 26, 4, 4);
        var bNew = W.Btn("إضافة قطعة", "plus", BtnKind.Primary, 120); bNew.Margin = new Padding(4, 26, 4, 4);
        bar.Controls.AddRange(new Control[] { bLow, bImport, bExport, bNew });
        var catBar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Bg, Padding = new Padding(0, 6, 0, 0) };
        cat.Dock = DockStyle.Right;
        catBar.Controls.Add(cat);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Bg, WrapContents = false };
        var bMinus = W.Btn("إنقاص واحدة", "minus", BtnKind.Secondary, 110);
        var bPlus = W.Btn("وصلت قطعة", "plus", BtnKind.Secondary, 110);
        var bEdit = W.Btn("تعديل", "pencil", BtnKind.Secondary, 90);
        var bDel = W.Btn("حذف", "trash-2", BtnKind.Danger, 80);
        var bCopy = W.Btn("نسخ قطع الموديل لموديل آخر", "copy", BtnKind.Ghost, 180);
        var bAddModel = W.Btn("قطعة لهذا الموديل", "circle-plus", BtnKind.Ghost, 140);
        actions.Controls.AddRange(new Control[] { bMinus, bPlus, bEdit, bDel, bAddModel, bCopy });

        grid.Columns.Add("name", "القطعة");
        grid.Columns.Add("model", "الموديل");
        grid.Columns.Add("sup", "المورد");
        grid.Columns.Add("qty", "المخزون");
        grid.Columns.Add("cost", "الشراء");
        grid.Columns.Add("sale", "البيع");
        grid.Columns.Add("profit", "الربح");
        grid.Columns.Add("notes", "ملاحظات");
        grid.Columns["name"].FillWeight = 180;
        grid.Columns["notes"].FillWeight = 150;
        grid.CellFormatting += Format;
        grid.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || !rows[e.RowIndex].Header) return;
            var m = rows[e.RowIndex].Model;
            if (!open.Remove(m)) open.Add(m);
            Reload();
        };
        grid.CellDoubleClick += (s, e) => { if (SelItem is InvItem i) Edit(i); };
        grid.KeyDown += (s, e) =>
        {
            if (SelItem is not InvItem i) return;
            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) { Qty(i, 1); e.Handled = true; }
            if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) { Qty(i, -1); e.Handled = true; }
            if (e.KeyCode == Keys.Delete) { Delete(i); e.Handled = true; }
        };

        Controls.Add(grid);
        Controls.Add(actions);
        Controls.Add(catBar);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        Controls.Add(ledger);

        Ui2.OnIdle(search, Reload, 150);
        cat.Changed += _ => Reload();
        mode.Changed += _ => Reload();
        sort.SelectedIndexChanged += (s, e) => Reload();
        bLow.Click += (s, e) => { lowOnly = !lowOnly; Reload(); };
        bNew.Click += (s, e) => Edit(null, "");
        bMinus.Click += (s, e) => { if (SelItem is InvItem i) Qty(i, -1); };
        bPlus.Click += (s, e) => { if (SelItem is InvItem i) Qty(i, 1); };
        bEdit.Click += (s, e) => { if (SelItem is InvItem i) Edit(i); };
        bDel.Click += (s, e) => { if (SelItem is InvItem i) Delete(i); };
        bAddModel.Click += (s, e) => Edit(null, SelModel == "قطع عامة بدون موديل" ? "" : SelModel ?? "");
        bCopy.Click += (s, e) => { if (SelModel is string m) CopyModel(m); else Toast.Show("اختر قطعة أو موديلًا من القائمة أولًا.", Tone.Info); };
        bExport.Click += (s, e) => Export();
        bImport.Click += (s, e) => Import();
    }

    public void LowOnly() { lowOnly = true; search.Text = ""; cat.Value = ""; Reload(); }

    RowItem Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;
    InvItem SelItem => Sel?.Item;
    string SelModel => Sel == null ? null : Sel.Header ? Sel.Model : (Sel.Item.Compatible == "" ? "قطع عامة بدون موديل" : Sel.Item.Compatible);

    void Format(object s, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
        var r = rows[e.RowIndex];
        if (r.Header)
        {
            e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = Theme.SurfaceAlt;
            e.CellStyle.Font = Theme.FS(10);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.BrandDark;
            return;
        }
        var col = grid.Columns[e.ColumnIndex].Name;
        var i = r.Item;
        if (col == "qty")
        {
            var st = Calc.StockState(i);
            e.CellStyle.ForeColor = st == "out" ? Pal.Bad : st == "low" ? Pal.Wait : st == "none" ? Theme.Subtle : Theme.Ink;
        }
        if (col == "sale") e.CellStyle.ForeColor = Pal.Good;
        if (col == "profit") e.CellStyle.ForeColor = i.SalePrice - i.Cost >= 0 ? Pal.Good : Pal.Bad;
        if (col == "sup" && best(i)) e.CellStyle.ForeColor = Pal.Good;
    }

    Func<InvItem, bool> best = _ => false;

    /// <summary>أرخص مورد لنفس القطعة ونفس الموديل</summary>
    static Func<InvItem, bool> Cheapest()
    {
        string Key(InvItem i) => Txt.Fold(i.Compatible) + "|" + Txt.Fold(i.Name);
        var min = new Dictionary<string, double>(); var cnt = new Dictionary<string, int>();
        foreach (var i in Store.Inventory)
        {
            var k = Key(i);
            cnt[k] = cnt.GetValueOrDefault(k) + 1;
            if (i.Cost > 0 && (!min.ContainsKey(k) || i.Cost < min[k])) min[k] = i.Cost;
        }
        return i => { var k = Key(i); return cnt.GetValueOrDefault(k) > 1 && i.Cost > 0 && min.TryGetValue(k, out var m) && i.Cost <= m; };
    }

    static string StockText(InvItem i) => Calc.StockState(i) switch
    {
        "none" => "—", "out" => $"{i.Qty}  نفد", "low" => $"{i.Qty}  منخفض", _ => i.Qty.ToString()
    };

    public override void Reload()
    {
        var all = Store.Inventory;
        double cost = all.Sum(i => i.Cost), sale = all.Sum(i => i.SalePrice);
        int models = all.Select(i => Txt.Fold(i.Compatible)).Where(x => x != "").Distinct().Count();
        var tracked = all.Where(i => i.Qty != null).ToList();
        int outN = all.Count(i => Calc.StockState(i) == "out"), lowN = all.Count(i => Calc.StockState(i) == "low");
        ledger.Set(new[]
        {
            new Ledger.Cell("أصناف مسجّلة", all.Count.ToString(), $"{models} موديل — {tracked.Count} بمتابعة كمية"),
            new Ledger.Cell("قيمة المخزون", Txt.Money(tracked.Sum(i => Math.Max(0, i.Qty.Value) * i.Cost)), "الكمية × سعر الشراء"),
            new Ledger.Cell("تحتاج إعادة طلب", (outN + lowN).ToString(), outN > 0 ? $"{outN} نفدت تماماً" : "لا شيء نفد", null, outN + lowN > 0 ? -1 : 0),
            new Ledger.Cell("متوسط هامش الربح", cost > 0 ? Math.Round((sale - cost) / cost * 100) + "%" : "—", null, null, 1),
        });
        ledger.Height = ledger.HeightFor(Math.Max(S(400), ledger.Width));
        bLow.Kind = lowOnly ? BtnKind.Soft : BtnKind.Secondary;
        var q = Txt.Fold(search.Text);
        var list = all.Where(i => (cat.Value == "" || i.Category == cat.Value) && (!lowOnly || Calc.StockState(i) is "low" or "out") &&
            Txt.Matches(string.Join(" ", i.Name, i.Compatible, i.Supplier, i.Category, i.Notes), q));
        double Margin(InvItem i) => i.Cost > 0 ? (i.SalePrice - i.Cost) / i.Cost : 0;
        list = sort.SelectedIndex switch
        {
            1 => list.OrderByDescending(i => i.UpdatedAt, StringComparer.Ordinal),
            2 => list.OrderBy(i => i.Cost),
            3 => list.OrderByDescending(i => i.Cost),
            4 => list.OrderByDescending(i => i.SalePrice),
            5 => list.OrderByDescending(i => i.SalePrice - i.Cost),
            6 => list.OrderByDescending(Margin),
            7 => list.OrderBy(i => i.Supplier == "" ? "ي" : i.Supplier, StringComparer.CurrentCulture),
            _ => list.OrderBy(i => i.Name, StringComparer.CurrentCulture),
        };
        var items = list.ToList();
        best = Cheapest();
        rows = new();
        bool table = mode.Value == "table";
        grid.Columns["model"].Visible = table;
        if (table) rows.AddRange(items.Select(i => new RowItem(false, null, null, i)));
        else
        {
            var groups = Calc.GroupByName(items.Where(i => i.Compatible != ""), i => i.Compatible).Select(g => (g.Label, g.Items)).ToList();
            var general = items.Where(i => i.Compatible == "").ToList();
            if (general.Count > 0) groups.Add(("قطع عامة بدون موديل", general));
            bool expandAll = q != "" || cat.Value != "" || lowOnly || groups.Count == 1;
            foreach (var (label, g) in groups.OrderBy(x => x.Label, StringComparer.CurrentCulture))
            {
                rows.Add(new RowItem(true, label, g, null));
                if (expandAll || open.Contains(label)) rows.AddRange(g.Select(i => new RowItem(false, label, g, i)));
            }
        }
        string keep = SelItem?.Id ?? (Sel?.Header == true ? "h:" + Sel.Model : null);
        grid.Rows.Clear();
        foreach (var r in rows)
        {
            if (r.Header)
            {
                var sales = r.Group.Select(i => i.SalePrice).ToList();
                double mn = sales.Min(), mx = sales.Max();
                bool isOpen = open.Contains(r.Model) || q != "" || cat.Value != "" || lowOnly;
                grid.Rows.Add((isOpen ? "▾  " : "◂  ") + r.Model, "", $"{r.Group.Count} صنف", "", "", "البيع: " + (mn == mx ? Txt.Money(mn) : Txt.Money(mn) + " – " + Txt.Money(mx)), "", "");
            }
            else
            {
                var i = r.Item;
                double p = i.SalePrice - i.Cost;
                grid.Rows.Add($"{i.Name}   ·  {i.Category}", i.Compatible, (i.Supplier == "" ? "—" : i.Supplier) + (best(i) ? "   ✓ أرخص مورد" : ""), StockText(i),
                    Txt.Num(i.Cost), Txt.Num(i.SalePrice), $"{Txt.Num(p)}  ({(i.Cost > 0 ? Math.Round(p / i.Cost * 100) : 0)}%)", i.Notes);
            }
        }
        if (keep != null)
        {
            int idx = rows.FindIndex(r => r.Item?.Id == keep || (r.Header && "h:" + r.Model == keep));
            if (idx >= 0) grid.CurrentCell = grid.Rows[idx].Cells[0];
        }
        if (all.Count == 0) Toast.Show("قائمة القطع فارغة — سجّل القطع وأسعارها لتختارها داخل الطلب بضغطة واحدة.", Tone.Info);
    }

    void Qty(InvItem i, int d)
    {
        if (i.Qty == null) { Toast.Show("الكمية غير متابَعة لهذه القطعة — عدّلها واكتب الكمية.", Tone.Info); return; }
        i.Qty += d;
        i.UpdatedAt = Txt.Now;
        Store.SaveInv(i);
        Store.NotifyChanged();
    }

    void Edit(InvItem i, string model = "")
    {
        using var d = new InvItemDialog(i, model, cat.Value);
        if (d.ShowModal() == DialogResult.OK && d.Saved?.Compatible is string m && m != "") open.Add(m);
    }

    void Delete(InvItem i)
    {
        if (!W.Confirm($"حذف {i.Name}؟", $"{(i.Compatible == "" ? "بدون موديل" : i.Compatible)} — {(i.Supplier == "" ? "بدون مورد" : i.Supplier)}\nلن تتأثر الطلبات التي استخدمت هذه القطعة.", "حذف", true)) return;
        Store.DeleteInv(i);
        Store.NotifyChanged();
        Toast.Show("حُذفت القطعة");
    }

    void CopyModel(string src)
    {
        using var d = new DialogShell("نسخ قطع موديل", 520, 300, "copy");
        var t = new TextBox { Width = 460 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Surface };
        flow.Controls.Add(W.Note($"سيتم نسخ كل قطع «{src}» بأسعارها إلى موديل جديد، ويمكنك تعديلها بعد ذلك.", 460, 44));
        flow.Controls.Add(W.Labeled("اسم الموديل الجديد", t, "smartphone"));
        W.Suggest(t, Store.Inventory.Select(i => i.Compatible).Concat(Store.Orders.Select(o => o.Device)));
        d.Body.Controls.Add(flow);
        var ok = d.AddButton("نسخ القطع", DialogResult.None, BtnKind.Primary, "copy");
        d.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) =>
        {
            var target = t.Text.Trim();
            if (target == "") { t.Focus(); return; }
            if (Txt.Fold(target) == Txt.Fold(src)) { Dialogs.Warn("اختر اسم موديل مختلف."); return; }
            var items = Store.Inventory.Where(i => Txt.Fold(i.Compatible == "" ? "قطع عامة بدون موديل" : i.Compatible) == Txt.Fold(src)).ToList();
            var now = Txt.Now;
            var copies = items.Select(i => new InvItem { Id = Txt.Uid("inv"), Name = i.Name, Category = i.Category, Compatible = target, Supplier = i.Supplier, Cost = i.Cost, SalePrice = i.SalePrice, Qty = i.Qty == null ? null : 0, MinQty = i.MinQty, Notes = i.Notes, UpdatedAt = now }).ToList();
            Store.Inventory.InsertRange(0, copies);
            Store.SaveInvMany(copies);
            open.Add(target);
            d.DialogResult = DialogResult.OK;
            d.Close();
            Store.NotifyChanged();
            Toast.Show($"نُسخت {copies.Count} قطعة إلى {target}");
        };
        d.ShowModal();
    }

    void Export()
    {
        if (Store.Inventory.Count == 0) { Toast.Show("القائمة فارغة، لا يوجد ما يُصدَّر.", Tone.Info); return; }
        var f = W.SaveFile("CSV|*.csv", $"اسعار_القطع_{Txt.Today}.csv");
        if (f == null) return;
        try { Csv.ExportInventory(f); Toast.Show("تم تصدير قائمة الأسعار"); } catch (Exception ex) { Dialogs.Warn("تعذّر التصدير: " + ex.Message); }
    }

    void Import()
    {
        var f = W.OpenFile("CSV|*.csv;*.txt");
        if (f == null) return;
        List<InvItem> items;
        try { items = Csv.ReadInventory(f); } catch (Exception ex) { Dialogs.Warn("تعذّرت قراءة الملف: " + ex.Message); return; }
        if (items.Count == 0) { Dialogs.Warn("لم يُعثر على قطع صالحة في الملف. الأعمدة المتوقعة: " + string.Join("، ", Csv.InvHead) + "."); return; }
        var r = W.Ask3($"استيراد {items.Count} قطعة", $"القائمة الحالية فيها {Store.Inventory.Count} قطعة.\nالدمج يضيف الجديدة ويحدّث المطابقة (نفس الموديل والاسم والمورد).", "دمج مع الحالية", "استبدال القائمة كلها");
        if (r == false) return;
        if (r == null)
        {
            if (!W.Confirm("استبدال القائمة كلها؟", $"ستُحذف {Store.Inventory.Count} قطعة حالية.", "استبدال", true)) return;
            Store.ReplaceAll(Store.Orders.ToList(), Store.Trash.ToList(), items, Store.Expenses.ToList(), Store.SupplierTx.ToList(), Store.Drivers.ToList());
        }
        else
        {
            int added = 0, updated = 0;
            var changed = new List<InvItem>();
            foreach (var n in items)
            {
                var ex = Store.Inventory.FirstOrDefault(i => Txt.Fold(i.Compatible) == Txt.Fold(n.Compatible) && Txt.Fold(i.Name) == Txt.Fold(n.Name) && Txt.Fold(i.Supplier) == Txt.Fold(n.Supplier));
                if (ex != null)
                {
                    ex.Name = n.Name; ex.Category = n.Category; ex.Compatible = n.Compatible; ex.Supplier = n.Supplier; ex.Cost = n.Cost; ex.SalePrice = n.SalePrice; ex.Notes = n.Notes;
                    ex.Qty = n.Qty ?? ex.Qty; ex.MinQty = n.MinQty ?? ex.MinQty; ex.UpdatedAt = Txt.Now;
                    changed.Add(ex); updated++;
                }
                else { Store.Inventory.Insert(0, n); changed.Add(n); added++; }
            }
            Store.SaveInvMany(changed);
            Toast.Show($"أُضيفت {added} وحُدّثت {updated} قطعة");
        }
        Store.NotifyChanged();
    }
}

/// <summary>إضافة / تعديل قطعة في قائمة الأسعار</summary>
public class InvItemDialog : DialogShell
{
    readonly InvItem item;
    readonly TextBox tName = new() { Width = 420 }, tModel = new() { Width = 420 }, tSup = new() { Width = 420 }, tNotes = new() { Width = 420 },
                     tQty = new() { Width = 200, PlaceholderText = "فارغ = لا أتابع الكمية" }, tMin = new() { Width = 200, PlaceholderText = "افتراضياً 1" };
    readonly ComboBox cbCat = W.Combo(420, K.InvCats);
    readonly NumericUpDown nCost = W.Money(200), nSale = W.Money(200);
    readonly Label lblProfit = W.Note("", 420, 28);
    public InvItem Saved { get; private set; }

    public InvItemDialog(InvItem i, string model, string category) : base(i != null ? "تعديل قطعة" : model != "" ? $"إضافة قطعة لـ {model}" : "إضافة قطعة", 520, 720, "package-plus")
    {
        item = i;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        flow.Controls.Add(W.Labeled("اسم القطعة *", tName));
        flow.Controls.Add(W.Labeled("التصنيف", cbCat));
        flow.Controls.Add(W.Labeled("الموديل المتوافق", tModel, "smartphone"));
        flow.Controls.Add(W.Labeled("المورد", tSup, "store"));
        var money = W.Flow();
        money.Controls.Add(W.Labeled("تكلفة الشراء *", nCost));
        money.Controls.Add(W.Labeled("سعر البيع للزبون *", nSale));
        flow.Controls.Add(money);
        flow.Controls.Add(lblProfit);
        var qty = W.Flow();
        qty.Controls.Add(W.Labeled("الكمية في المخزون", tQty));
        qty.Controls.Add(W.Labeled("نبّهني عندما تصل إلى", tMin));
        flow.Controls.Add(qty);
        flow.Controls.Add(W.Labeled("ملاحظات", tNotes));
        Body.Controls.Add(flow);
        W.Suggest(tModel, Store.Inventory.Select(x => x.Compatible).Concat(Store.Orders.Select(o => o.Device)));
        W.Suggest(tSup, Store.Inventory.Select(x => x.Supplier).Concat(Store.SupplierTx.Select(t => t.Supplier)).Concat(Store.Orders.SelectMany(o => o.Parts.Select(p => p.Supplier))));
        var ok = AddButton("حفظ القطعة", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) => Save();
        tName.Text = i?.Name ?? "";
        tModel.Text = i?.Compatible ?? model;
        tSup.Text = i?.Supplier ?? "";
        tNotes.Text = i?.Notes ?? "";
        W.Set(nCost, i?.Cost ?? 0);
        W.Set(nSale, i?.SalePrice ?? 0);
        tQty.Text = i?.Qty?.ToString() ?? "";
        tMin.Text = i?.MinQty?.ToString() ?? "";
        if (i != null && i.Category != "" && !cbCat.Items.Contains(i.Category)) cbCat.Items.Add(i.Category);
        W.Pick(cbCat, i?.Category ?? (category != "" ? category : K.InvCats[0]));
        nCost.ValueChanged += (s, e) => Profit();
        nSale.ValueChanged += (s, e) => Profit();
        Profit();
        Shown += (s, e) => tName.Focus();
    }

    void Profit()
    {
        double c = (double)nCost.Value, s = (double)nSale.Value, p = s - c;
        lblProfit.Text = c > 0 || s > 0 ? $"الربح المتوقع: {Txt.Money(p)}   ({(c > 0 ? Math.Round(p / c * 100) : 0)}% هامش)" : "الربح المتوقع: —";
        lblProfit.ForeColor = p >= 0 ? Pal.Good : Pal.Bad;
    }

    void Save()
    {
        if (tName.Text.Trim() == "") { tName.Focus(); Dialogs.Warn("اكتب اسم القطعة وسعر الشراء وسعر البيع."); return; }
        if (nCost.Value == 0 && nSale.Value == 0 && !W.Confirm("بدون أسعار؟", "سعر الشراء وسعر البيع صفر. حفظ القطعة على أي حال؟", "حفظ")) return;
        var n = new InvItem
        {
            Id = item?.Id ?? Txt.Uid("inv"), Name = tName.Text.Trim(), Category = cbCat.Text, Compatible = tModel.Text.Trim(), Supplier = tSup.Text.Trim(),
            Cost = (double)nCost.Value, SalePrice = (double)nSale.Value, Qty = Txt.OptInt(tQty.Text), MinQty = Txt.OptInt(tMin.Text), Notes = tNotes.Text.Trim(), UpdatedAt = Txt.Now
        };
        Store.SaveInv(n);
        Saved = n;
        DialogResult = DialogResult.OK;
        Close();
        Store.NotifyChanged();
        Toast.Show(item != null ? "تم حفظ تعديل القطعة" : "أُضيفت القطعة إلى القائمة");
    }
}

// ============================== حسابات الموردين ==============================
public class SuppliersPage : Page
{
    public override string Title => "حسابات الموردين";
    public override string Desc => "ما أخذته بالدَّين من الموردين وما دفعته لهم. لا يؤثر على الربح، فتكلفة القطع محسوبة في الطلبات.";
    public override string PageIcon => "store";

    readonly Ledger ledger = new() { Dock = DockStyle.Top };
    readonly DataGridView grid = W.Grid();
    readonly ModernButton bDefects;
    List<Calc.SupplierBalance> rows = new();

    public SuppliersPage()
    {
        var bar = Theme.Bar();
        var bPay = W.Btn("دفعة لمورد", "wallet", BtnKind.Success, 120);
        var bBuy = W.Btn("شراء بالدَّين", "plus", BtnKind.Primary, 120);
        var bStatement = W.Btn("كشف حساب المورد", "scroll-text", BtnKind.Secondary, 140);
        bDefects = W.Btn("القطع المعيبة", "triangle-alert", BtnKind.Secondary, 140);
        bar.Controls.AddRange(new Control[] { bBuy, bPay, bStatement, bDefects });
        grid.Columns.Add("name", "المورد");
        grid.Columns.Add("purchases", "المشتريات");
        grid.Columns.Add("payments", "المدفوع");
        grid.Columns.Add("returns", "مرتجعات معيبة");
        grid.Columns.Add("balance", "الرصيد");
        grid.Columns.Add("last", "آخر حركة");
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            var c = grid.Columns[e.ColumnIndex].Name;
            if (c is "payments" or "returns") e.CellStyle.ForeColor = Pal.Good;
            if (c == "balance") e.CellStyle.ForeColor = rows[e.RowIndex].Balance > 0 ? Pal.Bad : Pal.Good;
        };
        grid.CellDoubleClick += (s, e) => { if (Sel is Calc.SupplierBalance x) SupplierStatementDialog.Open(x.Key); };
        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Bg });
        Controls.Add(ledger);
        bPay.Click += (s, e) => SupplierTxDialog.Open("payment", Sel?.Name ?? "");
        bBuy.Click += (s, e) => SupplierTxDialog.Open("purchase", Sel?.Name ?? "");
        bStatement.Click += (s, e) => { if (Sel is Calc.SupplierBalance x) SupplierStatementDialog.Open(x.Key); };
        bDefects.Click += (s, e) => DefectsDialog.Open();
    }

    Calc.SupplierBalance Sel => grid.CurrentRow != null && grid.CurrentRow.Index < rows.Count ? rows[grid.CurrentRow.Index] : null;

    public override void Reload()
    {
        rows = Calc.SupplierBalances().OrderByDescending(x => x.Balance).ThenByDescending(x => x.Last, StringComparer.Ordinal).ToList();
        double owed = rows.Sum(x => Math.Max(0, x.Balance));
        var month = Txt.Today[..7];
        double Sum(string type) => Store.SupplierTx.Where(t => t.Type == type && t.Date.StartsWith(month)).Sum(t => t.Amount);
        ledger.Set(new[]
        {
            new Ledger.Cell("المستحق للموردين", Txt.Money(owed), $"{rows.Count(x => x.Balance > 0)} مورد", Pal.Bad, owed > 0 ? -1 : 1),
            new Ledger.Cell("مشتريات بالدَّين هذا الشهر", Txt.Money(Sum("purchase")), null, Pal.Slate),
            new Ledger.Cell("مدفوع للموردين هذا الشهر", Txt.Money(Sum("payment")), null, Pal.Good),
            new Ledger.Cell("قطع معيبة بانتظار الإرجاع", Defects.PendingCount.ToString(),
                Defects.PendingCount > 0 ? "قيمتها " + Txt.Money(Store.Defects.Where(d => d.Status == "pending").Sum(d => d.Cost)) : "لا شيء عندك", Pal.Amber, Defects.PendingCount > 0 ? -1 : 0),
        });
        bDefects.Text = Defects.PendingCount > 0 ? $"القطع المعيبة ({Defects.PendingCount})" : "القطع المعيبة";
        bDefects.Kind = Defects.PendingCount > 0 ? BtnKind.Amber : BtnKind.Secondary;
        bDefects.FitWidth(140);
        ledger.Height = ledger.HeightFor(Math.Max(S(400), ledger.Width));
        grid.Rows.Clear();
        foreach (var x in rows)
            grid.Rows.Add(x.Name, Txt.Money(x.Purchases), Txt.Money(x.Payments), x.Returns > 0 ? Txt.Money(x.Returns) : "—",
                x.Balance > 0 ? Txt.Money(x.Balance) : x.Balance < 0 ? Txt.Money(-x.Balance) + "  (لك عنده)" : "مسدد", Txt.FmtDate(x.Last));
    }
}

/// <summary>شراء بالدَّين أو دفعة لمورد</summary>
public class SupplierTxDialog : DialogShell
{
    readonly Seg type = new(("purchase", "شراء بالدَّين"), ("payment", "دفعة للمورد"));
    readonly TextBox tSup = new() { Width = 420 }, tNote = new() { Width = 420 };
    readonly NumericUpDown nAmount = W.Money(200);
    readonly DateTimePicker dDate = new() { Width = 200, Format = DateTimePickerFormat.Short };

    SupplierTxDialog(string kind, string supplier) : base(kind == "payment" ? "دفعة للمورد" : "شراء بالدَّين", 520, 470, "store")
    {
        type.Value = kind;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
        type.Margin = new Padding(6, 4, 6, 8);
        flow.Controls.Add(type);
        flow.Controls.Add(W.Labeled("المورد *", tSup, "store"));
        var r = W.Flow();
        r.Controls.Add(W.Labeled("المبلغ *", nAmount));
        r.Controls.Add(W.Labeled("التاريخ", dDate));
        flow.Controls.Add(r);
        flow.Controls.Add(W.Labeled("ملاحظة", tNote));
        Body.Controls.Add(flow);
        tSup.Text = supplier;
        W.Suggest(tSup, Calc.SupplierBalances().Select(x => x.Name).Concat(Store.Inventory.Select(i => i.Supplier)).Concat(Store.Orders.SelectMany(o => o.Parts.Select(p => p.Supplier))));
        type.Changed += v => Text = v == "payment" ? "دفعة للمورد" : "شراء بالدَّين";
        var ok = AddButton("حفظ", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) => Save();
        Shown += (s, e) => { if (supplier != "") nAmount.Focus(); else tSup.Focus(); };
    }

    public static void Open(string kind, string supplier)
    {
        using var d = new SupplierTxDialog(kind, supplier);
        d.ShowModal();
    }

    void Save()
    {
        var sup = tSup.Text.Trim();
        double amount = (double)nAmount.Value;
        if (sup == "" || amount <= 0) { Dialogs.Warn("اكتب اسم المورد والمبلغ."); return; }
        // الكتابة المسجلة سابقًا لنفس المورد
        var known = Calc.SupplierBalances().FirstOrDefault(x => x.Key == Txt.Fold(sup));
        var t = new SupplierTx { Id = Txt.Uid("st"), Supplier = known?.Name ?? sup, Type = type.Value, Amount = amount, Date = Txt.Iso(dDate.Value), Note = tNote.Text.Trim() };
        Store.AddSupplierTx(t);
        DialogResult = DialogResult.OK;
        Close();
        Store.NotifyChanged();
        Toast.Show($"{(t.Type == "payment" ? "سُجّلت دفعة" : "سُجّل شراء")} {Txt.Money(amount)} — {t.Supplier}");
    }
}

/// <summary>كشف حساب مورد بالرصيد المتراكم</summary>
public class SupplierStatementDialog : DialogShell
{
    readonly string key;
    readonly Ledger ledger = new() { Dock = DockStyle.Top, Height = 100, MinCell = 160 };
    readonly DataGridView grid = W.Grid();
    List<SupplierTx> rows = new();

    SupplierStatementDialog(string key) : base("كشف حساب مورد", 920, 680, "scroll-text")
    {
        this.key = key;
        grid.Columns.Add("date", "التاريخ");
        grid.Columns.Add("type", "الحركة");
        grid.Columns.Add("note", "ملاحظة");
        grid.Columns.Add("amount", "المبلغ");
        grid.Columns.Add("run", "الرصيد");
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "rm", HeaderText = "", Text = "حذف", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, FillWeight = 40, DefaultCellStyle = { BackColor = Theme.DangerSoft, ForeColor = Theme.Danger, SelectionBackColor = Theme.DangerSoft, SelectionForeColor = Theme.Danger } });
        grid.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || grid.Columns[e.ColumnIndex].Name != "rm") return;
            var t = rows[e.RowIndex];
            var linked = t.Type == "return" ? Store.Defects.FirstOrDefault(d => d.StxId == t.Id) : null;
            if (!W.Confirm("حذف هذه الحركة؟", $"{Calc.StxLabel(t)} {Txt.Money(t.Amount)} — {Txt.FmtDate(t.Date)}" +
                (linked != null ? $"\nستعود القطعة «{linked.PartName}» إلى قائمة القطع المعيبة بانتظار الإرجاع." : ""), "حذف", true)) return;
            if (linked != null) Defects.Reopen(linked);
            else Store.DeleteSupplierTx(t);
            Store.NotifyChanged();
            Toast.Show("حُذفت الحركة");
        };
        Body.Controls.Add(grid);
        Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Surface });
        Body.Controls.Add(ledger);
        AddButton("دفعة للمورد", DialogResult.None, BtnKind.Success, "wallet").Click += (s, e) => SupplierTxDialog.Open("payment", Current()?.Name ?? "");
        AddButton("شراء بالدَّين", DialogResult.None, BtnKind.Secondary, "plus").Click += (s, e) => SupplierTxDialog.Open("purchase", Current()?.Name ?? "");
        AddButton("طباعة الكشف", DialogResult.None, BtnKind.Secondary, "printer").Click += (s, e) => Print();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        Render();
        void Changed() { if (!IsDisposed && IsHandleCreated) BeginInvoke(Render); }
        Store.Changed += Changed;
        FormClosed += (s, e) => Store.Changed -= Changed;
    }

    public static void Open(string key)
    {
        using var d = new SupplierStatementDialog(key);
        d.ShowModal();
    }

    Calc.SupplierBalance Current() => Calc.SupplierBalances().FirstOrDefault(s => s.Key == key);

    void Render()
    {
        var x = Current();
        if (x == null) { if (IsHandleCreated) Close(); return; }
        Text = "كشف حساب — " + x.Name;
        ledger.Set(new[]
        {
            new Ledger.Cell("المشتريات", Txt.Money(x.Purchases)),
            new Ledger.Cell("المدفوع", Txt.Money(x.Payments), null, null, 1),
            new Ledger.Cell("مرتجعات معيبة", Txt.Money(x.Returns), "خُصمت من الحساب", null, x.Returns > 0 ? 1 : 0),
            new Ledger.Cell("الرصيد", Txt.Money(Math.Abs(x.Balance)), x.Balance > 0 ? "عليك للمورد" : x.Balance < 0 ? "لك عند المورد" : "مسدد", null, x.Balance > 0 ? -1 : 1),
        });
        double run = 0;
        var list = x.Items.OrderBy(t => t.Date, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal).Select(t => { run += Calc.StxSign(t); return (t, run); }).Reverse().ToList();
        rows = list.Select(v => v.t).ToList();
        grid.Rows.Clear();
        foreach (var (t, r) in list) grid.Rows.Add(Txt.FmtDate(t.Date), Calc.StxLabel(t), t.Note, Txt.Money(t.Amount), Txt.Money(r));
    }

    void Print()
    {
        var x = Current();
        if (x == null) return;
        double run = 0;
        var rowsHtml = x.Items.OrderBy(t => t.Date, StringComparer.Ordinal).Select(t => { run += Calc.StxSign(t); return new[] { Txt.FmtDate(t.Date), Calc.StxLabel(t), t.Note, Txt.Money(t.Amount), Txt.Money(run) }; }).ToList();
        var body = Printer.Header("كشف حساب مورد") +
            $"<div class=\"grid\"><div><span class=\"k\">المورد: </span><b>{Txt.Esc(x.Name)}</b></div><div><span class=\"k\">الرصيد: </span><b class=\"{(x.Balance > 0 ? "bad" : "good")}\">{Txt.Esc(Txt.Money(x.Balance))}</b></div></div>" +
            Printer.Table(new[] { "التاريخ", "الحركة", "ملاحظة", "المبلغ", "الرصيد" }, rowsHtml);
        Printer.Doc(body, "كشف " + x.Name, "720px");
    }
}
