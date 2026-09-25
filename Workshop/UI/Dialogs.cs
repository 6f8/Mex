using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>تفاصيل الطلب: مراحل العمل، تغيير الحالة بضغطة، البيانات، الصورة، القطع والحساب والدفعات</summary>
public partial class OrderView : DialogShell
{
    readonly string id;
    Control content;

    public OrderView(string orderId) : base("تفاصيل الطلب", 1080, 820, "clipboard-list")
    {
        id = orderId;
        var o = Calc.Find(id);
        Text = o == null ? "الطلب غير موجود" : $"{o.Device} — {o.CustomerName}   ({o.RefNo})";
        content = Build();
        Body.Controls.Add(content);
        var bEdit = AddButton("تعديل", DialogResult.None, BtnKind.Primary, "pencil");
        bEdit.Click += (s, e) => { var x = Calc.Find(id); Close(); Ui2.Later(() => Acts.Edit(x)); };
        if (o != null && Calc.RemainingOf(o) > 0)
        {
            var bPay = AddButton("تسجيل دفعة", DialogResult.None, BtnKind.Success, "wallet");
            bPay.Click += (s, e) => { QuickPayDialog.ForOrder(Calc.Find(id)); Refresh2(); };
        }
        AddButton("واتساب", DialogResult.None, BtnKind.Secondary, "message-circle").Click += (s, e) => Acts.WhatsApp(Calc.Find(id));
        var bPrint = AddButton("طباعة", DialogResult.None, BtnKind.Secondary, "printer");
        bPrint.Click += (s, e) => { if (Calc.Find(id) is Order x) PrintMenu.Show(bPrint, x); };
        if (o != null && (o.Parts.Count > 0 || Calc.Find(o.WarrantyOf)?.Parts.Count > 0))
            AddButton("قطعة معيبة", DialogResult.None, BtnKind.Ghost, "triangle-alert").Click += (s, e) => { if (Calc.Find(id) is Order x) DefectDialog.ForOrder(x); };
        var bDel = AddButton("حذف", DialogResult.None, BtnKind.Danger, "trash-2");
        bDel.Click += (s, e) => { if (Acts.Delete(Calc.Find(id))) Close(); };
        Store.Changed += OnChanged;
        FormClosed += (s, e) => Store.Changed -= OnChanged;
    }

    void OnChanged() { if (!IsDisposed && IsHandleCreated) BeginInvoke(Refresh2); }

    void Refresh2()
    {
        if (IsDisposed) return;
        var o = Calc.Find(id);
        if (o == null) { Close(); return; }
        var y = (content as ScrollableControl)?.AutoScrollPosition ?? Point.Empty;
        Body.SuspendLayout();
        Body.Controls.Remove(content);
        content.Dispose();
        content = Dpi.Fit(Body, Build());
        Body.Controls.Add(content);
        Body.ResumeLayout();
        if (content is ScrollableControl sc) sc.AutoScrollPosition = new Point(-y.X, -y.Y);
    }

    static Label Lbl(string text, Font f, Color c, int w, int h = 24) => new()
    {
        Text = text, AutoSize = false, Width = w, Height = h, Font = f, ForeColor = c, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface, AutoEllipsis = true
    };

    static Control KV(string k, string v, int w = 440, Color? color = null, int h = 54)
    {
        var p = new Panel { Width = w, Height = h, Margin = new Padding(4, 2, 4, 2), BackColor = Theme.Surface };
        var vl = Lbl(v == "" ? "—" : v, Theme.FS(10), color ?? Theme.Ink, w, h - 22);
        vl.Dock = DockStyle.Fill;
        vl.TextAlign = ContentAlignment.TopLeft;
        var kl = Lbl(k, Theme.F(8.5f), Theme.Muted, w, 20);
        kl.Dock = DockStyle.Top;
        p.Controls.Add(vl);
        p.Controls.Add(kl);
        return p;
    }

    Control Build()
    {
        var o = Calc.Find(id);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(0, 4, 0, 16) };
        if (o == null) { flow.Controls.Add(W.Note("الطلب غير موجود — ربما حُذف.", 880)); return flow; }
        flow.Controls.Add(new StepsBar { Status = o.Status, Width = 900, Height = 54, Margin = new Padding(4, 4, 4, 8) });
        flow.Controls.Add(W.Note("تغيير الحالة", 900, 22));
        var st = W.Flow();
        foreach (var s in K.Statuses)
        {
            var b = new ModernButton { Text = s, Kind = s == o.Status ? BtnKind.Primary : BtnKind.Secondary, Height = 36, Margin = new Padding(3) };
            b.FitWidth(80);
            var target = s;
            b.Click += (_, _) => Acts.SetStatus(Calc.Find(id), target);
            st.Controls.Add(b);
        }
        st.MaximumSize = new Size(920, 0);
        flow.Controls.Add(st);

        var grid = W.Flow();
        grid.MaximumSize = new Size(920, 0);
        grid.Margin = new Padding(0, 10, 0, 0);
        grid.Controls.Add(KV("الزبون", o.CustomerName));
        if (Accounts.Find(o.AccountId) is Account acc) grid.Controls.Add(KV("الحساب", $"{acc.Name} ({Accounts.KindText(acc)}) — يُسدَّد من الحساب", 890, Theme.BrandDark));
        grid.Controls.Add(KV("الهاتف", o.Phone));
        grid.Controls.Add(KV("الجهاز", o.Device));
        grid.Controls.Add(KV("نوع العطل", o.IssueType));
        if (o.Technician != "" || Techs.All.Count > 0)
            grid.Controls.Add(KV("الفني", o.Technician == "" ? "لم يُحدَّد" : o.Technician + (o.Status == K.Done && Techs.Commission(o) > 0 ? $"   (عمولته {Txt.Money(Techs.Commission(o))})" : ""), 890, null, 54));
        grid.Controls.Add(KV("وصف العطل", o.Issue, 890, null, 70));
        if (o.Passcode != "")
        {
            var p = (Panel)KV("رمز القفل", "••••");
            var v = p.Controls.OfType<Label>().First(l => l.Dock == DockStyle.Fill);
            var eye = new ModernButton { Kind = BtnKind.Ghost, IconName = "eye", Size = new Size(30, 30), Dock = DockStyle.Right };
            var hide = new System.Windows.Forms.Timer { Interval = 15000 };
            hide.Tick += (_, _) => { hide.Stop(); if (!v.IsDisposed) v.Text = "••••"; };
            eye.Click += (_, _) => { bool shown = v.Text != "••••"; v.Text = shown ? "••••" : o.Passcode; if (!shown) hide.Start(); };
            p.Disposed += (_, _) => hide.Dispose();
            p.Controls.Add(eye);
            eye.BringToFront();
            grid.Controls.Add(p);
        }
        grid.Controls.Add(KV("الملحقات", o.Accessories.Count > 0 ? string.Join("، ", o.Accessories) : "لا شيء"));
        var we = Calc.WarrantyEnd(o);
        grid.Controls.Add(KV("الضمان", o.Warranty + (we != "" ? (Calc.InWarranty(o) ? " — ساري حتى " : " — انتهى في ") + Txt.FmtDate(we) : ""), 440, we != "" ? (Calc.InWarranty(o) ? Pal.Good : Pal.Bad) : null));
        if (o.Imei != "") grid.Controls.Add(KV("IMEI / الرقم التسلسلي", o.Imei));
        var src = Calc.Find(o.WarrantyOf);
        if (src != null) grid.Controls.Add(LinkKV("طلب ضمان — رجع بالضمان من الطلب", $"{src.RefNo} — {src.IssueType} — {Txt.FmtDate(src.DateDelivered)}", src));
        foreach (var r in Store.Orders.Where(x => x.WarrantyOf == o.Id))
            grid.Controls.Add(LinkKV("رجع بالضمان بعد هذا الطلب", $"{r.RefNo} — {Txt.FmtDate(r.DateReceived)}", r));
        string checks = o.ChecksNA ? "الجهاز لم يكن يعمل عند الاستلام، فلم يُفحص"
            : o.Checks.Count == 0 ? "لم يُسجَّل فحص عند الاستلام"
            : string.Join("   ", Lists.OrderChecks(o).Where(c => c.State == "bad").Select(c => "✕ " + c.Title)
                .Concat(Lists.OrderChecks(o).Where(c => c.State == "ok").Select(c => "✓ " + c.Title)));
        grid.Controls.Add(KV("حالة الجهاز عند الاستلام", checks, 890, o.Checks.Values.Contains("bad") ? Pal.Bad : null, 70));
        grid.Controls.Add(KV("مدة العمل", Calc.DurationText(o), 290));
        grid.Controls.Add(KV("تاريخ الاستلام", Txt.FmtDate(o.DateReceived), 290));
        grid.Controls.Add(KV("التسليم المتوقع", Txt.FmtDate(o.DateEstimated) + (Calc.IsLate(o) ? $" (متأخر {Calc.LateDays(o)} يوم)" : ""), 290, Calc.IsLate(o) ? Pal.Bad : null));
        grid.Controls.Add(KV("التسليم الفعلي", Txt.FmtDate(o.DateDelivered), 290));
        if (o.Notes != "") grid.Controls.Add(KV("ملاحظات", o.Notes, 890, null, 80));
        flow.Controls.Add(grid);

        BuildExtras(flow, o);

        if (o.PhotoRef != null && Store.LoadPhoto(o.PhotoRef) is Image img)
        {
            flow.Controls.Add(W.Head("صورة الجهاز عند الاستلام", 900));
            var pb = new PictureBox { Width = 400, Height = 240, SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = Theme.SurfaceAlt, Cursor = Cursors.Hand, Margin = new Padding(6) };
            pb.Click += (_, _) => PhotoDialog.Show(img);
            pb.Disposed += (_, _) => img.Dispose();
            flow.Controls.Add(pb);
        }

        flow.Controls.Add(W.Head("القطع والحساب", 900));
        if (o.Parts.Count == 0) flow.Controls.Add(W.Note("لا توجد قطع مسجّلة", 900));
        foreach (var i in o.X.Items) flow.Controls.Add(Line($"🔧 {i.Desc}{(i.Approved ? "" : "   — لم يوافق عليه الزبون")}", Txt.Money(i.Price), i.Approved ? null : Theme.Muted));
        foreach (var p in o.Parts) flow.Controls.Add(Line($"{(p.Name == "" ? "قطعة" : p.Name)}{(p.Supplier != "" ? $"  ({p.Supplier})" : "")}{(p.Serial != "" ? $"  رقم {p.Serial}" : "")}", Txt.Money(p.Cost)));
        foreach (var d in Store.Defects.Where(d => d.OrderId == o.Id))
            flow.Controls.Add(Line($"⚠ قطعة معيبة: {d.PartName}{(d.Supplier != "" ? $"  ({d.Supplier})" : "")} — {Defects.StateText(d)}", Txt.Money(d.Cost), Pal.Bad));
        double rem = Calc.RemainingOf(o), cost = Calc.PartsCost(o), prof = Calc.ShownProfit(o);
        var money = W.Flow();
        money.Margin = new Padding(0, 8, 0, 0);
        money.Controls.Add(KV(o.Status == K.Cancelled ? "أجرة الفحص (الطلب ملغى)" : "السعر على الزبون", Txt.Money(Calc.ChargeOf(o)), 290));
        money.Controls.Add(KV("تكلفة القطع / الربح", $"{Txt.Money(cost)}  ({Txt.Money(prof)})", 290, prof >= 0 ? Pal.Good : Pal.Bad));
        money.Controls.Add(KV(o.PaymentStatus, rem > 0 ? "باقي " + Txt.Money(rem) : "مسدد", 290, rem > 0 ? Pal.Bad : Pal.Good));
        flow.Controls.Add(money);
        if (o.PaymentHistory.Count > 0)
        {
            flow.Controls.Add(W.Head("الدفعات", 900));
            foreach (var p in o.PaymentHistory)
                flow.Controls.Add(p.IsRefund
                    ? Line($"↩ مُرجَع للزبون {Txt.FmtDate(p.Date)} — {p.Method} — {p.Note.Replace("استرجاع: ", "")}", "- " + Txt.Money(-p.Amount), Pal.Bad)
                    : Line($"{Txt.FmtDate(p.Date)} — {p.Method}{(p.Note != "" ? " — " + p.Note : "")}", Txt.Money(p.Amount), Pal.Good));
        }
        if (o.Paid > 0)
        {
            var pr = W.Flow(false);
            var bRefund = W.Btn("إرجاع مبلغ للزبون", "rotate-ccw", BtnKind.Ghost, 150);
            bRefund.Click += (_, _) => RefundDialog.ForOrder(Calc.Find(id));
            pr.Controls.Add(bRefund);
            if (Refunds.Overpaid(o) > 0)
            {
                var bKeep = W.Btn($"حفظ الزائد ({Txt.Money(Refunds.Overpaid(o))}) رصيداً للزبون", "wallet", BtnKind.Soft, 220);
                bKeep.Click += (_, _) => { if (Calc.Find(id) is Order x) { Credits.KeepOverpaid(x); Store.NotifyChanged(); Toast.Show("حُفظ الزائد رصيداً للزبون"); } };
                pr.Controls.Add(bKeep);
            }
            flow.Controls.Add(pr);
        }
        BuildAfterPayments(flow, o);

        // ---------- التذكيرات ----------
        var rems = Reminders.For(o.Id);
        flow.Controls.Add(W.Head(rems.Count > 0 ? "التذكيرات" : "التذكيرات — لا يوجد", 900));
        foreach (var r in rems)
        {
            var line = Line($"{(r.Done ? "✓ " : "⏰ ")}{r.Text}", r.Done ? "أُنجز" : Reminders.When(r),
                r.Done ? Theme.Muted : string.CompareOrdinal(r.Date, Txt.Today) <= 0 ? Pal.Bad : Pal.Primary);
            line.Cursor = Cursors.Hand;
            foreach (Control c in line.Controls) { c.Cursor = Cursors.Hand; c.Click += (_, _) => { Reminders.SetDone(r, !r.Done); Store.NotifyChanged(); }; }
            new ToolTip().SetToolTip(line.Controls[0], "انقر لتعليمه منجزاً أو إلغاء ذلك");
            flow.Controls.Add(line);
        }
        var bRem = W.Btn("تذكير جديد", "bell", BtnKind.Soft, 130);
        bRem.Click += (_, _) => ReminderDialog.New(id);
        flow.Controls.Add(bRem);

        // ---------- سجل التعديلات ----------
        if (o.History.Count > 0)
        {
            flow.Controls.Add(W.Head("سجل التعديلات", 900));
            foreach (var h in o.History.AsEnumerable().Reverse())
                flow.Controls.Add(Line(h.Text, Txt.FmtDate(Txt.Cut10(h.At)) + " " + (Txt.ParseTime(h.At)?.ToString("HH:mm") ?? ""), Theme.Muted));
        }
        return flow;
    }

    static Control Line(string left, string right, Color? rc = null)
    {
        var p = new Panel { Width = 890, Height = 34, Margin = new Padding(6, 0, 6, 0), BackColor = Theme.Surface };
        var a = Lbl(right, Theme.FS(10), rc ?? Theme.Ink, 200, 34); a.Dock = DockStyle.Left; a.TextAlign = ContentAlignment.MiddleRight;
        var b = Lbl(left, Theme.F(10), Theme.Text2, 690, 34); b.Dock = DockStyle.Fill;
        p.Controls.Add(b);
        p.Controls.Add(a);
        p.Paint += (s, e) => { using var pen = new Pen(Theme.Border) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }; e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1); };
        return p;
    }

    Control LinkKV(string k, string v, Order target)
    {
        var p = (Panel)KV(k, v, 890);
        var l = p.Controls.OfType<Label>().First(x => x.Dock == DockStyle.Fill);
        l.ForeColor = Theme.Brand;
        l.Cursor = Cursors.Hand;
        l.Click += (_, _) => { Close(); Ui2.Later(() => Acts.View(target)); };
        return p;
    }
}

/// <summary>رسالة واتساب: قوالب جاهزة ونص قابل للتعديل، ثم فتح واتساب أو نسخ النص</summary>
public class WaDialog : DialogShell
{
    public record Template(string Id, string Title, string Text);
    readonly TextBox text = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Theme.F(10.5f), Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    readonly string phone;

    public WaDialog(string name, string phone, List<Template> templates, int pick) : base("رسالة واتساب", 860, 600, "message-circle", Pal.Good)
    {
        this.phone = phone;
        Text = $"رسالة واتساب — {name}   {(string.IsNullOrWhiteSpace(phone) ? "(لا يوجد رقم هاتف)" : phone)}";
        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 250, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(0, 0, 6, 0) };
        var btns = new List<ModernButton>();
        for (int i = 0; i < templates.Count; i++)
        {
            var t = templates[i];
            var b = new ModernButton { Text = t.Title, Kind = i == pick ? BtnKind.Soft : BtnKind.Ghost, Width = 236, Height = 42, Margin = new Padding(0, 2, 0, 2) };
            b.Click += (_, _) => { text.Text = t.Text; foreach (var x in btns) x.Kind = x == b ? BtnKind.Soft : BtnKind.Ghost; };
            btns.Add(b);
            side.Controls.Add(b);
        }
        var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Theme.SurfaceAlt };
        var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Theme.Surface };
        inner.Controls.Add(text);
        box.Controls.Add(inner);
        Body.Controls.Add(box);
        if (templates.Count > 1) { Body.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 12, BackColor = Theme.Surface }); Body.Controls.Add(side); }
        text.Text = templates[Math.Clamp(pick, 0, templates.Count - 1)].Text;
        var send = AddButton("فتح واتساب", DialogResult.None, BtnKind.Success, "send");
        AddButton("نسخ النص", DialogResult.None, BtnKind.Secondary, "copy").Click += (_, _) => W.Copy(text.Text);
        AddButton("إلغاء", DialogResult.Cancel, BtnKind.Ghost);
        send.Click += (_, _) =>
        {
            var p = Txt.WaPhone(this.phone);
            if (p == "") { W.Copy(text.Text); Dialogs.Warn("لا يوجد رقم هاتف صالح — نُسخ النص لتلصقه يدوياً."); return; }
            W.OpenUrl($"https://wa.me/{p}?text={Uri.EscapeDataString(text.Text)}");
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}

/// <summary>تسجيل دفعة سريعة لطلب واحد أو لكل طلبات زبون (الديون والمبالغ المتوقعة)</summary>
public class QuickPayDialog : DialogShell
{
    readonly string orderId, customerKey;
    readonly Seg method = Seg.Of(K.PayMethods);
    Control rows;

    QuickPayDialog(string orderId, string customerKey, string name) : base("تسجيل دفعة — " + name, 760, 520, "wallet", Pal.Good)
    {
        this.orderId = orderId;
        this.customerKey = customerKey;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Surface, WrapContents = false };
        top.Controls.Add(new Label { Text = "طريقة الدفع", AutoSize = false, Width = 100, Height = 44, Font = Theme.F(9.5f), ForeColor = Theme.Text2, TextAlign = ContentAlignment.MiddleLeft });
        top.Controls.Add(method);
        rows = Build();
        Body.Controls.Add(rows);
        Body.Controls.Add(top);
        Body.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 26, Text = "عدّل المبلغ إن كانت الدفعة جزئية. تُسجَّل بتاريخ اليوم.", Font = Theme.F(8.5f), ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft });
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Secondary);
    }

    public static void ForOrder(Order o)
    {
        if (o == null || Calc.RemainingOf(o) <= 0) return;
        using var d = new QuickPayDialog(o.Id, null, o.CustomerName);
        d.ShowModal();
    }

    public static void ForCustomer(string key)
    {
        var c = Calc.GetCustomers().FirstOrDefault(x => x.Key == key);
        if (c == null || c.Unpaid.Count + c.Pending.Count == 0) return;
        using var d = new QuickPayDialog(null, key, c.Name);
        d.ShowModal();
    }

    List<Order> Orders()
    {
        if (orderId != null) { var o = Calc.Find(orderId); return o != null && Calc.RemainingOf(o) > 0 ? new() { o } : new(); }
        var c = Calc.GetCustomers().FirstOrDefault(x => x.Key == customerKey);
        return c == null ? new() : c.Unpaid.Concat(c.Pending).ToList();
    }

    Control Build()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        foreach (var o in Orders())
        {
            var row = new Panel { Width = 690, Height = 70, Margin = new Padding(2, 4, 2, 4), BackColor = Theme.SurfaceAlt };
            var info = new Label
            {
                Text = $"{o.Device}   {o.RefNo}{(o.Status != K.Done ? "   • " + o.Status : "")}\nباقي {Txt.Money(Calc.RemainingOf(o))} من {Txt.Money(Calc.ChargeOf(o))}{(o.Status == K.Cancelled ? " (أجرة فحص)" : "")}",
                Dock = DockStyle.Fill, Font = Theme.F(9.5f), ForeColor = Theme.Ink, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0)
            };
            var amount = W.Money(160);
            W.Set(amount, Calc.RemainingOf(o));
            var ab = new InputBox(amount, 160) { Dock = DockStyle.Left, Margin = new Padding(0) };
            var host = new Panel { Dock = DockStyle.Left, Width = 176, Padding = new Padding(8, 15, 8, 15), BackColor = Theme.SurfaceAlt };
            host.Controls.Add(ab);
            var rec = new ModernButton { Text = "تسجيل", Kind = BtnKind.Success, IconName = "check", Dock = DockStyle.Left, Width = 100 };
            var bh = new Panel { Dock = DockStyle.Left, Width = 116, Padding = new Padding(8, 15, 8, 15), BackColor = Theme.SurfaceAlt };
            bh.Controls.Add(rec);
            var target = o;
            rec.Click += (_, _) => Record(target, (double)amount.Value);
            row.Controls.Add(info);
            row.Controls.Add(host);
            row.Controls.Add(bh);
            flow.Controls.Add(row);
        }
        return flow;
    }

    void Record(Order o, double amount)
    {
        o = Calc.Find(o.Id);
        if (o == null) return;
        double rem = Calc.RemainingOf(o);
        if (amount <= 0) return;
        if (amount > rem + 0.001) { Dialogs.Warn($"المبلغ أكبر من المتبقي ({Txt.Money(rem)})."); return; }
        var n = o.Clone();
        n.PaymentHistory.Add(new Payment { Id = Txt.Uid("inst"), Amount = amount, Date = Txt.Today, Note = "دفعة سريعة", Method = method.Value });
        n.Paid += amount;
        n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
        n.UpdatedAt = Txt.Now;
        Store.SaveOrder(n);
        Store.NotifyChanged();
        Toast.Show($"سُجّلت دفعة {Txt.Money(amount)} — {n.CustomerName}");
        if (n.PaymentStatus == K.PayFull)
        {
            var left = Orders();
            if (left.Count == 0) { DialogResult = DialogResult.OK; Close(); }
            else Rebuild();
            Ui2.Later(() => Acts.OfferReceipt(n));
            return;
        }
        Rebuild();
    }

    void Rebuild()
    {
        if (Orders().Count == 0) { Close(); return; }
        Body.SuspendLayout();
        Body.Controls.Remove(rows);
        rows.Dispose();
        rows = Dpi.Fit(Body, Build());
        Body.Controls.Add(rows);
        rows.BringToFront();
        Body.ResumeLayout();
    }
}

/// <summary>ملف الزبون: الأرقام، طلب جديد له، دفعة، تذكير، وكل طلباته</summary>
public class CustomerDialog : DialogShell
{
    public CustomerDialog(string key) : base("الزبون", 1080, 720, "user")
    {
        var c = Calc.GetCustomers().FirstOrDefault(x => x.Key == key);
        if (c == null) { Load += (s, e) => Close(); return; }
        Text = c.Name + (c.Phone != "" ? "   —   " + c.Phone : "");
        var ledger = new Ledger { Dock = DockStyle.Top, Height = 100, MinCell = 150 };
        ledger.Set(new[]
        {
            new Ledger.Cell("عدد الطلبات", c.Orders.Count.ToString()),
            new Ledger.Cell("مجموع التعامل", Txt.Money(c.Spent)),
            new Ledger.Cell("الربح منه", Txt.Money(c.Profit), null, null, c.Profit >= 0 ? 1 : -1),
            new Ledger.Cell("دين عليه", Txt.Money(c.Debt), "أجهزة مُسلّمة", null, c.Debt > 0 ? -1 : 0),
            new Ledger.Cell("متوقع عند التسليم", Txt.Money(c.Expected), "أجهزة في الورشة"),
            new Ledger.Cell("رصيده عندنا", Txt.Money(Credits.Balance(key)), "يُستعمل في طلبه القادم", Pal.Good),
        });
        var grid = new OrdersGrid();
        grid.Fill(c.Orders.OrderByDescending(o => o.CreatedAt, StringComparer.Ordinal));
        grid.OpenOrder += Acts.View;
        Body.Controls.Add(grid);
        Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Surface });
        Body.Controls.Add(ledger);
        AddButton("طلب جديد له", DialogResult.None, BtnKind.Primary, "plus").Click += (s, e) => { Close(); Ui2.Later(() => Acts.New(new Order { CustomerName = c.Name, Phone = c.Phone })); };
        if (c.Debt > 0 || c.Expected > 0) AddButton("تسجيل دفعة", DialogResult.None, BtnKind.Success, "wallet").Click += (s, e) => QuickPayDialog.ForCustomer(key);
        if (c.Debt > 0) AddButton("تذكير بالدين", DialogResult.None, BtnKind.Secondary, "message-circle").Click += (s, e) => Acts.DebtReminder(c);
        AddButton("ملف الزبون", DialogResult.None, BtnKind.Secondary, "printer").Click += (s, e) => Printer.CustomerFile(key);
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        void Changed()
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                var c2 = Calc.GetCustomers().FirstOrDefault(x => x.Key == key);
                if (c2 == null) return;
                grid.Fill(c2.Orders.OrderByDescending(o => o.CreatedAt, StringComparer.Ordinal));
                ledger.Set(new[]
                {
                    new Ledger.Cell("عدد الطلبات", c2.Orders.Count.ToString()),
                    new Ledger.Cell("مجموع التعامل", Txt.Money(c2.Spent)),
                    new Ledger.Cell("الربح منه", Txt.Money(c2.Profit), null, null, c2.Profit >= 0 ? 1 : -1),
                    new Ledger.Cell("دين عليه", Txt.Money(c2.Debt), "أجهزة مُسلّمة", null, c2.Debt > 0 ? -1 : 0),
                    new Ledger.Cell("متوقع عند التسليم", Txt.Money(c2.Expected), "أجهزة في الورشة"),
                    new Ledger.Cell("رصيده عندنا", Txt.Money(Credits.Balance(key)), "يُستعمل في طلبه القادم", Pal.Good),
                });
            });
        }
        Store.Changed += Changed;
        FormClosed += (s, e) => Store.Changed -= Changed;
    }

    public static void Open(string key)
    {
        using var d = new CustomerDialog(key);
        d.ShowModal();
    }
}
