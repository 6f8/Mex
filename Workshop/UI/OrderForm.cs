using System.Drawing.Imaging;
using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>زر فحص الاستلام: يُضغط للتبديل بين «لم يُفحص» ← «يعمل» ← «لا يعمل»</summary>
public class TriChip : Control
{
    string state = "";
    bool hover;
    public string Key { get; init; }
    public string State { get => state; set { state = value ?? ""; Invalidate(); } }
    public bool Disabled { get; set; }

    public TriChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(176, 36);
        Margin = new Padding(4);
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (Disabled) return;
        State = state == "" ? "ok" : state == "ok" ? "bad" : "";
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Gfx.OpaqueBack(this));
        Gfx.Hq(g);
        var (bg, border, fg, mark) = state switch
        {
            "ok" => (Pal.GoodSoft, Gfx.Mix(Pal.Good, Color.White, 0.5f), Pal.Good, "✓"),
            "bad" => (Pal.BadSoft, Gfx.Mix(Pal.Bad, Color.White, 0.5f), Pal.Bad, "✕"),
            _ => (hover ? Theme.SurfaceAlt : Theme.Surface, Theme.BorderStrong, Theme.Text2, ""),
        };
        if (Disabled) { bg = Theme.SurfaceAlt; fg = Theme.Subtle; }
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Gfx.FillRound(g, r, S(9f), bg);
        Gfx.DrawRound(g, r, S(9f), border);
        var box = new Rectangle(Width - S(30), (Height - S(20)) / 2, S(20), S(20));
        Gfx.FillRound(g, box, S(5f), state == "" ? Theme.GraySoft : fg);
        if (mark != "") TextRenderer.DrawText(g, mark, Theme.FS(9), box, Color.White, Gfx.Center);
        TextRenderer.DrawText(g, Text, Theme.F(9), new Rectangle(S(6), 0, Width - S(42), Height), fg, Gfx.RtlStart | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>طلب صيانة جديد / تعديل طلب</summary>
public partial class OrderForm : DialogShell
{
    readonly string id;
    readonly Order existing;
    readonly TextBox tName = new() { Width = 300 }, tPhone = new() { Width = 200 }, tDevice = new() { Width = 300 }, tPass = new() { Width = 170 },
                     tImei = new() { Width = 300, PlaceholderText = "IMEI أو الرقم التسلسلي" },
                     tIssue = new() { Width = 954, Height = 64, Multiline = true, PlaceholderText = "وصف العطل كما ذكره الزبون" },
                     tNotes = new() { Width = 640, Height = 90, Multiline = true, PlaceholderText = "ملاحظات داخلية (لا تُطبع)" };
    readonly ComboBox cbType = W.Combo(200, K.IssueTypes), cbStatus = W.Combo(200, K.Statuses), cbWarranty = W.Combo(200, K.Warranties);
    readonly List<Toggle> accToggles = new();
    readonly ComboBox cbTech = W.Combo(200, Array.Empty<string>());
    readonly ComboBox cbAccount = W.Combo(300, Array.Empty<string>());
    readonly List<Account> accountItems = new();
    readonly Label lblAccount = W.Note("", 600, 22);
    const string NoTech = "— بدون فني —";
    readonly Label lblImei = W.Note("", 420, 22);
    readonly Panel warrantyBox = new() { Width = 950, Height = 46, BackColor = Theme.Surface, Margin = new Padding(6, 4, 6, 4), Visible = false };
    readonly Label warrantyText = new() { Dock = DockStyle.Fill, Font = Theme.F(9.5f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0) };
    readonly ModernButton warrantyBtn = new() { Kind = BtnKind.Primary, Height = 34, Dock = DockStyle.Left };
    readonly List<TriChip> chips = new();
    readonly Toggle tgNA = new() { Text = "الجهاز لا يعمل / لا يمكن فحصه", Width = 330, Height = 34 };
    readonly DataGridView parts = W.Grid();
    readonly NumericUpDown nPrice = W.Money(200), nFee = W.Money(200), nPayAmount = W.Money(170);
    readonly ComboBox cbMethod = W.Combo(150, K.PayMethods);
    readonly DateTimePicker dPay = new() { Width = 150, Format = DateTimePickerFormat.Short };
    readonly TextBox tPayNote = new() { Width = 200, PlaceholderText = "ملاحظة الدفعة" };
    readonly DataGridView pays = W.Grid();
    readonly Label lblCost = new(), lblProfit = new(), lblRem = new(), lblPay = new();
    readonly DateTimePicker dReceived = new() { Width = 180, Format = DateTimePickerFormat.Short },
                            dEstimated = new() { Width = 180, Format = DateTimePickerFormat.Short, ShowCheckBox = true },
                            dDelivered = new() { Width = 180, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    readonly PictureBox photo = new() { Width = 180, Height = 130, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.SurfaceAlt, Cursor = Cursors.Hand, Margin = new Padding(6) };
    readonly ModernButton bPhotoRm;
    readonly Toggle tgLabel = new() { Text = "طباعة ملصق الجهاز بعد الحفظ", Width = 300, Height = 34 };
    readonly List<Payment> payments = new();
    string warrantyOf;
    byte[] photoData;
    bool photoChanged;
    string snapshot;
    bool saved;

    public OrderForm(string orderId, Order prefill) : base(orderId == null ? (prefill != null ? "طلب جديد لنفس الزبون" : "طلب صيانة جديد") : "تعديل الطلب", 1060, 860, orderId == null ? "file-plus" : "square-pen")
    {
        id = orderId;
        existing = Calc.Find(orderId);
        if (existing != null) Text = $"تعديل الطلب {existing.RefNo}";
        var src = existing ?? prefill ?? new Order();

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(0, 0, 0, 20) };
        FlowLayoutPanel Row() { var r = W.Flow(); r.Margin = new Padding(0, 0, 0, 2); flow.Controls.Add(r); return r; }

        // ---------- قفل الطلب المسلّم ----------
        if (existing != null && Locking.IsLocked(existing))
        {
            var lockNote = new Label
            {
                Text = $"🔒 هذا الطلب {existing.Status}. تعديل السعر أو الدفعات السابقة أو الحالة يحتاج كتابة السبب عند الحفظ، ويُسجَّل في سجل الطلب.",
                AutoSize = false, Width = 950, Height = 40, Margin = new Padding(6, 6, 6, 2), Font = Theme.F(9.5f), ForeColor = Pal.AmberInk, BackColor = Pal.AmberSoft,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0)
            };
            flow.Controls.Add(lockNote);
        }

        // ---------- الزبون والجهاز ----------
        flow.Controls.Add(W.Head("الزبون والجهاز", 960));
        accountItems.AddRange(Store.Accounts.OrderBy(a => a.Name, StringComparer.CurrentCulture));
        if (accountItems.Count > 0 || Accounts.Find(src.AccountId) != null)
        {
            if (Accounts.Find(src.AccountId) is Account cur && !accountItems.Contains(cur)) accountItems.Add(cur);
            cbAccount.Items.Add("— زبون عادي —");
            cbAccount.Items.AddRange(accountItems.Select(a => (object)$"{a.Name}   ({Accounts.KindText(a)})").ToArray());
            cbAccount.SelectedIndex = Math.Max(0, accountItems.FindIndex(a => a.Id == src.AccountId) + 1);
            var ra = W.Flow(false);
            ra.Controls.Add(W.Labeled("الحساب (تاجر أو شركة)", cbAccount, "store"));
            lblAccount.Margin = new Padding(6, 30, 6, 0);
            ra.Controls.Add(lblAccount);
            flow.Controls.Add(ra);
        }
        var r1 = Row();
        r1.Controls.Add(W.Labeled("اسم الزبون *", tName, "user"));
        r1.Controls.Add(W.Labeled("رقم الهاتف", tPhone, "phone"));
        r1.Controls.Add(W.Labeled("الجهاز والموديل *", tDevice, "smartphone"));
        var r2 = Row();
        r2.Controls.Add(W.Labeled("رمز القفل", tPass, "lock"));
        r2.Controls.Add(W.Labeled("نوع العطل", cbType));
        r2.Controls.Add(W.Labeled("الحالة", cbStatus));
        r2.Controls.Add(W.Labeled("الضمان", cbWarranty));
        var r3 = Row();
        r3.Controls.Add(W.Labeled("وصف العطل *", tIssue));
        flow.Controls.Add(W.Note("الملحقات المستلمة مع الجهاز", 960));
        var acc = Row();
        // عنصر حُذف من القائمة ويبقى محفوظاً في هذا الطلب يظهر أيضاً حتى لا يضيع عند التعديل
        foreach (var a in K.Accessories.Concat(src.Accessories).Distinct())
        {
            var t = new Toggle { Text = a, Width = 170, Height = 34, Margin = new Padding(6, 2, 6, 2) };
            accToggles.Add(t);
            acc.Controls.Add(t);
        }
        var r4 = Row();
        r4.Controls.Add(W.Labeled("IMEI / الرقم التسلسلي", tImei, "hash"));
        cbTech.Items.Add(NoTech);
        cbTech.Items.AddRange(Techs.All.Select(t => (object)t.Name).ToArray());
        if (src.Technician != "" && Techs.Find(src.Technician) == null) cbTech.Items.Add(src.Technician);
        cbTech.SelectedIndex = 0;
        if (cbTech.Items.Count > 1) r4.Controls.Add(W.Labeled("الفني", cbTech, "wrench"));
        lblImei.Margin = new Padding(6, 30, 6, 0);
        r4.Controls.Add(lblImei);
        warrantyBtn.FitWidth(120);
        warrantyBox.Controls.Add(warrantyText);
        warrantyBox.Controls.Add(warrantyBtn);
        warrantyBox.Paint += (s, e) => { Gfx.Hq(e.Graphics); Gfx.DrawRound(e.Graphics, new RectangleF(0.5f, 0.5f, warrantyBox.Width - 1.5f, warrantyBox.Height - 1.5f), S(10f), Theme.BorderStrong); };
        flow.Controls.Add(warrantyBox);
        BuildIntakeExtras(flow);

        // ---------- فحص الاستلام ----------
        flow.Controls.Add(W.Head("حالة الجهاز عند الاستلام", 960));
        flow.Controls.Add(W.Note("اضغط للتبديل: لم يُفحص ← يعمل ← لا يعمل", 960, 22));
        var chk = Row();
        chk.MaximumSize = new Size(980, 0);
        var extraChecks = (existing?.Checks.Keys ?? Enumerable.Empty<string>()).Where(k => !K.Checks.Any(c => c.Key == k)).Select(k => (k, Lists.CheckTitle(k)));
        foreach (var (k, title) in K.Checks.Concat(extraChecks)) { var c = new TriChip { Key = k, Text = title }; chips.Add(c); chk.Controls.Add(c); }
        var naRow = Row();
        tgNA.Margin = new Padding(6, 4, 6, 4);
        naRow.Controls.Add(tgNA);
        tgNA.CheckedChanged += (s, e) => { foreach (var c in chips) { c.Disabled = tgNA.Checked; c.Invalidate(); } };
        BuildDamage(flow);
        BuildItems(flow);

        // ---------- القطع ----------
        flow.Controls.Add(W.Head("القطع المستبدلة", 960));
        parts.ReadOnly = false;
        parts.SelectionMode = DataGridViewSelectionMode.CellSelect;
        parts.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "اسم القطعة", FillWeight = 220 });
        parts.Columns.Add(new DataGridViewTextBoxColumn { Name = "supplier", HeaderText = "المورد", FillWeight = 150 });
        parts.Columns.Add(new DataGridViewTextBoxColumn { Name = "cost", HeaderText = "التكلفة", FillWeight = 90, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
        parts.Columns.Add(new DataGridViewTextBoxColumn { Name = "inv", Visible = false });
        parts.Columns.Add(new DataGridViewTextBoxColumn { Name = "supw", Visible = false });
        parts.Columns.Add(new DataGridViewTextBoxColumn { Name = "serial", HeaderText = "الرقم التسلسلي", FillWeight = 110 });
        parts.Columns["serial"].DisplayIndex = 3;
        parts.Columns.Add(new DataGridViewButtonColumn { Name = "rm", HeaderText = "", Text = "حذف", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, FillWeight = 40, DefaultCellStyle = { BackColor = Theme.DangerSoft, ForeColor = Theme.Danger, SelectionBackColor = Theme.DangerSoft, SelectionForeColor = Theme.Danger } });
        parts.AllowUserToAddRows = false;
        var partsHost = new Panel { Width = 956, Height = 170, Margin = new Padding(6, 2, 6, 2), BackColor = Theme.Surface };
        partsHost.Controls.Add(parts);
        flow.Controls.Add(partsHost);
        var partBtns = Row();
        var bPick = W.Btn("من قائمة القطع", "package-search", BtnKind.Soft, 150);
        var bAddPart = W.Btn("قطعة", "plus", BtnKind.Secondary, 100);
        partBtns.Controls.Add(bPick);
        partBtns.Controls.Add(bAddPart);
        bAddPart.Click += (s, e) => { int i = parts.Rows.Add("", "", "", "", null, ""); parts.CurrentCell = parts.Rows[i].Cells["name"]; parts.BeginEdit(true); };
        bPick.Click += (s, e) => PickPart();
        parts.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || parts.Columns[e.ColumnIndex].Name != "rm") return;
            var row = parts.Rows[e.RowIndex];
            string n = Convert.ToString(row.Cells["name"].Value)?.Trim();
            if ((n != "" || Txt.ParseMoney(row.Cells["cost"].Value) > 0) && !W.Confirm("حذف هذه القطعة؟", n == "" ? "قطعة بدون اسم" : n, "حذف", true)) return;
            BeginInvoke(() => { if (row.Index >= 0) { parts.Rows.Remove(row); UpdateMoney(); } });
        };
        parts.CellEndEdit += (s, e) =>
        {
            if (parts.Columns[e.ColumnIndex].Name == "cost")
            {
                double v = Txt.ParseMoney(parts.Rows[e.RowIndex].Cells["cost"].Value);
                parts.Rows[e.RowIndex].Cells["cost"].Value = v > 0 ? Txt.Num(v) : "";
            }
            UpdateMoney();
        };
        var suppliers = SupplierNames();
        parts.EditingControlShowing += (s, e) =>
        {
            if (e.Control is not TextBox tb) return;
            if (parts.CurrentCell?.OwningColumn.Name == "supplier") W.Suggest(tb, suppliers);
            else tb.AutoCompleteMode = AutoCompleteMode.None;
        };

        // ---------- الحساب والدفعات ----------
        flow.Controls.Add(W.Head("الحساب والدفعات", 960));
        var m1 = Row();
        m1.Controls.Add(W.Labeled("السعر على الزبون *", nPrice, "circle-dollar-sign"));
        m1.Controls.Add(W.Labeled("أجرة الفحص إن أُلغي الطلب", nFee));
        foreach (var l in new[] { lblPay, lblCost, lblProfit, lblRem })
        {
            l.AutoSize = false; l.Width = 128; l.Height = 60; l.Font = Theme.FS(10); l.Margin = new Padding(6, 2, 6, 2);
            l.TextAlign = ContentAlignment.MiddleLeft; l.BackColor = Theme.SurfaceAlt; l.Padding = new Padding(8, 0, 8, 0);
            m1.Controls.Add(l);
        }
        var payHost = new Panel { Width = 956, Height = 130, Margin = new Padding(6, 6, 6, 2), BackColor = Theme.Surface };
        pays.Columns.Add(new DataGridViewTextBoxColumn { Name = "amount", HeaderText = "المبلغ", FillWeight = 100 });
        pays.Columns.Add(new DataGridViewTextBoxColumn { Name = "method", HeaderText = "الطريقة", FillWeight = 90 });
        pays.Columns.Add(new DataGridViewTextBoxColumn { Name = "date", HeaderText = "التاريخ", FillWeight = 90 });
        pays.Columns.Add(new DataGridViewTextBoxColumn { Name = "note", HeaderText = "ملاحظة", FillWeight = 180 });
        pays.Columns.Add(new DataGridViewButtonColumn { Name = "rm", HeaderText = "", Text = "حذف", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, FillWeight = 40, DefaultCellStyle = { BackColor = Theme.DangerSoft, ForeColor = Theme.Danger, SelectionBackColor = Theme.DangerSoft, SelectionForeColor = Theme.Danger } });
        pays.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0 || pays.Columns[e.ColumnIndex].Name != "rm" || e.RowIndex >= payments.Count) return;
            int i = e.RowIndex;
            BeginInvoke(() => { if (i < payments.Count) { payments.RemoveAt(i); RenderPayments(); } });
        };
        payHost.Controls.Add(pays);
        flow.Controls.Add(payHost);
        var m2 = Row();
        m2.Controls.Add(W.Labeled("مبلغ الدفعة", nPayAmount));
        m2.Controls.Add(W.Labeled("طريقة الدفع", cbMethod));
        m2.Controls.Add(W.Labeled("التاريخ", dPay));
        m2.Controls.Add(W.Labeled("ملاحظة", tPayNote));
        var bAddPay = W.Btn("تسجيل", "wallet", BtnKind.Success, 90); bAddPay.Margin = new Padding(4, 27, 4, 4);
        var bFull = W.Btn("استلام المبلغ كاملاً", "check", BtnKind.Soft, 150); bFull.Margin = new Padding(4, 27, 4, 4);
        var bRefund = W.Btn("إرجاع مبلغ", "rotate-ccw", BtnKind.Ghost, 110); bRefund.Margin = new Padding(4, 27, 4, 4);
        m2.Controls.Add(bAddPay);
        m2.Controls.Add(bFull);
        m2.Controls.Add(bRefund);
        bAddPay.Click += (s, e) => AddPayment();
        bFull.Click += (s, e) => PayFull();
        bRefund.Click += (s, e) => AddRefund();
        BuildMoneyExtras(flow);

        // ---------- التواريخ ----------
        flow.Controls.Add(W.Head("التواريخ", 960));
        var d1 = Row();
        d1.Controls.Add(W.Labeled("تاريخ الاستلام", dReceived));
        d1.Controls.Add(W.Labeled("التسليم المتوقع", dEstimated));
        d1.Controls.Add(W.Labeled("التسليم الفعلي", dDelivered));

        // ---------- الصورة والملاحظات ----------
        flow.Controls.Add(W.Head("صورة الجهاز والملاحظات", 960));
        var ph = Row();
        ph.Controls.Add(photo);
        var phBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        var bPhoto = W.Btn("اختيار صورة", "folder-open", BtnKind.Secondary, 130);
        bPhotoRm = W.Btn("إزالة الصورة", "trash-2", BtnKind.Danger, 130);
        phBtns.Controls.Add(bPhoto);
        phBtns.Controls.Add(bPhotoRm);
        ph.Controls.Add(phBtns);
        ph.Controls.Add(W.Labeled("ملاحظات داخلية", tNotes));
        bPhoto.Click += (s, e) => ChoosePhoto();
        bPhotoRm.Click += (s, e) => { photoChanged = true; SetPhoto(null); };
        photo.Click += (s, e) => { if (photo.Image != null) PhotoDialog.Show(photo.Image); };
        tgLabel.Margin = new Padding(6, 8, 6, 4);
        tgLabel.Checked = Store.Flag("label_after_save");
        if (existing == null) flow.Controls.Add(tgLabel);

        Body.Controls.Add(flow);
        Body.Padding = new Padding(16, 4, 10, 6);

        var bSave = AddButton("حفظ الطلب", DialogResult.None, BtnKind.Primary, "save");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        if (existing != null)
        {
            var bClone = AddButton("طلب جديد لنفس الزبون", DialogResult.None, BtnKind.Ghost, "copy");
            bClone.Click += (s, e) =>
            {
                if (!ConfirmDiscard()) return;
                var o = existing;
                saved = true;
                Close();
                Ui2.Later(() => Acts.New(new Order { CustomerName = o.CustomerName, Phone = o.Phone, Device = o.Device, Passcode = o.Passcode, Accessories = o.Accessories.ToList(), AccountId = o.AccountId }));
            };
        }
        bSave.Click += (s, e) => Save();
        AcceptButton = null;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.F10 || (e.Control && e.KeyCode == Keys.S)) { e.Handled = true; Save(); } };

        // ---------- التعبئة ----------
        tName.Text = src.CustomerName; tPhone.Text = src.Phone; tDevice.Text = src.Device; tPass.Text = src.Passcode;
        if (existing != null && existing.IssueType != "" && !cbType.Items.Contains(existing.IssueType)) cbType.Items.Add(existing.IssueType);
        W.Pick(cbType, cbType.Items.Contains(src.IssueType) ? src.IssueType : K.IssueTypes[0]);
        if (existing != null && !cbStatus.Items.Contains(existing.Status)) cbStatus.Items.Add(existing.Status);
        W.Pick(cbStatus, existing?.Status ?? K.Statuses[0]);
        tIssue.Text = src.Issue;
        if (existing != null && existing.Warranty != "" && !cbWarranty.Items.Contains(existing.Warranty)) cbWarranty.Items.Add(existing.Warranty);
        W.Pick(cbWarranty, cbWarranty.Items.Contains(src.Warranty) ? src.Warranty : K.Warranties[0]);
        if (src.Technician != "") W.Pick(cbTech, Techs.Find(src.Technician)?.Name ?? src.Technician);
        W.Set(nPrice, existing?.Price ?? 0);
        AccountChanged(false);
        cbAccount.SelectedIndexChanged += (s, e) => AccountChanged(true);
        W.Set(nFee, existing?.CheckFee ?? 0);
        tImei.Text = src.Imei;
        foreach (var c in chips) c.State = existing != null && existing.Checks.TryGetValue(c.Key, out var v) ? v : "";
        tgNA.Checked = existing?.ChecksNA ?? false;
        warrantyOf = existing?.WarrantyOf;
        dReceived.Value = Txt.ParseDate(existing?.DateReceived) ?? DateTime.Today;
        SetDate(dEstimated, existing?.DateEstimated);
        SetDate(dDelivered, existing?.DateDelivered);
        tNotes.Text = existing?.Notes ?? "";
        foreach (var t in accToggles) t.Checked = src.Accessories.Contains(t.Text);
        foreach (var p in existing?.Parts ?? new()) parts.Rows.Add(p.Name, p.Supplier, p.Cost > 0 ? Txt.Num(p.Cost) : "", p.InventoryItemId ?? "", null, p.SupWarranty?.ToString() ?? "", p.Serial);
        if (existing == null || existing.Parts.Count == 0) parts.Rows.Add("", "", "", "", null, "");
        if (existing != null)
        {
            payments.AddRange(existing.PaymentHistory.Select(p => new Payment { Id = p.Id, Amount = p.Amount, Date = p.Date, Note = p.Note, Method = p.Method }));
            double legacy = existing.Paid - payments.Sum(p => p.Amount);
            if (legacy > 0) payments.Insert(0, new Payment { Id = Txt.Uid("inst"), Amount = legacy, Date = existing.DateReceived, Note = "دفعة مسجّلة سابقاً" });
            if (existing.PhotoRef != null) { photoData = Store.GetPhoto(existing.PhotoRef); SetPhoto(photoData); }
        }
        else SetPhoto(null);
        dPay.Value = DateTime.Today;
        RenderPayments();
        UpdateImei();
        RenderWarranty();

        // ---------- الإكمال التلقائي والأحداث ----------
        W.Suggest(tName, Store.Orders.Select(o => o.CustomerName));
        W.Suggest(tDevice, Models.All());
        tName.Leave += (s, e) =>
        {
            // الهاتف من طلب سابق لنفس الزبون
            if (tPhone.Text.Trim() != "") return;
            var n = Txt.Fold(tName.Text);
            if (n == "") return;
            var prev = Store.Orders.OrderByDescending(o => o.CreatedAt, StringComparer.Ordinal).FirstOrDefault(o => Txt.Fold(o.CustomerName) == n && o.Phone != "");
            if (prev != null) { tPhone.Text = prev.Phone; Toast.Show("تمت تعبئة الهاتف من طلب سابق لنفس الزبون"); }
        };
        var wTimer = new System.Windows.Forms.Timer { Interval = 300 };
        wTimer.Tick += (s, e) => { wTimer.Stop(); if (!IsDisposed) RenderWarranty(); };
        foreach (var c in new Control[] { tName, tPhone, tDevice, tImei }) c.TextChanged += (s, e) => { wTimer.Stop(); wTimer.Start(); };
        Disposed += (s, e) => wTimer.Dispose();
        tImei.TextChanged += (s, e) =>
        {
            UpdateImei();
            // الموديل من أول 8 أرقام في IMEI (من أجهزة سابقة بنفس البداية)
            if (tDevice.Text.Trim() == "" && Imei.Length == 15 && Models.FromImei(Imei) is string model) { tDevice.Text = model; Toast.Show("عُرف الموديل من IMEI: " + model); }
        };
        dReceived.ValueChanged += (s, e) => RenderWarranty();
        nPrice.ValueChanged += (s, e) => UpdateMoney();
        nFee.ValueChanged += (s, e) => UpdateMoney();
        cbStatus.SelectedIndexChanged += (s, e) =>
        {
            UpdateMoney();
            var st = cbStatus.Text;
            if (st == K.Done && !dDelivered.Checked) { dDelivered.Value = DateTime.Today; dDelivered.Checked = true; }
            else if (K.OpenStatuses.Contains(st)) dDelivered.Checked = false;
        };
        warrantyBtn.Click += (s, e) => WarrantyAction();
        UpdateMoney();
        FillExtras(src, existing == null);
        snapshot = Snapshot();
        Shown += (s, e) => { flow.AutoScrollPosition = Point.Empty; tName.Focus(); };
    }

    static void SetDate(DateTimePicker d, string iso)
    {
        if (Txt.ParseDate(iso) is DateTime v) { d.Value = v; d.Checked = true; }
        else { d.Value = DateTime.Today; d.Checked = false; }
    }
    static string DateOf(DateTimePicker d) => d.ShowCheckBox && !d.Checked ? "" : Txt.Iso(d.Value);

    List<string> SupplierNames() => Calc.GroupByName(Store.Orders.SelectMany(o => o.Parts.Select(p => p.Supplier))
        .Concat(Store.Inventory.Select(i => i.Supplier)).Concat(Store.SupplierTx.Select(t => t.Supplier)), x => x).Select(g => g.Label).ToList();

    Account SelectedAccount => cbAccount.SelectedIndex > 0 && cbAccount.SelectedIndex - 1 < accountItems.Count ? accountItems[cbAccount.SelectedIndex - 1] : null;

    void AccountChanged(bool user)
    {
        var a = SelectedAccount;
        if (a == null) { lblAccount.Text = ""; return; }
        var parts = new List<string>();
        if (a.Discount > 0) parts.Add($"خصم {Txt.Num(a.Discount)}% على أسعار القطع");
        if (Accounts.Due(a) > 0) parts.Add("مستحق عليه " + Txt.Money(Accounts.Due(a)));
        if (a.Note != "") parts.Add(a.Note);
        lblAccount.Text = string.Join("   •   ", parts);
        lblAccount.ForeColor = Theme.BrandDark;
        if (user && tName.Text.Trim() == "") { tName.Text = a.Name; if (tPhone.Text.Trim() == "") tPhone.Text = a.Phone; }
    }

    // ---------------- القطع ----------------
    List<Part> FormParts() => parts.Rows.Cast<DataGridViewRow>().Select(r => new Part
    {
        Name = Convert.ToString(r.Cells["name"].Value)?.Trim() ?? "",
        Supplier = Convert.ToString(r.Cells["supplier"].Value)?.Trim() ?? "",
        Cost = Math.Max(0, Txt.ParseMoney(r.Cells["cost"].Value)),
        InventoryItemId = Convert.ToString(r.Cells["inv"].Value) is string s && s != "" ? s : null,
        SupWarranty = Txt.OptInt(r.Cells["supw"].Value) is int w && w > 0 ? w : null,
        Serial = Convert.ToString(r.Cells["serial"].Value)?.Trim() ?? ""
    }).Where(p => p.Name != "" || p.Cost > 0).ToList();

    void PickPart()
    {
        parts.EndEdit();
        using var d = new PickPartDialog(tDevice.Text.Trim());
        if (d.ShowModal() != DialogResult.OK || d.Item == null) return;
        var i = d.Item;
        if (i.Qty != null)
        {
            // الكمية تُخصم عند الحفظ: نحسب ما يستعمله هذا النموذج
            int savedUse = existing != null ? Calc.InvUsage(existing.Parts).GetValueOrDefault(i.Id) : 0;
            int inForm = Calc.InvUsage(FormParts()).GetValueOrDefault(i.Id);
            int available = i.Qty.Value + savedUse - inForm;
            if (available <= 0 && !W.Confirm("القطعة غير متوفرة في المخزون", $"{i.Name}{(i.Compatible != "" ? " (" + i.Compatible + ")" : "")}\nالكمية المتاحة: {available}. هل تريد إضافتها على أي حال؟ ستصبح الكمية بالسالب حتى تسجّل وصول بضاعة.", "إضافة على أي حال")) return;
        }
        // صف البداية الفارغ يُستبدل
        if (parts.Rows.Count == 1 && FormParts().Count == 0) parts.Rows.Clear();
        parts.Rows.Add(i.Compatible != "" ? $"{i.Name} ({i.Compatible})" : i.Name, i.Supplier, i.Cost > 0 ? Txt.Num(i.Cost) : "", i.Id, null, i.SupWarranty?.ToString() ?? "");
        // سعر التاجر: السعر المقترح ناقص خصم حسابه
        var acc = SelectedAccount;
        double sale = Accounts.PriceFor(acc, i.SalePrice);
        string who = acc != null && acc.Discount > 0 ? $" (سعر {acc.Name} بخصم {Txt.Num(acc.Discount)}%)" : "";
        if (nPrice.Value == 0 && sale > 0) { W.Set(nPrice, sale); Toast.Show($"أُضيفت {i.Name} وضُبط السعر {Txt.Money(sale)}{who}"); }
        else Toast.Show($"أُضيفت {i.Name}. سعر البيع المقترح {Txt.Money(sale)}{who}");
        UpdateMoney();
    }

    // ---------------- الدفعات ----------------
    double FormPaid => payments.Sum(p => p.Amount);
    bool Cancelling => cbStatus.Text == K.Cancelled;

    void RenderPayments()
    {
        pays.Rows.Clear();
        foreach (var p in payments)
        {
            int i = pays.Rows.Add(p.IsRefund ? "↩ " + Txt.Money(-p.Amount) : Txt.Money(p.Amount), p.Method, Txt.FmtDate(p.Date), p.Note);
            if (p.IsRefund) pays.Rows[i].DefaultCellStyle.ForeColor = Pal.Bad;
        }
        UpdateMoney();
    }

    void AddPayment()
    {
        double a = (double)nPayAmount.Value;
        if (a <= 0) { nPayAmount.Focus(); Toast.Show("أدخل مبلغ الدفعة.", Tone.Warning); return; }
        payments.Add(new Payment { Id = Txt.Uid("inst"), Amount = a, Date = Txt.Iso(dPay.Value), Note = tPayNote.Text.Trim(), Method = cbMethod.Text });
        W.Set(nPayAmount, 0); tPayNote.Clear();
        RenderPayments();
    }

    void AddRefund()
    {
        double paid = FormPaid;
        if (paid <= 0) { Toast.Show("لا توجد دفعات لإرجاع مبلغ منها", Tone.Info); return; }
        double due = Cancelling ? (double)nFee.Value : (double)nPrice.Value;
        using var d = new RefundDialog(tDevice.Text.Trim() == "" ? "الطلب" : tDevice.Text.Trim(), paid, Math.Max(0, paid - due));
        if (d.ShowModal() != DialogResult.OK) return;
        payments.Add(new Payment { Id = Txt.Uid("rf"), Amount = -d.Amount, Date = d.Date, Method = d.Method, Note = "استرجاع: " + d.Reason });
        RenderPayments();
    }

    void PayFull()
    {
        double due = Cancelling ? (double)nFee.Value : (double)nPrice.Value;
        double rem = due - FormPaid;
        if (rem <= 0) { Toast.Show(due > 0 ? "المبلغ مسدد بالكامل مسبقاً." : "أدخل السعر أولاً.", Tone.Info); return; }
        payments.Add(new Payment { Id = Txt.Uid("inst"), Amount = rem, Date = Txt.Today, Note = "سداد كامل المتبقي", Method = cbMethod.Text });
        RenderPayments();
    }

    void UpdateMoney()
    {
        double price = Cancelling ? (double)nFee.Value : (double)nPrice.Value;
        double cost = FormParts().Sum(p => p.Cost), paid = FormPaid;
        double profit = price - cost, rem = Math.Max(0, price - paid);
        bool over = price > 0 && paid > price;
        lblCost.Text = "تكلفة القطع\n" + Txt.Money(cost);
        lblCost.ForeColor = Theme.Ink;
        lblProfit.Text = (Cancelling ? "الفحص ناقص القطع\n" : "الربح المتوقع\n") + Txt.Money(profit);
        lblProfit.ForeColor = profit >= 0 ? Pal.Good : Pal.Bad;
        lblRem.Text = (over ? "مقبوض زيادة\n" + Txt.Money(paid - price) : (Cancelling ? "المتبقي من الفحص\n" : "المتبقي\n") + Txt.Money(rem));
        lblRem.ForeColor = over || rem > 0 ? Pal.Bad : Pal.Good;
        var st = Json.DerivePay(price, paid);
        lblPay.Text = st + "\nمقبوض " + Txt.Money(paid);
        lblPay.ForeColor = K.StatusColors(st).Fg;
        if (!fillingExtras) CreditInfo();
    }

    // ---------------- IMEI والضمان ----------------
    string Imei => new(Txt.LatinDigits(tImei.Text).Where(c => !char.IsWhiteSpace(c)).ToArray());

    void UpdateImei()
    {
        var v = Imei;
        if (v == "") { lblImei.Text = ""; return; }
        if (v.Length == 15 && v.All(char.IsAsciiDigit))
        {
            bool ok = Calc.ImeiValid(v);
            lblImei.Text = ok ? "رقم IMEI صحيح" : "رقم IMEI غير صحيح — تحقق منه";
            lblImei.ForeColor = ok ? Pal.Good : Pal.Bad;
        }
        else if (v.All(char.IsAsciiDigit)) { lblImei.Text = $"{v.Length} رقم — رقم IMEI يتكون من 15 رقماً"; lblImei.ForeColor = Theme.Muted; }
        else { lblImei.Text = "رقم تسلسلي"; lblImei.ForeColor = Theme.Muted; }
    }

    Order warrantyPrev;
    void RenderWarranty()
    {
        if (warrantyOf != null)
        {
            var src = Calc.Find(warrantyOf);
            warrantyText.Text = $"✓ طلب ضمان للطلب {src?.RefNo ?? "—"}{(src != null ? " — سُلّم في " + Txt.FmtDate(src.DateDelivered) : "")}";
            warrantyText.ForeColor = Pal.Good;
            warrantyBtn.Text = "إلغاء الربط"; warrantyBtn.Kind = BtnKind.Secondary; warrantyBtn.FitWidth(110);
            warrantyBox.BackColor = warrantyText.BackColor = Pal.GoodSoft;
            warrantyBox.Visible = true;
            return;
        }
        warrantyPrev = Calc.FindPreviousRepair(id, tName.Text, tPhone.Text, tDevice.Text, Imei);
        if (warrantyPrev == null) { warrantyBox.Visible = false; return; }
        var end = Calc.WarrantyEnd(warrantyPrev);
        bool inW = Calc.InWarranty(warrantyPrev, Txt.Iso(dReceived.Value));
        warrantyText.Text = inW
            ? $"هذا الجهاز ضمن ضمان الطلب {warrantyPrev.RefNo} ({warrantyPrev.IssueType}) — الضمان ساري حتى {Txt.FmtDate(end)}"
            : $"دخل هذا الجهاز الورشة سابقاً: {warrantyPrev.RefNo} {warrantyPrev.IssueType} — {(end != "" ? "انتهى ضمانه في " + Txt.FmtDate(end) : "بدون ضمان")}";
        warrantyText.ForeColor = inW ? Pal.Primary : Pal.AmberInk;
        warrantyBtn.Text = inW ? "تسجيله كطلب ضمان" : "عرضه";
        warrantyBtn.Kind = inW ? BtnKind.Primary : BtnKind.Secondary;
        warrantyBtn.FitWidth(110);
        warrantyBox.BackColor = warrantyText.BackColor = inW ? Theme.BrandSoft : Pal.AmberSoft;
        warrantyBox.Visible = true;
    }

    void WarrantyAction()
    {
        if (warrantyOf != null) { warrantyOf = null; RenderWarranty(); return; }
        if (warrantyPrev == null) return;
        if (!Calc.InWarranty(warrantyPrev, Txt.Iso(dReceived.Value))) { Acts.View(warrantyPrev); return; }
        warrantyOf = warrantyPrev.Id;
        if (tImei.Text.Trim() == "" && warrantyPrev.Imei != "") tImei.Text = warrantyPrev.Imei;
        if (nPrice.Value == 0) W.Set(nPrice, 0);
        RenderWarranty();
        Toast.Show("رُبط كطلب ضمان. السعر 0 — عدّله إن كان الإصلاح غير مشمول.");
    }

    // ---------------- الصورة ----------------
    void SetPhoto(byte[] data)
    {
        photoData = data;
        photo.Image?.Dispose();
        photo.Image = null;
        if (data != null)
            try { using var ms = new MemoryStream(data); using var img = Image.FromStream(ms); photo.Image = new Bitmap(img); } catch { photoData = null; }
        bPhotoRm.Visible = photo.Image != null;
    }

    void ChoosePhoto()
    {
        var f = W.OpenFile("صور|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif");
        if (f == null) return;
        try
        {
            using var src = Image.FromFile(f);
            int max = 1280, w = src.Width, h = src.Height;
            if (w > h && w > max) { h = h * max / w; w = max; } else if (h >= w && h > max) { w = w * max / h; h = max; }
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, w, h);
            }
            using var ms = new MemoryStream();
            var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ps = new EncoderParameters(1);
            ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 72L);
            bmp.Save(ms, enc, ps);
            photoChanged = true;
            SetPhoto(ms.ToArray());
        }
        catch { Dialogs.Warn("تعذّرت قراءة الصورة."); }
    }

    // ---------------- الإغلاق والحفظ ----------------
    string Snapshot() => string.Join("|", new[]
    {
        tName.Text, tPhone.Text, tDevice.Text, tPass.Text, tImei.Text, cbType.Text, cbStatus.Text, tIssue.Text, cbWarranty.Text, cbTech.Text, cbAccount.Text,
        nPrice.Value.ToString(), nFee.Value.ToString(), Txt.Iso(dReceived.Value), DateOf(dEstimated), DateOf(dDelivered), tNotes.Text,
        string.Join(",", accToggles.Where(t => t.Checked).Select(t => t.Text)),
        string.Join(";", FormParts().Select(p => $"{p.Name}/{p.Supplier}/{p.Cost}/{p.InventoryItemId}/{p.Serial}")),
        string.Join(";", payments.Select(p => $"{p.Amount}/{p.Date}/{p.Method}/{p.Note}")),
        photoChanged.ToString(), string.Join(",", chips.Select(c => c.State)), tgNA.Checked.ToString(), warrantyOf ?? "", ExtrasSnapshot()
    });

    bool ConfirmDiscard()
    {
        parts.EndEdit();
        return Snapshot() == snapshot || W.Confirm("تجاهل التغييرات؟", "هناك بيانات لم تُحفظ في هذا الطلب.", "تجاهل وإغلاق", true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!saved && !ConfirmDiscard()) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    void Save()
    {
        parts.EndEdit();
        var need = new (Control C, string V)[] { (tName, tName.Text.Trim()), (tDevice, tDevice.Text.Trim()), (tIssue, tIssue.Text.Trim()) };
        var missing = need.FirstOrDefault(x => x.V == "");
        if (missing.C != null) { missing.C.Focus(); Dialogs.Warn("أكمل الحقول المطلوبة: الاسم، الجهاز، ووصف العطل."); return; }
        double price = (double)nPrice.Value, paid = FormPaid, checkFee = (double)nFee.Value;
        bool cancelling = Cancelling;
        if (!cancelling && price > 0 && paid > price) { Dialogs.Warn($"المبالغ المقبوضة ({Txt.Money(paid)}) أكبر من السعر ({Txt.Money(price)}). عدّل الدفعات أو السعر."); return; }
        if (!cancelling && price <= 0 && paid > 0 && warrantyOf == null && !W.Confirm("السعر صفر", "سجّلت دفعات والسعر صفر. هل تريد الحفظ على أي حال؟", "حفظ")) return;
        if (cancelling && paid > checkFee) { Dialogs.Warn($"الطلب ملغى والمقبوض ({Txt.Money(paid)}) أكبر من أجرة الفحص ({Txt.Money(checkFee)}).\nاكتب أجرة الفحص، أو سجّل المبلغ الذي أرجعته للزبون بزر «إرجاع مبلغ»."); return; }
        var imei = Imei;
        if (imei.Length == 15 && imei.All(char.IsAsciiDigit) && !Calc.ImeiValid(imei) &&
            !W.Confirm("رقم IMEI غير صحيح", $"{imei}\nرقم التحقق لا يطابق. هل تريد الحفظ على أي حال؟", "حفظ على أي حال")) { tImei.Focus(); return; }
        if (!ValidateExtras(price)) return;
        string name = tName.Text.Trim(), device = tDevice.Text.Trim(), received = Txt.Iso(dReceived.Value);
        if (existing == null)
        {
            var dup = Store.Orders.FirstOrDefault(o => o.Status != K.Cancelled && Txt.Fold(o.CustomerName) == Txt.Fold(name) && Txt.Fold(o.Device) == Txt.Fold(device) && o.DateReceived == received);
            if (dup != null)
            {
                var r = W.Ask3("طلب مشابه موجود", $"يوجد طلب لنفس الزبون ونفس الجهاز بتاريخ {Txt.FmtDate(received)} ({dup.RefNo}).\nهل تريد إضافة طلب جديد رغم ذلك؟", "إضافة على أي حال", "فتح الطلب الموجود");
                if (r == null) { saved = true; Close(); Ui2.Later(() => Acts.View(dup)); return; }
                if (r == false) return;
            }
        }
        var status = cbStatus.Text;
        // الطلب المسلّم أو الملغى: تغيير السعر أو الدفعات السابقة أو الحالة يحتاج سبباً
        string lockNote = null;
        if (existing != null && Locking.IsLocked(existing))
        {
            var diffs = new List<string>();
            if (Math.Abs(price - existing.Price) > 0.001) diffs.Add($"السعر {Txt.Money(existing.Price)} ← {Txt.Money(price)}");
            if (Math.Abs(checkFee - existing.CheckFee) > 0.001) diffs.Add($"أجرة الفحص {Txt.Money(existing.CheckFee)} ← {Txt.Money(checkFee)}");
            double oldCost = Calc.PartsCost(existing), newCost = FormParts().Sum(p => p.Cost);
            if (Math.Abs(oldCost - newCost) > 0.001) diffs.Add($"تكلفة القطع {Txt.Money(oldCost)} ← {Txt.Money(newCost)}");
            if (status != existing.Status) diffs.Add($"الحالة {existing.Status} ← {status}");
            var kept = payments.Select(p => p.Id).ToHashSet();
            foreach (var p in existing.PaymentHistory.Where(p => !kept.Contains(p.Id)))
                diffs.Add($"حُذفت {(p.IsRefund ? "حركة إرجاع" : "دفعة")} {Txt.Money(Math.Abs(p.Amount))} بتاريخ {Txt.FmtDate(p.Date)}");
            if (diffs.Count > 0)
            {
                var reason = Ask.Reason("تعديل طلب " + existing.Status, $"هذا الطلب {existing.Status}. التغييرات:\n- " + string.Join("\n- ", diffs),
                    new[] { "خطأ في الإدخال", "خصم للزبون", "تصحيح السعر", "رجع الجهاز للإصلاح" }, "حفظ التعديل");
                if (reason == null) return;
                lockNote = string.Join("؛ ", diffs) + " — السبب: " + reason;
            }
        }
        var now = Txt.Now;
        string delivered = DateOf(dDelivered);
        if (status == K.Done && delivered == "") delivered = Txt.Today;
        if (K.OpenStatuses.Contains(status)) delivered = "";
        bool justDelivered = status == K.Done && (existing == null || existing.Status != K.Done);
        var o = existing?.Clone() ?? new Order { Id = Txt.Uid(), RefNo = Txt.GenRef(Calc.UsedRefs()), CreatedAt = now, StartedAt = now };
        o.CustomerName = name;
        o.Phone = Txt.LatinDigits(tPhone.Text.Trim());
        o.Device = device;
        o.Status = status;
        o.Issue = tIssue.Text.Trim();
        o.IssueType = cbType.Text;
        o.Technician = cbTech.SelectedIndex <= 0 ? "" : cbTech.Text;
        if (cbAccount.Items.Count > 0) o.AccountId = SelectedAccount?.Id;
        o.Passcode = justDelivered && Store.ClearPasscodeOnDelivery ? "" : tPass.Text.Trim();
        o.Imei = imei;
        o.CheckFee = checkFee;
        o.Checks = chips.Where(c => c.State != "").ToDictionary(c => c.Key, c => c.State);
        o.ChecksNA = tgNA.Checked;
        o.WarrantyOf = warrantyOf;
        o.Accessories = accToggles.Where(t => t.Checked).Select(t => t.Text).ToList();
        o.Warranty = cbWarranty.Text;
        o.Parts = FormParts();
        o.Price = price;
        o.Paid = paid;
        o.PaymentHistory = payments.Select(p => new Payment { Id = p.Id, Amount = p.Amount, Date = p.Date, Note = p.Note, Method = p.Method }).ToList();
        o.PaymentStatus = Json.DerivePay(cancelling ? checkFee : price, paid);
        o.DateReceived = received;
        o.DateEstimated = DateOf(dEstimated);
        o.DateDelivered = delivered;
        o.Notes = tNotes.Text.Trim();
        o.CompletedAt = status == K.Done ? existing?.CompletedAt ?? now : null;
        o.ReadyAt = status == K.Ready ? (existing?.Status == K.Ready ? existing.ReadyAt : null) ?? now : null;
        o.CancelledAt = status == K.Cancelled ? (existing?.Status == K.Cancelled ? existing.CancelledAt : null) ?? now : null;
        o.StatusAt = existing != null && existing.Status == status ? existing.StatusAt : now;
        o.UpdatedAt = now;
        if (photoChanged)
        {
            if (photoData != null) { o.PhotoRef = "ph_" + o.Id; Store.SetPhoto(o.PhotoRef, photoData); }
            else { if (existing?.PhotoRef != null) Store.RemovePhoto(existing.PhotoRef); o.PhotoRef = null; }
        }
        ApplyExtras(o);
        // فحص الجودة قبل أن يصبح الجهاز جاهزاً
        if (status == K.Ready && existing?.Status != K.Ready && QC.Required && !QC.Done(o) && !QcDialog.Run(o)) return;
        var oldIds = existing?.PaymentHistory.Select(p => p.Id).ToHashSet() ?? new HashSet<string>();
        var newRefunds = o.PaymentHistory.Where(p => p.IsRefund && !oldIds.Contains(p.Id)).ToList();
        foreach (var rf in newRefunds) Locking.Log(o, $"أُرجع للزبون {Txt.Money(-rf.Amount)} ({rf.Method}) — {rf.Note.Replace("استرجاع: ", "")}");
        if (lockNote != null) Locking.Log(o, "تعديل بعد الإغلاق: " + lockNote);
        try { Store.SaveOrder(o); }
        catch (Exception ex) { Dialogs.Error("تعذّر حفظ الطلب: " + ex.Message); return; }
        var ranOut = Calc.ApplyStockChange(existing?.Parts, o.Parts);
        foreach (var rf in newRefunds)
            Notify.Alert("refund", $"↩️ إرجاع مبلغ للزبون\n{o.RefNo} — {o.CustomerName} — {o.Device}\nالمبلغ: {Txt.Money(-rf.Amount)} ({rf.Method})\n{rf.Note}");
        if (lockNote != null) Notify.Alert("locked", $"🔒 تعديل طلب {existing.Status}\n{o.RefNo} — {o.CustomerName} — {o.Device}\n{lockNote}");
        if (justDelivered && o.AccountId == null && Calc.RemainingOf(o) > 0)
            Notify.Alert("debt", $"⚠️ سُلّم جهاز وعليه دين\n{o.RefNo} — {o.CustomerName} ({o.Phone})\n{o.Device}\nالمتبقي: {Txt.Money(Calc.RemainingOf(o))} من {Txt.Money(o.Price)}");
        saved = true;
        DialogResult = DialogResult.OK;
        bool isNew = existing == null, label = tgLabel.Checked;
        bool wasFull = existing?.PaymentStatus == K.PayFull;
        if (isNew) Store.SetFlag("label_after_save", label);
        Close();
        Store.NotifyChanged();
        // ما بعد الحفظ يُعرض بعد إغلاق النافذة
        Ui2.Later(() =>
        {
            Toast.Show(isNew ? $"تم تسجيل الطلب {o.RefNo}" : "تم حفظ التعديلات");
            if (ranOut.Count > 0) Toast.Show("نفدت من المخزون: " + string.Join("، ", ranOut), Tone.Warning);
            if (isNew && label) Printer.Label(o);
            if (o.PaymentStatus == K.PayFull && Calc.ChargeOf(o) > 0 && !wasFull) Acts.OfferReceipt(o);
        });
    }
}

/// <summary>اختيار قطعة من قائمة الأسعار (مجمّعة حسب الموديل)</summary>
public class PickPartDialog : DialogShell
{
    readonly TextBox search = new() { Width = 600, PlaceholderText = "ابحث باسم القطعة أو الموديل أو المورد" };
    readonly DataGridView grid = W.Grid();
    List<InvItem> shown = new();
    public InvItem Item { get; private set; }

    public PickPartDialog(string device) : base("اختيار قطعة من القائمة", 900, 640, "package-search")
    {
        search.Text = device;
        var top = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Surface };
        var box = new InputBox(search, 600, "search") { Location = new Point(0, 6), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        top.Controls.Add(box);
        top.Resize += (s, e) => box.SetBounds(0, S(6), top.Width, S(40));
        grid.Columns.Add("model", "الموديل");
        grid.Columns.Add("name", "القطعة");
        grid.Columns.Add("cat", "التصنيف");
        grid.Columns.Add("sup", "المورد");
        grid.Columns.Add("cost", "الشراء");
        grid.Columns.Add("qty", "المخزون");
        grid.Columns.Add("sale", "البيع");
        Body.Controls.Add(grid);
        Body.Controls.Add(top);
        var ok = AddButton("إضافة", DialogResult.None, BtnKind.Primary, "plus");
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        ok.Click += (s, e) => Accept();
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Accept(); };
        grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; Accept(); } };
        Ui2.OnIdle(search, Render);
        search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down) { grid.Focus(); e.Handled = true; } };
        Render();
        Shown += (s, e) => { search.Focus(); search.SelectAll(); };
    }

    void Render()
    {
        var q = Txt.Fold(search.Text);
        var list = Store.Inventory.Where(i => Txt.Matches(string.Join(" ", i.Name, i.Compatible, i.Supplier, i.Category), q)).ToList();
        if (list.Count == 0 && q != "") list = Store.Inventory.ToList();
        shown = list.OrderBy(i => i.Compatible == "" ? "ي" : i.Compatible, StringComparer.CurrentCulture).ThenBy(i => i.Name, StringComparer.CurrentCulture).ToList();
        grid.Rows.Clear();
        foreach (var i in shown)
            grid.Rows.Add(i.Compatible == "" ? "قطع عامة" : i.Compatible, i.Name, i.Category, i.Supplier == "" ? "بدون مورد" : i.Supplier, Txt.Num(i.Cost),
                i.Qty?.ToString() ?? "—", Txt.Num(i.SalePrice));
        if (Store.Inventory.Count == 0) Toast.Show("قائمة القطع فارغة — أضف القطع وأسعارها من صفحة قطع الغيار.", Tone.Info);
    }

    void Accept()
    {
        if (grid.CurrentRow == null || grid.CurrentRow.Index >= shown.Count) return;
        Item = shown[grid.CurrentRow.Index];
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>عرض الصورة بحجم كبير</summary>
public static class PhotoDialog
{
    public static void Show(Image img)
    {
        using var d = new DialogShell("صورة الجهاز", 900, 720, "eye");
        var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = Theme.Surface };
        d.Body.Controls.Add(pb);
        d.AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
        d.FormClosed += (s, e) => pb.Image = null;
        d.ShowModal();
    }
}

public static class Ui2
{
    /// <summary>تنفيذ بعد انتهاء الحدث الحالي (مثل فتح نافذة بعد إغلاق أخرى)</summary>
    public static void Later(Action a)
    {
        var main = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is MainForm) ?? Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (main != null && main.IsHandleCreated && !main.IsDisposed) main.BeginInvoke(a);
        else a();
    }

    /// <summary>تأخير البحث حتى تتوقف الكتابة لحظة</summary>
    public static void OnIdle(TextBox box, Action action, int ms = 180)
    {
        var timer = new System.Windows.Forms.Timer { Interval = ms };
        timer.Tick += (s, e) => { timer.Stop(); if (!box.IsDisposed) action(); };
        box.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
        box.Disposed += (s, e) => timer.Dispose();
    }
}
