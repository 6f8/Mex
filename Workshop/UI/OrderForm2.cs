using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>أقسام الطلب الإضافية: المصدر، الخدمة والشحن، رسم الخدوش، بنود الإصلاح، السقف، الملصق، التقسيط، رصيد الزبون</summary>
public partial class OrderForm
{
    OrderExtra x = new();
    readonly ComboBox cbSource = W.Combo(220, Lists.Get("sources"), true);
    readonly TextBox tArea = new() { Width = 220, PlaceholderText = "الحي أو المنطقة" };
    readonly Label lblPhone = W.Note("", 300, 22);
    readonly Seg svc = new(Extra.Services);
    readonly TextBox tAddress = new() { Width = 500, PlaceholderText = "العنوان بالتفصيل" };
    readonly DateTimePicker dVisit = new() { Width = 200, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd  HH:mm", ShowCheckBox = true };
    readonly NumericUpDown nSvcFee = W.Money(160), nShipFee = W.Money(160);
    readonly TextBox tShipCo = new() { Width = 220, PlaceholderText = "شركة التوصيل" }, tShipNo = new() { Width = 220, PlaceholderText = "رقم الشحنة" };
    readonly ComboBox cbShipState = W.Combo(180, new[] { "" }.Concat(Extra.ShipStates));
    readonly FlowLayoutPanel svcBox = new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
    readonly FlowLayoutPanel shipBox = W.Flow(false);
    readonly DamageMap damage = new() { Size = new Size(420, 260) };
    readonly Label lblMarks = W.Note("", 500, 60);
    readonly DataGridView items = W.Grid();
    readonly NumericUpDown nBudget = W.Money(180);
    readonly TextBox tSeal = new() { Width = 180, PlaceholderText = "رقم الملصق" };
    readonly Label lblBudget = W.Note("", 500, 22), lblPlan = W.Note("", 600, 22), lblCredit = W.Note("", 600, 22);
    readonly ModernButton bUseCredit = W.Btn("استعمال رصيد الزبون", "wallet", BtnKind.Soft, 170), bKeepCredit = W.Btn("حفظ الزائد رصيداً", "wallet", BtnKind.Ghost, 150);
    readonly TextBox tReferrer = new() { Width = 220, PlaceholderText = "اسم الزبون الذي أرسله" };
    readonly ComboBox cbExt = W.Combo(220, Array.Empty<string>());
    readonly ModernButton bUsePoints = W.Btn("استعمال النقاط", "gift", BtnKind.Soft, 150);
    List<(string Name, double Price)> extOpts = new();
    double lastFees;
    bool fillingExtras;

    // ---------------- البناء ----------------
    void BuildIntakeExtras(FlowLayoutPanel flow)
    {
        var r = W.Flow(false);
        r.Controls.Add(W.Labeled("كيف عرف بالمحل؟", cbSource));
        r.Controls.Add(W.Labeled("المنطقة", tArea, "pin"));
        r.Controls.Add(W.Labeled("أحاله زبون؟", tReferrer, "users"));
        lblPhone.Margin = new Padding(6, 30, 6, 0);
        r.Controls.Add(lblPhone);
        flow.Controls.Add(r);
        W.Suggest(tArea, Store.Orders.Select(o => o.X.Area));
        W.Suggest(tReferrer, Store.Orders.Select(o => o.CustomerName));

        flow.Controls.Add(W.Head("الخدمة والتوصيل", 960));
        svc.Margin = new Padding(6, 2, 6, 4);
        flow.Controls.Add(svc);
        var a = W.Flow(false);
        a.Controls.Add(W.Labeled("العنوان", tAddress, "pin"));
        a.Controls.Add(W.Labeled("موعد الزيارة / الاستلام", dVisit, "calendar"));
        svcBox.Controls.Add(a);
        var f = W.Flow(false);
        f.Controls.Add(W.Labeled("أجرة الانتقال (تُضاف للسعر)", nSvcFee));
        svcBox.Controls.Add(f);
        shipBox.Controls.Add(W.Labeled("شركة التوصيل", tShipCo));
        shipBox.Controls.Add(W.Labeled("رقم الشحنة", tShipNo));
        shipBox.Controls.Add(W.Labeled("حالة الشحن", cbShipState));
        shipBox.Controls.Add(W.Labeled("أجرة الشحن (تُضاف للسعر)", nShipFee));
        svcBox.Controls.Add(shipBox);
        flow.Controls.Add(svcBox);
        W.Suggest(tShipCo, Store.Orders.Select(o => o.X.ShipCompany));
        svc.Changed += _ => ServiceVisibility();
        nSvcFee.ValueChanged += (s, e) => FeesChanged();
        nShipFee.ValueChanged += (s, e) => FeesChanged();
        tPhone.Leave += (s, e) => PhoneCheck();
        tName.Leave += (s, e) => CreditInfo();
        tPhone.Leave += (s, e) => CreditInfo();
    }

    void ServiceVisibility()
    {
        bool other = svc.Value != "shop";
        svcBox.Visible = other;
        shipBox.Visible = svc.Value == "ship";
    }

    void BuildDamage(FlowLayoutPanel flow)
    {
        flow.Controls.Add(W.Head("الخدوش والكسور عند الاستلام", 960));
        var r = W.Flow(false);
        damage.Margin = new Padding(6);
        r.Controls.Add(damage);
        var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        side.Controls.Add(W.Note("انقر على الرسم لتحديد مكان كل خدش أو كسر، وبالزر الأيمن على العلامة لحذفها. تُطبع على الفاتورة وبطاقة العمل.", 480, 60));
        side.Controls.Add(lblMarks);
        r.Controls.Add(side);
        flow.Controls.Add(r);
        damage.Changed += () => lblMarks.Text = DamageMap.Describe(damage.Marks);
    }

    void BuildItems(FlowLayoutPanel flow)
    {
        flow.Controls.Add(W.Head("بنود الإصلاح (اختياري)", 960));
        flow.Controls.Add(W.Note("إذا كان في الجهاز أكثر من عطل: بند لكل عطل بسعره. السعر الكلي = البنود الموافَق عليها + أجور الانتقال والشحن.", 960, 22));
        items.ReadOnly = false;
        items.AllowUserToAddRows = false;
        items.SelectionMode = DataGridViewSelectionMode.CellSelect;
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "desc", HeaderText = "البند", FillWeight = 260 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "price", HeaderText = "السعر", FillWeight = 90, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
        items.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ok", HeaderText = "وافق الزبون", FillWeight = 70 });
        items.Columns.Add(new DataGridViewButtonColumn { Name = "rm", HeaderText = "", Text = "حذف", UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat, FillWeight = 40, DefaultCellStyle = { BackColor = Theme.DangerSoft, ForeColor = Theme.Danger, SelectionBackColor = Theme.DangerSoft, SelectionForeColor = Theme.Danger } });
        var host = new Panel { Width = 956, Height = 130, Margin = new Padding(6, 2, 6, 2), BackColor = Theme.Surface, Visible = false };
        host.Controls.Add(items);
        flow.Controls.Add(host);
        var bAdd = W.Btn("بند", "plus", BtnKind.Secondary, 90);
        flow.Controls.Add(bAdd);
        bAdd.Click += (s, e) =>
        {
            host.Visible = true;
            int i = items.Rows.Add("", "", true);
            items.CurrentCell = items.Rows[i].Cells["desc"];
            items.BeginEdit(true);
        };
        items.CellContentClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (items.Columns[e.ColumnIndex].Name == "rm") { var row = items.Rows[e.RowIndex]; BeginInvoke(() => { if (row.Index >= 0) items.Rows.Remove(row); ItemsChanged(); }); }
            else if (items.Columns[e.ColumnIndex].Name == "ok") items.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        items.CellValueChanged += (s, e) => { if (!fillingExtras) ItemsChanged(); };
        items.CellEndEdit += (s, e) =>
        {
            if (items.Columns[e.ColumnIndex].Name == "price")
            {
                double v = Txt.ParseMoney(items.Rows[e.RowIndex].Cells["price"].Value);
                items.Rows[e.RowIndex].Cells["price"].Value = v > 0 ? Txt.Num(v) : "";
            }
            ItemsChanged();
        };
        items.RowsAdded += (s, e) => host.Visible = true;
        items.RowsRemoved += (s, e) => host.Visible = items.Rows.Count > 0;
    }

    void BuildMoneyExtras(FlowLayoutPanel flow)
    {
        var r = W.Flow(false);
        r.Controls.Add(W.Labeled("سقف السعر المسموح (اختياري)", nBudget));
        r.Controls.Add(W.Labeled("رقم ملصق الضمان", tSeal, "tag"));
        extOpts = ExtWarranty.Options();
        cbExt.Items.Add("بدون");
        cbExt.Items.AddRange(extOpts.Select(e => (object)$"{e.Name} — {Txt.Money(e.Price)}").ToArray());
        cbExt.SelectedIndex = 0;
        if (extOpts.Count > 0) r.Controls.Add(W.Labeled("ضمان ممتد (يُباع)", cbExt, "shield-check"));
        cbExt.SelectedIndexChanged += (s, e) => FeesChanged();
        var bPlan = W.Btn("خطة تقسيط", "calendar", BtnKind.Secondary, 120); bPlan.Margin = new Padding(4, 27, 4, 4);
        r.Controls.Add(bPlan);
        bUseCredit.Margin = bKeepCredit.Margin = new Padding(4, 27, 4, 4);
        r.Controls.Add(bUseCredit);
        r.Controls.Add(bKeepCredit);
        bUsePoints.Margin = new Padding(4, 27, 4, 4);
        r.Controls.Add(bUsePoints);
        flow.Controls.Add(r);
        foreach (var l in new[] { lblBudget, lblPlan, lblCredit }) { l.Margin = new Padding(6, 0, 6, 0); flow.Controls.Add(l); }
        flow.Controls.Add(W.Note("أجرة الفحص تُستحق فقط إذا أُلغي الإصلاح؛ إذا وافق الزبون لا تُضاف على السعر، وما دفعه مقدّماً يُحسب من السعر.", 960, 22));
        nBudget.ValueChanged += (s, e) => BudgetInfo();
        nPrice.ValueChanged += (s, e) => BudgetInfo();
        bPlan.Click += (s, e) =>
        {
            double due = (double)nPrice.Value - FormPaid;
            if (due <= 0 && x.Plan.Count == 0) { Toast.Show("لا يوجد مبلغ متبقٍ لتقسيطه", Tone.Info); return; }
            using var d = new PlanDialog(Math.Max(0, due), x.Plan);
            if (d.ShowModal() != DialogResult.OK) return;
            x.Plan = d.Plan;
            PlanInfo();
        };
        bUseCredit.Click += (s, e) =>
        {
            double avail = AvailableCredit(), due = (Cancelling ? (double)nFee.Value : (double)nPrice.Value) - FormPaid;
            double use = Math.Min(avail, due);
            if (use <= 0) { Toast.Show(avail <= 0 ? "لا يوجد رصيد لهذا الزبون" : "لا يوجد مبلغ متبقٍ", Tone.Info); return; }
            payments.Add(new Payment { Id = Txt.Uid("cr"), Amount = use, Date = Txt.Today, Method = Lists.Credit, Note = "من رصيد الزبون" });
            RenderPayments();
            CreditInfo();
        };
        bUsePoints.Click += (s, e) =>
        {
            int pts = AvailablePoints();
            double due = (double)nPrice.Value - FormPaid, use = Math.Min(Loyalty.Worth(pts), due);
            if (pts < Loyalty.MinRedeem) { Toast.Show($"رصيده {pts} نقطة — أقل من الحد الأدنى للاستبدال ({Loyalty.MinRedeem})", Tone.Info); return; }
            if (use <= 0) { Toast.Show("لا يوجد مبلغ متبقٍ", Tone.Info); return; }
            int spend = (int)Math.Ceiling(use / Loyalty.PointValue);
            payments.Add(new Payment { Id = Txt.Uid("pt"), Amount = use, Date = Txt.Today, Method = Lists.Points, Note = $"استبدال {spend} نقطة" });
            RenderPayments();
            CreditInfo();
        };
        bKeepCredit.Click += (s, e) =>
        {
            double due = Cancelling ? (double)nFee.Value : (double)nPrice.Value, over = FormPaid - due;
            if (over <= 0) { Toast.Show("لا يوجد مبلغ زائد", Tone.Info); return; }
            payments.Add(new Payment { Id = Txt.Uid("cr"), Amount = -over, Date = Txt.Today, Method = Lists.Credit, Note = "تحويل الزائد إلى رصيد الزبون" });
            RenderPayments();
            CreditInfo();
        };
    }

    // ---------------- التعبئة ----------------
    void FillExtras(Order src, bool isNew)
    {
        fillingExtras = true;
        x = existing != null ? Extra.Copy(existing.X) : new OrderExtra();
        if (existing == null && src != null) { x.Area = src.X.Area; x.Address = src.X.Address; }
        cbSource.Text = x.Source;
        tArea.Text = x.Area;
        svc.Value = x.Service;
        tAddress.Text = x.Address;
        if (Txt.ParseTime(x.VisitAt) is DateTime vt) { dVisit.Value = vt; dVisit.Checked = true; } else { dVisit.Value = DateTime.Now; dVisit.Checked = false; }
        W.Set(nSvcFee, x.ServiceFee);
        W.Set(nShipFee, x.ShipFee);
        lastFees = x.ServiceFee + x.ShipFee;
        tShipCo.Text = x.ShipCompany; tShipNo.Text = x.ShipTracking; W.Pick(cbShipState, x.ShipStatus);
        damage.Marks = x.Marks.Select(m => new Mark { Side = m.Side, X = m.X, Y = m.Y, Note = m.Note }).ToList();
        lblMarks.Text = DamageMap.Describe(damage.Marks);
        foreach (var i in x.Items) items.Rows.Add(i.Desc, i.Price > 0 ? Txt.Num(i.Price) : "", i.Approved);
        if (x.MaxBudget is double b) W.Set(nBudget, b);
        tSeal.Text = x.SealNo;
        tReferrer.Text = x.ReferredBy;
        int ei = extOpts.FindIndex(e => e.Name == x.ExtWarranty);
        if (ei < 0 && x.ExtWarranty != "") { extOpts.Add((x.ExtWarranty, x.ExtWarrantyFee)); cbExt.Items.Add($"{x.ExtWarranty} — {Txt.Money(x.ExtWarrantyFee)}"); ei = extOpts.Count - 1; }
        cbExt.SelectedIndex = ei + 1;
        lastFees = Fees;
        fillingExtras = false;
        ServiceVisibility();
        ItemsChanged(false);
        BudgetInfo();
        PlanInfo();
        CreditInfo();
        PhoneCheck();
    }

    string ExtrasSnapshot() => string.Join("|", cbSource.Text, tArea.Text, svc.Value, tAddress.Text, dVisit.Checked ? dVisit.Value.ToString("s") : "", nSvcFee.Value, nShipFee.Value,
        tShipCo.Text, tShipNo.Text, cbShipState.Text, nBudget.Value, tSeal.Text, tReferrer.Text, cbExt.SelectedIndex, string.Join(";", damage.Marks.Select(m => $"{m.Side}{m.X}{m.Y}{m.Note}")),
        string.Join(";", FormItems().Select(i => $"{i.Desc}/{i.Price}/{i.Approved}")), string.Join(";", x.Plan.Select(p => p.Date + p.Amount)));

    List<JobItem> FormItems() => items.Rows.Cast<DataGridViewRow>().Select(r => new JobItem
    {
        Desc = Convert.ToString(r.Cells["desc"].Value)?.Trim() ?? "",
        Price = Math.Max(0, Txt.ParseMoney(r.Cells["price"].Value)),
        Approved = r.Cells["ok"].Value is bool b ? b : true,
    }).Where(i => i.Desc != "" || i.Price > 0).ToList();

    // ---------------- الحسابات ----------------
    double ExtFee => cbExt.SelectedIndex > 0 && cbExt.SelectedIndex <= extOpts.Count ? extOpts[cbExt.SelectedIndex - 1].Price : 0;
    double Fees => (double)nSvcFee.Value + (double)nShipFee.Value + ExtFee;

    void FeesChanged()
    {
        if (fillingExtras) return;
        double now = Fees;
        if (FormItems().Count > 0) ItemsChanged();
        else W.Set(nPrice, Math.Max(0, (double)nPrice.Value + now - lastFees));
        lastFees = now;
    }

    void ItemsChanged(bool setPrice = true)
    {
        var list = FormItems();
        nPrice.Enabled = list.Count == 0;
        if (list.Count > 0 && setPrice) W.Set(nPrice, list.Where(i => i.Approved).Sum(i => i.Price) + Fees);
        else if (list.Count > 0 && !setPrice) nPrice.Enabled = false;
    }

    void BudgetInfo()
    {
        double b = (double)nBudget.Value, price = (double)nPrice.Value, cost = FormParts().Sum(p => p.Cost);
        if (b <= 0) { lblBudget.Text = ""; return; }
        bool over = price > b || cost > b;
        lblBudget.Text = over ? $"⚠ تجاوز السقف الذي حدده الزبون ({Txt.Money(b)}) — اتصل به قبل المتابعة" : $"سقف الزبون {Txt.Money(b)} — ضمن الحد";
        lblBudget.ForeColor = over ? Pal.Bad : Pal.Good;
    }

    void PlanInfo()
    {
        lblPlan.Text = x.Plan.Count == 0 ? "" : $"📅 تقسيط: {x.Plan.Count} أقساط — " + string.Join("، ", x.Plan.Take(4).Select(p => $"{Txt.FmtShortDate(p.Date)} {Txt.Money(p.Amount)}")) + (x.Plan.Count > 4 ? "…" : "");
        lblPlan.ForeColor = Theme.BrandDark;
    }

    string CustomerKeyNow => Calc.CustomerKey(tName.Text.Trim(), Txt.LatinDigits(tPhone.Text.Trim()));

    /// <summary>رصيد الزبون المتاح: من طلباته الأخرى، ناقص ما استُعمل في هذا النموذج</summary>
    double AvailableCredit()
    {
        var key = CustomerKeyNow;
        double others = -Store.Orders.Where(o => o.Id != existing?.Id && Calc.CustomerKey(o) == key).SelectMany(o => o.PaymentHistory).Where(p => p.IsCredit).Sum(p => p.Amount);
        double here = -payments.Where(p => p.IsCredit).Sum(p => p.Amount);
        double granted = Grants.For(key).Sum(g => g.Amount);   // مكافآت الإحالة وما شابه
        return Math.Round(others + here + granted, 2);
    }

    /// <summary>نقاط الزبون المتاحة: المكتسبة ناقص المستعملة في طلباته الأخرى وفي هذا النموذج</summary>
    int AvailablePoints()
    {
        if (!Loyalty.On) return 0;
        var key = CustomerKeyNow;
        double used = Store.Orders.Where(o => o.Id != existing?.Id && Calc.CustomerKey(o) == key).SelectMany(o => o.PaymentHistory).Where(p => p.IsPoints).Sum(p => p.Amount)
                      + payments.Where(p => p.IsPoints).Sum(p => p.Amount);
        return Math.Max(0, Loyalty.Earned(key) - (int)Math.Round(used / Loyalty.PointValue));
    }

    void CreditInfo()
    {
        double c = tName.Text.Trim() == "" ? 0 : AvailableCredit();
        bUseCredit.Visible = c > 0;
        bUseCredit.Text = c > 0 ? $"استعمال الرصيد ({Txt.Money(c)})" : "استعمال رصيد الزبون";
        bUseCredit.FitWidth(170);
        double due = Cancelling ? (double)nFee.Value : (double)nPrice.Value;
        bKeepCredit.Visible = FormPaid - due > 0.001;
        int pts = tName.Text.Trim() == "" || SelectedAccount != null ? 0 : AvailablePoints();
        bUsePoints.Visible = Loyalty.On && pts >= Loyalty.MinRedeem;
        bUsePoints.Text = $"استعمال النقاط ({pts} = {Txt.Money(Loyalty.Worth(pts))})";
        bUsePoints.FitWidth(150);
        lblCredit.Text = string.Join("   ", new[] { c > 0 ? $"💳 لهذا الزبون رصيد {Txt.Money(c)} من طلبات سابقة" : "", Loyalty.On && pts > 0 ? $"🎁 نقاط الولاء: {pts}" : "" }.Where(t => t != ""));
        lblCredit.ForeColor = Pal.Good;
    }

    void PhoneCheck()
    {
        var p = Phones.Problem(tPhone.Text);
        lblPhone.Text = p == "" ? "" : "⚠ " + p;
        lblPhone.ForeColor = Pal.Bad;
    }

    // ---------------- الحفظ ----------------
    /// <summary>تحققات قبل الحفظ: الهاتف، اسم الموديل، السقف، حد ائتمان التاجر</summary>
    bool ValidateExtras(double price)
    {
        var pp = Phones.Problem(tPhone.Text);
        if (pp != "" && !W.Confirm("رقم الهاتف", $"{tPhone.Text}\n{pp}. الحفظ على أي حال؟", "حفظ")) { tPhone.Focus(); return false; }
        var similar = Models.Similar(tDevice.Text);
        if (similar != null)
        {
            var r = W.Ask3("اسم الجهاز", $"كتبت «{tDevice.Text.Trim()}». هل تقصد «{similar}»؟\nتوحيد الاسم يجعل البحث والتقارير أدق.", $"نعم، «{similar}»", "الإبقاء كما كتبت");
            if (r == false) return false;
            if (r == true) tDevice.Text = similar;
        }
        double b = (double)nBudget.Value;
        if (b > 0 && price > b && !W.Confirm("تجاوز سقف الزبون", $"السعر {Txt.Money(price)} أكبر من السقف الذي حدده الزبون ({Txt.Money(b)}).\nهل اتصلت به ووافق؟", "نعم، وافق")) return false;
        if (SelectedAccount is Account acc && acc.CreditLimit > 0)
        {
            double other = Accounts.OrdersOf(acc).Where(o => o.Id != existing?.Id).Sum(Calc.RemainingOf);
            double mine = Math.Max(0, price - FormPaid);
            if (other + mine > acc.CreditLimit &&
                !W.Confirm("تجاوز حد الائتمان", $"{acc.Name}: عليه {Txt.Money(other)} وحدّه {Txt.Money(acc.CreditLimit)}.\nبهذا الطلب يصبح {Txt.Money(other + mine)}. المتابعة؟", "متابعة")) return false;
        }
        if (svc.Value is "onsite" or "pickup" && tAddress.Text.Trim() == "" && !W.Confirm("بدون عنوان", "اخترت خدمة خارج المحل دون كتابة العنوان. الحفظ على أي حال؟", "حفظ")) return false;
        return true;
    }

    void ApplyExtras(Order o)
    {
        x.Source = cbSource.Text.Trim();
        x.Area = tArea.Text.Trim();
        x.Service = svc.Value;
        x.Address = x.Service == "shop" ? "" : tAddress.Text.Trim();
        x.VisitAt = x.Service != "shop" && dVisit.Checked ? dVisit.Value.ToString("yyyy-MM-ddTHH:mm:00") : "";
        x.ServiceFee = x.Service == "shop" ? 0 : (double)nSvcFee.Value;
        bool ship = x.Service == "ship";
        x.ShipCompany = ship ? tShipCo.Text.Trim() : ""; x.ShipTracking = ship ? tShipNo.Text.Trim() : ""; x.ShipStatus = ship ? cbShipState.Text : "";
        x.ShipFee = ship ? (double)nShipFee.Value : 0;
        x.Marks = damage.Marks.ToList();
        x.Items = FormItems();
        x.MaxBudget = nBudget.Value > 0 ? (double)nBudget.Value : null;
        x.SealNo = tSeal.Text.Trim();
        x.ReferredBy = tReferrer.Text.Trim();
        if (x.ReferredBy != "" && Txt.Fold(x.ReferredBy) != Txt.Fold(o.CustomerName))
            x.ReferredPhone = Store.Orders.Where(p => Txt.Fold(p.CustomerName) == Txt.Fold(x.ReferredBy) && p.Phone != "").OrderByDescending(p => p.DateReceived, StringComparer.Ordinal).FirstOrDefault()?.Phone ?? x.ReferredPhone;
        else { x.ReferredBy = ""; x.ReferredPhone = ""; }
        x.ExtWarranty = cbExt.SelectedIndex > 0 && cbExt.SelectedIndex <= extOpts.Count ? extOpts[cbExt.SelectedIndex - 1].Name : "";
        x.ExtWarrantyFee = x.ExtWarranty != "" ? ExtFee : 0;
        if (x.Branch == "") x.Branch = Branches.Current;
        o.X = x;
    }
}
