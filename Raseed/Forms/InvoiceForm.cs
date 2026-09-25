using System.Data;

namespace Raseed;

/// <summary>
/// القائمة الموحّدة: بيع / شراء / إرجاع بيع / إرجاع شراء / عرض سعر / إتلاف / سندات المخزن.
/// الترتيب المألوف: الحساب وعنوانه وهاتفه في الأعلى، ثم سطر إدخال المادة (المادة، العدد، السعر، المجموع)،
/// ثم الجدول (ت، المادة، العدد، السعر، المجموع، الرقم التسلسلي، الملاحظة، حذف)،
/// وفي الأسفل: الخصم، الواصل، الباقي، الرصيد السابق والحالي، وخيارا «طباعة» و«تقسيط القائمة».
/// - صرف المخزون بطريقة FEFO (الأقرب انتهاءً يُصرف أولًا) مع تتبع الوجبات وتواريخ الصلاحية
/// - ثلاثة أسعار (مفرد، جملة، خاص) — البيع بالقياس — سقف الذمة — الأقساط — التوصيل — مراكز الكلفة
/// </summary>
public class InvoiceForm : BaseForm
{
    readonly string type;
    readonly ComboBox cbParty = Ui.Combo(250), cbWh = Ui.Combo(170), cbLevel = Ui.Combo(110),
                      cbBox = Ui.Combo(200), cbCC = Ui.Combo(150), cbDel = Ui.Combo(170);
    readonly DateTimePicker dtDate = new() { Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" };
    readonly NumericUpDown nDisc = Ui.Num(130), nPaid = Ui.Num(150), nFee = Ui.Num(100), nQtyIn = Ui.Num(90, 2), nPriceIn = Ui.Num(130, 2);
    readonly TextBox txtFind = new() { Width = 340, PlaceholderText = "الباركود أو اسم المادة ثم Enter" }, txtNotes = new() { Width = 220 },
                     tNo = new() { Width = 90, ReadOnly = true, TextAlign = HorizontalAlignment.Center },
                     tAddress = new() { Width = 180, ReadOnly = true }, tPhone = new() { Width = 140, ReadOnly = true },
                     tSumIn = new() { Width = 130, ReadOnly = true, TextAlign = HorizontalAlignment.Center };
    readonly Toggle chkPrint = new() { Text = "طباعة", Width = 110, Height = 34 }, chkInst = new() { Text = "تقسيط القائمة", Width = 190, Height = 34 };
    readonly DataGridView grid = Ui.NewGrid(false);
    readonly StatLabel lblTotal = new(), lblNet = new() { ValueColor = Theme.Brand, ValueSize = 17 }, lblRemain = new() { ValueColor = Theme.Danger },
                       lblPrev = new(), lblAfter = new() { ValueColor = Theme.BrandDark };
    readonly Label lblInfo = new() { Dock = DockStyle.Fill, Padding = new Padding(2), Font = Theme.F(10), ForeColor = Theme.Text2 };
    DataTable items;
    bool busy, paidTouched, settingFind;
    long editId, lastId, guarantorId;
    double oldEffect;     // أثر القائمة القديمة على رصيد الحساب (عند التعديل)
    double prevBalance;   // رصيد الحساب قبل هذه القائمة
    DataRow pending;      // مادة اختيرت بالاسم وتنتظر العدد والسعر

    bool IsOut => InvoiceOps.IsOut(type);
    bool UsesExpiry => type is "Purchase" or "SaleReturn" or "StockIn";
    bool SaleSide => type is "Sale" or "SaleReturn" or "Quote";
    // سندات المخزن والإتلاف: بلا حساب ولا دفع، والسعر فيها هو الكلفة
    bool HasParty => type is not ("Damage" or "StockIn" or "StockOut");
    bool HasPayment => Ui.HasPayment(type);
    bool CostDoc => !HasParty;
    bool IsQuote => type == "Quote";
    // أثر الباقي على رصيد الحساب: البيع وإرجاع الشراء يزيدان ما عليه لنا
    double Sign => type is "Sale" or "PurchaseReturn" ? 1 : -1;
    DataTable serials;   // الأرقام التسلسلية (IMEI) للبحث بالمسح
    Dictionary<string, long> barcodeMap = new();   // كل باركودات المواد (الباركودات المتعددة)

    record Line(long ItemId, string Name, double Len, double Wid, double Qty, double Price, string Expiry, string Serials, string Note);

    static Control Stat(string caption, StatLabel value, int width = 150)
    {
        value.Caption = caption;
        value.Size = new Size(width, 64);
        value.Margin = new Padding(8, 2, 8, 2);
        return value;
    }

    string PriceCaption => CostDoc ? "الكلفة" : type is "Purchase" or "PurchaseReturn" ? "سعر الشراء" : "سعر البيع";

    public InvoiceForm(string invoiceType, long editInvoiceId = 0)
    {
        type = invoiceType;
        editId = editInvoiceId;
        Text = Ui.DocTitle(type);
        KeyPreview = true;

        // ---------- الترويسة: الحساب وعنوانه وهاتفه، المخزن، نوع السعر، التاريخ ورقم القائمة ----------
        var head = Theme.Bar();
        if (HasParty)
        {
            string kind = SaleSide ? "عميل" : "مورد";
            Ui.FillCombo(cbParty, "SELECT id,name,price_level,phone,credit_limit,address FROM parties WHERE kind=@p0 OR kind='عميل ومورد' ORDER BY name",
                true, SaleSide ? "زبون نقدي" : "— بدون مورد —", kind);
            Ui.MakeSearchable(cbParty);
            head.Controls.Add(Ui.Labeled("الحساب", cbParty));
            head.Controls.Add(Ui.Labeled("العنوان", tAddress));
            head.Controls.Add(Ui.Labeled("الهاتف", tPhone));
        }
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id");
        Ui.SelectId(cbWh, Ui.DefaultWarehouse());
        head.Controls.Add(Ui.Labeled("المخزن", cbWh));
        if (SaleSide)
        {
            cbLevel.Items.AddRange(new object[] { "مفرد", "جملة", "خاص" });
            cbLevel.SelectedIndex = 0;
            if (Features.On("feat_three_prices")) head.Controls.Add(Ui.Labeled("نوع السعر", cbLevel));
        }
        if (HasPayment)
        {
            Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
            head.Controls.Add(Ui.Labeled("الصندوق", cbBox));
        }
        Ui.FillCombo(cbCC, "SELECT id,name FROM cost_centers ORDER BY id", true);
        if (!IsQuote) head.Controls.Add(Ui.Labeled("مركز الكلفة", cbCC));
        if (type == "Sale")
        {
            Ui.FillCombo(cbDel, "SELECT id,name,fee FROM delivery_companies ORDER BY name", true, "— بدون توصيل —");
            head.Controls.Add(Ui.Labeled("شركة التوصيل", cbDel));
            head.Controls.Add(Ui.Labeled("أجور التوصيل", nFee));
            cbDel.SelectedIndexChanged += (s, e) => { var r = Ui.GetRow(cbDel); nFee.Value = r == null ? 0 : (decimal)Db.D(r["fee"]); };
        }
        head.Controls.Add(Ui.Labeled("ملاحظات", txtNotes));
        dtDate.Value = DateTime.Now;
        head.Controls.Add(Ui.Labeled("التأريخ", dtDate));
        head.Controls.Add(Ui.Labeled(Ui.IsStockDoc(type) ? "رقم السند" : "رقم القائمة", tNo));

        // ---------- سطر إدخال المادة ----------
        var entry = Theme.Bar();
        var findBox = new InputBox(txtFind, 350, "scan-barcode");
        entry.Controls.Add(Ui.Labeled("المادة / الباركود", findBox));
        nQtyIn.Minimum = 0; nQtyIn.Value = 1;
        entry.Controls.Add(Ui.Labeled("العدد", nQtyIn));
        entry.Controls.Add(Ui.Labeled(PriceCaption, nPriceIn));
        entry.Controls.Add(Ui.Labeled("المجموع", tSumIn));
        var bAdd = new ModernButton { Text = "إضافة", IconName = "plus", Kind = BtnKind.Success, Height = 42, Margin = new Padding(6, 27, 4, 4) };
        bAdd.FitWidth(100);
        entry.Controls.Add(bAdd);
        bAdd.Click += (s, e) => { if (pending != null) AddPending(); else FindAndAdd(); };
        txtFind.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindAndAdd(); } };
        txtFind.TextChanged += (s, e) => { if (!settingFind) pending = null; };
        nQtyIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; nPriceIn.Focus(); nPriceIn.Select(0, nPriceIn.Text.Length); } };
        nPriceIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (pending != null) AddPending(); else txtFind.Focus(); } };
        nQtyIn.ValueChanged += (s, e) => UpdateSumIn();
        nPriceIn.ValueChanged += (s, e) => UpdateSumIn();
        nPriceIn.Enabled = Session.Can("edit_price") && type is not ("Damage" or "StockOut");

        // ---------- جدول الأسطر ----------
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        AddCol("no", "ت", true, 28);
        AddCol("item_id", "", true, 10, false);
        AddCol("measure", "", true, 10, false);
        AddCol("code", "الرمز", true, 55);
        AddCol("name", "المادة", true, 200);
        AddCol("unit", "الوحدة", true, 50);
        AddCol("len", "الطول", false, 55, false);
        AddCol("wid", "العرض", false, 55, false);
        AddCol("qty", "العدد", false, 60);
        AddCol("price", PriceCaption, !Session.Can("edit_price") || type is "Damage" or "StockOut", 75);
        AddCol("expiry", "الصلاحية (yyyy-mm-dd)", false, 95, UsesExpiry);
        AddCol("total", "المجموع", true, 85);
        AddCol("serials", "الرقم التسلسلي", true, 110, type is "Sale" or "SaleReturn" && Features.On("feat_serials"));   // الأرقام الممسوحة لهذا السطر
        AddCol("note", "الملاحظة", false, 110);
        grid.Columns["note"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "del", HeaderText = "حذف", Text = "حذف", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, FillWeight = 40, MinimumWidth = 64,
            DefaultCellStyle = { BackColor = Theme.DangerSoft, ForeColor = Theme.Danger, SelectionBackColor = Theme.DangerSoft, SelectionForeColor = Theme.Danger }
        });
        grid.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "del") return;
            grid.Rows.RemoveAt(e.RowIndex);
            Totals();
        };
        grid.RowsAdded += (s, e) => Renumber();
        grid.RowsRemoved += (s, e) => Renumber();
        grid.CellEndEdit += (s, e) => { RecalcRow(e.RowIndex); Totals(); };
        grid.SelectionChanged += (s, e) => { if (grid.CurrentRow != null) ShowInfo(ItemRow(Db.L(grid.CurrentRow.Cells["item_id"].Value)), false); };
        grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && grid.CurrentRow != null && !grid.IsCurrentCellInEditMode) { grid.Rows.Remove(grid.CurrentRow); Totals(); e.Handled = true; } };

        // ---------- لوحة معلومات المادة ----------
        var info = new CardPanel { Dock = DockStyle.Right, Width = 270, Title = "معلومات المادة", IconName = "info" };
        info.Controls.Add(lblInfo);

        // ---------- التذييل ----------
        var footCard = new CardPanel { Dock = DockStyle.Bottom, Height = 164, Padding = new Padding(14, 10, 14, 10) };
        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 78, WrapContents = false, BackColor = Theme.Surface };
        stats.Controls.Add(Stat("مجموع القائمة", lblTotal, 140));
        if (HasParty)
        {
            nDisc.Enabled = Session.Can("discount");
            stats.Controls.Add(Ui.Labeled("الخصم", nDisc));
            stats.Controls.Add(Stat("المجموع بعد الخصم", lblNet, 160));
        }
        if (HasPayment)
        {
            stats.Controls.Add(Ui.Labeled("الواصل", nPaid));
            stats.Controls.Add(Stat("الباقي", lblRemain, 130));
            stats.Controls.Add(Stat("الرصيد السابق", lblPrev, 140));
            stats.Controls.Add(Stat("الرصيد الحالي", lblAfter, 140));
        }
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Theme.Surface };
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 12, 0, 0) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
        bottom.Controls.Add(toggles);
        bottom.Controls.Add(actions);
        footCard.Controls.Add(bottom);
        footCard.Controls.Add(stats);

        chkPrint.Checked = Session.Can("print") && Settings.Get("print_after_save", "2") == "1";
        chkPrint.Visible = Session.Can("print");
        toggles.Controls.Add(chkPrint);
        if (type == "Sale" && Features.On("feat_installments")) toggles.Controls.Add(chkInst);

        bool stockDoc = Ui.IsStockDoc(type);
        var bNew = new ModernButton { Text = "جديد", IconName = "plus", Height = 44, Margin = new Padding(0, 0, 8, 0) }; bNew.FitWidth(120);
        var bSave = new ModernButton { Text = "حفظ", IconName = "save", Height = 44, Margin = new Padding(0, 0, 8, 0) }; bSave.FitWidth(130);
        var bDel = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 44, Margin = new Padding(0, 0, 8, 0) }; bDel.FitWidth(110);
        var bPrint = new ModernButton { Text = stockDoc ? "طباعة آخر سند" : "طباعة آخر قائمة", IconName = "printer", Kind = BtnKind.Secondary, Height = 44, Margin = new Padding(0, 0, 8, 0) }; bPrint.FitWidth(150);
        // RightToLeft: الأول يظهر في أقصى اليسار
        if (Session.Can("print")) actions.Controls.Add(bPrint);
        if (IsQuote)
        {
            var bConvert = new ModernButton { Text = "تحويل إلى قائمة بيع", IconName = "repeat", Kind = BtnKind.Accent, Height = 44, Margin = new Padding(0, 0, 8, 0) };
            bConvert.FitWidth(170);
            bConvert.Click += (s, e) => ConvertToSale();
            actions.Controls.Add(bConvert);
        }
        actions.Controls.Add(bDel);
        actions.Controls.Add(bSave);
        actions.Controls.Add(bNew);
        bSave.Click += (s, e) => Save();
        bNew.Click += (s, e) => { if (ConfirmClose()) { editId = 0; Reset(); } };
        bDel.Click += (s, e) => DeleteCurrent();
        bPrint.Click += (s, e) => { if (lastId > 0) InvoiceOps.BuildPrint(lastId)?.Print(); else Ui.Warn("لم تُحفظ أي قائمة بعد في هذه الشاشة."); };

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(info);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 12 });
        Controls.Add(footCard);
        Controls.Add(entry);
        Controls.Add(head);

        // ---------- الأحداث ----------
        nDisc.ValueChanged += (s, e) => Totals();
        nFee.ValueChanged += (s, e) => Totals();
        nPaid.ValueChanged += (s, e) => { if (!busy) { paidTouched = true; Totals(); } };
        // سعر الأقساط يختلف عن السعر النقدي إن وُجد
        chkInst.CheckedChanged += (s, e) => { if (!busy) { paidTouched = false; Reprice(); } };
        cbParty.SelectedIndexChanged += (s, e) => PartyChanged();
        cbLevel.SelectedIndexChanged += (s, e) => Reprice();
        cbWh.SelectedIndexChanged += (s, e) => { if (grid.CurrentRow != null) ShowInfo(ItemRow(Db.L(grid.CurrentRow.Cells["item_id"].Value)), false); };
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F2) { txtFind.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.F10) { Save(); e.Handled = true; }
        };

        LoadItems();
        lblInfo.Text = "اكتب اسم المادة أو امسح الباركود في «المادة / الباركود».\n\nالباركود يُضاف مباشرة، والاسم ينقلك إلى العدد ثم السعر (Enter).";
        ShowNumber();
        PartyChanged();
        Totals();
        if (editId > 0) LoadInvoice(editId);
        Shown += (s, e) => txtFind.Focus();
    }

    void AddCol(string name, string header, bool readOnly, int weight, bool visible = true)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = header, ReadOnly = readOnly, Visible = visible, FillWeight = weight,
            MinimumWidth = Math.Max(name == "no" ? 40 : 60, TextRenderer.MeasureText(header, Theme.FS(9.5f)).Width + 26),
            DefaultCellStyle = { Format = "#,0.##", BackColor = readOnly ? Theme.SurfaceAlt : Theme.Surface, Alignment = name is "name" ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleCenter }
        });
    }

    void Renumber()
    {
        foreach (DataGridViewRow r in grid.Rows) r.Cells["no"].Value = r.Index + 1;
    }

    void ShowNumber() => tNo.Text = (editId > 0 ? editId : Db.L(Db.Scalar("SELECT IFNULL(MAX(id),0)+1 FROM invoices"))).ToString();

    // مواد جديدة قد تُعرَّف في تبويب آخر أثناء بقاء القائمة مفتوحة
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
        // عمودا الطول والعرض يظهران فقط إن وُجدت مواد تُباع بالقياس
        bool anyMeasure = items.Rows.Cast<DataRow>().Any(r => Db.L(r["by_measure"]) == 1);
        grid.Columns["len"].Visible = grid.Columns["wid"].Visible = anyMeasure;
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

    double EntryQty => nQtyIn.Value > 0 ? (double)nQtyIn.Value : 1;

    void ClearEntry()
    {
        settingFind = true;
        txtFind.Clear();
        settingFind = false;
        pending = null;
        nQtyIn.Value = 1;
        nPriceIn.Value = 0;
        tSumIn.Text = "";
        txtFind.Focus();
    }

    void UpdateSumIn() => tSumIn.Text = pending == null ? "" : Ui.M((double)nQtyIn.Value * (double)nPriceIn.Value);

    /// <summary>
    /// الباركود (أو الرقم التسلسلي أو باركود الميزان أو الرمز) يُضاف مباشرة بالعدد المكتوب؛
    /// الاسم يختار المادة وينقل المؤشر إلى العدد ثم السعر، وEnter الأخير يضيفها
    /// </summary>
    void FindAndAdd()
    {
        var t = txtFind.Text.Trim();
        if (t == "") { if (pending != null) AddPending(); return; }
        if (pending != null && t == Db.S(pending["name"])) { nQtyIn.Focus(); nQtyIn.Select(0, nQtyIn.Text.Length); return; }
        var rows = items.Rows.Cast<DataRow>();
        // مسح رقم تسلسلي (IMEI): يضيف الجهاز نفسه. البيع يتطلب رقمًا متوفرًا، وإرجاع البيع رقمًا مُباعًا
        var sr = serials?.Rows.Cast<DataRow>().FirstOrDefault(x => string.Equals(Db.S(x["serial"]), t, StringComparison.OrdinalIgnoreCase));
        if (sr != null && (IsOut || type == "SaleReturn"))
        {
            bool sold = Db.L(sr["invoice_id"]) > 0;
            if (IsOut && sold) { Ui.Warn($"الرقم التسلسلي {t} مُباع مسبقًا."); return; }
            if (type == "SaleReturn" && !sold) { Ui.Warn($"الرقم التسلسلي {t} غير مُباع، لا يمكن إرجاعه."); return; }
            if (grid.Rows.Cast<DataGridViewRow>().Any(g => (Convert.ToString(g.Cells["serials"].Value) ?? "").Split(',').Contains(Db.S(sr["serial"]))))
            { Ui.Warn($"الرقم التسلسلي {t} موجود في القائمة."); return; }
            var ir = ItemRow(Db.L(sr["item_id"]));
            if (ir != null) { AddItem(ir, Db.S(sr["serial"]), 1); ClearEntry(); return; }
        }
        var exact = rows.FirstOrDefault(x => Db.S(x["barcode"]) == t)
             ?? (barcodeMap.TryGetValue(t, out var bid) ? ItemRow(bid) : null)
             ?? rows.FirstOrDefault(x => Db.S(x["code"]) == t);
        if (exact != null) { AddItem(exact, null, EntryQty); ClearEntry(); return; }
        // باركود الميزان: رمز المادة + الوزن
        if (ScaleCode.TryParse(t, out long plu, out double weight))
        {
            var sr2 = rows.FirstOrDefault(x => Db.L(x["use_scale"]) == 1 && long.TryParse(Db.S(x["code"]), out var c) && c == plu);
            if (sr2 != null)
            {
                var g = AddLine(sr2, 0.0, 0.0, weight, PriceFor(sr2), "");
                Totals();
                grid.CurrentCell = g.Cells["qty"];
                ShowInfo(sr2, true);
                ClearEntry();
                return;
            }
        }
        var r = rows.FirstOrDefault(x => Db.S(x["name"]) == t)
             ?? rows.FirstOrDefault(x => Db.S(x["name"]).Contains(t, StringComparison.OrdinalIgnoreCase));
        if (r == null) { Ui.Warn("لم يتم العثور على المادة: " + t); return; }
        // اختيار بالاسم: العدد ثم السعر ثم الإضافة
        pending = r;
        settingFind = true;
        txtFind.Text = Db.S(r["name"]);
        settingFind = false;
        if (nQtyIn.Value <= 0) nQtyIn.Value = 1;
        nPriceIn.Value = (decimal)Math.Max(0, PriceFor(r));
        UpdateSumIn();
        ShowInfo(r, true);
        nQtyIn.Focus();
        nQtyIn.Select(0, nQtyIn.Text.Length);
    }

    void AddPending()
    {
        if (pending == null) return;
        AddItem(pending, null, EntryQty, (double)nPriceIn.Value);
        ClearEntry();
    }

    void AddItem(DataRow r, string serial, double qty, double? price = null)
    {
        long id = Db.L(r["id"]);
        bool measure = Db.L(r["by_measure"]) == 1;
        double p = price ?? PriceFor(r);
        if (!measure && !UsesExpiry)
            foreach (DataGridViewRow gr in grid.Rows)
                if (Db.L(gr.Cells["item_id"].Value) == id && Math.Abs(Ui.V(gr.Cells["price"].Value) - p) < 0.001)
                {
                    gr.Cells["qty"].Value = Ui.V(gr.Cells["qty"].Value) + qty;
                    if (serial != null) AppendSerial(gr, serial);
                    RecalcRow(gr.Index); Totals(); ShowInfo(r, false);
                    return;
                }

        var g = AddLine(r, measure ? 1.0 : 0.0, 0.0, measure ? 1.0 : qty, p, "");
        if (serial != null) AppendSerial(g, serial);
        Totals();
        grid.CurrentCell = measure ? g.Cells["len"] : g.Cells["qty"];
        ShowInfo(r, price == null);
    }

    static void AppendSerial(DataGridViewRow g, string serial)
    {
        var cur = Convert.ToString(g.Cells["serials"].Value) ?? "";
        g.Cells["serials"].Value = cur == "" ? serial : cur + "," + serial;
        g.Cells["name"].ToolTipText = "الأرقام التسلسلية: " + g.Cells["serials"].Value;
    }

    DataGridViewRow AddLine(DataRow r, double len, double wid, double qty, double price, string expiry, string note = "")
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
        g.Cells["note"].Value = note ?? "";
        if (!measure) { g.Cells["len"].ReadOnly = true; g.Cells["wid"].ReadOnly = true; }
        else g.Cells["qty"].ReadOnly = true;
        RecalcRow(i);
        return g;
    }

    /// <summary>تحميل قائمة محفوظة للتعديل</summary>
    void LoadInvoice(long id)
    {
        var dt = Db.Query("SELECT * FROM invoices WHERE id=@p0", id);
        if (dt.Rows.Count == 0) { editId = 0; return; }
        var v = dt.Rows[0];
        Text = $"تعديل {Ui.DocTitle(type)} رقم {id}";
        busy = true;
        if (HasParty) Ui.SelectId(cbParty, Db.L(v["party_id"]));
        Ui.SelectId(cbWh, Db.L(v["warehouse_id"]));
        guarantorId = Db.L(v["guarantor_id"]);
        if (SaleSide) { int li = cbLevel.Items.IndexOf(Db.S(v["price_level"])); if (li >= 0) cbLevel.SelectedIndex = li; }
        chkInst.Checked = Db.S(v["pay_type"]) == "أقساط";
        if (HasPayment && Db.L(v["cashbox_id"]) > 0) Ui.SelectId(cbBox, Db.L(v["cashbox_id"]));
        Ui.SelectId(cbCC, Db.L(v["cost_center_id"]));
        if (type == "Sale") { Ui.SelectId(cbDel, Db.L(v["delivery_id"])); nFee.Value = (decimal)Db.D(v["delivery_fee"]); }
        if (DateTime.TryParse(Db.S(v["date"]), out var d)) dtDate.Value = d;
        txtNotes.Text = Db.S(v["notes"]);
        busy = false;
        AddLinesFrom(id, keepPrices: true);
        nDisc.Value = (decimal)Db.D(v["discount"]);
        oldEffect = HasPayment ? Sign * (Db.D(v["net"]) - Db.D(v["paid"])) : 0;
        paidTouched = true;
        busy = true; nPaid.Value = (decimal)Db.D(v["paid"]); busy = false;
        ShowNumber();
        PartyChanged(keepLevel: true);   // لا نغيّر مستوى السعر المحفوظ حتى لا يُعاد تسعير الأسطر
    }

    /// <summary>أسطر قائمة محفوظة (للتعديل، أو لتحويل عرض سعر إلى قائمة بيع)</summary>
    void AddLinesFrom(long id, bool keepPrices)
    {
        var srcType = Db.S(Db.Scalar("SELECT type FROM invoices WHERE id=@p0", id));
        var lines = InvoiceOps.IsOut(srcType)
            ? Db.Query(@"SELECT item_id, length, width, SUM(qty) AS qty, price, '' AS expiry, MAX(IFNULL(note,'')) AS note FROM invoice_lines WHERE invoice_id=@p0
                         GROUP BY item_id, price, length, width ORDER BY MIN(id)", id)
            : Db.Query("SELECT item_id, length, width, qty, price, IFNULL(expiry,'') AS expiry, IFNULL(note,'') AS note FROM invoice_lines WHERE invoice_id=@p0 ORDER BY id", id);
        foreach (DataRow l in lines.Rows)
        {
            var r = ItemRow(Db.L(l["item_id"]));
            if (r == null) continue;
            double len = Db.D(l["length"]), wid = Db.D(l["width"]), qty = Db.D(l["qty"]);
            if (Db.L(r["by_measure"]) == 1 && Math.Abs(len * (wid > 0 ? wid : 1) - qty) > 1e-6) { len = qty; wid = 0; }
            var g = AddLine(r, len, wid, qty, keepPrices ? Db.D(l["price"]) : PriceFor(r), keepPrices ? Db.S(l["expiry"]) : "", Db.S(l["note"]));
            // الأرقام التسلسلية المباعة بهذه القائمة تُربط بأول سطر للمادة
            if (keepPrices && IsOut && !grid.Rows.Cast<DataGridViewRow>().Any(x => x != g && Db.L(x.Cells["item_id"].Value) == Db.L(r["id"])))
                foreach (DataRow sr in Db.Query("SELECT serial FROM item_serials WHERE invoice_id=@p0 AND item_id=@p1", id, Db.L(r["id"])).Rows)
                    AppendSerial(g, Db.S(sr["serial"]));
        }
        Totals();
    }

    /// <summary>قائمة بيع جديدة من عرض سعر محفوظ (الحساب ونوع السعر والأسطر بأسعار العرض)</summary>
    public static InvoiceForm SaleFromQuote(long quoteId)
    {
        var f = new InvoiceForm("Sale");
        var v = Db.Query("SELECT party_id, price_level, discount, notes FROM invoices WHERE id=@p0", quoteId);
        if (v.Rows.Count == 0) return f;
        f.busy = true;
        Ui.SelectId(f.cbParty, Db.L(v.Rows[0]["party_id"]));
        int li = f.cbLevel.Items.IndexOf(Db.S(v.Rows[0]["price_level"])); if (li >= 0) f.cbLevel.SelectedIndex = li;
        f.busy = false;
        f.AddLinesFrom(quoteId, keepPrices: true);
        f.nDisc.Value = (decimal)Db.D(v.Rows[0]["discount"]);
        f.txtNotes.Text = $"من عرض السعر رقم {quoteId}";
        f.PartyChanged(keepLevel: true);
        f.Totals();
        return f;
    }

    void ConvertToSale()
    {
        if (!Session.Guard("sales")) return;
        if (grid.Rows.Count == 0) { Ui.Warn("عرض السعر فارغ."); return; }
        // العرض يُحفظ أولًا (أو تُحفظ تعديلاته) ليبقى مرجعًا، ثم يُفتح في قائمة بيع جديدة
        if (!Ui.Confirm("سيُحفظ عرض السعر ثم يُفتح في قائمة بيع جديدة. متابعة؟")) return;
        if (!SaveCore(out long id)) return;
        Reset();
        MainForm.Instance?.Open($"قائمة بيع — من عرض {id}", SaleFromQuote(id));
    }

    double PriceFor(DataRow r)
    {
        long id = Db.L(r["id"]);
        switch (type)
        {
            case "Sale":
            case "SaleReturn":
            case "Quote":
                {
                    var col = Convert.ToString(cbLevel.SelectedItem) switch { "جملة" => "price_wholesale", "خاص" => "price_special", _ => "price_retail" };
                    double p = Db.D(r[col]);
                    if (type == "Sale" && chkInst.Checked && Db.D(r["price_installment"]) > 0) p = Db.D(r["price_installment"]);
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

    void Totals()
    {
        bool was = busy;
        busy = true;
        lblTotal.Value = Ui.M(Total);
        lblNet.Value = Ui.M(Net);
        if (HasPayment)
        {
            // الواصل = كامل المبلغ ما لم يغيّره المستخدم (والمقدّم صفر عند التقسيط)
            if (!paidTouched) nPaid.Value = chkInst.Checked ? 0 : (decimal)Math.Max(0, Net);
            double remain = Net - (double)nPaid.Value;
            lblRemain.Value = Ui.M(remain);
            lblRemain.ValueColor = remain > 0.005 ? Theme.Danger : remain < -0.005 ? Theme.Warning : Theme.Success;
            lblPrev.Value = Ui.M(prevBalance);
            lblAfter.Value = Ui.M(prevBalance + Sign * remain);
        }
        busy = was;
    }

    void PartyChanged(bool keepLevel = false)
    {
        var r = Ui.GetRow(cbParty);
        tAddress.Text = r == null ? "" : Db.S(r["address"]);
        tPhone.Text = r == null ? "" : Db.S(r["phone"]);
        prevBalance = r == null ? 0 : Ui.PartyBalance(Db.L(r["id"])) - oldEffect;
        if (r != null && SaleSide && !keepLevel)
        {
            int i = cbLevel.Items.IndexOf(Db.S(r["price_level"]));
            if (i >= 0) cbLevel.SelectedIndex = i;
        }
        double lim = r == null || !Credit.Enabled ? 0 : Credit.Of(Db.L(r["id"])).Limit;
        lblPrev.ValueColor = lim > 0 && prevBalance > lim ? Theme.Danger : Theme.Ink;
        Totals();
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
            (note != "" ? $"\n⚠ الملاحظة عند البيع:\n{note}" : "");
        if (alert && note != "" && SaleSide)
            Dialogs.Message(note, "ملاحظة على المادة: " + Db.S(r["name"]), Tone.Warning);
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
                Convert.ToString(g.Cells["serials"].Value) ?? "", (Convert.ToString(g.Cells["note"].Value) ?? "").Trim()));
        }
        return list;
    }

    void Save()
    {
        if (!SaveCore(out long inv)) return;
        AfterSave(inv);
    }

    /// <summary>حفظ القائمة في قاعدة البيانات (بلا طباعة أو رسائل) — false عند الإلغاء أو الخطأ</summary>
    bool SaveCore(out long inv)
    {
        inv = 0;
        grid.EndEdit();
        List<Line> lines;
        try { lines = Lines(); } catch (Exception ex) { Ui.Warn(ex.Message); return false; }
        if (lines.Count == 0) { Ui.Warn("القائمة فارغة."); return false; }
        if (lines.Any(l => l.Qty <= 0)) { Ui.Warn("توجد مادة بعدد صفر أو سالب."); return false; }
        if (lines.Any(l => l.Price < 0)) { Ui.Warn("توجد مادة بسعر سالب."); return false; }

        long party = Ui.GetId(cbParty), wh = Ui.GetId(cbWh), box = Ui.GetId(cbBox), cc = Ui.GetId(cbCC), del = Ui.GetId(cbDel);
        string level = SaleSide ? Convert.ToString(cbLevel.SelectedItem) : "";
        double total = Total, disc = (double)nDisc.Value, fee = (double)nFee.Value, net = Net, paid = HasPayment ? (double)nPaid.Value : 0;
        // طريقة الدفع من الواصل: كامل = نقدي، أقل = آجل (على الحساب)، أو أقساط عند «تقسيط القائمة»
        string pay = !HasPayment ? "" : chkInst.Checked && type == "Sale" ? "أقساط" : paid >= net - 0.001 ? "نقدي" : "آجل";

        if (wh == 0) { Ui.Warn("اختر المخزن."); return false; }
        if (paid > 0 && box == 0) { Ui.Warn("اختر الصندوق."); return false; }
        if (paid > net + 0.001) { Ui.Warn("الواصل أكبر من مجموع القائمة بعد الخصم."); return false; }
        if (net < 0) { Ui.Warn("الخصم أكبر من قيمة القائمة."); return false; }
        if (type == "Sale" && pay != "نقدي" && party == 0) { Ui.Warn("البيع بالآجل أو بالأقساط يتطلب اختيار حساب الزبون."); return false; }
        // بدون حساب زبون لا يوجد مكان يُسجَّل فيه الباقي، فيضيع من الحسابات
        if (HasPayment && SaleSide && party == 0 && paid < net - 0.001)
        { Ui.Warn("الزبون النقدي يجب أن يدفع (أو يُعاد له) كامل المبلغ.\nلتسجيل الباقي دينًا اختر حساب الزبون."); return false; }

        // سقف الذمة: تنبيه أو منع حسب إعداد الحساب (ومن لديه صلاحية التجاوز يُسأل فقط)
        if (type == "Sale" && party > 0 && Credit.Enabled)
        {
            var cr = Credit.Of(party);
            double after = Ui.PartyBalance(party) - oldEffect + net - paid;
            if (cr.Limit > 0 && after > cr.Limit + 0.001)
            {
                string msg = $"تجاوز سقف الذمة!\nالسقف: {Ui.M(cr.Limit)} — الرصيد بعد القائمة: {Ui.M(after)}";
                if (cr.Block && !Session.Can("exceed_credit")) { Ui.Warn(msg + "\nالتعامل مع هذا الحساب ممنوع عند التجاوز."); return false; }
                if (!Ui.Confirm(msg + "\nهل تريد المتابعة؟")) return false;
            }
        }

        // الأقساط
        List<(int Seq, DateTime Due, double Amount)> plan = null;
        if (pay == "أقساط")
        {
            if (net - paid <= 0) { Ui.Warn("لا يوجد مبلغ متبقٍ للتقسيط."); return false; }
            using var dlg = new InstallmentDialog(net - paid, guarantorId);
            if (dlg.ShowModal() != DialogResult.OK) return false;
            plan = dlg.Plan;
            guarantorId = dlg.GuarantorId;
        }

        bool editing = editId > 0;
        using (var tx = new Tx())
        {
            try
            {
                if (editing) InvoiceOps.Remove(tx, editId);

                inv = tx.Insert(@"INSERT INTO invoices(id,type,date,party_id,warehouse_id,cashbox_id,cost_center_id,price_level,pay_type,total,discount,
                        delivery_id,delivery_fee,delivery_status,net,paid,notes,user_id)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17)",
                    Db.N(editId), type, dtDate.Value.ToString(Ui.DtFmt), Db.N(party), wh, Db.N(HasPayment ? box : 0), Db.N(cc), level, pay, total, disc,
                    Db.N(del), fee, del > 0 ? "قيد التوصيل" : null, net, paid, txtNotes.Text.Trim(), Session.UserId);

                const string insLine = "INSERT INTO invoice_lines(invoice_id,item_id,batch_id,length,width,qty,price,cost,expiry,note) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)";
                foreach (var l in lines)
                {
                    var ir = ItemRow(l.ItemId);
                    object note = l.Note == "" ? null : l.Note;
                    if (IsQuote || ir != null && Db.S(ir["item_type"]) == "خدمية")
                    {
                        // عرض السعر لا يمس المخزون، والمادة الخدمية لا مخزون لها
                        tx.Exec(insLine, inv, l.ItemId, null, l.Len, l.Wid, l.Qty, l.Price, 0.0, null, note);
                    }
                    else if (IsOut)
                    {
                        // «معدل الكلفة»: كلفة موحدة للمادة؛ وإلا كلفة كل وجبة كما هي
                        double avg = ir != null && Db.S(ir["cost_method"]) == "معدل الكلفة" ? StockOps.AvgCost(tx, l.ItemId) : -1;
                        // FEFO: الأقرب انتهاءً أولًا، ثم الوجبات بلا تاريخ
                        foreach (var t in StockOps.TakeFefo(tx, l.ItemId, wh, l.Qty))
                        {
                            double c = avg >= 0 ? avg : t.Cost;
                            tx.Exec(insLine, inv, l.ItemId, t.BatchId, l.Len, l.Wid, t.Qty, CostDoc ? c : l.Price, c, t.Expiry, note);
                        }
                    }
                    else
                    {
                        double cost = type is "Purchase" or "StockIn" ? l.Price : StockOps.AvgCost(tx, l.ItemId);
                        long bid = tx.Insert("INSERT INTO batches(item_id,warehouse_id,expiry,qty,cost,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                            l.ItemId, wh, l.Expiry == "" ? null : l.Expiry, l.Qty, cost, Ui.Now);
                        tx.Exec(insLine, inv, l.ItemId, bid, l.Len, l.Wid, l.Qty, l.Price, cost, l.Expiry == "" ? null : l.Expiry, note);
                    }
                }

                if (paid > 0)
                {
                    double rate = Ui.BoxRate(box);
                    tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,cost_center_id,invoice_id,note,user_id)
                              VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                        dtDate.Value.ToString(Ui.DtFmt), "دفعة فاتورة", box, Sign * paid / rate, rate, Db.N(party), Db.N(cc), inv,
                        $"{Ui.DocTitle(type)} رقم {inv}", Session.UserId);
                }

                // الأرقام التسلسلية: البيع يحجزها لهذه القائمة، وإرجاع البيع يعيدها متوفرة
                if (type is "Sale" or "SaleReturn")
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
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return false; }
        }
        if (editing) Db.Audit("تعديل قائمة", $"{Ui.DocTitle(type)} رقم {inv} — الصافي {Ui.M(net)}");
        lastId = inv;
        editId = 0;
        Text = Ui.DocTitle(type);
        return true;
    }

    void AfterSave(long inv)
    {
        double net = Net, paid = HasPayment ? (double)nPaid.Value : 0;
        long party = Ui.GetId(cbParty);
        var lines = Lines();
        string title = Ui.DocTitle(type);
        bool printed = false;
        if (chkPrint.Checked && Session.Can("print")) { InvoiceOps.BuildPrint(inv)?.Print(); printed = true; }

        var pr = Ui.GetRow(cbParty);
        string phone = pr == null ? "" : Db.S(pr["phone"]);
        if (type is "Sale" or "Quote" && phone != "" && Ui.Confirm($"{title} رقم {inv} محفوظة.\nهل تريد إرسالها للزبون عبر واتساب؟"))
        {
            var msg = $"{Settings.Get("shop_name")}\n{title} رقم {inv} — {dtDate.Value:yyyy/MM/dd}\n" +
                      string.Join("\n", lines.Select(l => $"• {l.Name} × {Ui.M(l.Qty)} = {Ui.M(l.Qty * l.Price)}")) +
                      $"\nالمجموع: {Ui.M(net)}" + (type == "Sale" ? $"\nالواصل: {Ui.M(paid)}\nرصيدكم الحالي: {Ui.M(Ui.PartyBalance(party))}" : "");
            _ = WhatsApp.Send(phone, msg);
        }
        else if (!printed) Toast.Show($"تم حفظ {title} رقم {inv}");
        Reset();
    }

    /// <summary>حذف القائمة المفتوحة للتعديل، أو تفريغ القائمة الجديدة</summary>
    void DeleteCurrent()
    {
        if (editId == 0)
        {
            if (grid.Rows.Count > 0 && Ui.Confirm("تفريغ القائمة الحالية؟")) Reset();
            return;
        }
        if (!Session.Guard("edit_invoice") || !Session.Guard("delete")) return;
        if (!Ui.Confirm($"حذف {Ui.DocTitle(type)} رقم {editId} مع إرجاع المخزون وحذف المبالغ المرتبطة بها؟")) return;
        using (var tx = new Tx())
        {
            try { InvoiceOps.Remove(tx, editId); tx.Commit(); }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return; }
        }
        Db.Audit("حذف قائمة", $"{Ui.DocTitle(type)} رقم {editId}");
        Toast.Show($"تم حذف {Ui.DocTitle(type)} رقم {editId}");
        editId = 0;
        Text = Ui.DocTitle(type);
        Reset();
    }

    void Reset()
    {
        guarantorId = 0;
        oldEffect = 0;
        paidTouched = false;
        grid.Rows.Clear();
        busy = true;
        chkInst.Checked = false;
        nDisc.Value = 0; nFee.Value = 0; nPaid.Value = 0;
        busy = false;
        paidTouched = false;
        if (cbDel.Items.Count > 0) cbDel.SelectedIndex = 0;
        txtNotes.Clear();
        dtDate.Value = DateTime.Now;
        LoadItems();
        ShowNumber();
        PartyChanged();
        lblInfo.Text = "";
        ClearEntry();
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
