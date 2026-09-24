using System.Data;

namespace Raseed;

/// <summary>
/// فاتورة موحّدة: بيع / شراء / إرجاع بيع / إرجاع شراء / إتلاف.
/// - صرف المخزون بطريقة FEFO (الأقرب انتهاءً يُصرف أولًا) مع تتبع الوجبات وتواريخ الصلاحية
/// - ثلاثة أسعار (مفرد، جملة، خاص) — البيع بالقياس — سقف الذمة — الأقساط — التوصيل — مراكز الكلفة
/// </summary>
public class InvoiceForm : BaseForm
{
    readonly string type;
    readonly ComboBox cbParty = Ui.Combo(270), cbWh = Ui.Combo(190), cbLevel = Ui.Combo(120), cbPay = Ui.Combo(130),
                      cbBox = Ui.Combo(230), cbCC = Ui.Combo(170), cbDel = Ui.Combo(190);
    readonly DateTimePicker dtDate = new() { Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" };
    readonly NumericUpDown nDisc = Ui.Num(120), nPaid = Ui.Num(150), nFee = Ui.Num(110);
    readonly TextBox txtFind = new() { Width = 470, PlaceholderText = "امسح الباركود أو اكتب رمز / اسم المادة ثم Enter" }, txtNotes = new() { Width = 260 };
    readonly DataGridView grid = Ui.NewGrid(false);
    readonly StatLabel lblTotal = new() { Caption = "الإجمالي" }, lblNet = new() { Caption = "الصافي", ValueColor = Theme.Brand, ValueSize = 17 },
                       lblRemain = new() { Caption = "المتبقي", ValueColor = Theme.Danger };
    readonly Label lblParty = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(10, 16, 10, 0) },
                   lblInfo = new() { Dock = DockStyle.Fill, Padding = new Padding(2), Font = Theme.F(10), ForeColor = Theme.Text2 };
    DataTable items;
    bool busy;
    long editId, lastId, guarantorId;
    double oldEffect;   // أثر الفاتورة القديمة على رصيد الجهة (عند التعديل)

    bool IsOut => InvoiceOps.IsOut(type);
    bool UsesExpiry => type is "Purchase" or "SaleReturn" or "StockIn";
    bool SaleSide => type is "Sale" or "SaleReturn";
    // سندات المخزن والإتلاف: بلا جهة ولا دفع، والسعر فيها هو الكلفة
    bool HasParty => type is not ("Damage" or "StockIn" or "StockOut");
    bool CostDoc => !HasParty;
    DataTable serials;   // الأرقام التسلسلية (IMEI) للبحث بالمسح
    Dictionary<string, long> barcodeMap = new();   // كل باركودات المواد (الباركودات المتعددة)

    record Line(long ItemId, string Name, double Len, double Wid, double Qty, double Price, string Expiry, string Serials);

    static Control Stat(string caption, StatLabel value, int width = 150)
    {
        value.Caption = caption;
        value.Size = new Size(width, 64);
        value.Margin = new Padding(8, 2, 8, 2);
        return value;
    }

    public InvoiceForm(string invoiceType, long editInvoiceId = 0)
    {
        type = invoiceType;
        editId = editInvoiceId;
        Text = Ui.DocTitle(type);
        KeyPreview = true;

        // ---------- الترويسة ----------
        var head = Theme.Bar();
        if (HasParty)
        {
            string kind = SaleSide ? "عميل" : "مورد";
            Ui.FillCombo(cbParty, "SELECT id,name,price_level,phone,credit_limit FROM parties WHERE kind=@p0 OR kind='عميل ومورد' ORDER BY name",
                true, SaleSide ? "— زبون نقدي —" : "— بدون مورد —", kind);
            Ui.MakeSearchable(cbParty);
            head.Controls.Add(Ui.Labeled(SaleSide ? "العميل" : "المورد", cbParty));
        }
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id");
        Ui.SelectId(cbWh, Ui.DefaultWarehouse());
        head.Controls.Add(Ui.Labeled("المخزن", cbWh));
        if (SaleSide)
        {
            cbLevel.Items.AddRange(new object[] { "مفرد", "جملة", "خاص" });
            cbLevel.SelectedIndex = 0;
            head.Controls.Add(Ui.Labeled("السعر", cbLevel));
        }
        if (HasParty)
        {
            cbPay.Items.Add("نقدي"); cbPay.Items.Add("آجل");
            if (type == "Sale") cbPay.Items.Add("أقساط");
            cbPay.SelectedIndex = 0;
            head.Controls.Add(Ui.Labeled("طريقة الدفع", cbPay));
            Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
            head.Controls.Add(Ui.Labeled("الصندوق", cbBox));
        }
        Ui.FillCombo(cbCC, "SELECT id,name FROM cost_centers ORDER BY id", true);
        head.Controls.Add(Ui.Labeled("مركز الكلفة", cbCC));
        if (type == "Sale")
        {
            Ui.FillCombo(cbDel, "SELECT id,name,fee FROM delivery_companies ORDER BY name", true, "— بدون توصيل —");
            head.Controls.Add(Ui.Labeled("شركة التوصيل", cbDel));
            head.Controls.Add(Ui.Labeled("أجور التوصيل", nFee));
            cbDel.SelectedIndexChanged += (s, e) => { var r = Ui.GetRow(cbDel); nFee.Value = r == null ? 0 : (decimal)Db.D(r["fee"]); };
        }
        dtDate.Value = DateTime.Now;
        head.Controls.Add(Ui.Labeled("التاريخ", dtDate));
        head.Controls.Add(Ui.Labeled("ملاحظات", txtNotes));

        // ---------- شريط البحث ----------
        var find = Theme.Bar();
        var findBox = new InputBox(txtFind, 480, "scan-barcode") { Height = 44, Margin = new Padding(6, 4, 6, 4) };
        find.Controls.Add(findBox);
        var bAdd = Theme.Btn("إضافة", Theme.Success, 100);
        bAdd.Height = 44;
        var bDelRow = Theme.Btn("حذف السطر", Theme.Danger, 110);
        bDelRow.Height = 44;
        find.Controls.Add(bAdd);
        find.Controls.Add(bDelRow);
        var findEnd = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(24, 0, 0, 0) };
        find.Controls.Add(findEnd);
        // مربع البحث يأخذ المساحة المتبقية ليبقى الشريط سطراً واحداً في الشاشات الصغيرة
        find.Resize += (s, e) =>
        {
            int others = find.Controls.Cast<Control>().Where(c => c != findBox).Sum(c => c.Width + c.Margin.Horizontal);
            int w = Math.Clamp(find.ClientSize.Width - find.Padding.Horizontal - others - findBox.Margin.Horizontal - 8, 260, 620);
            if (findBox.Width != w) findBox.Width = w;
        };
        bAdd.Margin = bDelRow.Margin = new Padding(4, 4, 4, 4);
        bAdd.Click += (s, e) => FindAndAdd();
        bDelRow.Click += (s, e) => { if (grid.CurrentRow != null) { grid.Rows.Remove(grid.CurrentRow); Totals(); } };
        txtFind.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindAndAdd(); } };

        // ---------- جدول الأسطر ----------
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        AddCol("item_id", "", true, 10, false);
        AddCol("measure", "", true, 10, false);
        AddCol("code", "الرمز", true, 60);
        AddCol("name", "المادة", true, 200);
        AddCol("unit", "الوحدة", true, 50);
        AddCol("len", "الطول", false, 55);
        AddCol("wid", "العرض", false, 55);
        AddCol("qty", "الكمية", false, 60);
        AddCol("price", CostDoc ? "الكلفة" : "السعر", !Session.Can("edit_price") || type is "Damage" or "StockOut", 75);
        AddCol("expiry", "الصلاحية (yyyy-mm-dd)", false, 95, UsesExpiry);
        AddCol("total", "المجموع", true, 85);
        AddCol("serials", "", true, 10, false);   // الأرقام التسلسلية الممسوحة لهذا السطر
        grid.CellEndEdit += (s, e) => { RecalcRow(e.RowIndex); Totals(); };
        grid.SelectionChanged += (s, e) => { if (grid.CurrentRow != null) ShowInfo(ItemRow(Db.L(grid.CurrentRow.Cells["item_id"].Value)), false); };

        // ---------- لوحة معلومات المادة ----------
        var info = new CardPanel { Dock = DockStyle.Right, Width = 300, Title = "معلومات المادة", IconName = "info" };
        info.Controls.Add(lblInfo);

        // ---------- التذييل ----------
        var footCard = new CardPanel { Dock = DockStyle.Bottom, Height = 104, Padding = new Padding(14, 12, 14, 12) };
        var foot = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 14, 0, 0) };
        footCard.Controls.Add(foot);
        footCard.Controls.Add(actions);
        foot.Controls.Add(Stat("الإجمالي", lblTotal, 130));
        if (HasParty)
        {
            nDisc.Enabled = Session.Can("discount");
            nDisc.Width = 110; nPaid.Width = 130;
            foot.Controls.Add(Ui.Labeled("الخصم", nDisc));
            foot.Controls.Add(Stat("الصافي", lblNet, 150));
            foot.Controls.Add(Ui.Labeled(type == "Sale" ? "المدفوع / المقدّم" : "المدفوع", nPaid));
            foot.Controls.Add(Stat("المتبقي", lblRemain, 130));
            lblParty.Margin = new Padding(10, 30, 10, 0);
            foot.Controls.Add(lblParty);
        }
        bool stockDoc = Ui.IsStockDoc(type);
        var bSave = new ModernButton { Text = stockDoc ? "حفظ السند" : "حفظ الفاتورة", IconName = "save", Height = 50, Font = Theme.FS(11), Margin = new Padding(6, 0, 0, 0) };
        bSave.FitWidth(170);
        var bNew = Theme.Btn(stockDoc ? "سند جديد" : "فاتورة جديدة", Theme.Gray, 120, "plus");
        var bPrint = Theme.Btn(stockDoc ? "طباعة آخر سند" : "طباعة آخر فاتورة", Theme.Purple, 150, "printer");
        bNew.Height = bPrint.Height = 44;
        bNew.Margin = bPrint.Margin = new Padding(4, 4, 4, 4);
        findEnd.Controls.Add(bNew);
        if (Session.Can("print")) findEnd.Controls.Add(bPrint);
        actions.Controls.Add(bSave);
        bSave.Click += (s, e) => Save();
        bNew.Click += (s, e) => { editId = 0; Reset(); };
        bPrint.Click += (s, e) => { if (lastId > 0) InvoiceOps.BuildPrint(lastId)?.Print(); else Ui.Warn("لم تُحفظ أي فاتورة بعد في هذه الشاشة."); };

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(info);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 12 });
        Controls.Add(footCard);
        Controls.Add(find);
        Controls.Add(head);

        // ---------- الأحداث ----------
        nDisc.ValueChanged += (s, e) => Totals();
        nFee.ValueChanged += (s, e) => Totals();
        nPaid.ValueChanged += (s, e) => { if (!busy) Totals(false); };
        // سعر الأقساط يختلف عن السعر النقدي إن وُجد
        cbPay.SelectedIndexChanged += (s, e) => { if (type == "Sale") Reprice(); else Totals(); };
        cbParty.SelectedIndexChanged += (s, e) => PartyChanged();
        cbLevel.SelectedIndexChanged += (s, e) => Reprice();
        cbWh.SelectedIndexChanged += (s, e) => { if (grid.CurrentRow != null) ShowInfo(ItemRow(Db.L(grid.CurrentRow.Cells["item_id"].Value)), false); };
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F2) { txtFind.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.F10) { Save(); e.Handled = true; }
        };

        LoadItems();
        lblInfo.Text = "ابحث عن مادة بالباركود أو الاسم لإضافتها.";
        Totals();
        if (editId > 0) LoadInvoice(editId);
        Shown += (s, e) => txtFind.Focus();
    }

    void AddCol(string name, string header, bool readOnly, int weight, bool visible = true)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = header, ReadOnly = readOnly, Visible = visible, FillWeight = weight,
            MinimumWidth = Math.Max(60, TextRenderer.MeasureText(header, Theme.FS(9.5f)).Width + 26),
            DefaultCellStyle = { Format = "#,0.##", BackColor = readOnly ? Theme.SurfaceAlt : Theme.Surface, Alignment = name is "name" ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleCenter }
        });
    }

    // مواد جديدة قد تُعرَّف في تبويب آخر أثناء بقاء الفاتورة مفتوحة
    public override void OnPageActivated() => LoadItems();

    public override bool ConfirmClose() =>
        grid.Rows.Count == 0 || Ui.Confirm($"{Text}: فيها {grid.Rows.Count} مادة لم تُحفظ.\nإغلاقها بدون حفظ؟");

    void LoadItems()
    {
        items = Db.Query(@"SELECT id,code,barcode,name,unit,price_retail,price_wholesale,price_special,price_installment,price_buy,
            IFNULL(buy_currency,'IQD') AS buy_currency, IFNULL(sell_currency,'IQD') AS sell_currency, IFNULL(item_type,'اعتيادية') AS item_type,
            IFNULL(cost_method,'كلفة الوجبة') AS cost_method, by_measure,medical_info,alert_note,IFNULL(use_scale,0) AS use_scale FROM items
            WHERE active=1 OR id IN (SELECT item_id FROM invoice_lines WHERE invoice_id=@p0)", editId);
        serials = Db.Query("SELECT serial, item_id, invoice_id FROM item_serials");
        barcodeMap = Db.Query("SELECT barcode, item_id FROM item_barcodes").Rows.Cast<DataRow>()
            .GroupBy(x => Db.S(x["barcode"])).ToDictionary(g => g.Key, g => Db.L(g.First()["item_id"]));
        var src = new AutoCompleteStringCollection();
        foreach (DataRow r in items.Rows)
        {
            src.Add(Db.S(r["name"]));
            if (Db.S(r["barcode"]) != "") src.Add(Db.S(r["barcode"]));
        }
        txtFind.AutoCompleteCustomSource = src;
        txtFind.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        txtFind.AutoCompleteSource = AutoCompleteSource.CustomSource;
    }

    DataRow ItemRow(long id) => items.Rows.Cast<DataRow>().FirstOrDefault(r => Db.L(r["id"]) == id);

    void FindAndAdd()
    {
        var t = txtFind.Text.Trim();
        if (t == "") return;
        var rows = items.Rows.Cast<DataRow>();
        // مسح رقم تسلسلي (IMEI): يضيف الجهاز نفسه. البيع يتطلب رقمًا متوفرًا، وإرجاع البيع رقمًا مُباعًا
        var sr = serials?.Rows.Cast<DataRow>().FirstOrDefault(x => string.Equals(Db.S(x["serial"]), t, StringComparison.OrdinalIgnoreCase));
        if (sr != null && (IsOut || type == "SaleReturn"))
        {
            bool sold = Db.L(sr["invoice_id"]) > 0;
            if (IsOut && sold) { Ui.Warn($"الرقم التسلسلي {t} مُباع مسبقًا."); return; }
            if (type == "SaleReturn" && !sold) { Ui.Warn($"الرقم التسلسلي {t} غير مُباع، لا يمكن إرجاعه."); return; }
            if (grid.Rows.Cast<DataGridViewRow>().Any(g => (Convert.ToString(g.Cells["serials"].Value) ?? "").Split(',').Contains(Db.S(sr["serial"]))))
            { Ui.Warn($"الرقم التسلسلي {t} موجود في الفاتورة."); return; }
            var ir = ItemRow(Db.L(sr["item_id"]));
            if (ir != null) { AddItem(ir, Db.S(sr["serial"])); txtFind.Clear(); txtFind.Focus(); return; }
        }
        var r = rows.FirstOrDefault(x => Db.S(x["barcode"]) == t)
             ?? (barcodeMap.TryGetValue(t, out var bid) ? ItemRow(bid) : null)
             ?? rows.FirstOrDefault(x => Db.S(x["code"]) == t || Db.S(x["name"]) == t);
        // باركود الميزان: رمز المادة + الوزن
        if (r == null && ScaleCode.TryParse(t, out long plu, out double weight))
        {
            var sr2 = rows.FirstOrDefault(x => Db.L(x["use_scale"]) == 1 && long.TryParse(Db.S(x["code"]), out var c) && c == plu);
            if (sr2 != null)
            {
                var g = AddLine(sr2, 0.0, 0.0, weight, PriceFor(sr2), "");
                Totals();
                grid.CurrentCell = g.Cells["qty"];
                ShowInfo(sr2, true);
                txtFind.Clear();
                txtFind.Focus();
                return;
            }
        }
        r ??= rows.FirstOrDefault(x => Db.S(x["name"]).Contains(t, StringComparison.OrdinalIgnoreCase));
        if (r == null) { Ui.Warn("لم يتم العثور على المادة: " + t); return; }
        AddItem(r);
        txtFind.Clear();
        txtFind.Focus();
    }

    void AddItem(DataRow r, string serial = null)
    {
        long id = Db.L(r["id"]);
        bool measure = Db.L(r["by_measure"]) == 1;
        if (!measure && !UsesExpiry)
            foreach (DataGridViewRow gr in grid.Rows)
                if (Db.L(gr.Cells["item_id"].Value) == id)
                {
                    gr.Cells["qty"].Value = Ui.V(gr.Cells["qty"].Value) + 1;
                    if (serial != null) AppendSerial(gr, serial);
                    RecalcRow(gr.Index); Totals(); ShowInfo(r, false);
                    return;
                }

        var g = AddLine(r, measure ? 1.0 : 0.0, 0.0, 1.0, PriceFor(r), "");
        if (serial != null) AppendSerial(g, serial);
        Totals();
        grid.CurrentCell = measure ? g.Cells["len"] : g.Cells["qty"];
        ShowInfo(r, true);
    }

    static void AppendSerial(DataGridViewRow g, string serial)
    {
        var cur = Convert.ToString(g.Cells["serials"].Value) ?? "";
        g.Cells["serials"].Value = cur == "" ? serial : cur + "," + serial;
        g.Cells["name"].ToolTipText = "الأرقام التسلسلية: " + g.Cells["serials"].Value;
    }

    DataGridViewRow AddLine(DataRow r, double len, double wid, double qty, double price, string expiry)
    {
        bool measure = Db.L(r["by_measure"]) == 1;
        int i = grid.Rows.Add();
        var g = grid.Rows[i];
        g.Cells["item_id"].Value = Db.L(r["id"]);
        g.Cells["measure"].Value = measure ? 1 : 0;
        g.Cells["code"].Value = Db.S(r["code"]);
        g.Cells["name"].Value = Db.S(r["name"]);
        g.Cells["unit"].Value = Db.S(r["unit"]);
        g.Cells["len"].Value = len;
        g.Cells["wid"].Value = wid;
        g.Cells["qty"].Value = qty;
        g.Cells["price"].Value = price;
        g.Cells["expiry"].Value = expiry ?? "";
        if (!measure) { g.Cells["len"].ReadOnly = true; g.Cells["wid"].ReadOnly = true; }
        else g.Cells["qty"].ReadOnly = true;
        RecalcRow(i);
        return g;
    }

    /// <summary>تحميل فاتورة محفوظة للتعديل</summary>
    void LoadInvoice(long id)
    {
        var dt = Db.Query("SELECT * FROM invoices WHERE id=@p0", id);
        if (dt.Rows.Count == 0) { editId = 0; return; }
        var v = dt.Rows[0];
        Text = $"تعديل {Ui.DocTitle(type)} رقم {id}";
        if (HasParty) Ui.SelectId(cbParty, Db.L(v["party_id"]));
        Ui.SelectId(cbWh, Db.L(v["warehouse_id"]));
        guarantorId = Db.L(v["guarantor_id"]);
        if (SaleSide) { int li = cbLevel.Items.IndexOf(Db.S(v["price_level"])); if (li >= 0) cbLevel.SelectedIndex = li; }
        if (HasParty)
        {
            int pi = cbPay.Items.IndexOf(Db.S(v["pay_type"])); if (pi >= 0) cbPay.SelectedIndex = pi;
            if (Db.L(v["cashbox_id"]) > 0) Ui.SelectId(cbBox, Db.L(v["cashbox_id"]));
        }
        Ui.SelectId(cbCC, Db.L(v["cost_center_id"]));
        if (type == "Sale") { Ui.SelectId(cbDel, Db.L(v["delivery_id"])); nFee.Value = (decimal)Db.D(v["delivery_fee"]); }
        if (DateTime.TryParse(Db.S(v["date"]), out var d)) dtDate.Value = d;
        txtNotes.Text = Db.S(v["notes"]);

        var lines = InvoiceOps.IsOut(type)
            ? Db.Query(@"SELECT item_id, length, width, SUM(qty) AS qty, price, '' AS expiry FROM invoice_lines WHERE invoice_id=@p0
                         GROUP BY item_id, price, length, width ORDER BY MIN(id)", id)
            : Db.Query("SELECT item_id, length, width, qty, price, IFNULL(expiry,'') AS expiry FROM invoice_lines WHERE invoice_id=@p0 ORDER BY id", id);
        foreach (DataRow l in lines.Rows)
        {
            var r = ItemRow(Db.L(l["item_id"]));
            if (r == null) continue;
            double len = Db.D(l["length"]), wid = Db.D(l["width"]), qty = Db.D(l["qty"]);
            if (Db.L(r["by_measure"]) == 1 && Math.Abs(len * (wid > 0 ? wid : 1) - qty) > 1e-6) { len = qty; wid = 0; }
            var g = AddLine(r, len, wid, qty, Db.D(l["price"]), Db.S(l["expiry"]));
            // الأرقام التسلسلية المباعة بهذه الفاتورة تُربط بأول سطر للمادة
            if (IsOut && !grid.Rows.Cast<DataGridViewRow>().Any(x => x != g && Db.L(x.Cells["item_id"].Value) == Db.L(r["id"])))
                foreach (DataRow sr in Db.Query("SELECT serial FROM item_serials WHERE invoice_id=@p0 AND item_id=@p1", id, Db.L(r["id"])).Rows)
                    AppendSerial(g, Db.S(sr["serial"]));
        }
        nDisc.Value = (decimal)Db.D(v["discount"]);
        Totals();
        nPaid.Value = (decimal)Db.D(v["paid"]);
        oldEffect = type == "Sale" ? Db.D(v["net"]) - Db.D(v["paid"]) : 0;
        PartyChanged(keepLevel: true);   // لا نغيّر مستوى السعر المحفوظ حتى لا يُعاد تسعير الأسطر
    }

    double PriceFor(DataRow r)
    {
        long id = Db.L(r["id"]);
        switch (type)
        {
            case "Sale":
            case "SaleReturn":
                {
                    var col = Convert.ToString(cbLevel.SelectedItem) switch { "جملة" => "price_wholesale", "خاص" => "price_special", _ => "price_retail" };
                    double p = Db.D(r[col]);
                    if (type == "Sale" && Convert.ToString(cbPay.SelectedItem) == "أقساط" && Db.D(r["price_installment"]) > 0) p = Db.D(r["price_installment"]);
                    // مادة مسعّرة بالدولار: تُحوَّل إلى الدينار بسعر الصرف الحالي
                    return Db.S(r["sell_currency"]) == "USD" ? p * Ui.Rate("USD") : p;
                }
            case "Purchase":
            case "PurchaseReturn":
            case "StockIn":
                {
                    double last = Db.D(Db.Scalar("SELECT cost FROM batches WHERE item_id=@p0 ORDER BY id DESC LIMIT 1", id));
                    if (last > 0) return last;
                    double pb = Db.D(r["price_buy"]);
                    return Db.S(r["buy_currency"]) == "USD" ? pb * Ui.Rate("USD") : pb;
                }
            default:
                return StockOps.AvgCost(id);
        }
    }

    void Reprice()
    {
        foreach (DataGridViewRow g in grid.Rows)
        {
            var r = ItemRow(Db.L(g.Cells["item_id"].Value));
            if (r != null) { g.Cells["price"].Value = PriceFor(r); RecalcRow(g.Index); }
        }
        Totals();
    }

    void RecalcRow(int i)
    {
        if (i < 0 || i >= grid.Rows.Count) return;
        var g = grid.Rows[i];
        if (Db.L(g.Cells["measure"].Value) == 1)
        {
            double len = Ui.V(g.Cells["len"].Value), wid = Ui.V(g.Cells["wid"].Value);
            g.Cells["qty"].Value = Math.Round(len * (wid > 0 ? wid : 1), 3);
        }
        g.Cells["total"].Value = Ui.V(g.Cells["qty"].Value) * Ui.V(g.Cells["price"].Value);
    }

    double Total => grid.Rows.Cast<DataGridViewRow>().Sum(g => Ui.V(g.Cells["total"].Value));
    double Net => Total - (double)nDisc.Value + (double)nFee.Value;

    void Totals(bool setPaid = true)
    {
        busy = true;
        lblTotal.Value = Ui.M(Total);
        lblNet.Value = Ui.M(Net);
        if (setPaid && Convert.ToString(cbPay.SelectedItem) == "نقدي") nPaid.Value = (decimal)Math.Max(0, Net);
        double remain = Net - (double)nPaid.Value;
        lblRemain.Value = Ui.M(remain);
        lblRemain.ValueColor = remain > 0.005 ? Theme.Danger : remain < -0.005 ? Theme.Warning : Theme.Success;
        busy = false;
    }

    void PartyChanged(bool keepLevel = false)
    {
        var r = Ui.GetRow(cbParty);
        if (r == null) { lblParty.Text = ""; return; }
        if (SaleSide && !keepLevel)
        {
            int i = cbLevel.Items.IndexOf(Db.S(r["price_level"]));
            if (i >= 0) cbLevel.SelectedIndex = i;
        }
        double bal = Ui.PartyBalance(Db.L(r["id"])), lim = Db.D(r["credit_limit"]);
        lblParty.Text = $"رصيد الجهة: {Ui.M(bal)}" + (lim > 0 ? $"  |  سقف الذمة: {Ui.M(lim)}" : "");
        lblParty.ForeColor = lim > 0 && bal > lim ? Theme.Danger : Theme.Muted;
    }

    void ShowInfo(DataRow r, bool alert)
    {
        if (r == null) return;
        long id = Db.L(r["id"]), wh = Ui.GetId(cbWh);
        double stock = Db.D(Db.Scalar("SELECT IFNULL(SUM(qty),0) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1", id, wh));
        var exp = Db.S(Db.Scalar("SELECT MIN(expiry) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1 AND qty>0 AND expiry IS NOT NULL AND expiry<>''", id, wh));
        string med = Db.S(r["medical_info"]), note = Db.S(r["alert_note"]);
        lblInfo.Text =
            $"{Db.S(r["name"])}\n\n" +
            $"الرصيد في المخزن: {Ui.M(stock)} {Db.S(r["unit"])}\n" +
            $"أقرب صلاحية: {(exp == "" ? "—" : exp)}\n\n" +
            $"مفرد: {Ui.M(Db.D(r["price_retail"]))}\nجملة: {Ui.M(Db.D(r["price_wholesale"]))}\nخاص: {Ui.M(Db.D(r["price_special"]))}\n" +
            (med != "" ? $"\n— البيانات الطبية —\n{med}\n" : "") +
            (note != "" ? $"\n⚠ تنبيه:\n{note}" : "");
        if (alert && note != "")
            Dialogs.Message(note, "تنبيه على المادة: " + Db.S(r["name"]), Tone.Warning);
        if (alert && exp != "" && DateTime.TryParse(exp, out var ed) && ed < DateTime.Today && IsOut)
            Ui.Warn("انتبه: توجد وجبة منتهية الصلاحية من هذه المادة في المخزن وستُصرف أولًا. يُفضّل إتلافها.");
    }

    List<Line> Lines()
    {
        var list = new List<Line>();
        foreach (DataGridViewRow g in grid.Rows)
        {
            string exp = Convert.ToString(g.Cells["expiry"].Value)?.Trim() ?? "";
            if (exp != "")
            {
                if (!DateTime.TryParse(exp, out var d)) throw new Exception($"تاريخ صلاحية غير صحيح للمادة {g.Cells["name"].Value}: {exp}");
                exp = d.ToString(Ui.DFmt);
            }
            list.Add(new Line(Db.L(g.Cells["item_id"].Value), Convert.ToString(g.Cells["name"].Value),
                Ui.V(g.Cells["len"].Value), Ui.V(g.Cells["wid"].Value), Ui.V(g.Cells["qty"].Value), Ui.V(g.Cells["price"].Value), exp,
                Convert.ToString(g.Cells["serials"].Value) ?? ""));
        }
        return list;
    }

    void Save()
    {
        grid.EndEdit();
        List<Line> lines;
        try { lines = Lines(); } catch (Exception ex) { Ui.Warn(ex.Message); return; }
        if (lines.Count == 0) { Ui.Warn("الفاتورة فارغة."); return; }
        if (lines.Any(l => l.Qty <= 0)) { Ui.Warn("توجد مادة بكمية صفر أو سالبة."); return; }
        if (lines.Any(l => l.Price < 0)) { Ui.Warn("توجد مادة بسعر سالب."); return; }

        long party = Ui.GetId(cbParty), wh = Ui.GetId(cbWh), box = Ui.GetId(cbBox), cc = Ui.GetId(cbCC), del = Ui.GetId(cbDel);
        string pay = HasParty ? Convert.ToString(cbPay.SelectedItem) : "";
        string level = SaleSide ? Convert.ToString(cbLevel.SelectedItem) : "";
        double total = Total, disc = (double)nDisc.Value, fee = (double)nFee.Value, net = Net, paid = HasParty ? (double)nPaid.Value : 0;

        if (wh == 0) { Ui.Warn("اختر المخزن."); return; }
        if (paid > 0 && box == 0) { Ui.Warn("اختر الصندوق."); return; }
        if (paid > net + 0.001) { Ui.Warn("المبلغ المدفوع أكبر من صافي الفاتورة."); return; }
        if (net < 0) { Ui.Warn("الخصم أكبر من قيمة الفاتورة."); return; }
        if (type == "Sale" && pay != "نقدي" && party == 0) { Ui.Warn("البيع بالآجل أو بالأقساط يتطلب اختيار العميل."); return; }
        // بدون حساب عميل لا يوجد مكان يُسجَّل فيه الباقي، فيضيع من الحسابات
        if (SaleSide && party == 0 && paid < net - 0.001)
        { Ui.Warn("الزبون النقدي يجب أن يدفع (أو يُعاد له) كامل المبلغ.\nلتسجيل الباقي دينًا اختر حساب العميل."); return; }

        // سقف الذمة
        if (type == "Sale" && party > 0)
        {
            double limit = Db.D(Db.Scalar("SELECT credit_limit FROM parties WHERE id=@p0", party));
            double after = Ui.PartyBalance(party) - (editId > 0 ? oldEffect : 0) + net - paid;
            if (limit > 0 && after > limit + 0.001)
            {
                if (!Session.Can("exceed_credit")) { Ui.Warn($"تجاوز سقف الذمة!\nالسقف: {Ui.M(limit)} — الرصيد بعد الفاتورة: {Ui.M(after)}"); return; }
                if (!Ui.Confirm($"تجاوز سقف الذمة ({Ui.M(limit)}). الرصيد بعد الفاتورة {Ui.M(after)}. هل تريد المتابعة؟")) return;
            }
        }

        // الأقساط
        List<(int Seq, DateTime Due, double Amount)> plan = null;
        if (pay == "أقساط")
        {
            if (net - paid <= 0) { Ui.Warn("لا يوجد مبلغ متبقٍ للتقسيط."); return; }
            using var dlg = new InstallmentDialog(net - paid, guarantorId);
            if (dlg.ShowModal() != DialogResult.OK) return;
            plan = dlg.Plan;
            guarantorId = dlg.GuarantorId;
        }

        long inv;
        bool editing = editId > 0;
        using (var tx = new Tx())
        {
            try
            {
                if (editing) InvoiceOps.Remove(tx, editId);

                inv = tx.Insert(@"INSERT INTO invoices(id,type,date,party_id,warehouse_id,cashbox_id,cost_center_id,price_level,pay_type,total,discount,
                        delivery_id,delivery_fee,delivery_status,net,paid,notes,user_id)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17)",
                    Db.N(editId), type, dtDate.Value.ToString(Ui.DtFmt), Db.N(party), wh, Db.N(box), Db.N(cc), level, pay, total, disc,
                    Db.N(del), fee, del > 0 ? "قيد التوصيل" : null, net, paid, txtNotes.Text.Trim(), Session.UserId);

                const string insLine = "INSERT INTO invoice_lines(invoice_id,item_id,batch_id,length,width,qty,price,cost,expiry) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)";
                foreach (var l in lines)
                {
                    var ir = ItemRow(l.ItemId);
                    if (ir != null && Db.S(ir["item_type"]) == "خدمية")
                    {
                        // مادة خدمية: لا مخزون ولا وجبات
                        tx.Exec(insLine, inv, l.ItemId, null, l.Len, l.Wid, l.Qty, l.Price, 0.0, null);
                    }
                    else if (IsOut)
                    {
                        // «معدل الكلفة»: كلفة موحدة للمادة؛ وإلا كلفة كل وجبة كما هي
                        double avg = ir != null && Db.S(ir["cost_method"]) == "معدل الكلفة" ? StockOps.AvgCost(tx, l.ItemId) : -1;
                        // FEFO: الأقرب انتهاءً أولًا، ثم الوجبات بلا تاريخ
                        foreach (var t in StockOps.TakeFefo(tx, l.ItemId, wh, l.Qty))
                        {
                            double c = avg >= 0 ? avg : t.Cost;
                            tx.Exec(insLine, inv, l.ItemId, t.BatchId, l.Len, l.Wid, t.Qty, CostDoc ? c : l.Price, c, t.Expiry);
                        }
                    }
                    else
                    {
                        double cost = type is "Purchase" or "StockIn" ? l.Price : StockOps.AvgCost(tx, l.ItemId);
                        long bid = tx.Insert("INSERT INTO batches(item_id,warehouse_id,expiry,qty,cost,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                            l.ItemId, wh, l.Expiry == "" ? null : l.Expiry, l.Qty, cost, Ui.Now);
                        tx.Exec(insLine, inv, l.ItemId, bid, l.Len, l.Wid, l.Qty, l.Price, cost, l.Expiry == "" ? null : l.Expiry);
                    }
                }

                if (paid > 0)
                {
                    double sign = type is "Sale" or "PurchaseReturn" ? 1 : -1;
                    double rate = Ui.BoxRate(box);
                    tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,cost_center_id,invoice_id,note,user_id)
                              VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                        dtDate.Value.ToString(Ui.DtFmt), "دفعة فاتورة", box, sign * paid / rate, rate, Db.N(party), Db.N(cc), inv,
                        $"{Ui.DocTitle(type)} رقم {inv}", Session.UserId);
                }

                // الأرقام التسلسلية: البيع يحجزها لهذه الفاتورة، وإرجاع البيع يعيدها متوفرة
                foreach (var l in lines.Where(x => x.Serials != ""))
                    foreach (var sn in l.Serials.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        tx.Exec("UPDATE item_serials SET invoice_id=@p0 WHERE item_id=@p1 AND serial=@p2",
                            IsOut ? (object)inv : null, l.ItemId, sn);

                if (plan != null)
                {
                    foreach (var p in plan)
                        tx.Exec("INSERT INTO installments(invoice_id,party_id,seq,due_date,amount) VALUES(@p0,@p1,@p2,@p3,@p4)",
                            inv, party, p.Seq, p.Due.ToString(Ui.DFmt), p.Amount);
                    tx.Exec("UPDATE invoices SET guarantor_id=@p0 WHERE id=@p1", Db.N(guarantorId), inv);
                }

                tx.Commit();
            }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return; }
        }
        if (editing) Db.Audit("تعديل فاتورة", $"{Ui.DocTitle(type)} رقم {inv} — الصافي {Ui.M(net)}");
        lastId = inv;
        editId = 0;
        Text = Ui.DocTitle(type);

        string pmode = Settings.Get("print_after_save", "2");
        bool told = false;
        if (Session.Can("print") && pmode != "0")
        {
            told = pmode == "2";
            if (pmode == "1" || Ui.Confirm($"تم حفظ {Ui.DocTitle(type)} رقم {inv}. هل تريد {(Ui.IsStockDoc(type) ? "طباعته" : "طباعتها")}؟")) InvoiceOps.BuildPrint(inv)?.Print();
        }

        var pr = Ui.GetRow(cbParty);
        string phone = pr == null ? "" : Db.S(pr["phone"]);
        if (type == "Sale" && phone != "" && Ui.Confirm($"الفاتورة رقم {inv} محفوظة.\nهل تريد إرسالها للعميل عبر واتساب؟"))
        {
            var msg = $"{Settings.Get("shop_name")}\nفاتورة رقم {inv} — {dtDate.Value:yyyy/MM/dd}\n" +
                      string.Join("\n", lines.Select(l => $"• {l.Name} × {Ui.M(l.Qty)} = {Ui.M(l.Qty * l.Price)}")) +
                      $"\nالصافي: {Ui.M(net)}\nالمدفوع: {Ui.M(paid)}\nرصيدكم الحالي: {Ui.M(Ui.PartyBalance(party))}";
            _ = WhatsApp.Send(phone, msg);
        }
        else if (!told) Ui.Info($"تم حفظ {Ui.DocTitle(type)} رقم {inv} بنجاح.");
        Reset();
    }

    void Reset()
    {
        guarantorId = 0;
        grid.Rows.Clear();
        nDisc.Value = 0; nFee.Value = 0; nPaid.Value = 0;
        if (cbDel.Items.Count > 0) cbDel.SelectedIndex = 0;
        txtNotes.Clear();
        dtDate.Value = DateTime.Now;
        LoadItems();
        PartyChanged();
        Totals();
        lblInfo.Text = "";
        txtFind.Focus();
    }
}

/// <summary>نافذة تقسيط المبلغ المتبقي</summary>
public class InstallmentDialog : DialogShell
{
    public List<(int Seq, DateTime Due, double Amount)> Plan = new();
    public long GuarantorId { get; private set; }

    public InstallmentDialog(double remaining, long guarantor = 0) : base("تقسيط المبلغ المتبقي", 460, 470, "calendar-clock")
    {
        var cbG = Ui.Combo(340);
        void FillG() => Ui.FillCombo(cbG, "SELECT id,name,phone FROM guarantors ORDER BY name", true, "— بدون كفيل —");
        FillG();
        Ui.SelectId(cbG, guarantor);
        var addG = new ModernButton { Kind = BtnKind.Success, IconName = "circle-plus", Size = new Size(40, 40), Margin = new Padding(4, 27, 4, 0) };
        new ToolTip().SetToolTip(addG, "إضافة كفيل جديد");
        addG.Click += (s, e) => { long nid = QuickAdd.Ask("كفيل جديد", "اسم الكفيل", "guarantors"); if (nid > 0) { FillG(); Ui.SelectId(cbG, nid); } };
        var nCount = Ui.Num(190); nCount.Minimum = 1; nCount.Maximum = 120; nCount.Value = 6;
        var nEvery = Ui.Num(190); nEvery.Minimum = 1; nEvery.Maximum = 12; nEvery.Value = 1;
        var dFirst = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddMonths(1) };
        var lbl = new Label { Width = 400, Height = 60, ForeColor = Theme.BrandDark, BackColor = Theme.BrandSoft, Font = Theme.FS(10), Padding = new Padding(10, 6, 10, 6), Margin = new Padding(6, 10, 6, 0) };
        void Upd() => lbl.Text = $"المبلغ المتبقي: {Ui.M(remaining)}\nقيمة القسط تقريبًا: {Ui.M(Math.Floor(remaining / (double)nCount.Value))}";
        nCount.ValueChanged += (s, e) => Upd();
        Upd();

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        flow.Controls.Add(Ui.Labeled("عدد الأقساط", nCount));
        flow.Controls.Add(Ui.Labeled("كل (شهر)", nEvery));
        flow.Controls.Add(Ui.Labeled("تاريخ أول قسط", dFirst));
        flow.Controls.Add(Ui.Labeled("الكفيل", cbG));
        flow.Controls.Add(addG);
        flow.Controls.Add(lbl);
        Body.Controls.Add(flow);

        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        var ok = AddButton("اعتماد الأقساط", DialogResult.None, BtnKind.Primary, "check");
        AcceptButton = ok;
        ok.Click += (s, e) =>
        {
            int c = (int)nCount.Value, every = (int)nEvery.Value;
            double each = Math.Floor(remaining / c);
            Plan.Clear();
            for (int i = 1; i <= c; i++)
                Plan.Add((i, dFirst.Value.Date.AddMonths((i - 1) * every), i == c ? remaining - each * (c - 1) : each));
            GuarantorId = Ui.GetId(cbG);
            DialogResult = DialogResult.OK;
        };
    }
}
