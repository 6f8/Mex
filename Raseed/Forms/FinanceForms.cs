using System.Data;

namespace Raseed;

/// <summary>السندات المالية: قبض، صرف، مصروف، راتب، تحويل بين الصناديق، صيرفة (تبديل عملة)</summary>
public class VoucherForm : BaseForm
{
    readonly ComboBox cbKind = Ui.Combo(130), cbBox = Ui.Combo(200), cbBox2 = Ui.Combo(200), cbParty = Ui.Combo(240),
                      cbEmp = Ui.Combo(200), cbCC = Ui.Combo(160);
    readonly NumericUpDown nAmt = Ui.Num(160, 2), nAmt2 = Ui.Num(160, 2);
    readonly DateTimePicker dt = new() { Width = 170, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" };
    readonly TextBox txtNote = new() { Width = 330 };
    readonly Label lblInfo = new() { AutoSize = true, ForeColor = Theme.BrandDark, Font = Theme.FS(10), Margin = new Padding(10, 36, 10, 0) };
    readonly DataGridView grid = Ui.NewGrid();
    readonly Control pBox2, pParty, pEmp, pAmt2, pCC;

    public VoucherForm()
    {
        cbKind.Items.AddRange(new object[] { "قبض", "صرف", "مصروف", "راتب", "تحويل", "صيرفة" });
        Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        Ui.FillCombo(cbBox2, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        Ui.FillCombo(cbParty, "SELECT id,name,phone FROM parties ORDER BY name", true);
        Ui.MakeSearchable(cbParty);
        Ui.FillCombo(cbEmp, "SELECT id,name,salary FROM employees WHERE active=1 ORDER BY name", true);
        Ui.FillCombo(cbCC, "SELECT id,name FROM cost_centers ORDER BY id", true);
        dt.Value = DateTime.Now;

        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("نوع السند", cbKind));
        bar.Controls.Add(Ui.Labeled("التاريخ", dt));
        bar.Controls.Add(Ui.Labeled("الصندوق / من", cbBox));
        bar.Controls.Add(pBox2 = Ui.Labeled("إلى الصندوق", cbBox2));
        bar.Controls.Add(pParty = Ui.Labeled("الجهة (عميل/مورد)", cbParty));
        bar.Controls.Add(pEmp = Ui.Labeled("الموظف", cbEmp));
        bar.Controls.Add(pCC = Ui.Labeled("مركز الكلفة", cbCC));
        bar.Controls.Add(Ui.Labeled("المبلغ", nAmt));
        bar.Controls.Add(pAmt2 = Ui.Labeled("المبلغ المستلم (بعملة الصندوق الثاني)", nAmt2));
        bar.Controls.Add(Ui.Labeled("البيان", txtNote));
        var bSave = Theme.Btn("حفظ السند", Theme.Success, 130);
        var bDel = Theme.Btn("حذف السند المحدد", Theme.Danger, 160);
        bar.Controls.Add(bSave);
        bar.Controls.Add(bDel);
        var bPrintV = Theme.Btn("طباعة السند", Theme.Purple, 120);
        if (Session.Can("print")) bar.Controls.Add(bPrintV);
        bPrintV.Click += (s, e) => { if (grid.CurrentRow != null) VoucherPrint(Db.L(grid.CurrentRow.Cells["id"].Value)); };
        Ui.GridTools(bar, grid, () => "السندات المالية");
        bar.Controls.Add(lblInfo);

        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(Theme.Title("السندات المالية والصيرفة"));

        cbKind.SelectedIndexChanged += (s, e) => KindChanged();
        cbParty.SelectedIndexChanged += (s, e) =>
        {
            long id = Ui.GetId(cbParty);
            lblInfo.Text = id > 0 ? $"رصيد الجهة: {Ui.M(Ui.PartyBalance(id))}" : "";
        };
        cbEmp.SelectedIndexChanged += (s, e) =>
        {
            var r = Ui.GetRow(cbEmp);
            if (r != null && cbKind.Text == "راتب") { nAmt.Value = (decimal)Db.D(r["salary"]); txtNote.Text = $"راتب شهر {DateTime.Now:yyyy/MM}"; }
        };
        bSave.Click += (s, e) => Save();
        bDel.Click += (s, e) => Delete();

        cbKind.SelectedIndex = 0;
        LoadGrid();
    }

    void KindChanged()
    {
        string k = cbKind.Text;
        pBox2.Visible = k is "تحويل" or "صيرفة";
        pAmt2.Visible = k == "صيرفة";
        pParty.Visible = k is "قبض" or "صرف";
        pEmp.Visible = k == "راتب";
        pCC.Visible = k is "قبض" or "صرف" or "مصروف" or "راتب";
    }

    void LoadGrid()
    {
        grid.DataSource = Db.Query(@"SELECT m.id, m.date AS [التاريخ], m.kind AS [النوع], c.name AS [الصندوق], m.amount AS [المبلغ],
            c.currency AS [العملة], p.name AS [الجهة], e.name AS [الموظف], cc.name AS [مركز الكلفة], m.note AS [البيان],
            m.invoice_id AS [فاتورة] FROM cash_moves m JOIN cashboxes c ON c.id=m.cashbox_id
            LEFT JOIN parties p ON p.id=m.party_id LEFT JOIN employees e ON e.id=m.employee_id
            LEFT JOIN cost_centers cc ON cc.id=m.cost_center_id ORDER BY m.id DESC LIMIT 500");
    }

    void Save()
    {
        if (!Session.Guard("vouchers")) return;
        string k = cbKind.Text, date = dt.Value.ToString(Ui.DtFmt), note = txtNote.Text.Trim();
        long box = Ui.GetId(cbBox), box2 = Ui.GetId(cbBox2), party = Ui.GetId(cbParty), emp = Ui.GetId(cbEmp), cc = Ui.GetId(cbCC);
        double amt = (double)nAmt.Value, amt2 = (double)nAmt2.Value, r1 = Ui.BoxRate(box), r2 = Ui.BoxRate(box2);
        string cur1 = Ui.BoxCurrency(box), cur2 = Ui.BoxCurrency(box2);
        if (amt <= 0 || box == 0) { Ui.Warn("أدخل المبلغ واختر الصندوق."); return; }

        const string ins = @"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,employee_id,cost_center_id,ref,note,user_id)
                             VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10)";
        using (var tx = new Tx())
        {
            string rf = Guid.NewGuid().ToString("N")[..10];
            switch (k)
            {
                case "قبض":
                    tx.Exec(ins, date, k, box, amt, r1, Db.N(party), null, Db.N(cc), null, note, Session.UserId); break;
                case "صرف":
                    tx.Exec(ins, date, k, box, -amt, r1, Db.N(party), null, Db.N(cc), null, note, Session.UserId); break;
                case "مصروف":
                    tx.Exec(ins, date, k, box, -amt, r1, null, null, Db.N(cc), null, note, Session.UserId); break;
                case "راتب":
                    if (!Session.Can("hr")) { Ui.Warn("ليس لديك صلاحية الموارد البشرية."); return; }
                    if (emp == 0) { Ui.Warn("اختر الموظف."); return; }
                    tx.Exec(ins, date, k, box, -amt, r1, null, emp, Db.N(cc), null, note, Session.UserId); break;
                case "تحويل":
                    if (box2 == 0 || box2 == box) { Ui.Warn("اختر صندوقًا مختلفًا للتحويل."); return; }
                    if (cur1 != cur2) { Ui.Warn("عملتا الصندوقين مختلفتان — استخدم سند «صيرفة»."); return; }
                    tx.Exec(ins, date, k, box, -amt, r1, null, null, null, rf, note, Session.UserId);
                    tx.Exec(ins, date, k, box2, amt, r2, null, null, null, rf, note, Session.UserId); break;
                case "صيرفة":
                    if (box2 == 0 || box2 == box || amt2 <= 0) { Ui.Warn("اختر الصندوق الثاني وأدخل المبلغ المستلم."); return; }
                    tx.Exec(ins, date, k, box, -amt, r1, null, null, null, rf, note, Session.UserId);
                    tx.Exec(ins, date, k, box2, amt2, r2, null, null, null, rf, note, Session.UserId); break;
            }
            tx.Commit();
        }

        if (k == "قبض" && party > 0)
        {
            var phone = Db.S(Ui.GetRow(cbParty)?["phone"]);
            if (phone != "" && Ui.Confirm("تم الحفظ. إرسال وصل القبض للعميل عبر واتساب؟"))
                _ = WhatsApp.Send(phone, $"{Settings.Get("shop_name")}\nتم استلام مبلغ {Ui.M(amt)} {Ui.BoxCurrency(box)} بتاريخ {dt.Value:yyyy/MM/dd}.\nرصيدكم المتبقي: {Ui.M(Ui.PartyBalance(party))}\nشكرًا لكم.");
        }
        nAmt.Value = 0; nAmt2.Value = 0; txtNote.Clear();
        LoadGrid();
    }

    public static void VoucherPrint(long id)
    {
        var dt = Db.Query(@"SELECT m.*, c.name AS box, c.currency, p.name AS pname, e.name AS ename, u.full_name AS uname FROM cash_moves m
            JOIN cashboxes c ON c.id=m.cashbox_id LEFT JOIN parties p ON p.id=m.party_id LEFT JOIN employees e ON e.id=m.employee_id
            LEFT JOIN users u ON u.id=m.user_id WHERE m.id=@p0", id);
        if (dt.Rows.Count == 0) return;
        var r = dt.Rows[0];
        double amt = Db.D(r["amount"]);
        var d = PrintDoc.Header((amt >= 0 ? "سند قبض" : "سند صرف") + " — " + Db.S(r["kind"]));
        d.Pair("رقم السند", id.ToString(), "التاريخ", Db.S(r["date"]).Length >= 16 ? Db.S(r["date"])[..16] : Db.S(r["date"]));
        string who = Db.S(r["pname"]) != "" ? Db.S(r["pname"]) : Db.S(r["ename"]);
        if (who != "") d.Pair(amt >= 0 ? "استلمنا من" : "صُرف إلى", who);
        d.Text($"المبلغ: {Ui.M(Math.Abs(amt))} {Db.S(r["currency"])}", 14, true, StringAlignment.Center);
        d.Pair("الصندوق", Db.S(r["box"]), "المستخدم", Db.S(r["uname"]));
        if (Db.S(r["note"]) != "") d.Text("البيان: " + Db.S(r["note"]), 10);
        long pid = Db.L(r["party_id"]);
        if (pid > 0) d.Text("الرصيد الحالي للحساب: " + Ui.M(Ui.PartyBalance(pid)), 10, false, StringAlignment.Center);
        d.Space(8);
        d.Pair("المستلم", "..................", "المحاسب", "..................");
        d.Footer();
        d.Print();
    }

    void Delete()
    {
        if (grid.CurrentRow == null || !Session.Guard("delete")) return;
        long id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var r = Db.Query("SELECT invoice_id, installment_id, repair_id, ref, amount, rate FROM cash_moves WHERE id=@p0", id).Rows[0];
        string rf = Db.S(r["ref"]);
        if (Db.L(r["invoice_id"]) > 0) { Ui.Warn("هذا المبلغ مرتبط بفاتورة — عدّل الفاتورة أو احذفها من سجل الفواتير."); return; }
        if (Db.L(r["repair_id"]) > 0) { Ui.Warn("هذا المبلغ مرتبط بوصل صيانة ولا يُحذف من هنا."); return; }
        if (rf.StartsWith("ADV:")) { Ui.Warn("هذه سلفة موظف — احذفها من الموارد البشرية ← السلف."); return; }
        long inst = Db.L(r["installment_id"]);
        if (!Ui.Confirm(inst > 0 ? "إلغاء تسديد القسط المحدد وإرجاعه غير مسدد؟" : "حذف السند المحدد؟")) return;
        using (var tx = new Tx())
        {
            if (inst > 0)
                tx.Exec("UPDATE installments SET paid=MAX(0,paid-@p0), paid_date=NULL, notified=NULL WHERE id=@p1", Db.D(r["amount"]) * Db.D(r["rate"]), inst);
            if (rf != "" && !rf.StartsWith("SAL:")) tx.Exec("DELETE FROM cash_moves WHERE ref=@p0", rf);
            else tx.Exec("DELETE FROM cash_moves WHERE id=@p0", id);
            tx.Commit();
        }
        Db.Audit("حذف سند", $"رقم {id}");
        LoadGrid();
    }
}

/// <summary>الأقساط: متابعة، تسديد، تذكير واتساب</summary>
public class InstallmentsForm : BaseForm
{
    readonly ComboBox cbFilter = Ui.Combo(170), cbBox = Ui.Combo(200);
    readonly DataGridView grid = Ui.NewGrid();

    public InstallmentsForm()
    {
        cbFilter.Items.AddRange(new object[] { "غير المسددة", "المستحقة قريبًا", "المتأخرة", "الكل" });
        Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");

        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("عرض", cbFilter));
        bar.Controls.Add(Ui.Labeled("صندوق التسديد", cbBox));
        var bPay = Theme.Btn("تسديد القسط", Theme.Success, 130);
        var bWa = Theme.Btn("تذكير واتساب", Theme.Accent, 140);
        var bAll = Theme.Btn("تذكير جميع المستحقين", Theme.Purple, 190);
        bar.Controls.AddRange(new Control[] { bPay, bWa, bAll });
        Ui.GridTools(bar, grid, () => "الأقساط — " + cbFilter.Text);

        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(Theme.Title("الأقساط"));

        cbFilter.SelectedIndexChanged += (s, e) => LoadGrid();
        bPay.Click += (s, e) => Pay();
        bWa.Click += (s, e) => Remind();
        bAll.Click += async (s, e) =>
        {
            if (!WhatsApp.IsCloud && !Ui.Confirm("وضع الإرسال التلقائي غير مفعّل — سيُفتح واتساب لكل عميل على حدة. متابعة؟")) return;
            int n = await WhatsApp.SendInstallmentReminders(false);
            Ui.Info($"تمت معالجة {n} تذكير.");
            LoadGrid();
        };
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || !grid.Columns.Contains("الحالة")) return;
            var st = Convert.ToString(grid.Rows[e.RowIndex].Cells["الحالة"].Value);
            if (st == "متأخر") e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
            else if (st == "مسدد") e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
        };
        cbFilter.SelectedIndex = 0;
    }

    void LoadGrid()
    {
        string where = cbFilter.SelectedIndex switch
        {
            0 => "WHERE t.amount-t.paid>0.001",
            1 => "WHERE t.amount-t.paid>0.001 AND t.due_date<=date('now','localtime','+'||@p0||' day')",
            2 => "WHERE t.amount-t.paid>0.001 AND t.due_date<date('now','localtime')",
            _ => ""
        };
        grid.DataSource = Db.Query($@"SELECT t.id, p.name AS [العميل], p.phone AS [الهاتف], t.invoice_id AS [الفاتورة], t.seq AS [القسط],
            t.due_date AS [الاستحقاق], t.amount AS [المبلغ], t.paid AS [المسدد], t.amount-t.paid AS [المتبقي],
            CASE WHEN t.amount-t.paid<=0.001 THEN 'مسدد' WHEN t.due_date<date('now','localtime') THEN 'متأخر' ELSE 'قائم' END AS [الحالة],
            t.notified AS [آخر تذكير] FROM installments t JOIN parties p ON p.id=t.party_id {where} ORDER BY t.due_date",
            Settings.Int("reminder_days", 3));
    }

    DataGridViewRow Row => grid.CurrentRow;

    void Pay()
    {
        if (Row == null || !Session.Guard("installments")) return;
        long id = Db.L(Row.Cells["id"].Value), box = Ui.GetId(cbBox);
        double rem = Db.D(Row.Cells["المتبقي"].Value);
        if (rem <= 0) { Ui.Info("القسط مسدد بالكامل."); return; }
        if (!Ui.AskNumber("تسديد قسط", "المبلغ المستلم", rem, out var amt) || amt <= 0) return;
        if (amt > rem + 0.001) { Ui.Warn("المبلغ أكبر من المتبقي على القسط."); return; }
        if (box == 0) { Ui.Warn("اختر صندوق التسديد."); return; }
        long party = Db.L(Db.Scalar("SELECT party_id FROM installments WHERE id=@p0", id));
        double rate = Ui.BoxRate(box);
        using (var tx = new Tx())
        {
            tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,installment_id,note,user_id)
                      VALUES(@p0,'تسديد قسط',@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                Ui.Now, box, amt / rate, rate, party, id, $"تسديد القسط رقم {Row.Cells["القسط"].Value}", Session.UserId);
            tx.Exec("UPDATE installments SET paid=paid+@p0, paid_date=@p1 WHERE id=@p2", amt, Ui.Today, id);
            tx.Commit();
        }
        LoadGrid();
    }

    void Remind()
    {
        if (Row == null) return;
        string phone = Convert.ToString(Row.Cells["الهاتف"].Value);
        if (string.IsNullOrWhiteSpace(phone)) { Ui.Warn("لا يوجد رقم هاتف للعميل."); return; }
        var text = WhatsApp.InstallmentText(Convert.ToString(Row.Cells["العميل"].Value), Db.L(Row.Cells["القسط"].Value),
            Db.D(Row.Cells["المتبقي"].Value), Convert.ToString(Row.Cells["الاستحقاق"].Value));
        long id = Db.L(Row.Cells["id"].Value);
        _ = WhatsApp.Send(phone, text).ContinueWith(t =>
        {
            if (t.Result) Db.Exec("UPDATE installments SET notified=@p0 WHERE id=@p1", Ui.Today, id);
        });
    }
}

/// <summary>المخازن: الأرصدة حسب الوجبة وتاريخ الصلاحية، المناقلة، الإتلاف</summary>
public class StockForm : BaseForm
{
    readonly ComboBox cbWh = Ui.Combo(180), cbView = Ui.Combo(200);
    readonly TextBox search = new() { Width = 240, PlaceholderText = "بحث باسم المادة..." };
    readonly DataGridView grid = Ui.NewGrid();
    readonly MainForm main;
    readonly int expDays = Settings.Int("expiry_days", 30);

    public StockForm(MainForm owner)
    {
        main = owner;
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id", true, "— كل المخازن —");
        cbView.Items.AddRange(new object[] { "الأرصدة حسب الوجبة", "قاربت على الانتهاء / منتهية", "تحت حد الطلب", "إجمالي المواد" });

        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("المخزن", cbWh));
        bar.Controls.Add(Ui.Labeled("العرض", cbView));
        bar.Controls.Add(Ui.Labeled("بحث", search));
        var bMove = Theme.Btn("مناقلة بين المخازن", Theme.Accent, 170);
        var bDamage = Theme.Btn("إتلاف مواد", Theme.Danger, 120);
        bar.Controls.Add(bMove);
        bar.Controls.Add(bDamage);
        Ui.GridTools(bar, grid, () => "المخزون — " + cbView.Text);

        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(Theme.Title("المخازن وتواريخ الصلاحية"));

        cbWh.SelectedIndexChanged += (s, e) => LoadGrid();
        cbView.SelectedIndexChanged += (s, e) => LoadGrid();
        search.TextChanged += (s, e) => LoadGrid();
        bMove.Click += (s, e) => Transfer();
        bDamage.Click += (s, e) => { if (Session.Guard("damage")) main.Open("إتلاف مواد", new InvoiceForm("Damage")); };
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || !grid.Columns.Contains("أيام متبقية")) return;
            var v = grid.Rows[e.RowIndex].Cells["أيام متبقية"].Value;
            if (v == null || v is DBNull) return;
            long d = Db.L(v);
            if (d < 0) e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
            else if (d <= expDays) e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
        };
        cbView.SelectedIndex = 0;
    }

    void LoadGrid()
    {
        long wh = Ui.GetId(cbWh);
        string like = "%" + search.Text.Trim() + "%";
        int days = Settings.Int("expiry_days", 30);
        switch (cbView.SelectedIndex)
        {
            case 0:
            case 1:
                grid.DataSource = Db.Query($@"SELECT b.id, i.name AS [المادة], w.name AS [المخزن], b.expiry AS [تاريخ الصلاحية],
                    CAST(julianday(b.expiry)-julianday(date('now','localtime')) AS INTEGER) AS [أيام متبقية],
                    b.qty AS [الكمية], i.unit AS [الوحدة], b.cost AS [الكلفة], b.qty*b.cost AS [القيمة], i.medical_info AS [البيانات الطبية]
                    FROM batches b JOIN items i ON i.id=b.item_id JOIN warehouses w ON w.id=b.warehouse_id
                    WHERE b.qty>0 AND (@p0=0 OR b.warehouse_id=@p0) AND i.name LIKE @p1
                    {(cbView.SelectedIndex == 1 ? "AND b.expiry IS NOT NULL AND b.expiry<>'' AND b.expiry<=date('now','localtime','+'||@p2||' day')" : "")}
                    ORDER BY (b.expiry IS NULL OR b.expiry=''), b.expiry", wh, like, days);
                break;
            case 2:
                grid.DataSource = Db.Query(@"SELECT i.id, i.name AS [المادة], i.min_qty AS [حد الطلب],
                    IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id AND (@p0=0 OR b.warehouse_id=@p0)),0) AS [الرصيد]
                    FROM items i WHERE i.min_qty>0 AND i.name LIKE @p1
                    AND IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id AND (@p0=0 OR b.warehouse_id=@p0)),0)<=i.min_qty", wh, like);
                break;
            default:
                grid.DataSource = Db.Query(@"SELECT i.id, i.name AS [المادة], i.unit AS [الوحدة], SUM(b.qty) AS [الرصيد],
                    SUM(b.qty*b.cost) AS [قيمة الكلفة], SUM(b.qty)*i.price_retail AS [قيمة البيع مفرد]
                    FROM items i JOIN batches b ON b.item_id=i.id WHERE b.qty>0 AND (@p0=0 OR b.warehouse_id=@p0) AND i.name LIKE @p1
                    GROUP BY i.id ORDER BY i.name", wh, like);
                break;
        }
    }

    void Transfer()
    {
        if (!Session.Guard("stock")) return;
        if (cbView.SelectedIndex > 1 || grid.CurrentRow == null) { Ui.Warn("اختر وجبة من عرض «الأرصدة حسب الوجبة»."); return; }
        long bid = Db.L(grid.CurrentRow.Cells["id"].Value);
        var b = Db.Query("SELECT * FROM batches WHERE id=@p0", bid).Rows[0];
        double have = Db.D(b["qty"]);
        var target = Ui.Pick("مناقلة", "المخزن المستلم", "SELECT id,name FROM warehouses WHERE id<>@p0", Db.L(b["warehouse_id"]));
        if (target == null) return;
        if (!Ui.AskNumber("مناقلة", $"الكمية المنقولة (المتوفر {Ui.M(have)})", have, out var q) || q <= 0) return;
        if (q > have + 1e-9) { Ui.Warn("الكمية أكبر من المتوفر."); return; }
        using (var tx = new Tx())
        {
            tx.Exec("UPDATE batches SET qty=qty-@p0 WHERE id=@p1", q, bid);
            tx.Exec("INSERT INTO batches(item_id,warehouse_id,expiry,qty,cost,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",
                b["item_id"], target.Id, b["expiry"], q, b["cost"], Ui.Now);
            tx.Commit();
        }
        LoadGrid();
    }
}
