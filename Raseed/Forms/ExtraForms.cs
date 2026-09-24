using System.Data;

namespace Raseed;

/// <summary>ملصقات الباركود: اختيار المواد وعدد الملصقات، توليد باركود للمواد التي لا تملكه، طباعة</summary>
public class LabelsForm : BaseForm
{
    readonly DataGridView grid = Ui.NewGrid(false);
    readonly TextBox search = new() { Width = 240, PlaceholderText = "بحث بالاسم أو الباركود" };
    readonly ComboBox cbLevel = Ui.Combo(110);
    readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };

    public LabelsForm()
    {
        cbLevel.Items.AddRange(new object[] { "مفرد", "جملة", "خاص" });
        cbLevel.SelectedIndex = 0;
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("بحث", search));
        bar.Controls.Add(Ui.Labeled("السعر المطبوع", cbLevel));
        var bGen = Theme.Btn("توليد باركود للمواد بدون باركود", Theme.Warning, 250);
        var bStock = Theme.Btn("العدد = الرصيد", Theme.Gray, 130);
        var bClear = Theme.Btn("تصفير", Theme.Gray, 80);
        var bPrev = Theme.Btn("معاينة", Theme.Accent, 90);
        var bPrint = Theme.Btn("طباعة", Theme.Success, 90);
        bar.Controls.AddRange(new Control[] { bGen, bStock, bClear, bPrev, bPrint });

        var side = new Panel { Dock = DockStyle.Right, Width = 300, BackColor = Color.White, Padding = new Padding(10) };
        side.Controls.Add(preview);
        side.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 60, ForeColor = Theme.Muted,
            Text = $"مقاس الملصق: {Settings.Get("label_w")}×{Settings.Get("label_h")} ملم — يُغيَّر من الإعدادات" });
        side.Controls.Add(new Label { Text = "معاينة الباركود", Dock = DockStyle.Top, Height = 30, Font = Theme.F(10, FontStyle.Bold) });

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        Controls.Add(side);
        Controls.Add(bar);
        Controls.Add(Theme.Title("ملصقات الباركود"));

        search.TextChanged += (s, e) => Reload();
        grid.SelectionChanged += (s, e) =>
        {
            if (grid.CurrentRow == null) return;
            var code = Convert.ToString(grid.CurrentRow.Cells["الباركود"].Value);
            preview.Image?.Dispose();
            preview.Image = string.IsNullOrWhiteSpace(code) ? null : Code128.Image(code, 560, 200);
        };
        bGen.Click += (s, e) => Generate();
        bStock.Click += (s, e) => { foreach (DataGridViewRow r in grid.Rows) r.Cells["العدد"].Value = Math.Max(0, (long)Math.Ceiling(Db.D(r.Cells["الرصيد"].Value))); };
        bClear.Click += (s, e) => { foreach (DataGridViewRow r in grid.Rows) r.Cells["العدد"].Value = 0L; };
        bPrev.Click += (s, e) => Print(true);
        bPrint.Click += (s, e) => Print(false);
        Reload();
    }

    void Reload()
    {
        var q = "%" + search.Text.Trim() + "%";
        var dt = Db.Query(@"SELECT i.id, i.name AS [المادة], i.barcode AS [الباركود], i.price_retail AS [مفرد], i.price_wholesale AS [جملة], i.price_special AS [خاص],
            IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0) AS [الرصيد], 0 AS [العدد]
            FROM items i WHERE i.active=1 AND (i.name LIKE @p0 OR IFNULL(i.barcode,'') LIKE @p0) ORDER BY i.name", q);
        grid.DataSource = dt;
        foreach (DataGridViewColumn c in grid.Columns) c.ReadOnly = c.Name != "العدد";
        if (grid.Columns.Contains("العدد")) grid.Columns["العدد"].DefaultCellStyle.BackColor = Color.FromArgb(255, 251, 235);
    }

    void Generate()
    {
        if (!Session.Guard("items")) return;
        var rows = Db.Query("SELECT id FROM items WHERE IFNULL(barcode,'')=''");
        if (rows.Rows.Count == 0) { Ui.Info("جميع المواد لديها باركود."); return; }
        if (!Ui.Confirm($"سيتم توليد باركود داخلي لـ {rows.Rows.Count} مادة. متابعة؟")) return;
        using (var tx = new Tx())
        {
            foreach (DataRow r in rows.Rows)
            {
                long id = Db.L(r["id"]);
                // 2 + رقم المادة بعشرة أرقام (بادئة 2 مخصصة للاستخدام الداخلي)
                tx.Exec("UPDATE items SET barcode=@p0 WHERE id=@p1", "2" + id.ToString("D10"), id);
            }
            tx.Commit();
        }
        Reload();
    }

    void Print(bool preview)
    {
        if (!Session.Guard("labels")) return;
        grid.EndEdit();
        string col = cbLevel.Text;
        var list = new List<LabelPrinter.Label>();
        foreach (DataGridViewRow r in grid.Rows)
        {
            long n = Db.L(r.Cells["العدد"].Value);
            var code = Convert.ToString(r.Cells["الباركود"].Value);
            if (n <= 0) continue;
            if (string.IsNullOrWhiteSpace(code)) { Ui.Warn($"المادة «{r.Cells["المادة"].Value}» بدون باركود. استخدم زر التوليد أولاً."); return; }
            for (int i = 0; i < n && list.Count < 5000; i++)
                list.Add(new LabelPrinter.Label(Convert.ToString(r.Cells["المادة"].Value), code, Db.D(r.Cells[col].Value)));
        }
        if (list.Count == 0) { Ui.Warn("حدد عدد الملصقات في عمود «العدد»."); return; }
        LabelPrinter.Print(list, preview);
    }
}

/// <summary>جرد المخزون: مقارنة الرصيد الدفتري بالفعلي وإنشاء قيود التسوية تلقائياً</summary>
public class StockCountForm : BaseForm
{
    readonly DataGridView grid = Ui.NewGrid(false);
    readonly ComboBox cbWh = Ui.Combo(200);
    readonly TextBox search = new() { Width = 220, PlaceholderText = "بحث..." };
    readonly Label lbl = new() { Dock = DockStyle.Bottom, Height = 34, Font = Theme.F(11, FontStyle.Bold), BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };

    public StockCountForm()
    {
        Ui.FillCombo(cbWh, "SELECT id,name FROM warehouses ORDER BY id");
        var bar = Theme.Bar();
        bar.Controls.Add(Ui.Labeled("المخزن", cbWh));
        bar.Controls.Add(Ui.Labeled("تصفية", search));
        var bLoad = Theme.Btn("تحميل المواد", Theme.Accent, 130);
        var bPost = Theme.Btn("اعتماد الجرد", Theme.Success, 130);
        bar.Controls.AddRange(new Control[] { bLoad, bPost });
        Ui.GridTools(bar, grid, () => "ورقة جرد — " + cbWh.Text);

        Controls.Add(grid);
        Controls.Add(lbl);
        Controls.Add(bar);
        Controls.Add(Theme.Title("جرد المخزون وتسوية الفروقات"));

        bLoad.Click += (s, e) => Reload();
        search.TextChanged += (s, e) =>
        {
            foreach (DataGridViewRow r in grid.Rows)
                r.Visible = search.Text == "" || Convert.ToString(r.Cells["المادة"].Value).Contains(search.Text, StringComparison.OrdinalIgnoreCase)
                            || Convert.ToString(r.Cells["الباركود"].Value) == search.Text;
        };
        grid.CellEndEdit += (s, e) => Diff();
        bPost.Click += (s, e) => Post();
        lbl.Text = "   اختر المخزن واضغط «تحميل المواد»، ثم أدخل الكمية الفعلية لكل مادة.";
    }

    void Reload()
    {
        var dt = Db.Query(@"SELECT i.id, i.name AS [المادة], i.barcode AS [الباركود], i.unit AS [الوحدة],
            IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id AND b.warehouse_id=@p0),0) AS [الرصيد الدفتري]
            FROM items i WHERE i.active=1 ORDER BY i.name", Ui.GetId(cbWh));
        dt.Columns.Add("الكمية الفعلية", typeof(double));
        dt.Columns.Add("الفرق", typeof(double));
        foreach (DataRow r in dt.Rows) { r["الكمية الفعلية"] = r["الرصيد الدفتري"]; r["الفرق"] = 0.0; }
        grid.DataSource = dt;
        foreach (DataGridViewColumn c in grid.Columns) c.ReadOnly = c.Name != "الكمية الفعلية";
        grid.Columns["الكمية الفعلية"].DefaultCellStyle.BackColor = Color.FromArgb(255, 251, 235);
        Diff();
    }

    void Diff()
    {
        if (grid.DataSource is not DataTable dt) return;
        int n = 0;
        foreach (DataRow r in dt.Rows)
        {
            double d = Db.D(r["الكمية الفعلية"]) - Db.D(r["الرصيد الدفتري"]);
            r["الفرق"] = Math.Round(d, 4);
            if (Math.Abs(d) > 1e-9) n++;
        }
        lbl.Text = $"   عدد المواد التي فيها فروقات: {n}";
        lbl.ForeColor = n > 0 ? Theme.Danger : Theme.Success;
    }

    void Post()
    {
        if (!Session.Guard("stock")) return;
        grid.EndEdit();
        Diff();
        if (grid.DataSource is not DataTable dt) return;
        var diffs = dt.Rows.Cast<DataRow>().Where(r => Math.Abs(Db.D(r["الفرق"])) > 1e-9).ToList();
        if (diffs.Count == 0) { Ui.Info("لا توجد فروقات."); return; }
        if (!Ui.Confirm($"سيتم تسجيل التسوية لـ {diffs.Count} مادة (النقص كإتلاف، والزيادة كإدخال بكلفة المعدل). متابعة؟")) return;
        long wh = Ui.GetId(cbWh);
        string now = Ui.Now;
        using (var tx = new Tx())
        {
            try
            {
                long outInv = 0, inInv = 0;
                double outTotal = 0, inTotal = 0;
                foreach (var r in diffs)
                {
                    long item = Db.L(r["id"]);
                    double d = Db.D(r["الفرق"]);
                    if (d < 0)
                    {
                        if (outInv == 0) outInv = tx.Insert("INSERT INTO invoices(type,date,warehouse_id,total,net,paid,notes,user_id) VALUES('Damage',@p0,@p1,0,0,0,'تسوية جرد — نقص',@p2)", now, wh, Session.UserId);
                        foreach (var t in StockOps.TakeFefo(tx, item, wh, -d))
                        {
                            tx.Exec("INSERT INTO invoice_lines(invoice_id,item_id,batch_id,qty,price,cost,expiry) VALUES(@p0,@p1,@p2,@p3,@p4,@p4,@p5)", outInv, item, t.BatchId, t.Qty, t.Cost, t.Expiry);
                            outTotal += t.Qty * t.Cost;
                        }
                    }
                    else
                    {
                        if (inInv == 0) inInv = tx.Insert("INSERT INTO invoices(type,date,warehouse_id,pay_type,total,net,paid,notes,user_id) VALUES('Purchase',@p0,@p1,'آجل',0,0,0,'تسوية جرد — زيادة',@p2)", now, wh, Session.UserId);
                        double cost = StockOps.AvgCost(item);
                        long bid = tx.Insert("INSERT INTO batches(item_id,warehouse_id,qty,cost,created) VALUES(@p0,@p1,@p2,@p3,@p4)", item, wh, d, cost, now);
                        tx.Exec("INSERT INTO invoice_lines(invoice_id,item_id,batch_id,qty,price,cost) VALUES(@p0,@p1,@p2,@p3,@p4,@p4)", inInv, item, bid, d, cost);
                        inTotal += d * cost;
                    }
                }
                // قيود الجرد لا تؤثر على الصناديق؛ فاتورة الزيادة بلا مورد
                if (outInv > 0) tx.Exec("UPDATE invoices SET total=@p0, net=@p0 WHERE id=@p1", outTotal, outInv);
                if (inInv > 0) tx.Exec("UPDATE invoices SET total=@p0, net=@p0 WHERE id=@p1", inTotal, inInv);
                tx.Commit();
            }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return; }
        }
        Db.Audit("اعتماد جرد", $"المخزن {cbWh.Text} — {diffs.Count} مادة");
        Ui.Info("تم اعتماد الجرد وتسوية الفروقات.");
        Reload();
    }
}

/// <summary>تغيير كلمة مرور المستخدم الحالي</summary>
public class PasswordDialog : BaseForm
{
    public PasswordDialog()
    {
        Text = "تغيير كلمة المرور";
        Width = 380; Height = 330;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var old = new TextBox { Width = 320, UseSystemPasswordChar = true };
        var p1 = new TextBox { Width = 320, UseSystemPasswordChar = true };
        var p2 = new TextBox { Width = 320, UseSystemPasswordChar = true };
        var ok = Theme.Btn("حفظ", Theme.Success, 120);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        flow.Controls.Add(Ui.Labeled("كلمة المرور الحالية", old));
        flow.Controls.Add(Ui.Labeled("كلمة المرور الجديدة", p1));
        flow.Controls.Add(Ui.Labeled("تأكيد كلمة المرور", p2));
        flow.Controls.Add(ok);
        Controls.Add(flow);
        AcceptButton = ok;
        ok.Click += (s, e) =>
        {
            var u = Db.Query("SELECT username, pass_hash FROM users WHERE id=@p0", Session.UserId).Rows[0];
            string user = Db.S(u["username"]);
            if (Session.Hash(user, old.Text) != Db.S(u["pass_hash"])) { Ui.Warn("كلمة المرور الحالية غير صحيحة."); return; }
            if (p1.Text.Length < 4) { Ui.Warn("كلمة المرور قصيرة جداً (4 أحرف على الأقل)."); return; }
            if (p1.Text != p2.Text) { Ui.Warn("التأكيد غير مطابق."); return; }
            Db.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", Session.Hash(user, p1.Text), Session.UserId);
            Ui.Info("تم تغيير كلمة المرور.");
            DialogResult = DialogResult.OK;
        };
    }
}
