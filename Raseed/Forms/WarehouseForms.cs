using System.Data;

namespace Raseed;

/// <summary>النقل بين المخازن: عدة مواد في عملية واحدة، مع حفظ الصلاحية والكلفة لكل وجبة وسجل بالعمليات</summary>
public class TransferForm : BaseForm
{
    readonly ComboBox cbFrom = Ui.Combo(230), cbTo = Ui.Combo(230);
    readonly TextBox txtFind = new() { Width = 420, PlaceholderText = "امسح الباركود أو اكتب اسم المادة ثم Enter" };
    readonly TextBox txtNotes = new() { Width = 300 };
    readonly DataGridView grid = Ui.NewGrid(false), history = Ui.NewGrid();
    DataTable items;

    public TransferForm()
    {
        Text = "نقل بين المخازن";
        KeyPreview = true;
        Ui.FillCombo(cbFrom, "SELECT id,name FROM warehouses ORDER BY id");
        Ui.FillCombo(cbTo, "SELECT id,name FROM warehouses ORDER BY id");
        if (cbTo.Items.Count > 1) cbTo.SelectedIndex = 1;

        var head = Theme.Bar();
        head.Controls.Add(Ui.Labeled("من المخزن", cbFrom));
        head.Controls.Add(Ui.Labeled("إلى المخزن", cbTo));
        head.Controls.Add(Ui.Labeled("ملاحظات", txtNotes));

        var find = Theme.Bar();
        find.Controls.Add(new InputBox(txtFind, 430, "scan-barcode") { Height = 44, Margin = new Padding(6, 4, 6, 4) });
        var bAdd = Theme.Btn("إضافة", Theme.Success, 100); bAdd.Height = 44; bAdd.Margin = new Padding(4);
        var bDel = Theme.Btn("حذف السطر", Theme.Danger, 110); bDel.Height = 44; bDel.Margin = new Padding(4);
        var bSave = new ModernButton { Text = "تنفيذ النقل", IconName = "arrow-left-right", Height = 44, Margin = new Padding(24, 4, 4, 4) };
        bSave.FitWidth(150);
        find.Controls.AddRange(new Control[] { bAdd, bDel, bSave });

        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "item_id", Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "المادة", ReadOnly = true, FillWeight = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "avail", HeaderText = "المتوفر في المخزن المصدر", ReadOnly = true, FillWeight = 110, DefaultCellStyle = { Format = "#,0.##", Alignment = DataGridViewContentAlignment.MiddleCenter, BackColor = Theme.SurfaceAlt } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "qty", HeaderText = "الكمية المنقولة", FillWeight = 100, DefaultCellStyle = { Format = "#,0.##", Alignment = DataGridViewContentAlignment.MiddleCenter } });

        var histCard = new CardPanel { Dock = DockStyle.Right, Width = 540, Title = "آخر عمليات النقل", Subtitle = "انقر نقرًا مزدوجًا لعرض التفاصيل", IconName = "history" };
        histCard.Controls.Add(history);

        Controls.Add(grid);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(histCard);
        Controls.Add(find);
        Controls.Add(head);

        bAdd.Click += (s, e) => FindAndAdd();
        bDel.Click += (s, e) => { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); };
        bSave.Click += (s, e) => Save();
        txtFind.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindAndAdd(); } };
        cbFrom.SelectedIndexChanged += (s, e) => RefreshAvailable();
        history.CellDoubleClick += (s, e) => ShowDetails();
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F2) { txtFind.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.F10) { Save(); e.Handled = true; }
        };

        LoadItems();
        LoadHistory();
        Shown += (s, e) => txtFind.Focus();
    }

    public override void OnPageActivated() => LoadItems();

    public override bool ConfirmClose() => grid.Rows.Count == 0 || Ui.Confirm("توجد مواد في قائمة النقل لم تُنفَّذ. إغلاق الشاشة؟");

    void LoadItems()
    {
        items = Db.Query("SELECT id, code, barcode, name FROM items WHERE active=1 AND IFNULL(item_type,'اعتيادية')<>'خدمية' ORDER BY name");
        var src = new AutoCompleteStringCollection();
        foreach (DataRow r in items.Rows) { src.Add(Db.S(r["name"])); if (Db.S(r["barcode"]) != "") src.Add(Db.S(r["barcode"])); }
        txtFind.AutoCompleteCustomSource = src;
        txtFind.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        txtFind.AutoCompleteSource = AutoCompleteSource.CustomSource;
    }

    double Available(long item) => Db.D(Db.Scalar("SELECT IFNULL(SUM(qty),0) FROM batches WHERE item_id=@p0 AND warehouse_id=@p1", item, Ui.GetId(cbFrom)));

    void RefreshAvailable()
    {
        foreach (DataGridViewRow g in grid.Rows) g.Cells["avail"].Value = Available(Db.L(g.Cells["item_id"].Value));
    }

    void FindAndAdd()
    {
        var t = txtFind.Text.Trim();
        if (t == "") return;
        var rows = items.Rows.Cast<DataRow>();
        var r = rows.FirstOrDefault(x => Db.S(x["barcode"]) == t || Db.S(x["code"]) == t || Db.S(x["name"]) == t)
             ?? rows.FirstOrDefault(x => Db.S(x["name"]).Contains(t, StringComparison.OrdinalIgnoreCase));
        if (r == null) { Ui.Warn("لم يتم العثور على المادة: " + t); return; }
        long id = Db.L(r["id"]);
        var existing = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(g => Db.L(g.Cells["item_id"].Value) == id);
        if (existing != null) existing.Cells["qty"].Value = Ui.V(existing.Cells["qty"].Value) + 1;
        else
        {
            int i = grid.Rows.Add(id, Db.S(r["name"]), Available(id), 1.0);
            grid.CurrentCell = grid.Rows[i].Cells["qty"];
        }
        txtFind.Clear();
        txtFind.Focus();
    }

    void Save()
    {
        if (!Session.Guard("stock")) return;
        grid.EndEdit();
        long from = Ui.GetId(cbFrom), to = Ui.GetId(cbTo);
        if (from == 0 || to == 0 || from == to) { Ui.Warn("اختر مخزنين مختلفين للنقل."); return; }
        var lines = grid.Rows.Cast<DataGridViewRow>().Select(g => (Item: Db.L(g.Cells["item_id"].Value), Name: Convert.ToString(g.Cells["name"].Value), Qty: Ui.V(g.Cells["qty"].Value))).ToList();
        if (lines.Count == 0) { Ui.Warn("أضف المواد المراد نقلها."); return; }
        if (lines.Any(l => l.Qty <= 0)) { Ui.Warn("توجد مادة بكمية صفر أو سالبة."); return; }
        using (var tx = new Tx())
        {
            try
            {
                long tid = tx.Insert("INSERT INTO transfers(date,from_wh,to_wh,notes,user_id) VALUES(@p0,@p1,@p2,@p3,@p4)", Ui.Now, from, to, txtNotes.Text.Trim(), Session.UserId);
                foreach (var l in lines) StockOps.Transfer(tx, tid, l.Item, from, to, l.Qty);
                tx.Commit();
            }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); return; }
        }
        Db.Audit("نقل بين المخازن", $"{cbFrom.Text} ← {cbTo.Text} — {lines.Count} مادة");
        Toast.Show($"تم نقل {lines.Count} مادة من {cbFrom.Text} إلى {cbTo.Text}");
        grid.Rows.Clear();
        txtNotes.Clear();
        LoadHistory();
        txtFind.Focus();
    }

    void LoadHistory() => history.DataSource = Db.Query(@"SELECT t.id, substr(t.date,1,16) AS [التاريخ], wf.name AS [من], wt.name AS [إلى],
        COUNT(DISTINCT l.item_id) AS [المواد], SUM(l.qty) AS [الكمية]
        FROM transfers t JOIN warehouses wf ON wf.id=t.from_wh JOIN warehouses wt ON wt.id=t.to_wh LEFT JOIN transfer_lines l ON l.transfer_id=t.id
        GROUP BY t.id ORDER BY t.id DESC LIMIT 300");

    void ShowDetails()
    {
        if (history.CurrentRow == null) return;
        long id = Db.L(history.CurrentRow.Cells["id"].Value);
        var dt = Db.Query(@"SELECT i.name, SUM(l.qty) q FROM transfer_lines l JOIN items i ON i.id=l.item_id WHERE l.transfer_id=@p0 GROUP BY l.item_id ORDER BY i.name", id);
        var notes = Db.S(Db.Scalar("SELECT notes FROM transfers WHERE id=@p0", id));
        var text = string.Join("\n", dt.Rows.Cast<DataRow>().Select(r => $"• {Db.S(r["name"])} × {Ui.M(Db.D(r["q"]))}"));
        if (notes != "") text += "\n\nملاحظات: " + notes;
        Dialogs.Message(text, $"عملية النقل رقم {id}", Tone.Info);
    }
}

/// <summary>إضافة سريعة لاسم (شركة، مخزن...) من داخل شاشة أخرى</summary>
public static class QuickAdd
{
    public static long Ask(string title, string caption, string table)
    {
        using var f = new DialogShell(title, 420, 250, "circle-plus");
        var t = new TextBox { Width = 372 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        flow.Controls.Add(Ui.Labeled(caption, t));
        f.Body.Controls.Add(flow);
        f.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        f.AddButton("إضافة", DialogResult.OK);
        f.Shown += (s, e) => t.Focus();
        if (f.ShowModal() != DialogResult.OK || t.Text.Trim() == "") return 0;
        return Db.Insert($"INSERT INTO {table}(name) VALUES(@p0)", t.Text.Trim());
    }
}
