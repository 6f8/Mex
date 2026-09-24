using System.Data;

namespace Raseed;

/// <summary>
/// شاشة المواد: القائمة والبحث في جهة، وبطاقة المادة بتبويبات في الجهة الأخرى
/// (المعلومات الأساسية، البيانات الإضافية، الرقم التسلسلي، تأريخ الصلاحية، التنبيهات).
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
    readonly Toggle tgScale = new() { Text = "استخدام الميزان (البيع بالوزن من باركود الميزان)", Width = 470 };
    readonly TextBox tBarcode = new() { Width = FieldW - 48, PlaceholderText = "امسح الباركود ثم Enter" };
    readonly ListBox lstBarcodes = new() { Width = FieldW, Height = 150, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(10.5f), IntegralHeight = false };
    readonly TextBox tAlert = new() { Width = 470 }, tOrigin = new() { Width = 470, PlaceholderText = "بلد المنشأ أو المورد الأصلي" };
    readonly TextBox tCode = new() { Width = 240 };
    readonly ComboBox cbCategory = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDown }, cbUnit = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDown };
    readonly Toggle tgMeasure = new() { Text = "تُباع بالقياس (الطول × العرض)", Width = 370 }, tgActive = new() { Text = "مادة فعّالة (تظهر في الفواتير)", Width = 370, Checked = true };
    readonly TextBox tMedical = new() { Width = 370, Height = 80, Multiline = true };

    // الأرقام التسلسلية
    readonly Toggle tgSerial = new() { Text = "استخدام الرقم التسلسلي (IMEI)", Width = 470 };
    readonly NumericUpDown nWarranty = Ui.Num(140);
    readonly TextBox tSerial = new() { Width = FieldW, PlaceholderText = "امسح الرقم ثم Enter" };
    readonly DataGridView gSerials = Ui.NewGrid();

    // تواريخ الصلاحية
    readonly DataGridView gBatches = Ui.NewGrid();

    // التنبيهات
    readonly Toggle cExpiry = Check("تاريخ الصلاحية"), cMin = Check("الحد الأدنى"), cMax = Check("الحد الأعلى"),
                    cSafety = Check("حد الأمان"), cStagnant = Check("فترة الركود"), cTarget = Check("هدف البيع");
    readonly DateTimePicker dExpiry = new() { Width = 170, Format = DateTimePickerFormat.Short };
    readonly NumericUpDown nAlertDays = Ui.Num(150), nMin = Ui.Num(170, 2), nMax = Ui.Num(170, 2), nSafety = Ui.Num(170, 2),
                           nStagnant = Ui.Num(170), nTarget = Ui.Num(170, 2);

    readonly DataGridView list = Ui.NewGrid();
    readonly TextBox search = new() { Width = 300, PlaceholderText = "اسم المادة أو الباركود أو الرمز..." };
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
        nWarranty.Maximum = 120; nWarranty.Minimum = 0;
        nAlertDays.Minimum = 0; nStagnant.Minimum = 0;

        // ---------- بطاقة المادة ----------
        formCard = new CardPanel { Dock = DockStyle.Fill, Title = "إضافة المواد", Subtitle = "مادة جديدة", IconName = "package-plus" };
        var tabs = new ModernTabs { Dock = DockStyle.Fill };
        tabs.Add("المعلومات الأساسية", Page(BasicPage()), "file-text");
        tabs.Add("البيانات الإضافية", Page(ExtraPage()), "sliders-horizontal");
        tabs.Add("الرقم التسلسلي", Page(SerialPage()), "hash");
        tabs.Add("تأريخ الصلاحية", Page(ExpiryPage()), "calendar-clock");
        tabs.Add("التنبيهات", Page(AlertsPage()), "bell");
        formCard.Controls.Add(tabs);

        // ---------- القائمة والأزرار ----------
        listCard = new CardPanel { Dock = DockStyle.Fill, Title = "قائمة المواد", IconName = "boxes" };
        var searchRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Surface, WrapContents = false };
        searchRow.Controls.Add(new Label { Text = "البحث", AutoSize = false, Width = 64, Height = 42, Font = Theme.FS(10.5f), ForeColor = Theme.Brand, TextAlign = ContentAlignment.MiddleLeft });
        var sb = new InputBox(search, 380, "search") { Height = 42, Margin = new Padding(4, 0, 4, 0) };
        searchRow.Controls.Add(sb);
        searchRow.Resize += (s, e) => sb.Width = Math.Max(200, searchRow.ClientSize.Width - 80);
        listCard.Controls.Add(list);
        listCard.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Surface });
        listCard.Controls.Add(searchRow);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(0, 10, 0, 0) };
        var bNew = new ModernButton { Text = "جديد", IconName = "plus", Height = 46, Margin = new Padding(0, 4, 8, 4) };
        var bSave = new ModernButton { Text = "حفظ", IconName = "save", Height = 46, Margin = new Padding(0, 4, 8, 4) };
        bDel = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 46, Margin = new Padding(0, 4, 8, 4) };
        actions.Controls.AddRange(new Control[] { bNew, bSave, bDel });
        actions.Resize += (s, e) =>
        {
            int w = Math.Max(110, (actions.ClientSize.Width - 24) / 3);
            foreach (Control b in actions.Controls) b.Width = w;
        };

        var left = new Panel { Dock = DockStyle.Right, Width = 560 };
        left.Controls.Add(listCard);
        left.Controls.Add(actions);

        Controls.Add(formCard);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(left);

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

    static Label Caption(string text, int width = 150) => new()
    {
        Text = text, AutoSize = false, Width = width, Height = 40, Font = Theme.FS(10), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 4, 0)
    };

    static Label Unit(string text) => new()
    {
        Text = text, AutoSize = false, Width = 90, Height = 40, Font = Theme.F(10), ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 0, 4, 0)
    };

    /// <summary>سطر: العنوان على اليمين ثم الحقل ثم أزرار إضافية (كما في الشاشات التقليدية المألوفة)</summary>
    static Control Row(string caption, params Control[] fields) => RowW(caption, 150, fields);

    static Control RowW(string caption, int captionWidth, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 3, 0, 3) };
        if (caption != null) row.Controls.Add(Caption(caption, captionWidth));
        foreach (var f in fields)
        {
            var c = f is Label or Toggle or ModernButton ? f : Ui.Wrap(f);
            if (c.Margin == new Padding(3)) c.Margin = new Padding(4, 0, 4, 0);
            row.Controls.Add(c);
        }
        return row;
    }

    static Toggle Check(string text) => new() { Text = text, Width = 170, Height = 40, Margin = new Padding(8, 0, 4, 0) };

    static ModernButton PlusButton(string tip)
    {
        var b = new ModernButton { Kind = BtnKind.Success, IconName = "circle-plus", Size = new Size(40, 40), Margin = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    static Label SectionLabel(string text, int width = 460) => new()
    {
        Text = text, AutoSize = false, Width = width, Height = 34, Font = Theme.FS(10.5f), ForeColor = Theme.Brand, TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(8, 8, 8, 2)
    };

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
        // عمودان: البيانات الإضافية المعتادة، وبجانبها بيانات التصنيف والبيانات الطبية
        // يلتف العمود الثاني تحت الأول في الشاشات الضيقة
        var cols = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Surface };
        var right = Stack();
        tgScale.Margin = new Padding(8, 4, 8, 10);
        right.Controls.Add(tgScale);

        var add = new ModernButton { Kind = BtnKind.Secondary, IconName = "plus", Size = new Size(40, 40), Margin = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(add, "إضافة الباركود للقائمة");
        add.Click += (s, e) => AddBarcode(tBarcode.Text);
        tBarcode.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddBarcode(tBarcode.Text); } };
        right.Controls.Add(Row("الباركود", tBarcode, add));
        var lstHost = Ui.Wrap(lstBarcodes);
        lstHost.Margin = new Padding(162, 0, 4, 6);
        right.Controls.Add(lstHost);
        var gen = new ModernButton { Text = "توليد باركود", IconName = "barcode", Height = 42, Width = (FieldW - 8) / 2, Margin = new Padding(4, 0, 4, 0) };
        gen.Click += (s, e) => AddBarcode(Barcodes.Generate(id, lstBarcodes.Items.Cast<string>()));
        var delBc = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 42, Width = (FieldW - 8) / 2, Margin = new Padding(4, 0, 4, 0) };
        delBc.Click += (s, e) => { if (lstBarcodes.SelectedIndex >= 0) lstBarcodes.Items.RemoveAt(lstBarcodes.SelectedIndex); };
        var btns = RowW(null, 0, gen, delBc);
        btns.Margin = new Padding(162, 0, 0, 6);
        right.Controls.Add(btns);
        right.Controls.Add(new Label { Text = "الأول في القائمة هو الباركود الأساسي (يُطبع على الملصق).\nيمكن إضافة أكثر من باركود للمادة نفسها.", AutoSize = false, Width = FieldW, Height = 38, ForeColor = Theme.Muted, Font = Theme.F(8.5f), Margin = new Padding(166, 0, 4, 4) });

        right.Controls.Add(SectionLabel("الملاحظة عند البيع"));
        var alertHost = Ui.Wrap(tAlert); alertHost.Margin = new Padding(8, 0, 8, 4);
        right.Controls.Add(alertHost);
        right.Controls.Add(SectionLabel("المصدر"));
        var originHost = Ui.Wrap(tOrigin); originHost.Margin = new Padding(8, 0, 8, 10);
        right.Controls.Add(originHost);
        var move = new ModernButton { Text = "حركة مادة", IconName = "arrow-left-right", Height = 44, Margin = new Padding(8, 4, 8, 8) };
        move.FitWidth(180);
        move.Click += (s, e) =>
        {
            if (id == 0) { Ui.Warn("اختر مادة من القائمة أولًا."); return; }
            if (!Session.Guard("reports")) return;
            MainForm.Instance?.Open($"حركة مادة — {tName.Text}", new ReportsForm("حركة مادة", id));
        };
        right.Controls.Add(move);

        var leftCol = Stack();
        leftCol.Margin = new Padding(24, 0, 0, 0);
        leftCol.Controls.Add(SectionLabel("التصنيف", 370));
        leftCol.Controls.Add(RowW("الرمز", 110, tCode));
        leftCol.Controls.Add(RowW("الصنف", 110, cbCategory));
        leftCol.Controls.Add(RowW("الوحدة", 110, cbUnit));
        tgMeasure.Margin = tgActive.Margin = new Padding(8, 6, 8, 2);
        leftCol.Controls.Add(tgMeasure);
        leftCol.Controls.Add(tgActive);
        leftCol.Controls.Add(SectionLabel("البيانات الطبية (للصيدليات)", 370));
        var medHost = Ui.Wrap(tMedical); medHost.Margin = new Padding(8, 0, 8, 4);
        leftCol.Controls.Add(medHost);

        cols.Controls.Add(right);
        cols.Controls.Add(leftCol);
        return cols;
    }

    Control SerialPage()
    {
        var st = Stack();
        tgSerial.Margin = new Padding(8, 4, 8, 8);
        st.Controls.Add(tgSerial);
        st.Controls.Add(Row("فترة الضمان", nWarranty, Unit("شهر")));
        tSerial.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddSerial(); } };
        var add = new ModernButton { Kind = BtnKind.Secondary, IconName = "plus", Size = new Size(40, 40), Margin = new Padding(4, 0, 4, 0) };
        new ToolTip().SetToolTip(add, "إضافة الرقم");
        add.Click += (s, e) => AddSerial();
        st.Controls.Add(Row("الرقم التسلسلي", tSerial, add));
        var host = new Panel { Width = 600, Height = 260, BackColor = Theme.Surface, Margin = new Padding(166, 4, 8, 8) };
        gSerials.Dock = DockStyle.Fill;
        host.Controls.Add(gSerials);
        st.Controls.Add(host);
        var del = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 42, Margin = new Padding(166, 0, 4, 8) };
        del.FitWidth(170);
        del.Click += (s, e) => DeleteSerial();
        st.Controls.Add(del);
        st.Controls.Add(Hint("امسح الرقم التسلسلي في فاتورة البيع لإضافة الجهاز نفسه؛ يُحجز الرقم للفاتورة ويعود متوفرًا إذا حُذفت."));
        return st;
    }

    Control ExpiryPage()
    {
        var st = Stack();
        st.Controls.Add(Hint("الوجبات الحالية في المخازن (يُصرف الأقرب انتهاءً أولًا). تاريخ صلاحية الكمية الافتتاحية وفترة التنبيه في تبويب «التنبيهات»."));
        var host = new Panel { Width = 760, Height = 340, BackColor = Theme.Surface, Margin = new Padding(8, 0, 8, 8) };
        gBatches.Dock = DockStyle.Fill;
        host.Controls.Add(gBatches);
        st.Controls.Add(host);
        return st;
    }

    Control AlertsPage()
    {
        var st = Stack();
        st.Controls.Add(RowW(null, 0, cExpiry, Ui.Wrap(dExpiry), Caption("فترة التنبيه", 130), Ui.Wrap(nAlertDays), Unit("يوم")));
        st.Controls.Add(RowW(null, 0, cMin, Ui.Wrap(nMin), Unit("عدد")));
        st.Controls.Add(RowW(null, 0, cMax, Ui.Wrap(nMax), Unit("عدد")));
        st.Controls.Add(RowW(null, 0, cSafety, Ui.Wrap(nSafety), Unit("عدد")));
        st.Controls.Add(RowW(null, 0, cStagnant, Ui.Wrap(nStagnant), Unit("يوم")));
        st.Controls.Add(RowW(null, 0, cTarget, Ui.Wrap(nTarget), Unit("خلال الشهر")));
        st.Controls.Add(Hint("تظهر التنبيهات في الصفحة الرئيسية وعلى جرس التنبيهات: النفاد (الحد الأدنى)، الاقتراب منه (حد الأمان)، تكدّس المخزون (الحد الأعلى)،"));
        st.Controls.Add(Hint("المادة التي لم تُبع خلال فترة الركود، والمادة التي لم تبلغ هدف البيع الشهري، والوجبات القريبة من انتهاء الصلاحية."));

        foreach (var (chk, ctl) in new (Toggle, Control)[] { (cExpiry, nAlertDays), (cMin, nMin), (cMax, nMax), (cSafety, nSafety), (cStagnant, nStagnant), (cTarget, nTarget) })
            chk.CheckedChanged += (s, e) => { ctl.Enabled = chk.Checked; if (chk == cExpiry) UpdateState(); };
        return st;
    }

    static Label Hint(string text) => new()
    {
        Text = text, AutoSize = false, Width = 760, Height = 26, ForeColor = Theme.Muted, Font = Theme.F(9),
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 4, 8, 0)
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
        list.DataSource = Db.Query(@"SELECT i.id, ROW_NUMBER() OVER (ORDER BY i.name) AS [ت], i.name AS [اسم المادة],
            IFNULL(w.name, (SELECT name FROM warehouses ORDER BY is_default DESC, id LIMIT 1)) AS [المخزن],
            IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0) AS [العدد]
            FROM items i LEFT JOIN warehouses w ON w.id=i.warehouse_id
            WHERE i.name LIKE @p0 OR IFNULL(i.code,'') LIKE @p0 OR EXISTS(SELECT 1 FROM item_barcodes x WHERE x.item_id=i.id AND x.barcode LIKE @p0)
            ORDER BY i.name", q);
        if (list.Columns.Contains("ت")) list.Columns["ت"].FillWeight = 30;
        listCard.Subtitle = $"{list.Rows.Count} مادة";
    }

    static string Cur(ComboBox cb) => cb.SelectedIndex == 1 ? "USD" : "IQD";
    static void SetCur(ComboBox cb, string v) => cb.SelectedIndex = v == "USD" ? 1 : 0;
    static void SetNum(NumericUpDown n, double v) => n.Value = (decimal)Math.Max((double)n.Minimum, Math.Min((double)n.Maximum, v));

    static void SetAlert(Toggle chk, NumericUpDown n, double v)
    {
        chk.Checked = v > 0;
        SetNum(n, v);
        n.Enabled = chk.Checked;
    }

    static double AlertValue(Toggle chk, NumericUpDown n) => chk.Checked ? (double)n.Value : 0;

    void New()
    {
        id = 0;
        Ui.SelectId(cbCompany, 0);
        Ui.SelectId(cbWh, Ui.DefaultWarehouse());
        cbType.SelectedIndex = 0; cbCost.SelectedIndex = 0; cbBuyCur.SelectedIndex = 0; cbSellCur.SelectedIndex = 0;
        foreach (var t in new[] { tName, tCode, tBarcode, tAlert, tOrigin, tMedical, tSerial }) t.Clear();
        lstBarcodes.Items.Clear();
        cbCategory.Text = ""; cbUnit.Text = "قطعة";
        foreach (var n in new[] { nQty, nBuy, nRetail, nWholesale, nSpecial, nInst, nWarranty }) n.Value = 0;
        tgMeasure.Checked = false; tgActive.Checked = true; tgSerial.Checked = false; tgScale.Checked = false;
        SetAlert(cMin, nMin, 0); SetAlert(cMax, nMax, 0); SetAlert(cSafety, nSafety, 0); SetAlert(cStagnant, nStagnant, 0); SetAlert(cTarget, nTarget, 0);
        cExpiry.Checked = false; nAlertDays.Value = Settings.Int("expiry_days", 30); nAlertDays.Enabled = false;
        dExpiry.Value = DateTime.Today.AddYears(1);
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
        Ui.SelectId(cbWh, Db.L(r["warehouse_id"]) > 0 ? Db.L(r["warehouse_id"]) : Ui.DefaultWarehouse());
        cbType.SelectedIndex = Db.S(r["item_type"]) == "خدمية" ? 1 : 0;
        cbCost.SelectedIndex = Db.S(r["cost_method"]) == "معدل الكلفة" ? 1 : 0;
        SetCur(cbBuyCur, Db.S(r["buy_currency"])); SetCur(cbSellCur, Db.S(r["sell_currency"]));
        tName.Text = Db.S(r["name"]); tCode.Text = Db.S(r["code"]); tBarcode.Clear();
        lstBarcodes.Items.Clear();
        foreach (var b in Barcodes.Of(itemId)) lstBarcodes.Items.Add(b);
        if (lstBarcodes.Items.Count == 0 && Db.S(r["barcode"]) != "") lstBarcodes.Items.Add(Db.S(r["barcode"]));
        cbCategory.Text = Db.S(r["category"]); cbUnit.Text = Db.S(r["unit"]);
        SetNum(nBuy, Db.D(r["price_buy"])); SetNum(nRetail, Db.D(r["price_retail"])); SetNum(nWholesale, Db.D(r["price_wholesale"]));
        SetNum(nSpecial, Db.D(r["price_special"])); SetNum(nInst, Db.D(r["price_installment"]));
        SetNum(nWarranty, Math.Round(Db.D(r["warranty_days"]) / 30.0));
        SetNum(nQty, Db.D(Db.Scalar("SELECT IFNULL(SUM(qty),0) FROM batches WHERE item_id=@p0", itemId)));
        tgMeasure.Checked = Db.L(r["by_measure"]) == 1; tgActive.Checked = Db.L(r["active"]) == 1;
        tgSerial.Checked = Db.L(r["track_serial"]) == 1; tgScale.Checked = Db.L(r["use_scale"]) == 1;
        tAlert.Text = Db.S(r["alert_note"]); tOrigin.Text = Db.S(r["origin"]); tMedical.Text = Db.S(r["medical_info"]);
        SetAlert(cMin, nMin, Db.D(r["min_qty"])); SetAlert(cMax, nMax, Db.D(r["max_qty"])); SetAlert(cSafety, nSafety, Db.D(r["safety_qty"]));
        SetAlert(cStagnant, nStagnant, Db.D(r["stagnant_days"])); SetAlert(cTarget, nTarget, Db.D(r["sales_target"]));
        // الصلاحية: أقرب تاريخ في المخزون، وفترة التنبيه الخاصة بالمادة
        var nearest = Db.S(Db.Scalar("SELECT MIN(expiry) FROM batches WHERE item_id=@p0 AND qty>0 AND IFNULL(expiry,'')<>''", itemId));
        long alertDays = Db.L(r["expiry_alert_days"]);
        cExpiry.Checked = nearest != "" || alertDays > 0;
        if (DateTime.TryParse(nearest, out var ne)) dExpiry.Value = ne;
        nAlertDays.Value = alertDays > 0 ? alertDays : Settings.Int("expiry_days", 30);
        nAlertDays.Enabled = cExpiry.Checked;
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
        // تاريخ الصلاحية يخص الكمية الافتتاحية؛ صلاحية الوجبات اللاحقة تُدخل في فاتورة الشراء
        dExpiry.Enabled = isNew && !service && cExpiry.Checked;
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

    void AddBarcode(string code)
    {
        code = (code ?? "").Trim();
        if (code == "") return;
        if (lstBarcodes.Items.Contains(code)) { tBarcode.Clear(); return; }
        long owner = Barcodes.Owner(code, id);
        if (owner > 0) { Ui.Warn($"الباركود {code} مستخدم للمادة «{Db.S(Db.Scalar("SELECT name FROM items WHERE id=@p0", owner))}»."); return; }
        lstBarcodes.Items.Add(code);
        lstBarcodes.SelectedIndex = lstBarcodes.Items.Count - 1;
        tBarcode.Clear();
        tBarcode.Focus();
    }

    void Save()
    {
        if (!Session.Guard("items")) return;
        var name = tName.Text.Trim();
        if (name == "") { Ui.Warn("أدخل اسم المادة."); tName.Focus(); return; }
        if (tBarcode.Text.Trim() != "") AddBarcode(tBarcode.Text);
        var codes = lstBarcodes.Items.Cast<string>().ToList();
        foreach (var c in codes)
            if (Barcodes.Owner(c, id) > 0) { Ui.Warn($"الباركود {c} مستخدم لمادة أخرى."); return; }
        bool service = cbType.SelectedIndex == 1;
        double qty = (double)nQty.Value, buy = (double)nBuy.Value;
        long wh = Ui.GetId(cbWh);
        if (id == 0 && qty > 0 && !service && wh == 0) { Ui.Warn("اختر المخزن للكمية الافتتاحية."); return; }
        if (cMin.Checked && cMax.Checked && nMax.Value > 0 && nMax.Value < nMin.Value) { Ui.Warn("الحد الأعلى أقل من الحد الأدنى."); return; }
        if (tgScale.Checked && !long.TryParse(tCode.Text.Trim(), out _))
        { Ui.Warn("مادة الميزان تحتاج «الرمز» رقمًا (رمز المادة في الميزان) — تجده في البيانات الإضافية."); return; }

        object[] vals =
        {
            name, tCode.Text.Trim(), codes.FirstOrDefault() ?? "", cbCategory.Text.Trim(), cbUnit.Text.Trim() == "" ? "قطعة" : cbUnit.Text.Trim(),
            (double)nRetail.Value, (double)nWholesale.Value, (double)nSpecial.Value, (double)nInst.Value, buy,
            AlertValue(cMin, nMin), tgMeasure.Checked ? 1 : 0, tMedical.Text.Trim(), tAlert.Text.Trim(), tgActive.Checked ? 1 : 0,
            (long)nWarranty.Value * 30, Db.N(Ui.GetId(cbCompany)), service ? "خدمية" : "اعتيادية", cbCost.SelectedIndex == 1 ? "معدل الكلفة" : "كلفة الوجبة",
            Cur(cbBuyCur), Cur(cbSellCur), tgSerial.Checked ? 1 : 0,
            tgScale.Checked ? 1 : 0, tOrigin.Text.Trim(), Db.N(wh), AlertValue(cMax, nMax), AlertValue(cSafety, nSafety),
            (long)AlertValue(cStagnant, nStagnant), AlertValue(cTarget, nTarget), cExpiry.Checked ? (long)nAlertDays.Value : 0
        };
        const string cols = "name,code,barcode,category,unit,price_retail,price_wholesale,price_special,price_installment,price_buy,min_qty,by_measure,medical_info,alert_note,active,warranty_days,company_id,item_type,cost_method,buy_currency,sell_currency,track_serial," +
                            "use_scale,origin,warehouse_id,max_qty,safety_qty,stagnant_days,sales_target,expiry_alert_days";
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
                    StockOps.StockIn(tx, wh, new[] { (id, qty, cost, cExpiry.Checked ? dExpiry.Value.ToString(Ui.DFmt) : "") },
                        "رصيد أول المدة — " + name);
                }
            }
            else
                tx.Exec($"UPDATE items SET {string.Join(",", colList.Select((c, i) => $"{c}=@p{i}"))} WHERE id=@p{colList.Length}", vals.Append(id).ToArray());
            Barcodes.Save(tx, id, codes);
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
            using (var tx = new Tx())
            {
                tx.Exec("DELETE FROM item_barcodes WHERE item_id=@p0", id);
                tx.Exec("DELETE FROM items WHERE id=@p0", id);
                tx.Commit();
            }
            Db.Audit("حذف مادة", tName.Text);
            Toast.Show("تم حذف المادة");
            LoadList();
            New();
        }
        catch { Ui.Warn("لا يمكن حذف هذه المادة لوجود حركات عليها (فواتير أو مخزون). يمكنك إيقافها من «مادة فعّالة»."); }
    }

    void AddSerial()
    {
        var n = tSerial.Text.Trim();
        if (n == "") return;
        if (id == 0) { Ui.Warn("احفظ المادة أولًا ثم أضف أرقامها التسلسلية."); return; }
        if (Db.L(Db.Scalar("SELECT COUNT(*) FROM item_serials WHERE serial=@p0", n)) > 0) { Ui.Warn($"الرقم {n} موجود مسبقًا."); tSerial.SelectAll(); return; }
        using (var tx = new Tx())
        {
            tx.Exec("INSERT INTO item_serials(item_id,serial,warehouse_id,created) VALUES(@p0,@p1,@p2,@p3)", id, n, Db.N(Ui.GetId(cbWh)), Ui.Now);
            if (!tgSerial.Checked) tx.Exec("UPDATE items SET track_serial=1 WHERE id=@p0", id);
            tx.Commit();
        }
        tgSerial.Checked = true;
        tSerial.Clear();
        tSerial.Focus();
        LoadSubGrids();
    }

    void DeleteSerial()
    {
        if (gSerials.CurrentRow == null || !Session.Guard("items")) return;
        if (Convert.ToString(gSerials.CurrentRow.Cells["الحالة"].Value) == "مباع") { Ui.Warn("لا يمكن حذف رقم مُباع."); return; }
        Db.Exec("DELETE FROM item_serials WHERE id=@p0", Db.L(gSerials.CurrentRow.Cells["id"].Value));
        LoadSubGrids();
    }
}
