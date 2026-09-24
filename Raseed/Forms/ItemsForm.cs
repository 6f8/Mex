using System.Data;

namespace Raseed;

/// <summary>
/// شاشة المواد: القائمة والبحث في جهة، وبطاقة المادة بتبويبات في الجهة الأخرى
/// (المعلومات الأساسية، البيانات الإضافية، الأرقام التسلسلية، تواريخ الصلاحية، التنبيهات).
/// </summary>
public class ItemsForm : BaseForm
{
    const int FieldW = 360;
    long id;

    // المعلومات الأساسية
    readonly ComboBox cbCompany = Ui.Combo(FieldW - 48), cbWh = Ui.Combo(FieldW - 48), cbType = Ui.Combo(FieldW), cbCost = Ui.Combo(FieldW),
                      cbBuyCur = Ui.Combo(128), cbSellCur = Ui.Combo(128);
    readonly TextBox tName = new() { Width = FieldW };
    readonly NumericUpDown nQty = Ui.Num(FieldW, 2), nBuy = Ui.Num(FieldW, 2), nRetail = Ui.Num(FieldW, 2), nWholesale = Ui.Num(FieldW, 2),
                           nSpecial = Ui.Num(FieldW, 2), nInst = Ui.Num(FieldW, 2);
    readonly Label lblQtyHint = new() { AutoSize = false, Width = 540, Height = 26, ForeColor = Theme.Muted, Font = Theme.F(8.5f), TextAlign = ContentAlignment.MiddleLeft };

    // البيانات الإضافية
    readonly TextBox tCode = new() { Width = FieldW }, tBarcode = new() { Width = FieldW - 48 };
    readonly ComboBox cbCategory = new() { Width = FieldW, DropDownStyle = ComboBoxStyle.DropDown }, cbUnit = new() { Width = FieldW, DropDownStyle = ComboBoxStyle.DropDown };
    readonly NumericUpDown nWarranty = Ui.Num(FieldW);
    readonly Toggle tgMeasure = new() { Text = "تُباع بالقياس (الطول × العرض)", Width = 470 }, tgActive = new() { Text = "مادة فعّالة (تظهر في الفواتير)", Width = 470, Checked = true };

    // الأرقام التسلسلية
    readonly Toggle tgSerial = new() { Text = "تتبع الأرقام التسلسلية (IMEI) لهذه المادة", Width = 470 };
    readonly TextBox tSerials = new() { Width = 360, Height = 90, Multiline = true, PlaceholderText = "رقم في كل سطر (يمكن اللصق أو المسح بالقارئ)" };
    readonly DataGridView gSerials = Ui.NewGrid();

    // تواريخ الصلاحية
    readonly DateTimePicker dOpenExpiry = new() { Width = FieldW, Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false };
    readonly DataGridView gBatches = Ui.NewGrid();

    // التنبيهات
    readonly NumericUpDown nMin = Ui.Num(FieldW, 2);
    readonly TextBox tAlert = new() { Width = 470, Height = 70, Multiline = true }, tMedical = new() { Width = 470, Height = 90, Multiline = true };

    readonly DataGridView list = Ui.NewGrid();
    readonly TextBox search = new() { Width = 300, PlaceholderText = "بحث بالاسم أو الباركود أو الرمز..." };
    readonly CardPanel formCard, listCard;
    readonly ModernButton bDel;

    public ItemsForm()
    {
        Text = "المواد";
        KeyPreview = true;
        cbType.Items.AddRange(new object[] { "اعتيادية", "خدمية" });
        cbCost.Items.AddRange(new object[] { "كلفة الوجبة", "معدل الكلفة" });
        foreach (var cb in new[] { cbBuyCur, cbSellCur }) cb.Items.AddRange(new object[] { "دينار", "دولار" });
        cbUnit.Items.AddRange(new object[] { "قطعة", "علبة", "كارتون", "شريط", "متر", "متر مربع", "كغم", "لتر" });
        nWarranty.Maximum = 3650;

        // ---------- بطاقة المادة ----------
        formCard = new CardPanel { Dock = DockStyle.Fill, Title = "إضافة المواد", Subtitle = "مادة جديدة", IconName = "package-plus" };
        var tabs = new ModernTabs { Dock = DockStyle.Fill };
        tabs.Add("المعلومات الأساسية", Page(BasicPage()), "file-text");
        tabs.Add("البيانات الإضافية", Page(ExtraPage()), "sliders-horizontal");
        tabs.Add("الرقم التسلسلي", Page(SerialPage()), "hash");
        tabs.Add("تأريخ الصلاحية", Page(ExpiryPage()), "calendar-clock");
        tabs.Add("التنبيهات", Page(AlertsPage()), "bell");

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Surface, Padding = new Padding(0, 10, 0, 0) };
        var bNew = new ModernButton { Text = "جديد", IconName = "plus", Height = 44, Margin = new Padding(4) }; bNew.FitWidth(130);
        var bSave = new ModernButton { Text = "حفظ", IconName = "save", Height = 44, Margin = new Padding(4) }; bSave.FitWidth(130);
        bDel = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Danger, Height = 44, Margin = new Padding(4) }; bDel.FitWidth(120);
        actions.Controls.AddRange(new Control[] { bSave, bNew, bDel });
        formCard.Controls.Add(tabs);
        formCard.Controls.Add(actions);

        // ---------- القائمة ----------
        listCard = new CardPanel { Dock = DockStyle.Right, Width = 560, Title = "قائمة المواد", IconName = "boxes" };
        var sb = new InputBox(search, 300, "search") { Dock = DockStyle.Top, Height = 42 };
        listCard.Controls.Add(list);
        listCard.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Surface });
        listCard.Controls.Add(sb);

        Controls.Add(formCard);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(listCard);

        bNew.Click += (s, e) => New();
        bSave.Click += (s, e) => Save();
        bDel.Click += (s, e) => Delete();
        search.TextChanged += (s, e) => LoadList();
        list.CellClick += (s, e) => { if (list.CurrentRow != null) LoadItem(Db.L(list.CurrentRow.Cells["id"].Value)); };
        list.KeyUp += (s, e) => { if (e.KeyCode is Keys.Up or Keys.Down && list.CurrentRow != null) LoadItem(Db.L(list.CurrentRow.Cells["id"].Value)); };
        cbType.SelectedIndexChanged += (s, e) => UpdateState();
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F10) { Save(); e.Handled = true; }
            if (e.KeyCode == Keys.F3) { search.Focus(); e.Handled = true; }
        };

        FillLookups();
        LoadList();
        New();
    }

    // ================= بناء الصفحات =================
    static Control Page(Control content)
    {
        var p = new Panel { BackColor = Theme.Surface, Padding = new Padding(0, 8, 0, 0), AutoScroll = true };
        content.Dock = DockStyle.Top;
        p.Controls.Add(content);
        return p;
    }

    static FlowLayoutPanel Stack() => new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Surface };

    /// <summary>سطر: العنوان على اليمين ثم الحقل ثم أزرار إضافية (كما في الشاشات التقليدية المألوفة)</summary>
    static Control Row(string caption, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(new Label { Text = caption, AutoSize = false, Width = 150, Height = 40, Font = Theme.FS(10), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 4, 0) });
        foreach (var f in fields)
        {
            var c = Ui.Wrap(f);
            c.Margin = new Padding(4, 0, 4, 0);
            row.Controls.Add(c);
        }
        return row;
    }

    static ModernButton PlusButton(string tip)
    {
        var b = new ModernButton { Kind = BtnKind.Success, IconName = "circle-plus", Size = new Size(40, 40), Margin = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    Control BasicPage()
    {
        var st = Stack();
        var addCompany = PlusButton("إضافة شركة جديدة");
        addCompany.Click += (s, e) => { long nid = QuickAdd.Ask("شركة جديدة", "اسم الشركة / الماركة", "companies"); if (nid > 0) { FillCompanies(); Ui.SelectId(cbCompany, nid); } };
        var addWh = PlusButton("إضافة مخزن جديد");
        addWh.Click += (s, e) =>
        {
            if (!Session.Guard("settings")) return;
            long nid = QuickAdd.Ask("مخزن جديد", "اسم المخزن", "warehouses");
            if (nid > 0) { Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id"); Ui.SelectId(cbWh, nid); }
        };
        st.Controls.Add(Row("اسم الشركة", cbCompany, addCompany));
        st.Controls.Add(Row("المخزن", cbWh, addWh));
        st.Controls.Add(Row("نوع المادة", cbType));
        st.Controls.Add(Row("اسم المادة", tName));
        st.Controls.Add(Row("احتساب الكلفة", cbCost));
        var sellCap = new Label { Text = "عملة البيع", AutoSize = false, Width = 96, Height = 40, Font = Theme.FS(10), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft };
        st.Controls.Add(Row("عملة الشراء", cbBuyCur, sellCap, cbSellCur));
        st.Controls.Add(Row("العدد", nQty));
        lblQtyHint.Margin = new Padding(8, 0, 4, 4);
        st.Controls.Add(lblQtyHint);
        st.Controls.Add(Row("سعر الشراء", nBuy));
        st.Controls.Add(Row("سعر البيع (مفرد)", nRetail));
        st.Controls.Add(Row("سعر الجملة", nWholesale));
        st.Controls.Add(Row("السعر الخاص", nSpecial));
        st.Controls.Add(Row("سعر الأقساط", nInst));
        return st;
    }

    Control ExtraPage()
    {
        var st = Stack();
        var gen = new ModernButton { Kind = BtnKind.Secondary, IconName = "barcode", Size = new Size(40, 40), Margin = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(gen, "توليد باركود داخلي");
        gen.Click += (s, e) =>
        {
            long next = id > 0 ? id : Db.L(Db.Scalar("SELECT IFNULL(MAX(id),0)+1 FROM items"));
            tBarcode.Text = "2" + next.ToString("D10");
        };
        st.Controls.Add(Row("الرمز", tCode));
        st.Controls.Add(Row("الباركود", tBarcode, gen));
        st.Controls.Add(Row("الصنف / المجموعة", cbCategory));
        st.Controls.Add(Row("الوحدة", cbUnit));
        st.Controls.Add(Row("الضمان (يوم)", nWarranty));
        tgMeasure.Margin = tgActive.Margin = new Padding(8, 8, 8, 4);
        st.Controls.Add(tgMeasure);
        st.Controls.Add(tgActive);
        return st;
    }

    Control SerialPage()
    {
        var st = Stack();
        tgSerial.Margin = new Padding(8, 4, 8, 8);
        st.Controls.Add(tgSerial);
        var add = new ModernButton { Text = "إضافة الأرقام", IconName = "plus", Kind = BtnKind.Soft, Height = 40, Margin = new Padding(4, 0, 4, 0) };
        add.FitWidth(130);
        add.Click += (s, e) => AddSerials();
        st.Controls.Add(Row("أرقام جديدة", tSerials, add));
        var del = new ModernButton { Text = "حذف الرقم المحدد", IconName = "trash-2", Kind = BtnKind.Danger, Height = 38, Margin = new Padding(8, 8, 4, 8) };
        del.FitWidth(150);
        del.Click += (s, e) => DeleteSerial();
        st.Controls.Add(del);
        var host = new Panel { Width = 640, Height = 260, BackColor = Theme.Surface, Margin = new Padding(8, 0, 8, 8) };
        gSerials.Dock = DockStyle.Fill;
        host.Controls.Add(gSerials);
        st.Controls.Add(host);
        st.Controls.Add(Hint("امسح الرقم التسلسلي في فاتورة البيع لإضافة الجهاز نفسه؛ يُحجز الرقم للفاتورة ويعود متوفرًا إذا حُذفت."));
        return st;
    }

    Control ExpiryPage()
    {
        var st = Stack();
        st.Controls.Add(Row("صلاحية الكمية الافتتاحية", dOpenExpiry));
        st.Controls.Add(Hint("الوجبات الحالية في المخازن (يُصرف الأقرب انتهاءً أولًا):"));
        var host = new Panel { Width = 640, Height = 300, BackColor = Theme.Surface, Margin = new Padding(8, 0, 8, 8) };
        gBatches.Dock = DockStyle.Fill;
        host.Controls.Add(gBatches);
        st.Controls.Add(host);
        return st;
    }

    Control AlertsPage()
    {
        var st = Stack();
        st.Controls.Add(Row("حد الطلب (تنبيه النفاد)", nMin));
        st.Controls.Add(Ui.Labeled("تنبيه يظهر عند إضافة المادة للفاتورة", tAlert));
        st.Controls.Add(Ui.Labeled("البيانات الطبية (الاسم العلمي، التركيز، الجرعة، التحذيرات)", tMedical));
        return st;
    }

    static Label Hint(string text) => new()
    {
        Text = text, AutoSize = false, Width = 640, Height = 30, ForeColor = Theme.Muted, Font = Theme.F(9),
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 4, 8, 4)
    };

    // ================= البيانات =================
    void FillCompanies() => Ui.FillCombo(cbCompany, "SELECT id,name FROM companies ORDER BY name", true, "افتراضي");

    void FillLookups()
    {
        FillCompanies();
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id");
        cbCategory.Items.Clear();
        foreach (DataRow r in Db.Query("SELECT DISTINCT category FROM items WHERE IFNULL(category,'')<>'' ORDER BY category").Rows) cbCategory.Items.Add(Db.S(r[0]));
    }

    void LoadList()
    {
        var q = "%" + search.Text.Trim() + "%";
        list.DataSource = Db.Query(@"SELECT i.id, i.name AS [اسم المادة], IFNULL(c.name,'') AS [الشركة],
            IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0) AS [العدد], i.price_retail AS [سعر البيع]
            FROM items i LEFT JOIN companies c ON c.id=i.company_id
            WHERE i.name LIKE @p0 OR IFNULL(i.barcode,'') LIKE @p0 OR IFNULL(i.code,'') LIKE @p0
            ORDER BY i.name", q);
        listCard.Subtitle = $"{list.Rows.Count} مادة";
    }

    static string Cur(ComboBox cb) => cb.SelectedIndex == 1 ? "USD" : "IQD";
    static void SetCur(ComboBox cb, string v) => cb.SelectedIndex = v == "USD" ? 1 : 0;
    static void SetNum(NumericUpDown n, double v) => n.Value = (decimal)Math.Max((double)n.Minimum, Math.Min((double)n.Maximum, v));

    void New()
    {
        id = 0;
        Ui.SelectId(cbCompany, 0);
        if (cbWh.Items.Count > 0) cbWh.SelectedIndex = 0;
        cbType.SelectedIndex = 0; cbCost.SelectedIndex = 0; cbBuyCur.SelectedIndex = 0; cbSellCur.SelectedIndex = 0;
        foreach (var t in new[] { tName, tCode, tBarcode, tAlert, tMedical, tSerials }) t.Clear();
        cbCategory.Text = ""; cbUnit.Text = "قطعة";
        foreach (var n in new[] { nQty, nBuy, nRetail, nWholesale, nSpecial, nInst, nMin, nWarranty }) n.Value = 0;
        tgMeasure.Checked = false; tgActive.Checked = true; tgSerial.Checked = false;
        dOpenExpiry.Value = DateTime.Today.AddYears(1); dOpenExpiry.Checked = false;
        formCard.Title = "إضافة المواد";
        formCard.Subtitle = "مادة جديدة";
        bDel.Enabled = false;
        LoadSubGrids();
        UpdateState();
        tName.Focus();
    }

    void LoadItem(long itemId)
    {
        var dt = Db.Query("SELECT * FROM items WHERE id=@p0", itemId);
        if (dt.Rows.Count == 0) return;
        var r = dt.Rows[0];
        id = itemId;
        Ui.SelectId(cbCompany, Db.L(r["company_id"]));
        cbType.SelectedIndex = Db.S(r["item_type"]) == "خدمية" ? 1 : 0;
        cbCost.SelectedIndex = Db.S(r["cost_method"]) == "معدل الكلفة" ? 1 : 0;
        SetCur(cbBuyCur, Db.S(r["buy_currency"])); SetCur(cbSellCur, Db.S(r["sell_currency"]));
        tName.Text = Db.S(r["name"]); tCode.Text = Db.S(r["code"]); tBarcode.Text = Db.S(r["barcode"]);
        cbCategory.Text = Db.S(r["category"]); cbUnit.Text = Db.S(r["unit"]);
        SetNum(nBuy, Db.D(r["price_buy"])); SetNum(nRetail, Db.D(r["price_retail"])); SetNum(nWholesale, Db.D(r["price_wholesale"]));
        SetNum(nSpecial, Db.D(r["price_special"])); SetNum(nInst, Db.D(r["price_installment"])); SetNum(nMin, Db.D(r["min_qty"]));
        SetNum(nWarranty, Db.D(r["warranty_days"]));
        SetNum(nQty, Db.D(Db.Scalar("SELECT IFNULL(SUM(qty),0) FROM batches WHERE item_id=@p0", itemId)));
        tgMeasure.Checked = Db.L(r["by_measure"]) == 1; tgActive.Checked = Db.L(r["active"]) == 1; tgSerial.Checked = Db.L(r["track_serial"]) == 1;
        tAlert.Text = Db.S(r["alert_note"]); tMedical.Text = Db.S(r["medical_info"]);
        formCard.Title = "تعديل المادة";
        formCard.Subtitle = $"{tName.Text} — رقم {id}";
        bDel.Enabled = true;
        LoadSubGrids();
        UpdateState();
    }

    void UpdateState()
    {
        bool service = cbType.SelectedIndex == 1;
        bool isNew = id == 0;
        nQty.Enabled = isNew && !service;
        cbWh.Enabled = isNew && !service;
        dOpenExpiry.Enabled = isNew && !service;
        lblQtyHint.Text = service ? "المادة الخدمية لا مخزون لها (مثل: أجور صيانة، شحن رصيد، خدمة توصيل)."
            : isNew ? "الكمية الافتتاحية تُضاف إلى المخزن المحدد بسعر الشراء (سند إدخال مخزني)."
            : "الرصيد الحالي في كل المخازن. لتغييره: إدخال مخزني أو إخراج مخزني أو تسوية مخزنية.";
    }

    void LoadSubGrids()
    {
        gSerials.DataSource = Db.Query(@"SELECT s.id, s.serial AS [الرقم التسلسلي], CASE WHEN s.invoice_id IS NULL THEN 'متوفر' ELSE 'مباع' END AS [الحالة],
            s.invoice_id AS [الفاتورة], s.created AS [تاريخ الإضافة] FROM item_serials s WHERE s.item_id=@p0 ORDER BY s.id DESC", id);
        gBatches.DataSource = Db.Query(@"SELECT b.id, w.name AS [المخزن], b.expiry AS [تاريخ الصلاحية],
            CAST(julianday(b.expiry)-julianday(date('now','localtime')) AS INTEGER) AS [أيام متبقية], b.qty AS [الكمية], b.cost AS [الكلفة]
            FROM batches b JOIN warehouses w ON w.id=b.warehouse_id WHERE b.item_id=@p0 AND b.qty>0
            ORDER BY (b.expiry IS NULL OR b.expiry=''), b.expiry", id);
    }

    void Save()
    {
        if (!Session.Guard("items")) return;
        var name = tName.Text.Trim();
        if (name == "") { Ui.Warn("أدخل اسم المادة."); tName.Focus(); return; }
        var barcode = tBarcode.Text.Trim();
        if (barcode != "" && Db.L(Db.Scalar("SELECT COUNT(*) FROM items WHERE barcode=@p0 AND id<>@p1", barcode, id)) > 0)
        { Ui.Warn("هذا الباركود مستخدم لمادة أخرى."); return; }
        bool service = cbType.SelectedIndex == 1;
        double qty = (double)nQty.Value, buy = (double)nBuy.Value;
        if (id == 0 && qty > 0 && !service && Ui.GetId(cbWh) == 0) { Ui.Warn("اختر المخزن للكمية الافتتاحية."); return; }

        object[] vals =
        {
            name, tCode.Text.Trim(), barcode, cbCategory.Text.Trim(), cbUnit.Text.Trim() == "" ? "قطعة" : cbUnit.Text.Trim(),
            (double)nRetail.Value, (double)nWholesale.Value, (double)nSpecial.Value, (double)nInst.Value, buy,
            (double)nMin.Value, tgMeasure.Checked ? 1 : 0, tMedical.Text.Trim(), tAlert.Text.Trim(), tgActive.Checked ? 1 : 0,
            (long)nWarranty.Value, Db.N(Ui.GetId(cbCompany)), service ? "خدمية" : "اعتيادية", cbCost.SelectedIndex == 1 ? "معدل الكلفة" : "كلفة الوجبة",
            Cur(cbBuyCur), Cur(cbSellCur), tgSerial.Checked ? 1 : 0
        };
        const string cols = "name,code,barcode,category,unit,price_retail,price_wholesale,price_special,price_installment,price_buy,min_qty,by_measure,medical_info,alert_note,active,warranty_days,company_id,item_type,cost_method,buy_currency,sell_currency,track_serial";
        var colList = cols.Split(',');
        bool isNew = id == 0;
        using (var tx = new Tx())
        {
            if (isNew)
            {
                id = tx.Insert($"INSERT INTO items({cols}) VALUES({string.Join(",", colList.Select((c, i) => "@p" + i))})", vals);
                if (qty > 0 && !service)
                {
                    // رصيد أول المدة: سند إدخال مخزني بكلفة سعر الشراء (بالدينار)
                    double cost = Cur(cbBuyCur) == "USD" ? buy * Ui.Rate("USD") : buy;
                    StockOps.StockIn(tx, Ui.GetId(cbWh), new[] { (id, qty, cost, dOpenExpiry.Checked ? dOpenExpiry.Value.ToString(Ui.DFmt) : "") },
                        "رصيد أول المدة — " + name);
                }
            }
            else
                tx.Exec($"UPDATE items SET {string.Join(",", colList.Select((c, i) => $"{c}=@p{i}"))} WHERE id=@p{colList.Length}", vals.Append(id).ToArray());
            tx.Commit();
        }
        Toast.Show(isNew ? $"تمت إضافة المادة «{name}»" : $"تم حفظ المادة «{name}»");
        long saved = id;
        FillLookups();
        LoadList();
        LoadItem(saved);
        foreach (DataGridViewRow r in list.Rows)
            if (Db.L(r.Cells["id"].Value) == saved) { list.CurrentCell = r.Cells["اسم المادة"]; break; }
    }

    void Delete()
    {
        if (id == 0 || !Session.Guard("items") || !Session.Guard("delete")) return;
        if (!Ui.Confirm($"حذف المادة «{tName.Text}»؟")) return;
        try
        {
            Db.Exec("DELETE FROM items WHERE id=@p0", id);
            Db.Audit("حذف مادة", tName.Text);
            Toast.Show("تم حذف المادة");
            LoadList();
            New();
        }
        catch { Ui.Warn("لا يمكن حذف هذه المادة لوجود حركات عليها (فواتير أو مخزون). يمكنك إيقافها من «مادة فعّالة»."); }
    }

    void AddSerials()
    {
        if (id == 0) { Ui.Warn("احفظ المادة أولًا ثم أضف أرقامها التسلسلية."); return; }
        var nums = tSerials.Lines.Select(x => x.Trim()).Where(x => x != "").Distinct().ToList();
        if (nums.Count == 0) return;
        int added = 0;
        var dup = new List<string>();
        using (var tx = new Tx())
        {
            foreach (var n in nums)
            {
                if (Db.L(tx.Scalar("SELECT COUNT(*) FROM item_serials WHERE serial=@p0", n)) > 0) { dup.Add(n); continue; }
                tx.Exec("INSERT INTO item_serials(item_id,serial,warehouse_id,created) VALUES(@p0,@p1,@p2,@p3)", id, n, Db.N(Ui.GetId(cbWh)), Ui.Now);
                added++;
            }
            if (!tgSerial.Checked) tx.Exec("UPDATE items SET track_serial=1 WHERE id=@p0", id);
            tx.Commit();
        }
        tgSerial.Checked = true;
        tSerials.Clear();
        LoadSubGrids();
        if (dup.Count > 0) Ui.Warn($"أُضيف {added} رقم. الأرقام التالية موجودة مسبقًا:\n" + string.Join("\n", dup.Take(20)));
        else Toast.Show($"تمت إضافة {added} رقم تسلسلي");
    }

    void DeleteSerial()
    {
        if (gSerials.CurrentRow == null || !Session.Guard("items")) return;
        if (Convert.ToString(gSerials.CurrentRow.Cells["الحالة"].Value) == "مباع") { Ui.Warn("لا يمكن حذف رقم مُباع."); return; }
        Db.Exec("DELETE FROM item_serials WHERE id=@p0", Db.L(gSerials.CurrentRow.Cells["id"].Value));
        LoadSubGrids();
    }
}
