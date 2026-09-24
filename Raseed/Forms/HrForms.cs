using System.Data;

namespace Raseed;

/// <summary>الموارد البشرية: الموظفون، الحضور والغياب، السلف والمكافآت والخصومات، مسير الرواتب</summary>
public class HrForm : BaseForm
{
    public HrForm()
    {
        var tabs = new ModernTabs { Dock = DockStyle.Fill };
        var emp = new Panel { BackColor = Theme.Bg };
        var crud = new CrudForm(Defs.Employees()) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
        emp.Controls.Add(crud); crud.Show();
        tabs.Add("الموظفون", emp, "users");
        tabs.Add(Attendance(), "calendar-days");
        tabs.Add(Moves(), "coins");
        tabs.Add(Payroll(), "banknote");
        Controls.Add(tabs);
    }

    static TabPage Page(string title, Control fill, Control top, Control bottom = null)
    {
        var p = new TabPage(title) { BackColor = Theme.Bg, Font = Theme.F() };
        p.Controls.Add(fill);
        if (bottom != null) p.Controls.Add(bottom);
        p.Controls.Add(top);
        return p;
    }

    // ---------------- الحضور والغياب ----------------
    TabPage Attendance()
    {
        var grid = Ui.NewGrid(false);
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "emp", Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "الموظف", ReadOnly = true, FillWeight = 160 });
        var st = new DataGridViewComboBoxColumn { Name = "status", HeaderText = "الحالة", FillWeight = 90, FlatStyle = FlatStyle.Flat };
        st.Items.AddRange("حاضر", "غائب", "إجازة", "متأخر", "عطلة");
        grid.Columns.Add(st);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "in", HeaderText = "وقت الحضور", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "out", HeaderText = "وقت الانصراف", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "note", HeaderText = "ملاحظة", FillWeight = 160 });

        var day = new DateTimePicker { Width = 150, Format = DateTimePickerFormat.Short, Value = DateTime.Today };
        var bLoad = Theme.Btn("عرض", Theme.Accent, 90);
        var bAll = Theme.Btn("الكل حاضر", Theme.Gray, 110);
        var bSave = Theme.Btn("حفظ الحضور", Theme.Success, 130);
        var top = Theme.Bar();
        top.Controls.Add(Ui.Labeled("اليوم", day));
        top.Controls.AddRange(new Control[] { bLoad, bAll, bSave });

        void Load()
        {
            grid.Rows.Clear();
            var d = day.Value.ToString(Ui.DFmt);
            var dt = Db.Query(@"SELECT e.id, e.name, a.status, a.time_in, a.time_out, a.note FROM employees e
                LEFT JOIN attendance a ON a.employee_id=e.id AND a.date=@p0 WHERE e.active=1 ORDER BY e.name", d);
            foreach (DataRow r in dt.Rows)
                grid.Rows.Add(Db.L(r["id"]), Db.S(r["name"]), Db.S(r["status"]) == "" ? "حاضر" : Db.S(r["status"]),
                    Db.S(r["time_in"]), Db.S(r["time_out"]), Db.S(r["note"]));
        }
        bLoad.Click += (s, e) => Load();
        day.ValueChanged += (s, e) => Load();
        bAll.Click += (s, e) => { foreach (DataGridViewRow r in grid.Rows) r.Cells["status"].Value = "حاضر"; };
        bSave.Click += (s, e) =>
        {
            if (!Session.Guard("hr")) return;
            grid.EndEdit();
            var d = day.Value.ToString(Ui.DFmt);
            using var tx = new Tx();
            foreach (DataGridViewRow r in grid.Rows)
                tx.Exec(@"INSERT INTO attendance(employee_id,date,status,time_in,time_out,note) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)
                          ON CONFLICT(employee_id,date) DO UPDATE SET status=excluded.status, time_in=excluded.time_in, time_out=excluded.time_out, note=excluded.note",
                    Db.L(r.Cells["emp"].Value), d, Convert.ToString(r.Cells["status"].Value), Convert.ToString(r.Cells["in"].Value),
                    Convert.ToString(r.Cells["out"].Value), Convert.ToString(r.Cells["note"].Value));
            tx.Commit();
            Ui.Info("تم حفظ الحضور ليوم " + d);
        };
        Load();
        return Page("الحضور والغياب", grid, top);
    }

    // ---------------- السلف والمكافآت والخصومات ----------------
    TabPage Moves()
    {
        var grid = Ui.NewGrid();
        var cbEmp = Ui.Combo(200); Ui.FillCombo(cbEmp, "SELECT id,name FROM employees WHERE active=1 ORDER BY name");
        var cbKind = Ui.Combo(110); cbKind.Items.AddRange(new object[] { "سلفة", "مكافأة", "خصم" }); cbKind.SelectedIndex = 0;
        var nAmt = Ui.Num(140, 2);
        var dt = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Value = DateTime.Today };
        var cbBox = Ui.Combo(190); Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        var tNote = new TextBox { Width = 220 };
        var bSave = Theme.Btn("حفظ", Theme.Success, 90);
        var bDel = Theme.Btn("حذف المحدد", Theme.Danger, 120);
        var top = Theme.Bar();
        top.Controls.Add(Ui.Labeled("الموظف", cbEmp));
        top.Controls.Add(Ui.Labeled("النوع", cbKind));
        top.Controls.Add(Ui.Labeled("المبلغ", nAmt));
        top.Controls.Add(Ui.Labeled("التاريخ", dt));
        var pBox = Ui.Labeled("الصندوق (للسلفة)", cbBox);
        top.Controls.Add(pBox);
        top.Controls.Add(Ui.Labeled("البيان", tNote));
        top.Controls.AddRange(new Control[] { bSave, bDel });
        cbKind.SelectedIndexChanged += (s, e) => pBox.Enabled = cbKind.Text == "سلفة";

        void Load() => grid.DataSource = Db.Query(@"SELECT h.id, h.date AS [التاريخ], e.name AS [الموظف], h.kind AS [النوع], h.amount AS [المبلغ], h.note AS [البيان]
            FROM hr_moves h JOIN employees e ON e.id=h.employee_id ORDER BY h.date DESC, h.id DESC LIMIT 1000");
        bSave.Click += (s, e) =>
        {
            if (!Session.Guard("hr")) return;
            long emp = Ui.GetId(cbEmp), box = Ui.GetId(cbBox);
            double amt = (double)nAmt.Value;
            if (emp == 0 || amt <= 0) { Ui.Warn("اختر الموظف وأدخل المبلغ."); return; }
            string kind = cbKind.Text, date = dt.Value.ToString(Ui.DFmt), rf = null;
            using (var tx = new Tx())
            {
                if (kind == "سلفة")
                {
                    if (box == 0) { Ui.Warn("اختر الصندوق."); return; }
                    rf = "ADV:" + Guid.NewGuid().ToString("N")[..10];
                    double rate = Ui.BoxRate(box);   // قراءة آمنة الآن (القراءة لا تحجز قفل الكتابة)
                    tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,employee_id,ref,note,user_id) VALUES(@p0,'سلفة',@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                        date + DateTime.Now.ToString(" HH:mm:ss"), box, -amt / rate, rate, emp, rf, "سلفة موظف — " + tNote.Text.Trim(), Session.UserId);
                }
                tx.Exec("INSERT INTO hr_moves(employee_id,date,kind,amount,note,cash_ref,user_id) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",
                    emp, date, kind, amt, tNote.Text.Trim(), rf, Session.UserId);
                tx.Commit();
            }
            nAmt.Value = 0; tNote.Clear();
            Load();
        };
        bDel.Click += (s, e) =>
        {
            if (grid.CurrentRow == null || !Session.Guard("delete")) return;
            if (!Ui.Confirm("حذف الحركة المحددة؟")) return;
            long id = Db.L(grid.CurrentRow.Cells["id"].Value);
            using var tx = new Tx();
            var rf = Db.S(tx.Scalar("SELECT cash_ref FROM hr_moves WHERE id=@p0", id));
            if (rf != "") tx.Exec("DELETE FROM cash_moves WHERE ref=@p0", rf);
            tx.Exec("DELETE FROM hr_moves WHERE id=@p0", id);
            tx.Commit();
            Load();
        };
        Load();
        return Page("السلف والمكافآت والخصومات", grid, top);
    }

    // ---------------- مسير الرواتب ----------------
    TabPage Payroll()
    {
        var grid = Ui.NewGrid();
        var month = new DateTimePicker { Width = 120, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM", ShowUpDown = true, Value = DateTime.Today };
        var cbBox = Ui.Combo(200); Ui.FillCombo(cbBox, "SELECT id, name||' ('||currency||')' FROM cashboxes ORDER BY id");
        var bCalc = Theme.Btn("احتساب", Theme.Accent, 100);
        var bPay = Theme.Btn("صرف راتب المحدد", Theme.Success, 150);
        var bPayAll = Theme.Btn("صرف جميع المتبقي", Theme.Purple, 160);
        var top = Theme.Bar();
        top.Controls.Add(Ui.Labeled("الشهر", month));
        top.Controls.Add(Ui.Labeled("صندوق الصرف", cbBox));
        top.Controls.AddRange(new Control[] { bCalc, bPay, bPayAll });
        Ui.GridTools(top, grid, () => "مسير رواتب " + month.Value.ToString("yyyy-MM"));
        var lbl = new Label { Dock = DockStyle.Bottom, Height = 44, Font = Theme.FS(10.5f), ForeColor = Theme.BrandDark, BackColor = Theme.BrandSoft, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0),
            Text = "   الصافي = الراتب + المكافآت − الخصومات − السلف − (أيام الغياب × الراتب ÷ 30)" };

        string M() => month.Value.ToString("yyyy-MM");
        void Calc() => grid.DataSource = Db.Query(@"
            SELECT id, name AS [الموظف], salary AS [الراتب], bonus AS [المكافآت], ded AS [الخصومات], adv AS [السلف], absent AS [أيام الغياب],
                   ROUND(absent*salary/30.0,0) AS [خصم الغياب],
                   ROUND(salary+bonus-ded-adv-absent*salary/30.0,0) AS [الصافي], paid AS [المصروف],
                   ROUND(salary+bonus-ded-adv-absent*salary/30.0,0)-paid AS [المتبقي]
            FROM (SELECT e.id, e.name, e.salary,
                IFNULL((SELECT SUM(amount) FROM hr_moves h WHERE h.employee_id=e.id AND h.kind='مكافأة' AND substr(h.date,1,7)=@p0),0) AS bonus,
                IFNULL((SELECT SUM(amount) FROM hr_moves h WHERE h.employee_id=e.id AND h.kind='خصم' AND substr(h.date,1,7)=@p0),0) AS ded,
                IFNULL((SELECT SUM(amount) FROM hr_moves h WHERE h.employee_id=e.id AND h.kind='سلفة' AND substr(h.date,1,7)=@p0),0) AS adv,
                IFNULL((SELECT COUNT(*) FROM attendance a WHERE a.employee_id=e.id AND a.status='غائب' AND substr(a.date,1,7)=@p0),0) AS absent,
                IFNULL((SELECT -SUM(amount*rate) FROM cash_moves m WHERE m.employee_id=e.id AND m.kind='راتب'
                        AND (m.ref=@p1 OR (m.ref IS NULL AND substr(m.date,1,7)=@p0))),0) AS paid
                FROM employees e WHERE e.active=1) ORDER BY name", M(), "SAL:" + M());

        void PayRow(Tx tx, DataGridViewRow r, long box, double rate)
        {
            double rem = Db.D(r.Cells["المتبقي"].Value);
            if (rem <= 0.5) return;
            tx.Exec(@"INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,employee_id,ref,note,user_id) VALUES(@p0,'راتب',@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                Ui.Now, box, -rem / rate, rate, Db.L(r.Cells["id"].Value), "SAL:" + M(), $"راتب شهر {M()} — {r.Cells["الموظف"].Value}", Session.UserId);
        }
        bCalc.Click += (s, e) => Calc();
        month.ValueChanged += (s, e) => Calc();
        bPay.Click += (s, e) =>
        {
            if (grid.CurrentRow == null || !Session.Guard("hr")) return;
            long box = Ui.GetId(cbBox);
            if (box == 0) { Ui.Warn("اختر صندوق الصرف."); return; }
            if (!Ui.Confirm($"صرف {Ui.M(Db.D(grid.CurrentRow.Cells["المتبقي"].Value))} للموظف {grid.CurrentRow.Cells["الموظف"].Value}؟")) return;
            double rate = Ui.BoxRate(box);
            using (var tx = new Tx()) { PayRow(tx, grid.CurrentRow, box, rate); tx.Commit(); }
            Calc();
        };
        bPayAll.Click += (s, e) =>
        {
            if (!Session.Guard("hr") || grid.Rows.Count == 0) return;
            long box = Ui.GetId(cbBox);
            if (box == 0) { Ui.Warn("اختر صندوق الصرف."); return; }
            double total = grid.Rows.Cast<DataGridViewRow>().Sum(r => Math.Max(0, Db.D(r.Cells["المتبقي"].Value)));
            if (!Ui.Confirm($"صرف رواتب شهر {M()} بمجموع {Ui.M(total)}؟")) return;
            double rate = Ui.BoxRate(box);
            using (var tx = new Tx())
            {
                foreach (DataGridViewRow r in grid.Rows) PayRow(tx, r, box, rate);
                tx.Commit();
            }
            Calc();
        };
        Calc();
        return Page("مسير الرواتب", grid, top, lbl);
    }
}
