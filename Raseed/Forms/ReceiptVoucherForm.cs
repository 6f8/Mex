using System.Data;

namespace Raseed;

/// <summary>
/// سند قبض / سند دفع بالترتيب المألوف: رقم وتاريخ السند، الحساب والصندوق، المبلغ والخصم بالدينار والدولار مع المبلغ كتابةً،
/// ولوحة الرصيد السابق والحالي. السند يُحفظ برقم واحد ويُحذف كاملًا، ويُطبع مع نسخة للعميل.
/// </summary>
public class ReceiptVoucherForm : BaseForm
{
    readonly string kind;   // «قبض» أو «صرف»
    readonly TextBox tNo = new() { Width = 170, TextAlign = HorizontalAlignment.Center },
                     tWordsIqd = new() { Width = 360, ReadOnly = true }, tWordsUsd = new() { Width = 360, ReadOnly = true },
                     tNote = new() { Width = 900, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly DateTimePicker dDate = new() { Width = 170, Format = DateTimePickerFormat.Short };
    readonly ComboBox cbParty = Ui.Combo(360), cbBox = Ui.Combo(360), cbBoxUsd = Ui.Combo(360);
    readonly NumericUpDown nIqd = Ui.Num(360, 2), nDiscIqd = Ui.Num(360, 2), nUsd = Ui.Num(360, 2), nDiscUsd = Ui.Num(360, 2);
    readonly Toggle tgPrint = new() { Text = "طباعة", Width = 110, Height = 34 }, tgCopy = new() { Text = "نسخة العميل", Width = 150, Height = 34 };
    readonly BalancePanel balance;
    readonly Label lblLastPay = new(), lblLastMove = new();
    readonly ModernButton bDel;
    long id, loadedParty;
    double oldEffect;   // أثر السند المفتوح للتعديل على رصيد حسابه
    long balParty = -1; double balValue;   // رصيد الحساب المختار (يُقرأ مرة لكل حساب بدل كل ضغطة مفتاح في المبلغ)
    bool noteTouched, loading;

    bool Receipt => kind == "قبض";
    string Title => Receipt ? "سند قبض" : "سند دفع";
    Color Tone => Receipt ? ColorTranslator.FromHtml("#3FA67E") : ColorTranslator.FromHtml("#F0A33A");

    public ReceiptVoucherForm(string voucherKind, long voucherId = 0)
    {
        kind = voucherKind;
        Text = Title;
        KeyPreview = true;
        AutoScroll = true;
        balance = new BalancePanel { Tone = Tone, Size = new Size(470, 226), Margin = new Padding(4, 0, 4, 10) };
        foreach (var n in new[] { nIqd, nDiscIqd, nUsd, nDiscUsd }) n.Minimum = 0;
        nDiscIqd.Enabled = nDiscUsd.Enabled = Session.Can("discount");

        Ui.FillCombo(cbParty, "SELECT id, name, kind FROM parties ORDER BY name");
        Ui.MakeSearchable(cbParty);
        Ui.FillCombo(cbBox, "SELECT id, name FROM cashboxes WHERE currency='IQD' ORDER BY id");
        Ui.FillCombo(cbBoxUsd, "SELECT id, name FROM cashboxes WHERE currency='USD' ORDER BY id", true, "— لا يوجد —");
        if (cbBoxUsd.Items.Count > 1) cbBoxUsd.SelectedIndex = 1;

        // ---------- العنوان بخط ملون ----------
        var head = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Surface };
        head.Paint += (s, e) =>
        {
            var g = e.Graphics;
            TextRenderer.DrawText(g, Title, Theme.FS(22), new Rectangle(Dpi.S(24), Dpi.S(8), head.Width - Dpi.S(48), Dpi.S(50)), Theme.BrandDark, Gfx.RtlStart);
            using var pen = new Pen(Tone, Dpi.S(4f));
            g.DrawLine(pen, Dpi.S(24), Dpi.S(66), head.Width - Dpi.S(24), Dpi.S(66));
        };

        // سطر متجاوب: العنوان ثم الحقل الذي يتمدد مع العرض، وينزل العنوان فوقه في الشاشات الضيقة
        Control Row(string caption, Control c, int capW = 140)
        {
            var label = new Label { Text = caption, AutoSize = false, Width = capW, Height = 40, Font = Theme.FS(11), ForeColor = Theme.BrandDark, TextAlign = ContentAlignment.MiddleLeft };
            var f = Ui.Wrap(c); f.Margin = new Padding(0);
            return new FormRow(label, new[] { f }) { Margin = new Padding(0, 4, 0, 4) };
        }
        FormStack Col() => new() { Padding = new Padding(0) };

        // ---------- عمود الدينار (يمين) وعمود الدولار ولوحة الرصيد (يسار)؛ ينزل الثاني تحت الأول في الشاشات الضيقة ----------
        var right = Col();
        right.Controls.Add(Row("رقم السند", tNo));
        right.Controls.Add(Row("تاريخ السند", dDate));
        right.Controls.Add(Row("اسم الحساب", cbParty));
        right.Controls.Add(Row("الصندوق", cbBox));
        right.Controls.Add(Row("المبلغ دينار", nIqd));
        right.Controls.Add(Row("الخصم دينار", nDiscIqd));
        right.Controls.Add(Row("المبلغ كتابة", tWordsIqd));

        var left = Col();
        left.Controls.Add(balance);
        left.Controls.Add(Row("صندوق الدولار", cbBoxUsd, 130));
        left.Controls.Add(Row("المبلغ دولار", nUsd, 130));
        left.Controls.Add(Row("الخصم دولار", nDiscUsd, 130));
        left.Controls.Add(Row("المبلغ كتابة", tWordsUsd, 130));

        var cols = new FormColumns { MinColumn = 420, Gap = 40, Margin = new Padding(0, 8, 0, 0) };
        cols.Controls.Add(right);
        cols.Controls.Add(left);

        var notesRow = Row("الملاحظات", tNote);
        notesRow.Margin = new Padding(0, 10, 0, 0);

        // ---------- آخر تسديد وآخر حركة، وكشف الحساب وتقرير السندات ----------
        foreach (var l in new[] { lblLastPay, lblLastMove })
        {
            l.AutoSize = false; l.Width = 320; l.Height = 34; l.Font = Theme.FS(10); l.ForeColor = Theme.BrandDark; l.TextAlign = ContentAlignment.MiddleLeft;
        }
        var bStatement = new ModernButton { Text = "كشف الحساب", IconName = "scroll-text", Height = 42 }; bStatement.FitWidth(150);
        var bReport = new ModernButton { Text = "تقرير السندات", IconName = "list", Height = 42 }; bReport.FitWidth(150);
        // سطر للتواريخ وسطر للزرين: لا يلتف زر واحد وحده إلى سطر جديد ويتداخل مع أزرار الحفظ
        var info = new FormRow(null, new Control[] { lblLastPay, lblLastMove }) { Margin = new Padding(144, 10, 0, 0) };
        var tools = new FormRow(null, new Control[] { bStatement, bReport }) { Margin = new Padding(144, 6, 0, 0), Gap = 10 };

        // ---------- الأزرار ----------
        var bNew = new ModernButton { Text = "جديد", IconName = "plus", Height = 48, Width = 160 };
        var bSave = new ModernButton { Text = "حفظ", IconName = "save", Height = 48, Width = 160 };
        bDel = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 48, Width = 160, Margin = new Padding(0, 0, 20, 0) };
        tgPrint.Visible = tgCopy.Visible = Session.Can("print");
        var actions = new FormRow(null, new Control[] { bNew, bSave, bDel, tgPrint, tgCopy }) { Margin = new Padding(144, 16, 0, 10), Gap = 10 };

        var body = new FormStack { Dock = DockStyle.Top, MaxContent = 1500, Padding = new Padding(24, 6, 24, 10) };
        body.Controls.Add(cols);
        body.Controls.Add(notesRow);
        body.Controls.Add(info);
        body.Controls.Add(tools);
        body.Controls.Add(actions);

        var card = new CardPanel { Dock = DockStyle.Top, Padding = new Padding(8) };
        card.Controls.Add(body);
        card.Controls.Add(head);
        // ارتفاع البطاقة = العنوان + المحتوى + الهوامش وظل البطاقة. يُعاد حسابه عند تغيّر أي منها
        // (كان يُحسب مرة عند تغيّر المحتوى فقط، فبعد تكبير الشاشة 125%/150% تُقص أزرار الحفظ في الأسفل)
        void FitCard()
        {
            int h = body.Height + head.Height + card.Padding.Vertical + Dpi.S(6);
            if (card.Height != h) card.Height = h;
        }
        body.SizeChanged += (s, e) => FitCard();
        head.SizeChanged += (s, e) => FitCard();
        card.PaddingChanged += (s, e) => FitCard();
        card.Layout += (s, e) => FitCard();
        Controls.Add(card);

        // ---------- الأحداث ----------
        foreach (var n in new[] { nIqd, nDiscIqd, nUsd, nDiscUsd }) n.ValueChanged += (s, e) => Recalc();
        cbParty.SelectedIndexChanged += (s, e) => { if (!loading) { Recalc(); ShowPartyInfo(); } };
        tNote.TextChanged += (s, e) => { if (!loading) noteTouched = true; };
        tNo.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (long.TryParse(tNo.Text.Trim(), out var n) && n > 0) LoadVoucher(n);
        };
        bNew.Click += (s, e) => New();
        bSave.Click += (s, e) => Save();
        bDel.Click += (s, e) => Delete();
        bStatement.Click += (s, e) =>
        {
            long p = Ui.GetId(cbParty);
            if (p == 0 || !Session.Guard("reports")) return;
            MainForm.Instance?.Open($"كشف حساب — {cbParty.Text}", new ReportsForm("كشف حساب", partyId: p));
        };
        bReport.Click += (s, e) =>
        {
            if (!Session.Guard("reports")) return;
            MainForm.Instance?.Open(Receipt ? "تسديد المبيعات" : "تسديد المشتريات",
                new ListReportForm(Receipt ? ListReportForm.Kind.SalesPayments : ListReportForm.Kind.PurchasePayments, Ui.GetId(cbParty)));
        };
        KeyDown += (s, e) => { if (e.KeyCode == Keys.F10) { Save(); e.Handled = true; } };

        if (voucherId > 0) LoadVoucher(voucherId); else New();
        Shown += (s, e) => cbParty.Focus();
    }

    // الرجوع إلى التبويب: الرصيد قد تغيّر من شاشة أخرى (فاتورة أو سند)
    public override void OnPageActivated() { balParty = -1; Recalc(); }

    Vouchers.Data Current() => new(kind, dDate.Value, Ui.GetId(cbParty), Ui.GetId(cbBox), Ui.GetId(cbBoxUsd),
        (double)nIqd.Value, (double)nUsd.Value, (double)nDiscIqd.Value, (double)nDiscUsd.Value, tNote.Text.Trim());

    void Recalc()
    {
        tWordsIqd.Text = Tafqeet.Money((double)nIqd.Value);
        tWordsUsd.Text = Tafqeet.Money((double)nUsd.Value, usd: true);
        long p = Ui.GetId(cbParty);
        double rate = Ui.Rate("USD");
        if (p != balParty) { balValue = p > 0 ? Ui.PartyBalance(p) : 0; balParty = p; }
        double prev = p > 0 ? balValue - (p == loadedParty ? oldEffect : 0) : 0;
        double now = prev + Vouchers.Effect(Current(), rate);
        balance.Set(prev, now, rate);
        if (!noteTouched && !loading && p > 0)
        {
            loading = true;
            var parts = new List<string>();
            if (nIqd.Value > 0) parts.Add($"{Ui.M((double)nIqd.Value)} دينار");
            if (nUsd.Value > 0) parts.Add($"{Ui.M((double)nUsd.Value)} دولار");
            tNote.Text = $"{Title} {(Receipt ? "من" : "إلى")} حساب {cbParty.Text} بقيمة {string.Join(" و", parts)}";
            loading = false;
        }
    }

    void ShowPartyInfo()
    {
        long p = Ui.GetId(cbParty);
        if (p == 0) { lblLastPay.Text = lblLastMove.Text = ""; return; }
        var lastPay = Db.S(Db.Scalar($"SELECT MAX(date) FROM cash_moves WHERE party_id=@p0 AND amount {(Receipt ? ">" : "<")} 0", p));
        var last = Db.Query(@"SELECT d, t FROM (
                SELECT date AS d, " + string.Format(Ui.TypeCaseSql, "type") + @" AS t FROM invoices WHERE party_id=@p0 AND type<>'Quote'
                UNION ALL SELECT date, kind FROM cash_moves WHERE party_id=@p0) ORDER BY d DESC LIMIT 1", p);
        lblLastPay.Text = "تأريخ آخر تسديد:  " + (lastPay == "" ? "—" : Ui.Cut(lastPay, 10));
        lblLastMove.Text = last.Rows.Count == 0 ? "تأريخ آخر حركة:  —" : $"تأريخ آخر حركة:  {Ui.Cut(Db.S(last.Rows[0]["d"]), 10)}   ({Db.S(last.Rows[0]["t"])})";
    }

    void New()
    {
        loading = true;
        id = 0; oldEffect = 0; loadedParty = 0; noteTouched = false; balParty = -1;
        tNo.Text = Vouchers.NextId().ToString();
        dDate.Value = DateTime.Today;
        foreach (var n in new[] { nIqd, nDiscIqd, nUsd, nDiscUsd }) n.Value = 0;
        tNote.Clear();
        if (cbBox.Items.Count > 0 && cbBox.SelectedIndex < 0) cbBox.SelectedIndex = 0;
        bDel.Enabled = false;
        loading = false;
        Recalc();
        ShowPartyInfo();
        cbParty.Focus();
    }

    public void LoadVoucher(long vid)
    {
        var dt = Db.Query("SELECT * FROM vouchers WHERE id=@p0 AND kind=@p1", vid, kind);
        if (dt.Rows.Count == 0) { Ui.Warn($"لا يوجد {Title} برقم {vid}."); tNo.Text = id > 0 ? id.ToString() : Vouchers.NextId().ToString(); return; }
        var r = dt.Rows[0];
        loading = true;
        id = vid;
        tNo.Text = vid.ToString();
        if (DateTime.TryParse(Db.S(r["date"]), out var d)) dDate.Value = d;
        Ui.SelectId(cbParty, Db.L(r["party_id"]));
        if (Db.L(r["box_id"]) > 0) Ui.SelectId(cbBox, Db.L(r["box_id"]));
        Ui.SelectId(cbBoxUsd, Db.L(r["box_usd_id"]));
        Ui.SetNum(nIqd, Db.D(r["iqd"])); Ui.SetNum(nDiscIqd, Db.D(r["disc_iqd"]));
        Ui.SetNum(nUsd, Db.D(r["usd"])); Ui.SetNum(nDiscUsd, Db.D(r["disc_usd"]));
        tNote.Text = Db.S(r["note"]);
        noteTouched = true;
        // أثر السند نفسه (بسعر صرفه المحفوظ) يُطرح لعرض الرصيد قبله
        oldEffect = Vouchers.Effect(Current(), Db.D(r["rate"]));
        loadedParty = Db.L(r["party_id"]);
        balParty = -1;
        bDel.Enabled = true;
        loading = false;
        Recalc();
        ShowPartyInfo();
    }

    void Save()
    {
        if (!Session.Guard("vouchers")) return;
        var d = Current();
        if (d.Party == 0) { Ui.Warn("اختر اسم الحساب."); cbParty.Focus(); return; }
        if (d.Iqd + d.Usd + d.DiscIqd + d.DiscUsd <= 0) { Ui.Warn("أدخل المبلغ بالدينار أو الدولار."); nIqd.Focus(); return; }
        if (d.Iqd > 0 && d.Box == 0) { Ui.Warn("اختر صندوق الدينار."); return; }
        if (d.Usd > 0 && d.BoxUsd == 0) { Ui.Warn("اختر صندوق الدولار (عرّف صندوقًا أو خزينة بعملة الدولار من الحسابات)."); return; }
        bool editing = id > 0;
        id = Vouchers.Save(id, d);
        balParty = -1;
        if (editing) Db.Audit("تعديل سند", $"{Title} رقم {id}");
        if (tgPrint.Checked) Print(id, tgCopy.Checked);
        Toast.Show($"تم حفظ {Title} رقم {id} — الرصيد الآن {Ui.M(Ui.PartyBalance(d.Party))}");
        if (Receipt && Db.S(Ui.GetRow(cbParty)?["kind"]) != "مورد")
        {
            var phone = Db.S(Db.Scalar("SELECT phone FROM parties WHERE id=@p0", d.Party));
            if (phone != "" && Ui.Confirm("إرسال وصل القبض للزبون عبر واتساب؟"))
                _ = WhatsApp.Send(phone, $"{Settings.Get("shop_name")}\nتم استلام {tNote.Text}\nبتاريخ {d.Date:yyyy/MM/dd} — سند رقم {id}\nرصيدكم المتبقي: {Ui.M(Ui.PartyBalance(d.Party))}\nشكرًا لكم.");
        }
        New();
    }

    void Delete()
    {
        if (id == 0 || !Session.Guard("vouchers") || !Session.Guard("delete")) return;
        if (!Ui.Confirm($"حذف {Title} رقم {id}؟")) return;
        Vouchers.Delete(id);
        balParty = -1;
        Toast.Show($"تم حذف {Title} رقم {id}");
        New();
    }

    /// <summary>طباعة السند (ومعه نسخة العميل عند الطلب)</summary>
    public static void Print(long vid, bool customerCopy)
    {
        var dt = Db.Query(@"SELECT v.*, p.name AS pname, b.name AS box, u.name AS ubox, us.full_name AS uname FROM vouchers v
            JOIN parties p ON p.id=v.party_id LEFT JOIN cashboxes b ON b.id=v.box_id LEFT JOIN cashboxes u ON u.id=v.box_usd_id
            LEFT JOIN users us ON us.id=v.user_id WHERE v.id=@p0", vid);
        if (dt.Rows.Count == 0) return;
        var r = dt.Rows[0];
        bool receipt = Db.S(r["kind"]) == "قبض";
        foreach (var copy in customerCopy ? new[] { "", "نسخة العميل" } : new[] { "" })
        {
            var d = PrintDoc.Header((receipt ? "سند قبض" : "سند دفع") + (copy != "" ? " — " + copy : ""));
            d.Pair("رقم السند", vid.ToString(), "التاريخ", Ui.Cut(Db.S(r["date"]), 10));
            d.Pair(receipt ? "استلمنا من" : "دفعنا إلى", Db.S(r["pname"]));
            double iqd = Db.D(r["iqd"]), usd = Db.D(r["usd"]), di = Db.D(r["disc_iqd"]), du = Db.D(r["disc_usd"]);
            if (iqd > 0) { d.Pair("المبلغ", Ui.M(iqd) + " دينار", "الصندوق", Db.S(r["box"])); d.Text(Tafqeet.Money(iqd), 10, true); }
            if (usd > 0) { d.Pair("المبلغ", Ui.M(usd) + " دولار", "الصندوق", Db.S(r["ubox"])); d.Text(Tafqeet.Money(usd, true), 10, true); }
            if (di > 0 || du > 0) d.Pair(receipt ? "الخصم الممنوح" : "الخصم المكتسب", string.Join(" + ", new[] { di > 0 ? Ui.M(di) + " دينار" : "", du > 0 ? Ui.M(du) + " دولار" : "" }.Where(x => x != "")));
            if (Db.S(r["note"]) != "") d.Text("البيان: " + Db.S(r["note"]), 10);
            d.Text("الرصيد الحالي للحساب: " + Ui.M(Ui.PartyBalance(Db.L(r["party_id"]))) + " دينار", 10, false, StringAlignment.Center);
            d.Space(10);
            d.Pair("المستلم", "..................", "المحاسب", Db.S(r["uname"]) == "" ? ".................." : Db.S(r["uname"]));
            d.Footer();
            d.Print();
        }
    }

    /// <summary>لوحة الرصيد السابق والحالي بالدينار والدولار (مدين/لنا أو دائن/علينا)</summary>
    sealed class BalancePanel : Control
    {
        double prev, now, rate = 1;
        public Color Tone { get; set; }

        public BalancePanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Set(double p, double n, double r) { prev = p; now = n; rate = r <= 0 ? 1 : r; Invalidate(); }

        static string Fmt(double v) => Math.Abs(v) < 0.005 ? "0" : $"{Ui.M(Math.Abs(v))} {(v > 0 ? "مدين/لنا" : "دائن/علينا")}";

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Gfx.OpaqueBack(this));
            Gfx.Hq(g);
            Gfx.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Dpi.S(8f), Tone);
            var rows = new (string K, string V)[]
            {
                ("الرصيد السابق دينار", Fmt(prev)), ("الرصيد الحالي دينار", Fmt(now)),
                ("الرصيد السابق دولار", Fmt(prev / rate)), ("الرصيد الحالي دولار", Fmt(now / rate)),
            };
            int y = Dpi.S(14), kw = Math.Min(Dpi.S(210), Width / 2);
            for (int i = 0; i < rows.Length; i++)
            {
                if (i == 2)
                {
                    using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Dpi.S(2f));
                    g.DrawLine(pen, Dpi.S(22), y + Dpi.S(4), Width - Dpi.S(22), y + Dpi.S(4));
                    y += Dpi.S(16);
                }
                TextRenderer.DrawText(g, rows[i].K, Theme.FS(11.5f), new Rectangle(Width - kw - Dpi.S(20), y, kw, Dpi.S(36)), Color.White, Gfx.RtlStart);
                TextRenderer.DrawText(g, rows[i].V, Theme.FS(10.5f), new Rectangle(Dpi.S(16), y, Width - kw - Dpi.S(40), Dpi.S(36)), Color.White, Gfx.RtlStart);
                y += Dpi.S(40);
            }
            TextRenderer.DrawText(g, "الدولار بالمعادل حسب سعر الصرف", Theme.F(8), new Rectangle(Dpi.S(16), Height - Dpi.S(24), Width - Dpi.S(32), Dpi.S(20)), Color.FromArgb(230, 255, 255, 255), Gfx.RtlStart);
        }
    }
}
