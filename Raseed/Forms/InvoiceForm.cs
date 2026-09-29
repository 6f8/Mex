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
    readonly DateTimePicker dtDate = new() { Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
    readonly ComboBox cbDiscType = Ui.Combo(120);
    readonly NumericUpDown nDisc = Ui.Num(130), nPaid = Ui.Num(150), nFee = Ui.Num(100), nQtyIn = Ui.Num(90, 2), nPriceIn = Ui.Num(130, 2);
    readonly TextBox txtFind = new() { Width = 300, PlaceholderText = "اسم المادة ثم Enter" }, txtNotes = new() { Width = 260 },
                     tBarcode = new() { Width = 240, PlaceholderText = "امسح الباركود" },
                     tNo = new() { Width = 90, ReadOnly = true, TextAlign = HorizontalAlignment.Center },
                     tAddress = new() { Width = 180, ReadOnly = true }, tPhone = new() { Width = 140, ReadOnly = true },
                     tSumIn = new() { Width = 130, ReadOnly = true, TextAlign = HorizontalAlignment.Center };
    readonly Toggle chkPrint = new() { Text = "طباعة", Width = 110, Height = 34 }, chkInst = new() { Text = "تقسيط القائمة", Width = 190, Height = 34 };
    readonly DataGridView grid = Ui.NewGrid(false);
    readonly TotalsBox box = new();
    // سطر معلومات المادة أسفل الجدول (الرصيد، أقرب صلاحية، الأسعار)
    readonly Label lblInfo = new() { Dock = DockStyle.Bottom, Height = 28, Font = Theme.F(9.5f), ForeColor = Theme.Text2, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 4, 0) };
    readonly ToolTip infoTip = new();
    DataTable items;
    bool busy, paidTouched, settingFind;
    long editId, lastId, guarantorId;
    double oldEffect;     // أثر القائمة القديمة على رصيد الحساب (عند التعديل)
    double prevBalance;   // رصيد الحساب قبل هذه القائمة
    bool prevOver;        // الرصيد السابق تجاوز سقف الذمة
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
    Dictionary<string, long> barcodeMap = new();   // كل باركودات المواد (الباركودات المتعددة)
    // فهارس في الذاكرة للبحث الفوري بالمسح (كان البحث يمر على كل المواد سطرًا سطرًا مع كل مسح وكل تحديد)
    Dictionary<long, DataRow> byId = new();
    Dictionary<string, DataRow> byBarcode = new(), byCode = new();
    long itemsVersion = -1;

    record Line(long ItemId, string Name, double Len, double Wid, double Qty, double Price, string Expiry, string Serials, string Note);


    string PriceCaption => CostDoc ? "الكلفة" : type is "Purchase" or "PurchaseReturn" ? "سعر الشراء" : "سعر البيع";

    public InvoiceForm(string invoiceType, long editInvoiceId = 0)
    {
        type = invoiceType;
        editId = editInvoiceId;
        Text = Ui.DocTitle(type);
        KeyPreview = true;

        // ================= الترتيب المألوف: العنوان بجانب الحقل، لوحة المواد والجدول، والمجاميع أسفل اليسار =================
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 4, 10, 8) };

        // ---------- الترويسة (سطران، والحقول الإضافية في سطر ثالث يُطوى) ----------
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Surface, Padding = new Padding(0, 2, 0, 6) };
        var rowList = headRow1;   // السطر الحالي من الترويسة (للتصغير المتناسب)
        void H(Control c) { head.Controls.Add(c); if (c is InlineField f) rowList?.Add(f); }
        if (HasParty)
        {
            string kind = SaleSide ? "عميل" : "مورد";
            Ui.FillCombo(cbParty, "SELECT id,name,price_level,phone,credit_limit,address FROM parties WHERE kind=@p0 OR kind='عميل ومورد' ORDER BY name",
                true, SaleSide ? "زبون نقدي" : "— بدون مورد —", kind);
            Ui.MakeSearchable(cbParty);
            H(new InlineField("الحساب", cbParty, 230));
            H(new InlineField("العنوان", tAddress, 150));
        }
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id");
        Ui.SelectId(cbWh, Ui.DefaultWarehouse());
        H(new InlineField("المخزن", cbWh, 150));
        dtDate.Value = DateTime.Now;
        H(new InlineField("التأريخ", dtDate, 130));
        var noField = new InlineField(Ui.IsStockDoc(type) ? "رقم السند" : "رقم القائمة", tNo, 80);
        H(noField);
        head.SetFlowBreak(noField, true);
        rowList = headRow2;

        H(new InlineField("الباركود", tBarcode, 230));
        if (HasParty) H(new InlineField("الهاتف", tPhone, 150));
        if (HasPayment)
        {
            Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
            H(new InlineField("الصندوق", cbBox, 190));
        }
        if (SaleSide)
        {
            cbLevel.Items.AddRange(new object[] { "مفرد", "جملة", "خاص" });
            cbLevel.SelectedIndex = 0;
            if (Features.On("feat_three_prices")) H(new InlineField("نوع السعر", cbLevel, 110));
        }
        var who = new Label { Text = $"المستخدم: {Session.UserName}", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.F(9), Margin = new Padding(14, 15, 6, 0) };
        H(who);

        Ui.FillCombo(cbCC, "SELECT id,name FROM cost_centers ORDER BY id", true);
        if (type == "Sale")
        {
            Ui.FillCombo(cbDel, "SELECT id,name,fee FROM delivery_companies ORDER BY name", true, "— بدون توصيل —");
            cbDel.SelectedIndexChanged += (s, e) => { var r = Ui.GetRow(cbDel); Ui.SetNum(nFee, r == null ? 0 : Db.D(r["fee"])); };
        }
        // زر «المزيد»: الحقول الثانوية تُطوى تلقائيًا في الشاشات القصيرة لتبقى مساحة كافية لأسطر القائمة
        var bMore = new ModernButton { Kind = BtnKind.Ghost, IconName = "chevron-down", Size = new Size(40, 40), Margin = new Padding(6, 3, 4, 3), TabStop = false };
        new ToolTip().SetToolTip(bMore, "إظهار / إخفاء الحقول الإضافية (مركز الكلفة، التوصيل، الملاحظات)");
        bMore.Click += (s, e) => { moreManual = !(moreManual ?? MoreVisible); ShowMore(moreManual.Value); bMore.IconName = moreManual.Value ? "chevron-up" : "chevron-down"; bMore.Invalidate(); };
        H(bMore);
        head.SetFlowBreak(bMore, true);
        headExtras = new Control[] { who, bMore };
        // حقول الترويسة تصغر بالتناسب ليبقى كل سطر في سطر واحد مهما ضاقت الشاشة
        head.Resize += (s, e) => FitHeader();
        rowList = null;   // الحقول الإضافية (السطر المطوي) تبقى بأحجامها
        if (!IsQuote) H(More(new InlineField("مركز الكلفة", cbCC, 160)));
        if (type == "Sale")
        {
            H(More(new InlineField("شركة التوصيل", cbDel, 170)));
            H(More(new InlineField("أجور التوصيل", nFee, 110)));
        }
        H(More(new InlineField("ملاحظات", txtNotes, 260)));
        tBarcode.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ScanBarcode(); } };

        // ---------- لوحة المواد: سطر الإدخال ثم الجدول ----------
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10, 6, 10, 4) };
        var entry = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Panel };
        var bAdd = new ModernButton { Kind = BtnKind.Ghost, IconName = "arrow-left-right", Size = new Size(50, 44), Dock = DockStyle.Left, TabStop = false };
        new ToolTip().SetToolTip(bAdd, "إضافة المادة إلى القائمة (Enter)");
        var entryFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(0, 2, 0, 0) };
        var findField = new InlineField("المادة", txtFind, 300);
        nQtyIn.Minimum = 0; nQtyIn.Value = 1;
        entryFlow.Controls.Add(findField);
        entryFlow.Controls.Add(new InlineField("العدد", nQtyIn, 80));
        entryFlow.Controls.Add(new InlineField(PriceCaption, nPriceIn, 120));
        entryFlow.Controls.Add(new InlineField("المجموع", tSumIn, 130));
        // حقل المادة يأخذ ما يتبقى من العرض (الشاشات الضيقة والعريضة)
        entryFlow.Resize += (s, e) =>
        {
            int others = entryFlow.Controls.Cast<Control>().Where(c => c != findField).Sum(c => c.Width + c.Margin.Horizontal);
            int w = Math.Clamp(entryFlow.ClientSize.Width - others - findField.Margin.Horizontal - Dpi.S(8), Dpi.S(220), Dpi.S(520));
            if (findField.Width != w) findField.Width = w;
        };
        entry.Controls.Add(entryFlow);
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
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = Theme.Surface;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        grid.GridColor = Theme.Border;
        grid.ColumnHeadersDefaultCellStyle.BackColor = grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.FS(10.5f);
        grid.DefaultCellStyle.Font = Theme.F(10.5f);
        grid.DefaultCellStyle.SelectionBackColor = Theme.BrandDark;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        AddCol("no", "ت", true, 22);
        AddCol("item_id", "", true, 10, false);
        AddCol("measure", "", true, 10, false);
        AddCol("code", "الرمز", true, 55, false);
        AddCol("name", "المادة", true, 320);
        AddCol("unit", "الوحدة", true, 50, false);
        AddCol("len", "الطول", false, 55, false);
        AddCol("wid", "العرض", false, 55, false);
        AddCol("qty", "العدد", false, 50);
        AddCol("wh", "المخزن", true, 110, Features.On("feat_warehouses"));
        AddCol("price", PriceCaption, !Session.Can("edit_price") || type is "Damage" or "StockOut", 75);
        AddCol("total", "المجموع", true, 80);
        // تاريخ الصلاحية: يُدخل في الشراء والإرجاع وإدخال المخزن، ويُعرض (أقرب صلاحية) في البيع والإخراج
        AddCol("expiry", "تأريخ الصلاحية", !UsesExpiry, 80);
        AddCol("serials", "الرقم التسلسلي", true, 90, type is "Sale" or "SaleReturn" && Features.On("feat_serials"));   // الأرقام الممسوحة لهذا السطر
        AddCol("note", "الملاحظة", false, 80);
        grid.Columns["note"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        foreach (var c in new[] { "price", "total" }) grid.Columns[c].DefaultCellStyle.Format = "#,0.##' د.ع'";
        // الأرقام تُكتب بلا فواصل أو عملة؛ نزيلها عند التعديل حتى لا تُرفض القيمة
        grid.CellParsing += (s, e) =>
        {
            if (e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name is not ("qty" or "price" or "len" or "wid") || e.Value is not string str) return;
            var clean = new string(str.Where(ch => ch is >= '0' and <= '9' or '.' or '-').ToArray());
            if (double.TryParse(clean, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            { e.Value = d; e.ParsingApplied = true; }
        };
        grid.RowsAdded += (s, e) => Renumber();
        grid.RowsRemoved += (s, e) => Renumber();
        grid.CellEndEdit += (s, e) => { RecalcRow(e.RowIndex); Totals(); };
        grid.SelectionChanged += (s, e) => { if (grid.CurrentRow != null) ShowInfo(ItemRow(Db.L(grid.CurrentRow.Cells["item_id"].Value)), false); };
        grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && grid.CurrentRow != null && !grid.IsCurrentCellInEditMode) { var row = grid.CurrentRow; e.Handled = true; BeginInvoke(() => RemoveLine(row)); } };
        // النقر بالزر الأيمن على سطر: حذفه بعد التأكيد (الحذف بعد انتهاء حدث النقر)
        grid.CellMouseUp += (s, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            var row = grid.Rows[e.RowIndex];
            BeginInvoke(() => { if (Ui.Confirm($"حذف السطر «{row.Cells["name"].Value}» من القائمة؟")) RemoveLine(row); });
        };
        panel.Controls.Add(grid);
        panel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Panel });
        panel.Controls.Add(entry);
        panel.Controls.Add(lblInfo);
        lblInfo.BackColor = Theme.Panel;

        // ---------- التذييل: الخصم والواصل، الأزرار، ولوحة المجاميع في اليسار ----------
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 124, BackColor = Theme.Surface };
        var rowA = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Surface, Padding = new Padding(0, 4, 0, 0) };
        var rowB = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Surface, Padding = new Padding(0, 4, 0, 0) };
        // خيارا «طباعة» و«تقسيط»: بجانب الأزرار إن اتسع السطر، وإلا فوق لوحة المجاميع
        var toggles = new FlowLayoutPanel { WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 2, 0, 0) };
        cbDiscType.Items.AddRange(new object[] { "مقطوعة", "نسبة %" });
        cbDiscType.SelectedIndex = 0;
        if (HasParty)
        {
            nDisc.Enabled = cbDiscType.Enabled = Session.Can("discount");
            rowA.Controls.Add(new InlineField("نوع الخصم", cbDiscType, 120));
            rowA.Controls.Add(new InlineField("الخصم", nDisc, 120));
        }
        if (HasPayment)
        {
            rowA.Controls.Add(new InlineField("الواصل", nPaid, 140));
        }
        box.Define(HasPayment ? new[] { "مجموع القائمة", "بعد الخصم", "الباقي", "الرصيد السابق", "الرصيد الحالي" }
                   : HasParty ? new[] { "مجموع القائمة", "المجموع بعد الخصم" } : new[] { "مجموع القائمة" });

        bool stockDoc = Ui.IsStockDoc(type);
        ModernButton Big(string text, BtnKind kind)
        {
            var b = new ModernButton { Text = text, Kind = kind, Font = Theme.FS(12), Size = new Size(104, 46), Margin = new Padding(0, 2, 8, 2), Radius = 6 };
            rowB.Controls.Add(b);
            return b;
        }
        var bNew = Big("جديد", BtnKind.Primary);
        var bSave = Big("حفظ", BtnKind.Primary);
        var bDel = Big("حذف", BtnKind.Coral);
        var bPrint = Big("طباعة", BtnKind.Amber);
        bPrint.Visible = Session.Can("print");
        new ToolTip().SetToolTip(bPrint, stockDoc ? "طباعة السند المفتوح أو آخر سند محفوظ" : "طباعة القائمة المفتوحة أو آخر قائمة محفوظة");
        if (IsQuote)
        {
            var bConvert = Big("تحويل", BtnKind.Primary);
            new ToolTip().SetToolTip(bConvert, "تحويل عرض السعر إلى قائمة بيع");
            bConvert.Click += (s, e) => ConvertToSale();
        }
        chkPrint.Checked = Session.Can("print") && Settings.Get("print_after_save", "2") == "1";
        chkPrint.Visible = Session.Can("print");
        chkPrint.Margin = chkInst.Margin = new Padding(8, 8, 4, 0);
        chkPrint.Width = 96;
        chkInst.Text = "تقسيط";
        chkInst.Width = 130;
        toggles.Controls.Add(chkPrint);
        if (type == "Sale" && Features.On("feat_installments")) toggles.Controls.Add(chkInst);
        bSave.Click += (s, e) => Save();
        bNew.Click += (s, e) => { if (ConfirmClose()) { editId = 0; Reset(); } };
        bDel.Click += (s, e) => DeleteCurrent();
        bPrint.Click += (s, e) =>
        {
            long pid = editId > 0 ? editId : lastId;
            if (pid > 0) InvoiceOps.BuildPrint(pid)?.Print(); else Ui.Warn("لم تُحفظ أي قائمة بعد في هذه الشاشة.");
        };
        foot.Controls.Add(rowA);
        foot.Controls.Add(rowB);
        foot.Controls.Add(toggles);
        foot.Controls.Add(box);
        // ترتيب التذييل يدويًا: المجاميع في اليسار، والسطران في اليمين يلتفان عند الضيق ويكبر التذييل معهما
        bool arranging = false;
        foot.Layout += (s, e) =>
        {
            if (arranging) return;
            arranging = true;
            try
            {
                int W = foot.ClientSize.Width, gap = Dpi.S(16);
                int bw = Math.Min(box.PreferredWidth, W * 42 / 100), ra = Math.Max(Dpi.S(200), W - bw - gap);
                // حقول الخصم والواصل تصغر بالتناسب لتبقى في سطر واحد
                InlineField.FitRow(rowA.Controls.OfType<InlineField>().ToList(), ra - rowA.Padding.Horizontal, Dpi.S(88));
                int ha = rowA.Controls.Count == 0 ? 0 : rowA.GetPreferredSize(new Size(ra, 0)).Height;
                int hb = rowB.GetPreferredSize(new Size(ra, 0)).Height;
                int bh = Dpi.S(88), top = Dpi.S(8);
                var ts = toggles.GetPreferredSize(Size.Empty);
                int nb = rowB.GetPreferredSize(Size.Empty).Width;
                bool beside = toggles.Controls.Cast<Control>().All(c => !c.Visible) || nb + ts.Width <= ra;
                int h = Math.Max(ha + hb, bh + (beside ? 0 : ts.Height)) + top + Dpi.S(4);
                rowA.SetBounds(W - ra, top, ra, ha);
                rowB.SetBounds(W - ra, top + ha, ra, hb);
                box.SetBounds(0, h - bh - Dpi.S(4), bw, bh);
                if (beside)
                {
                    rowB.SetBounds(W - Math.Min(ra, nb), top + ha, Math.Min(ra, nb), hb);
                    toggles.SetBounds(Math.Max(0, W - nb - ts.Width), top + ha + (hb - ts.Height) / 2, ts.Width, ts.Height);
                }
                else toggles.SetBounds(0, h - bh - Dpi.S(4) - ts.Height, Math.Min(bw, ts.Width), ts.Height);
                // تغيير ارتفاع التذييل أثناء ترتيبه يترك موضعه قديمًا؛ يُؤجَّل حتى ينتهي الترتيب الحالي
                if (foot.Height != h)
                {
                    if (foot.IsHandleCreated) foot.BeginInvoke(() => { if (!foot.IsDisposed && foot.Height != h) foot.Height = h; });
                    else foot.Height = h;
                }
            }
            finally { arranging = false; }
        };

        root.Controls.Add(panel);
        root.Controls.Add(foot);
        root.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Surface });
        root.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        root.Controls.Add(head);
        Controls.Add(root);
        headBar = head;
        Resize += (s, e) => FitHeight();

        // ---------- الأحداث ----------
        nDisc.ValueChanged += (s, e) => Totals();
        cbDiscType.SelectedIndexChanged += (s, e) =>
        {
            // نسبة مئوية: حتى 100 بمنزلتين عشريتين؛ مقطوعة: مبلغ
            bool pct = DiscPercent;
            nDisc.Value = 0;
            nDisc.DecimalPlaces = pct ? 2 : 0;
            nDisc.Maximum = pct ? 100 : 1_000_000_000_000m;
            Totals();
        };
        nFee.ValueChanged += (s, e) => Totals();
        nPaid.ValueChanged += (s, e) => { if (!busy) { paidTouched = true; Totals(); } };
        // سعر الأقساط يختلف عن السعر النقدي إن وُجد
        chkInst.CheckedChanged += (s, e) => { if (!busy) { paidTouched = false; Reprice(); } };
        cbParty.SelectedIndexChanged += (s, e) => PartyChanged();
        cbLevel.SelectedIndexChanged += (s, e) => Reprice();
        cbWh.SelectedIndexChanged += (s, e) =>
        {
            foreach (DataGridViewRow g in grid.Rows) g.Cells["wh"].Value = cbWh.Text;
            if (grid.CurrentRow != null) ShowInfo(ItemRow(Db.L(grid.CurrentRow.Cells["item_id"].Value)), false);
        };
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F2) { tBarcode.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.F3) { txtFind.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.F10) { Save(); e.Handled = true; }
        };

        LoadItems();
        lblInfo.Text = "امسح الباركود في «الباركود» (F2) فتُضاف المادة مباشرة، أو اكتب اسمها في «المادة» (F3) ثم العدد والسعر و Enter.  •  الزر الأيمن على السطر لحذفه.";
        ShowNumber();
        PartyChanged();
        Totals();
        if (editId > 0) LoadInvoice(editId);
        Shown += (s, e) => tBarcode.Focus();
    }

    // ---------- التكيف مع حجم الشاشة ----------
    readonly List<Control> moreFields = new();
    bool? moreManual;
    Control headBar;
    bool MoreVisible => moreFields.Count > 0 && moreFields[0].Visible;

    Control More(Control c) { moreFields.Add(c); return c; }

    readonly List<InlineField> headRow1 = new(), headRow2 = new();
    Control[] headExtras = Array.Empty<Control>();

    /// <summary>يصغّر حقول كل سطر من الترويسة بالتناسب (العناوين كما هي) حتى يتسع السطر دون التفاف</summary>
    void FitHeader()
    {
        if (headBar == null) return;
        int avail = headBar.ClientSize.Width - headBar.Padding.Horizontal - Dpi.S(6);
        InlineField.FitRow(headRow1, avail, Dpi.S(96));
        // اسم المستخدم معلومة ثانوية: يُخفى إن لم يتسع له السطر الثاني (حتى لا ينزل زر «المزيد» لسطر وحده)
        if (headExtras.Length > 1)
        {
            int need = headRow2.Sum(f => f.CaptionWidth + f.Margin.Horizontal + Math.Min(Dpi.S(96), f.NaturalField)) + Dpi.S(10);
            var who = headExtras[0];
            bool fits = need + headExtras.Sum(c => c.PreferredSize.Width + c.Margin.Horizontal) <= avail;
            if (who.Visible != fits) who.Visible = fits;
        }
        InlineField.FitRow(headRow2, avail - headExtras.Where(c => c.Visible).Sum(c => c.Width + c.Margin.Horizontal), Dpi.S(96));
    }


    void ShowMore(bool on)
    {
        if (moreFields.Count == 0 || MoreVisible == on) return;
        headBar.SuspendLayout();
        foreach (var c in moreFields) c.Visible = on;
        headBar.ResumeLayout(true);
    }

    /// <summary>
    /// الأولوية لأسطر القائمة مع بقاء المجاميع وأزرار الحفظ ظاهرة دائمًا: في الشاشات القصيرة تُطوى الحقول الإضافية،
    /// وفي الضيقة تُخفى لوحة معلومات المادة (والجدول يمرَّر داخليًا).
    /// </summary>
    void FitHeight()
    {
        if (headBar == null) return;
        if (moreManual == null) ShowMore(ClientSize.Height >= Dpi.S(760));
    }

    void AddCol(string name, string header, bool readOnly, int weight, bool visible = true)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = header, ReadOnly = readOnly, Visible = visible, FillWeight = weight,
            MinimumWidth = Math.Max(Dpi.S(name == "no" ? 40 : 60), TextRenderer.MeasureText(header, Theme.FS(9.5f)).Width + Dpi.S(26)),
            DefaultCellStyle = { Format = "#,0.##", BackColor = Theme.Surface, Alignment = name is "name" ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleCenter }
        });
    }

    void RemoveLine(DataGridViewRow row)
    {
        if (IsDisposed || row.DataGridView != grid || row.Index < 0) return;
        try
        {
            if (grid.IsCurrentCellInEditMode) grid.CancelEdit();
            grid.Rows.Remove(row);
        }
        catch (InvalidOperationException) { return; }
        Totals();
    }

    void Renumber()
    {
        foreach (DataGridViewRow r in grid.Rows) r.Cells["no"].Value = r.Index + 1;
    }

    void ShowNumber() => tNo.Text = (editId > 0 ? editId : Db.L(Db.Scalar("SELECT IFNULL(MAX(id),0)+1 FROM invoices"))).ToString();

    // مواد جديدة قد تُعرَّف في تبويب آخر أثناء بقاء القائمة مفتوحة (يُعاد التحميل فقط إذا تغيّرت المواد فعلًا)
    public override void OnPageActivated() { if (itemsVersion != Db.ItemsVersion) LoadItems(); }

    public override bool ConfirmClose() =>
        grid.Rows.Count == 0 || Ui.Confirm($"{Text}: فيها {grid.Rows.Count} مادة لم تُحفظ.\nإغلاقها بدون حفظ؟");

    void LoadItems()
    {
        itemsVersion = Db.ItemsVersion;
        items = Db.Query(@"SELECT id,code,barcode,name,unit,price_retail,price_wholesale,price_special,price_installment,price_buy,
            IFNULL(buy_currency,'IQD') AS buy_currency, IFNULL(sell_currency,'IQD') AS sell_currency, IFNULL(item_type,'اعتيادية') AS item_type,
            IFNULL(cost_method,'كلفة الوجبة') AS cost_method, by_measure,medical_info,alert_note,IFNULL(use_scale,0) AS use_scale FROM items
            WHERE active=1 OR id IN (SELECT item_id FROM invoice_lines WHERE invoice_id=@p0)", editId);
        barcodeMap = Db.Query("SELECT barcode, item_id FROM item_barcodes").Rows.Cast<DataRow>()
            .GroupBy(x => Db.S(x["barcode"])).ToDictionary(g => g.Key, g => Db.L(g.First()["item_id"]));
        byId = new(); byBarcode = new(); byCode = new();
        foreach (DataRow r in items.Rows)
        {
            byId[Db.L(r["id"])] = r;
            var bc = Db.S(r["barcode"]); if (bc != "") byBarcode.TryAdd(bc, r);
            var cd = Db.S(r["code"]); if (cd != "") byCode.TryAdd(cd, r);
        }
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

    DataRow ItemRow(long id) => byId.TryGetValue(id, out var r) ? r : null;

    /// <summary>الرقم التسلسلي الممسوح (من قاعدة البيانات مباشرة: لا تحميل لكل الأرقام في الذاكرة)</summary>
    static DataRow FindSerial(string t)
    {
        var dt = Db.Query("SELECT serial, item_id, invoice_id FROM item_serials WHERE serial=@p0 COLLATE NOCASE LIMIT 1", t);
        return dt.Rows.Count > 0 ? dt.Rows[0] : null;
    }

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
        var sr = IsOut || type == "SaleReturn" ? FindSerial(t) : null;
        if (sr != null)
        {
            bool sold = Db.L(sr["invoice_id"]) > 0;
            if (IsOut && sold) { Ui.Warn($"الرقم التسلسلي {t} مُباع مسبقًا."); return; }
            if (type == "SaleReturn" && !sold) { Ui.Warn($"الرقم التسلسلي {t} غير مُباع، لا يمكن إرجاعه."); return; }
            if (grid.Rows.Cast<DataGridViewRow>().Any(g => (Convert.ToString(g.Cells["serials"].Value) ?? "").Split(',').Contains(Db.S(sr["serial"]))))
            { Ui.Warn($"الرقم التسلسلي {t} موجود في القائمة."); return; }
            var ir = ItemRow(Db.L(sr["item_id"]));
            if (ir != null) { AddItem(ir, Db.S(sr["serial"]), 1); ClearEntry(); return; }
        }
        var exact = (byBarcode.TryGetValue(t, out var byBc) ? byBc : null)
             ?? (barcodeMap.TryGetValue(t, out var bid) ? ItemRow(bid) : null)
             ?? (byCode.TryGetValue(t, out var byCd) ? byCd : null);
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
        Ui.SetNum(nPriceIn, Math.Max(0, PriceFor(r)));
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
        g.Cells["expiry"].Value = !UsesExpiry && string.IsNullOrEmpty(expiry) ? NearestExpiry(Db.L(r["id"])) : expiry ?? "";
        g.Cells["wh"].Value = cbWh.Text;
        g.Cells["note"].Value = note ?? "";
        if (!measure) { g.Cells["len"].ReadOnly = true; g.Cells["wid"].ReadOnly = true; }
        else g.Cells["qty"].ReadOnly = true;
        RecalcRow(i);
        return g;
    }

    /// <summary>أقرب تاريخ صلاحية متوفر للمادة في المخزن المختار (للعرض في البيع والإخراج)</summary>
    string NearestExpiry(long itemId) =>
        Db.S(Db.Scalar("SELECT MIN(expiry) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1 AND qty>0 AND expiry IS NOT NULL AND expiry<>''", itemId, Ui.GetId(cbWh)));

    /// <summary>حقل «الباركود» في الترويسة: المسح يضيف المادة مباشرة (أو الرقم التسلسلي أو باركود الميزان)</summary>
    void ScanBarcode()
    {
        var t = tBarcode.Text.Trim();
        if (t == "") return;
        settingFind = true;
        txtFind.Text = t;
        settingFind = false;
        FindAndAdd();
        tBarcode.Clear();
        // أُضيفت المادة: يبقى المؤشر في الباركود للمسح التالي؛ وإن كان اسمًا فينتقل إلى العدد
        if (pending == null) tBarcode.Focus();
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
        if (type == "Sale") { Ui.SelectId(cbDel, Db.L(v["delivery_id"])); Ui.SetNum(nFee, Db.D(v["delivery_fee"])); }
        if (DateTime.TryParse(Db.S(v["date"]), out var d)) dtDate.Value = d;
        txtNotes.Text = Db.S(v["notes"]);
        busy = false;
        AddLinesFrom(id, keepPrices: true);
        cbDiscType.SelectedIndex = 0;
        Ui.SetNum(nDisc, Db.D(v["discount"]));
        oldEffect = HasPayment ? Sign * (Db.D(v["net"]) - Db.D(v["paid"])) : 0;
        paidTouched = true;
        busy = true; Ui.SetNum(nPaid, Db.D(v["paid"])); busy = false;
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
        f.cbDiscType.SelectedIndex = 0;
        Ui.SetNum(f.nDisc, Db.D(v.Rows[0]["discount"]));
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
    bool DiscPercent => cbDiscType.SelectedIndex == 1;
    /// <summary>الخصم بالمبلغ: مقطوع كما كُتب، أو نسبة من مجموع القائمة</summary>
    double Disc => DiscPercent ? Math.Round(Total * (double)nDisc.Value / 100.0, 2) : (double)nDisc.Value;
    double Net => Total - Disc + (double)nFee.Value;
    static string Money(double v) => Ui.M(v) + " د.ع";

    void Totals()
    {
        bool was = busy;
        busy = true;
        box.Set(0, Money(Total));
        if (HasParty) box.Set(1, Money(Net), null, true);
        if (HasPayment)
        {
            // الواصل = كامل المبلغ ما لم يغيّره المستخدم (والمقدّم صفر عند التقسيط)
            if (!paidTouched) Ui.SetNum(nPaid, chkInst.Checked ? 0 : Math.Max(0, Net));
            double remain = Net - (double)nPaid.Value;
            box.Set(2, Money(remain), remain > 0.005 ? Theme.Danger : remain < -0.005 ? Theme.Warning : Theme.Success);
            box.Set(3, Money(prevBalance), prevOver ? Theme.Danger : null);
            box.Set(4, Money(prevBalance + Sign * remain), Theme.BrandDark);
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
        prevOver = lim > 0 && prevBalance > lim;
        Totals();
    }

    void ShowInfo(DataRow r, bool alert)
    {
        if (r == null) return;
        long id = Db.L(r["id"]), wh = Ui.GetId(cbWh);
        double stock = Db.D(Db.Scalar("SELECT IFNULL(SUM(qty),0) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1", id, wh));
        var exp = Db.S(Db.Scalar("SELECT MIN(expiry) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1 AND qty>0 AND expiry IS NOT NULL AND expiry<>''", id, wh));
        string med = Db.S(r["medical_info"]), note = Db.S(r["alert_note"]);
        lblInfo.Text = $"{Db.S(r["name"])}   •   الرصيد في المخزن: {Ui.M(stock)} {Db.S(r["unit"])}   •   أقرب صلاحية: {(exp == "" ? "—" : exp)}" +
                       $"   •   مفرد {Ui.M(Db.D(r["price_retail"]))}  /  جملة {Ui.M(Db.D(r["price_wholesale"]))}  /  خاص {Ui.M(Db.D(r["price_special"]))}" +
                       (note != "" ? $"   •   ⚠ {note}" : "");
        infoTip.SetToolTip(lblInfo, lblInfo.Text + (med != "" ? "\n\nالبيانات الطبية:\n" + med : ""));
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
            // في البيع والإخراج يُعرض تاريخ أقرب وجبة للعلم فقط؛ الصرف الفعلي بطريقة FEFO عند الحفظ
            string exp = UsesExpiry ? Convert.ToString(g.Cells["expiry"].Value)?.Trim() ?? "" : "";
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
        double total = Total, disc = Disc, fee = (double)nFee.Value, net = Net, paid = HasPayment ? (double)nPaid.Value : 0;
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
        cbDiscType.SelectedIndex = 0;
        nDisc.Value = 0; nFee.Value = 0; nPaid.Value = 0;
        busy = false;
        paidTouched = false;
        if (cbDel.Items.Count > 0) cbDel.SelectedIndex = 0;
        txtNotes.Clear();
        dtDate.Value = DateTime.Now;
        // المواد لا تتغير بحفظ القائمة: إعادة تحميلها (مع قائمة الإكمال التلقائي) بعد كل حفظ كانت تبطئ البيع
        if (itemsVersion != Db.ItemsVersion) LoadItems();
        ShowNumber();
        PartyChanged();
        lblInfo.Text = "";
        ClearEntry();
    }
}

/// <summary>لوحة المجاميع أسفل القائمة: أعمدة بعنوان ملون وقيمة كبيرة (مجموع القائمة، بعد الخصم، الباقي، الرصيد)</summary>
public class TotalsBox : Control
{
    readonly List<(string Caption, string Value, Color? Color, bool Big)> cols = new();

    public TotalsBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void Define(params string[] captions)
    {
        cols.Clear();
        foreach (var c in captions) cols.Add((c, "0", null, false));
        Invalidate();
    }

    public void Set(int i, string value, Color? color = null, bool big = false)
    {
        if (i < 0 || i >= cols.Count) return;
        cols[i] = (cols[i].Caption, value, color, big);
        Invalidate();
    }

    static string Short(string c) => c switch
    {
        "مجموع القائمة" => "المجموع", "المجموع بعد الخصم" => "بعد الخصم", "الرصيد السابق" => "السابق", "الرصيد الحالي" => "الحالي", _ => c
    };

    /// <summary>العرض المناسب بالبكسل الفعلي</summary>
    public int PreferredWidth => Math.Max(1, cols.Count) * Dpi.S(cols.Count > 3 ? 104 : 150);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var bg = Gfx.Mix(Theme.Orange, Color.White, 0.86f);
        var line = Gfx.Mix(Theme.Orange, Color.White, 0.55f);
        var cap = Gfx.Mix(Theme.Orange, Color.Black, 0.12f);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Gfx.FillRound(g, r, Dpi.S(6f), bg);
        Gfx.DrawRound(g, r, Dpi.S(6f), line, Math.Max(1f, Dpi.S(1.2f)));
        if (cols.Count == 0) return;
        float cw = (float)Width / cols.Count;
        // إن ضاق عمود عن عنوانه تُختصر العناوين كلها معًا (مظهر متناسق)
        int capW = (int)cw - Dpi.S(8);
        bool shortAll = cols.Any(c => TextRenderer.MeasureText(c.Caption, Theme.FS(9)).Width > capW);
        for (int i = 0; i < cols.Count; i++)
        {
            // من اليمين: العمود الأول في أقصى اليمين
            float x = Width - (i + 1) * cw;
            if (i > 0)
                using (var pen = new Pen(line, Math.Max(1f, Dpi.S(1f))))
                    g.DrawLine(pen, x + cw, Dpi.S(12), x + cw, Height - Dpi.S(12));
            var (caption, value, color, big) = cols[i];
            var tr = new Rectangle((int)x + Dpi.S(4), Dpi.S(6), (int)cw - Dpi.S(8), Dpi.S(34));
            // خط العنوان يصغر قليلًا إن ضاق العمود، ثم ينقسم على سطرين حتى يظهر كاملًا
            var cf = Theme.FS(10);
            foreach (var sz in new[] { 9.5f, 9f })
                if (TextRenderer.MeasureText(caption, cf).Width > tr.Width) cf = Theme.FS(sz);
            // عمود ضيق جدًا: العنوان المختصر (السابق، الحالي...)
            if (shortAll) caption = Short(caption);
            TextRenderer.DrawText(g, caption, cf, tr, cap,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft | TextFormatFlags.NoPadding);
            var vr = new Rectangle((int)x + Dpi.S(2), Dpi.S(40), (int)cw - Dpi.S(4), Height - Dpi.S(46));
            var vf = Theme.FS(big ? 14 : 13);
            foreach (var sz in new[] { 12f, 11f, 10f, 9f })
                if (TextRenderer.MeasureText(value, vf).Width > vr.Width) vf = Theme.FS(sz);
            TextRenderer.DrawText(g, value, vf, vr, color ?? (big ? cap : Theme.Text2), Gfx.Center);
        }
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
        var addG = new ModernButton { Kind = BtnKind.Success, IconName = "circle-plus", Size = new Size(40, 40), Margin = new Padding(4, 24, 4, 0) };
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
