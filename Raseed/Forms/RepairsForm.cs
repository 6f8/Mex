using System.Data;

namespace Raseed;

/// <summary>الصيانة: استلام الأجهزة، متابعة الحالة، قطع الغيار، التسليم والتحصيل، وصل الاستلام، إشعار واتساب</summary>
public class RepairsForm : BaseForm
{
    public static readonly string[] Statuses = { "مستلم", "قيد الفحص", "بانتظار قطعة", "قيد التصليح", "جاهز", "تم التسليم", "لا يصلح", "ملغي" };

    readonly DataGridView grid = Ui.NewGrid(), parts = Ui.NewGrid();
    readonly ComboBox cbFilter = Ui.Combo(150), cbParty = Ui.Combo(300), cbTech = Ui.Combo(300), cbBox = Ui.Combo(300), cbWh = Ui.Combo(300), cbStatus = Ui.Combo(150);
    readonly TextBox search = new() { Width = 230, PlaceholderText = "رقم الوصل / الاسم / الهاتف / IMEI" };
    readonly TextBox tName = T(), tPhone = T(), tDevice = T(), tSerial = T(), tAcc = T(), tLock = T(),
                     tFault = T(true), tReport = T(true), tNotes = T(true);
    readonly NumericUpDown nEst = Ui.Num(300), nAdv = Ui.Num(300), nWarranty = Ui.Num(300);
    readonly Label lblMode = new() { AutoSize = true, ForeColor = Theme.BrandDark, Font = Theme.FS(10), Margin = new Padding(8, 16, 8, 0) };
    readonly Label lblSum = new() { Dock = DockStyle.Bottom, Height = 40, ForeColor = Theme.BrandDark, Font = Theme.FS(10), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.BrandSoft, Padding = new Padding(10, 0, 10, 0) };
    readonly Control pAdv, pBox;
    long id;

    static TextBox T(bool memo = false) => memo
        ? new TextBox { Width = 300, Multiline = true, Height = 56, ScrollBars = ScrollBars.Vertical }
        : new TextBox { Width = 300 };

    public RepairsForm()
    {
        cbFilter.Items.Add("الأجهزة داخل المحل");
        cbFilter.Items.Add("الكل");
        cbFilter.Items.AddRange(Statuses);
        cbStatus.Items.AddRange(Statuses);
        Ui.FillCombo(cbParty, "SELECT id,name,phone FROM parties WHERE kind<>'مورد' ORDER BY name", true, "— زبون عابر —");
        Ui.MakeSearchable(cbParty);
        Ui.FillCombo(cbTech, "SELECT id,name FROM employees WHERE active=1 ORDER BY name", true);
        Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id");
        Ui.SelectId(cbWh, Ui.DefaultWarehouse());
        nWarranty.Maximum = 3650;

        // ---------- الأدوات ----------
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("عرض", cbFilter));
        bar.Controls.Add(Ui.Labeled("بحث", search));
        var bNew = Theme.Btn("استلام جديد", Theme.Gray, 120);
        var bSave = Theme.Btn("حفظ", Theme.Success, 90);
        var bSetStatus = Theme.Btn("تغيير الحالة", Theme.Accent, 120);
        var bPart = Theme.Btn("صرف قطعة", Theme.Warning, 100);
        var bUnpart = Theme.Btn("إرجاع قطعة", Theme.Gray, 110);
        var bDeliver = Theme.Btn("تسليم وتحصيل", Theme.Success, 130);
        var bPrint = Theme.Btn("طباعة الوصل", Theme.Purple, 120);
        var bWa = Theme.Btn("إشعار واتساب", Theme.Accent, 120);
        var bDel = Theme.Btn("حذف", Theme.Danger, 70);
        bar.Controls.Add(Ui.Labeled("الحالة الجديدة", cbStatus));
        bar.Controls.AddRange(new Control[] { bSetStatus, bNew, bSave, bPart, bUnpart, bDeliver, bPrint, bWa, bDel, lblMode });

        // ---------- المحرر ----------
        var edCard = new CardPanel { Dock = DockStyle.Right, Width = 376, Title = "بيانات الجهاز", IconName = "smartphone" };
        var ed = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };
        edCard.Controls.Add(ed);
        ed.Controls.Add(Ui.Labeled("حساب الزبون (اختياري)", cbParty));
        ed.Controls.Add(Ui.Labeled("اسم الزبون", tName));
        ed.Controls.Add(Ui.Labeled("الهاتف (واتساب)", tPhone));
        ed.Controls.Add(Ui.Labeled("الجهاز (النوع والموديل)", tDevice));
        ed.Controls.Add(Ui.Labeled("IMEI / الرقم التسلسلي", tSerial));
        ed.Controls.Add(Ui.Labeled("العطل حسب الزبون", tFault));
        ed.Controls.Add(Ui.Labeled("الملحقات المستلمة (شاحن، كفر، شريحة...)", tAcc));
        ed.Controls.Add(Ui.Labeled("رمز القفل / النمط", tLock));
        ed.Controls.Add(Ui.Labeled("الفني المسؤول", cbTech));
        ed.Controls.Add(Ui.Labeled("الكلفة التقديرية", nEst));
        ed.Controls.Add(pAdv = Ui.Labeled("العربون المدفوع الآن", nAdv));
        ed.Controls.Add(pBox = Ui.Labeled("الصندوق", cbBox));
        ed.Controls.Add(Ui.Labeled("مخزن قطع الغيار", cbWh));
        ed.Controls.Add(Ui.Labeled("الضمان (يوم)", nWarranty));
        ed.Controls.Add(Ui.Labeled("تقرير الفني", tReport));
        ed.Controls.Add(Ui.Labeled("ملاحظات", tNotes));

        parts.Dock = DockStyle.Bottom;
        parts.Height = 150;
        var partsTitle = new Label { Text = "قطع الغيار المصروفة لهذا الجهاز", Dock = DockStyle.Bottom, Height = 34, ForeColor = Theme.Text2, Font = Theme.FS(10), TextAlign = ContentAlignment.BottomLeft };

        Controls.Add(grid);
        Controls.Add(partsTitle);
        Controls.Add(parts);
        Controls.Add(lblSum);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(edCard);
        Controls.Add(bar);
        Controls.Add(Theme.Title("الصيانة — استلام وتسليم الأجهزة"));

        cbParty.SelectedIndexChanged += (s, e) =>
        {
            var r = Ui.GetRow(cbParty);
            if (r == null || id > 0) return;
            tName.Text = Db.S(r["name"]);
            tPhone.Text = Db.S(r["phone"]);
        };
        cbFilter.SelectedIndexChanged += (s, e) => LoadGrid();
        Ui.OnTextIdle(search, LoadGrid);
        grid.CellClick += (s, e) => LoadSelected();
        grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex < 0 || !grid.Columns.Contains("الحالة")) return;
            var st = Convert.ToString(grid.Rows[e.RowIndex].Cells["الحالة"].Value);
            e.CellStyle.BackColor = st switch
            {
                "جاهز" => Color.FromArgb(220, 252, 231),
                "بانتظار قطعة" => Color.FromArgb(255, 237, 213),
                "لا يصلح" or "ملغي" => Color.FromArgb(254, 226, 226),
                "تم التسليم" => Color.FromArgb(241, 245, 249),
                _ => e.CellStyle.BackColor
            };
        };
        bNew.Click += (s, e) => New();
        bSave.Click += (s, e) => Save();
        bSetStatus.Click += (s, e) => SetStatus();
        bPart.Click += (s, e) => AddPart();
        bUnpart.Click += (s, e) => RemovePart();
        bDeliver.Click += (s, e) => Deliver();
        bPrint.Click += (s, e) => { if (id > 0) BuildTicket(id).Print(); };
        bWa.Click += (s, e) => NotifyReady();
        bDel.Click += (s, e) => Delete();

        cbFilter.SelectedIndex = 0;
        New();
    }

    void LoadGrid()
    {
        var q = search.Text.Trim();
        string f = cbFilter.SelectedIndex switch
        {
            0 => "r.status NOT IN ('تم التسليم','ملغي')",
            1 => "1=1",
            _ => "r.status=@p2"
        };
        grid.DataSource = Db.Query($@"SELECT r.id, r.id AS [الوصل], r.date_in AS [الاستلام], r.customer AS [الزبون], r.phone AS [الهاتف],
            r.device AS [الجهاز], r.fault AS [العطل], e.name AS [الفني], r.status AS [الحالة], r.estimate AS [التقديري],
            IFNULL((SELECT SUM(amount*rate) FROM cash_moves m WHERE m.repair_id=r.id),0) AS [المدفوع],
            IFNULL((SELECT SUM(qty*cost) FROM repair_parts p WHERE p.repair_id=r.id),0) AS [كلفة القطع],
            CAST(julianday('now','localtime')-julianday(r.date_in) AS INTEGER) AS [أيام]
            FROM repairs r LEFT JOIN employees e ON e.id=r.technician_id
            WHERE {f} AND (@p0='' OR CAST(r.id AS TEXT)=@p0 OR r.customer LIKE @p1 OR r.phone LIKE @p1 OR r.serial LIKE @p1 OR r.device LIKE @p1)
            ORDER BY r.id DESC LIMIT 1000", q, "%" + q + "%", cbFilter.SelectedIndex >= 2 ? cbFilter.Text : "");
        var counts = Db.Query("SELECT status, COUNT(*) c FROM repairs WHERE status NOT IN ('تم التسليم','ملغي') GROUP BY status");
        lblSum.Text = "   داخل المحل: " + (counts.Rows.Count == 0 ? "لا يوجد" :
            string.Join("   |   ", counts.Rows.Cast<DataRow>().Select(r => $"{Db.S(r["status"])}: {Db.L(r["c"])}")));
    }

    void LoadParts()
    {
        parts.DataSource = Db.Query(@"SELECT p.id, i.name AS [القطعة], p.qty AS [الكمية], p.cost AS [الكلفة], p.qty*p.cost AS [المجموع], p.date AS [التاريخ]
            FROM repair_parts p JOIN items i ON i.id=p.item_id WHERE p.repair_id=@p0", id);
    }

    void New()
    {
        id = 0;
        foreach (var t in new[] { tName, tPhone, tDevice, tSerial, tAcc, tLock, tFault, tReport, tNotes }) t.Clear();
        nEst.Value = 0; nAdv.Value = 0; nWarranty.Value = 0;
        if (cbParty.Items.Count > 0) cbParty.SelectedIndex = 0;
        if (cbTech.Items.Count > 0) cbTech.SelectedIndex = 0;
        cbStatus.SelectedIndex = 0;
        pAdv.Enabled = pBox.Enabled = true;
        lblMode.Text = "استلام جهاز جديد";
        LoadParts();
        tName.Focus();
    }

    void LoadSelected()
    {
        if (grid.CurrentRow == null) return;
        long rid = Db.L(grid.CurrentRow.Cells["id"].Value);
        var dt = Db.Query("SELECT * FROM repairs WHERE id=@p0", rid);
        if (dt.Rows.Count == 0) return;
        var r = dt.Rows[0];
        id = rid;
        Ui.SelectId(cbParty, Db.L(r["party_id"]));
        tName.Text = Db.S(r["customer"]); tPhone.Text = Db.S(r["phone"]); tDevice.Text = Db.S(r["device"]);
        tSerial.Text = Db.S(r["serial"]); tFault.Text = Db.S(r["fault"]); tAcc.Text = Db.S(r["accessories"]);
        tLock.Text = Db.S(r["lock_code"]); tReport.Text = Db.S(r["report"]); tNotes.Text = Db.S(r["notes"]);
        Ui.SelectId(cbTech, Db.L(r["technician_id"]));
        if (Db.L(r["warehouse_id"]) > 0) Ui.SelectId(cbWh, Db.L(r["warehouse_id"]));
        Ui.SetNum(nEst, Db.D(r["estimate"]));
        Ui.SetNum(nWarranty, Db.L(r["warranty_days"]));
        nAdv.Value = 0;
        pAdv.Enabled = pBox.Enabled = false;
        int si = Array.IndexOf(Statuses, Db.S(r["status"]));
        cbStatus.SelectedIndex = si >= 0 ? si : 0;
        lblMode.Text = $"الوصل رقم {id} — {Db.S(r["status"])}";
        LoadParts();
    }

    string Status => id == 0 ? "" : Db.S(Db.Scalar("SELECT status FROM repairs WHERE id=@p0", id));

    void Save()
    {
        if (!Session.Guard("repairs")) return;
        if (tName.Text.Trim() == "" || tDevice.Text.Trim() == "") { Ui.Warn("أدخل اسم الزبون ونوع الجهاز."); return; }
        long party = Ui.GetId(cbParty), tech = Ui.GetId(cbTech), wh = Ui.GetId(cbWh), box = Ui.GetId(cbBox);
        double adv = (double)nAdv.Value;
        if (id > 0 && Status == "تم التسليم") { Ui.Warn("الجهاز مُسلَّم، لا يمكن تعديل الوصل."); return; }
        if (id == 0 && adv > 0 && box == 0) { Ui.Warn("اختر الصندوق لاستلام العربون."); return; }

        object[] vals = { Db.N(party), tName.Text.Trim(), tPhone.Text.Trim(), tDevice.Text.Trim(), tSerial.Text.Trim(), tFault.Text.Trim(),
            tAcc.Text.Trim(), tLock.Text.Trim(), Db.N(tech), (double)nEst.Value, Db.N(wh), (long)nWarranty.Value, tReport.Text.Trim(), tNotes.Text.Trim() };
        bool isNew = id == 0;
        long rid = id;
        using (var tx = new Tx())
        {
            if (isNew)
            {
                rid = tx.Insert(@"INSERT INTO repairs(party_id,customer,phone,device,serial,fault,accessories,lock_code,technician_id,estimate,
                    warehouse_id,warranty_days,report,notes,date_in,status,user_id)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,'مستلم',@p15)",
                    vals.Concat(new object[] { Ui.Now, Session.UserId }).ToArray());
                if (adv > 0)
                {
                    double rate = Ui.BoxRate(box);
                    tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,repair_id,note,user_id)
                              VALUES(@p0,'عربون صيانة',@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                        Ui.Now, box, adv / rate, rate, Db.N(party), rid, $"عربون صيانة — وصل {rid} — {tDevice.Text.Trim()}", Session.UserId);
                }
            }
            else
                tx.Exec(@"UPDATE repairs SET party_id=@p0,customer=@p1,phone=@p2,device=@p3,serial=@p4,fault=@p5,accessories=@p6,lock_code=@p7,
                    technician_id=@p8,estimate=@p9,warehouse_id=@p10,warranty_days=@p11,report=@p12,notes=@p13 WHERE id=@p14",
                    vals.Concat(new object[] { rid }).ToArray());
            tx.Commit();
        }
        id = rid;
        LoadGrid();
        long saved = id;
        if (isNew && Session.Can("print") && Ui.Confirm($"تم استلام الجهاز بوصل رقم {saved}.\nهل تريد طباعة وصل الاستلام؟"))
            BuildTicket(saved).Print();
        SelectRow(saved);
    }

    void SelectRow(long rid)
    {
        foreach (DataGridViewRow r in grid.Rows)
            if (Db.L(r.Cells["id"].Value) == rid) { grid.CurrentCell = r.Cells["الوصل"]; LoadSelected(); return; }
        New();
    }

    void SetStatus()
    {
        if (id == 0 || !Session.Guard("repairs")) { if (id == 0) Ui.Warn("اختر جهازًا من القائمة."); return; }
        var st = cbStatus.Text;
        if (st == "تم التسليم") { Deliver(); return; }
        if (Status == "تم التسليم") { Ui.Warn("الجهاز مُسلَّم مسبقًا."); return; }
        Db.Exec("UPDATE repairs SET status=@p0, date_ready=CASE WHEN @p0='جاهز' THEN @p1 ELSE date_ready END WHERE id=@p2", st, Ui.Now, id);
        long rid = id;
        LoadGrid();
        SelectRow(rid);
        if (st == "جاهز" && tPhone.Text.Trim() != "" && Ui.Confirm("هل تريد إشعار الزبون بجاهزية الجهاز عبر واتساب؟")) NotifyReady();
    }

    void NotifyReady()
    {
        if (id == 0) return;
        var phone = tPhone.Text.Trim();
        if (phone == "") { Ui.Warn("لا يوجد رقم هاتف."); return; }
        double paid = Db.D(Db.Scalar("SELECT SUM(amount*rate) FROM cash_moves WHERE repair_id=@p0", id));
        double price = Math.Max(0, (double)nEst.Value - paid);
        string msg = Status == "جاهز"
            ? Ui.Fill(Settings.Get("repair_ready_msg"), ("name", tName.Text.Trim()), ("device", tDevice.Text.Trim()), ("id", id), ("price", Ui.M(price)), ("shop", Settings.Get("shop_name")))
            : $"{Settings.Get("shop_name")}\nعزيزي {tName.Text.Trim()}، حالة جهازكم {tDevice.Text.Trim()} (وصل رقم {id}): {Status}.";
        _ = WhatsApp.Send(phone, msg);
    }

    void AddPart()
    {
        if (id == 0 || !Session.Guard("repairs")) { if (id == 0) Ui.Warn("اختر جهازًا من القائمة."); return; }
        if (Status == "تم التسليم") { Ui.Warn("الجهاز مُسلَّم مسبقًا."); return; }
        long wh = Ui.GetId(cbWh);
        using var dlg = new DialogShell("صرف قطعة غيار", 470, 330, "package-minus");
        var cb = Ui.Combo(410);
        Ui.FillCombo(cb, @"SELECT i.id, i.name||'  (المتوفر: '||SUM(b.qty)||')' FROM items i JOIN batches b ON b.item_id=i.id
            WHERE b.warehouse_id=@p0 AND b.qty>0 GROUP BY i.id ORDER BY i.name", false, "", wh);
        Ui.MakeSearchable(cb);
        var n = Ui.Num(150, 2); n.Value = 1;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        flow.Controls.Add(Ui.Labeled("القطعة", cb));
        flow.Controls.Add(Ui.Labeled("الكمية", n));
        dlg.Body.Controls.Add(flow);
        dlg.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        dlg.AddButton("صرف القطعة", DialogResult.OK, BtnKind.Primary, "package-minus");
        if (dlg.ShowModal() != DialogResult.OK) return;
        long item = Ui.GetId(cb);
        double q = (double)n.Value;
        if (item == 0 || q <= 0) return;
        using (var tx = new Tx())
        {
            try
            {
                foreach (var t in StockOps.TakeFefo(tx, item, wh, q))
                    tx.Exec("INSERT INTO repair_parts(repair_id,item_id,batch_id,qty,cost,date) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", id, item, t.BatchId, t.Qty, t.Cost, Ui.Now);
                tx.Exec("UPDATE repairs SET warehouse_id=@p0 WHERE id=@p1", wh, id);
                tx.Commit();
            }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return; }
        }
        LoadParts();
        long rid = id; LoadGrid(); SelectRow(rid);
    }

    void RemovePart()
    {
        if (parts.CurrentRow == null || !Session.Guard("repairs")) return;
        if (Status == "تم التسليم") { Ui.Warn("الجهاز مُسلَّم مسبقًا."); return; }
        long pid = Db.L(parts.CurrentRow.Cells["id"].Value);
        if (!Ui.Confirm("إرجاع القطعة المحددة إلى المخزن؟")) return;
        using (var tx = new Tx())
        {
            var pr = tx.Query("SELECT batch_id, qty FROM repair_parts WHERE id=@p0", pid);
            if (pr.Rows.Count == 0) { LoadParts(); return; }
            var r = pr.Rows[0];
            tx.Exec("UPDATE batches SET qty=qty+@p0 WHERE id=@p1", Db.D(r["qty"]), Db.L(r["batch_id"]));
            tx.Exec("DELETE FROM repair_parts WHERE id=@p0", pid);
            tx.Commit();
        }
        LoadParts();
        long rid = id; LoadGrid(); SelectRow(rid);
    }

    void Deliver()
    {
        if (id == 0 || !Session.Guard("repairs")) { if (id == 0) Ui.Warn("اختر جهازًا من القائمة."); return; }
        if (Status == "تم التسليم") { Ui.Warn("الجهاز مُسلَّم مسبقًا."); return; }
        long party = Ui.GetId(cbParty);
        double paidBefore = Db.D(Db.Scalar("SELECT IFNULL(SUM(amount*rate),0) FROM cash_moves WHERE repair_id=@p0", id));

        using var dlg = new DialogShell($"تسليم الجهاز — وصل {id}", 450, 470, "handshake");
        var nPrice = Ui.Num(360, 2); nPrice.Value = nEst.Value;
        var nPay = Ui.Num(360, 2);
        var cb = Ui.Combo(360); Ui.FillCombo(cb, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        var lbl = new Label { Width = 364, Height = 34, ForeColor = Theme.BrandDark, Font = Theme.FS(10), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 2, 6, 2) };
        void Upd()
        {
            Ui.SetNum(nPay, (double)nPrice.Value - paidBefore);
            lbl.Text = $"المدفوع سابقًا (عربون): {Ui.M(paidBefore)}";
        }
        nPrice.ValueChanged += (s, e) => Upd();
        Upd();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        flow.Controls.Add(Ui.Labeled("السعر النهائي للتصليح", nPrice));
        flow.Controls.Add(lbl);
        flow.Controls.Add(Ui.Labeled("المبلغ المستلم الآن (سالب = إرجاع للزبون)", nPay));
        flow.Controls.Add(Ui.Labeled("الصندوق", cb));
        dlg.Body.Controls.Add(flow);
        dlg.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        dlg.AddButton("تأكيد التسليم", DialogResult.OK);
        if (dlg.ShowModal() != DialogResult.OK) return;

        double price = (double)nPrice.Value, pay = (double)nPay.Value;
        long box = Ui.GetId(cb);
        if (Math.Abs(pay) > 0.001 && box == 0) { Ui.Warn("اختر الصندوق."); return; }
        if (party == 0 && Math.Abs(price - paidBefore - pay) > 0.01)
        { Ui.Warn("الزبون العابر يجب أن يسدد المبلغ كاملًا. لتسجيل المتبقي دينًا اختر حساب الزبون أولًا ثم احفظ."); return; }
        using (var tx = new Tx())
        {
            tx.Exec("UPDATE repairs SET status='تم التسليم', final_price=@p0, date_out=@p1, party_id=@p2 WHERE id=@p3", price, Ui.Now, Db.N(party), id);
            if (party > 0) tx.Exec("UPDATE cash_moves SET party_id=@p0 WHERE repair_id=@p1", party, id);
            if (Math.Abs(pay) > 0.001)
            {
                double rate = Ui.BoxRate(box);
                tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,repair_id,note,user_id)
                          VALUES(@p0,'صيانة',@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                    Ui.Now, box, pay / rate, rate, Db.N(party), id, $"تسليم جهاز — وصل {id} — {tDevice.Text.Trim()}", Session.UserId);
            }
            tx.Commit();
        }
        long rid = id;
        if (Session.Can("print") && Ui.Confirm("تم التسليم. هل تريد طباعة وصل التسليم؟")) BuildTicket(rid).Print();
        LoadGrid();
        SelectRow(rid);
    }

    void Delete()
    {
        if (id == 0 || !Session.Guard("delete")) return;
        if (Db.L(Db.Scalar("SELECT COUNT(*) FROM repair_parts WHERE repair_id=@p0", id)) > 0) { Ui.Warn("أرجع قطع الغيار المصروفة أولًا."); return; }
        bool hasCash = Db.L(Db.Scalar("SELECT COUNT(*) FROM cash_moves WHERE repair_id=@p0", id)) > 0;
        if (!Ui.Confirm(hasCash ? "لهذا الوصل مبالغ مقبوضة وسيتم حذفها أيضًا. متابعة؟" : "حذف الوصل المحدد؟")) return;
        using (var tx = new Tx())
        {
            tx.Exec("DELETE FROM cash_moves WHERE repair_id=@p0", id);
            tx.Exec("DELETE FROM repairs WHERE id=@p0", id);
            tx.Commit();
        }
        Db.Audit("حذف وصل صيانة", $"رقم {id} — {tName.Text} — {tDevice.Text}");
        LoadGrid();
        New();
    }

    /// <summary>وصل الاستلام / التسليم للطباعة</summary>
    public static PrintDoc BuildTicket(long rid)
    {
        var rt = Db.Query("SELECT r.*, e.name AS tech FROM repairs r LEFT JOIN employees e ON e.id=r.technician_id WHERE r.id=@p0", rid);
        if (rt.Rows.Count == 0) return PrintDoc.Header("وصل صيانة").Text($"الوصل رقم {rid} غير موجود.");
        var r = rt.Rows[0];
        bool delivered = Db.S(r["status"]) == "تم التسليم";
        double paid = Db.D(Db.Scalar("SELECT IFNULL(SUM(amount*rate),0) FROM cash_moves WHERE repair_id=@p0", rid));
        var d = PrintDoc.Header(delivered ? "وصل تسليم جهاز" : "وصل استلام جهاز للصيانة");
        d.Pair("رقم الوصل", rid.ToString(), "التاريخ", Db.S(r["date_in"]).Length >= 16 ? Db.S(r["date_in"])[..16] : Db.S(r["date_in"]));
        d.Pair("الزبون", Db.S(r["customer"]), "الهاتف", Db.S(r["phone"]));
        d.Pair("الجهاز", Db.S(r["device"]), "IMEI", Db.S(r["serial"]));
        d.Text("العطل: " + Db.S(r["fault"]), 10);
        if (Db.S(r["accessories"]) != "") d.Text("الملحقات: " + Db.S(r["accessories"]), 10);
        if (Db.S(r["report"]) != "") d.Text("تقرير الفني: " + Db.S(r["report"]), 10);
        d.Line();
        if (delivered)
        {
            d.Pair("السعر النهائي", Ui.M(Db.D(r["final_price"])), "المدفوع", Ui.M(paid));
            d.Pair("تاريخ التسليم", Db.S(r["date_out"]).Length >= 16 ? Db.S(r["date_out"])[..16] : Db.S(r["date_out"]),
                   "الضمان", Db.L(r["warranty_days"]) > 0 ? $"{Db.L(r["warranty_days"])} يوم" : "بدون");
        }
        else d.Pair("الكلفة التقديرية", Ui.M(Db.D(r["estimate"])), "العربون", Ui.M(paid));
        var terms = Settings.Get("repair_terms");
        if (terms != "") { d.Space(4); d.Text(terms, 8); }
        d.Space(6);
        d.Pair("توقيع الزبون", "..................", "توقيع المحل", "..................");
        d.Footer();
        d.Barcode("R" + rid);
        return d;
    }
}
