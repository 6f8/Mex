using System.Data;

namespace Raseed;

public static class Theme
{
    public static readonly Color Primary = Color.FromArgb(15, 40, 71);
    public static readonly Color PrimaryHover = Color.FromArgb(30, 62, 104);
    public static readonly Color Accent = Color.FromArgb(14, 132, 214);
    public static readonly Color Success = Color.FromArgb(22, 150, 74);
    public static readonly Color Danger = Color.FromArgb(210, 45, 45);
    public static readonly Color Warning = Color.FromArgb(222, 110, 20);
    public static readonly Color Purple = Color.FromArgb(124, 58, 237);
    public static readonly Color Gray = Color.FromArgb(100, 116, 139);
    public static readonly Color Bg = Color.FromArgb(241, 245, 249);
    public static readonly Color Ink = Color.FromArgb(30, 41, 59);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);

    public static Font F(float size = 10f, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public static Button Btn(string text, Color? color = null, int width = 130)
    {
        var c = color ?? Accent;
        var b = new Button
        {
            Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = c,
            ForeColor = Color.White, Font = F(10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(4, 6, 4, 4)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(c, 0.1f);
        return b;
    }

    public static Label Title(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Height = 44, Font = F(14, FontStyle.Bold), ForeColor = Ink,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0)
    };

    public static FlowLayoutPanel Bar() => new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(4), WrapContents = true, BackColor = Color.White
    };

    public static Control Card(string title, string value, Color color)
    {
        var p = new Panel { Width = 215, Height = 104, BackColor = Color.White, Margin = new Padding(8) };
        var bar = new Panel { Dock = DockStyle.Top, Height = 5, BackColor = color };
        var v = new Label { Text = value, Dock = DockStyle.Fill, Font = F(17, FontStyle.Bold), ForeColor = color, TextAlign = ContentAlignment.MiddleCenter };
        var t = new Label { Text = title, Dock = DockStyle.Top, Height = 30, ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter };
        p.Controls.Add(v); p.Controls.Add(t); p.Controls.Add(bar);
        return p;
    }

    public static void Grid(DataGridView g, bool readOnly = true)
    {
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = Primary;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Primary;
        g.ColumnHeadersDefaultCellStyle.Font = F(10, FontStyle.Bold);
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 38;
        g.RowTemplate.Height = 32;
        g.DefaultCellStyle.Font = F(10);
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(207, 232, 252);
        g.DefaultCellStyle.SelectionForeColor = Ink;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        g.GridColor = Color.FromArgb(226, 232, 240);
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToResizeRows = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.ReadOnly = readOnly;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.DataBindingComplete += (s, e) =>
        {
            foreach (DataGridViewColumn c in g.Columns)
            {
                if (c.Name == "id") c.Visible = false;
                if (c.ValueType == typeof(double)) c.DefaultCellStyle.Format = "#,0.##";
            }
        };
    }
}

/// <summary>نموذج أساسي: عربي من اليمين لليسار، خط وألوان موحدة</summary>
public class BaseForm : Form
{
    public BaseForm()
    {
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = Theme.F();
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        StartPosition = FormStartPosition.CenterScreen;
    }
}

/// <summary>عنصر في القوائم المنسدلة</summary>
public class Opt
{
    public long Id;
    public string Name;
    public object Tag;
    public override string ToString() => Name;
}

public static class Ui
{
    public const string DtFmt = "yyyy-MM-dd HH:mm:ss", TFmt = "yyyy-MM-dd HH:mm", DFmt = "yyyy-MM-dd";
    public static string Now => DateTime.Now.ToString(DtFmt);
    public static string Today => DateTime.Now.ToString(DFmt);

    public static readonly Dictionary<string, string> TypeNames = new()
    {
        ["Sale"] = "بيع", ["Purchase"] = "شراء", ["SaleReturn"] = "إرجاع بيع",
        ["PurchaseReturn"] = "إرجاع شراء", ["Damage"] = "إتلاف"
    };
    public static string TypeName(string t) => TypeNames.TryGetValue(t ?? "", out var n) ? n : t;

    public const string TypeCaseSql = "CASE {0} WHEN 'Sale' THEN 'بيع' WHEN 'Purchase' THEN 'شراء' WHEN 'SaleReturn' THEN 'إرجاع بيع' WHEN 'PurchaseReturn' THEN 'إرجاع شراء' ELSE 'إتلاف' END";

    public static string M(double v) => (Math.Abs(v) < 0.005 ? 0 : v).ToString("#,0.##");
    public static double V(object o) => o is double d ? d : double.TryParse(Convert.ToString(o), out var x) ? x : 0;

    public static ComboBox Combo(int width = 200) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };

    public static NumericUpDown Num(int width = 140, int decimals = 0) => new()
    {
        Width = width, Maximum = 1_000_000_000_000m, Minimum = -1_000_000_000_000m,
        DecimalPlaces = decimals, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Center
    };

    public static void FillCombo(ComboBox cb, string sql, bool none = false, string noneText = "— بدون —", params object[] p)
    {
        cb.Items.Clear();
        if (none) cb.Items.Add(new Opt { Id = 0, Name = noneText });
        foreach (DataRow r in Db.Query(sql, p).Rows)
            cb.Items.Add(new Opt { Id = Db.L(r[0]), Name = Db.S(r[1]), Tag = r });
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    public static long GetId(ComboBox cb) => cb.SelectedItem is Opt o ? o.Id : 0;
    public static DataRow GetRow(ComboBox cb) => cb.SelectedItem is Opt o ? o.Tag as DataRow : null;

    public static void SelectId(ComboBox cb, long id)
    {
        for (int i = 0; i < cb.Items.Count; i++)
            if (cb.Items[i] is Opt o && o.Id == id) { cb.SelectedIndex = i; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    /// <summary>قائمة منسدلة قابلة للبحث بالكتابة</summary>
    public static void MakeSearchable(ComboBox cb)
    {
        cb.DropDownStyle = ComboBoxStyle.DropDown;
        cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        cb.AutoCompleteSource = AutoCompleteSource.ListItems;
        cb.Leave += (s, e) =>
        {
            if (cb.SelectedIndex >= 0) return;
            for (int i = 0; i < cb.Items.Count; i++)
                if (cb.Items[i].ToString() == cb.Text) { cb.SelectedIndex = i; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        };
    }

    /// <summary>عنوان صغير فوق حقل الإدخال</summary>
    public static Control Labeled(string caption, Control c)
    {
        var p = new Panel { Width = c.Width + 4, Height = c.Height + 26, Margin = new Padding(6, 2, 6, 2) };
        var l = new Label { Text = caption, Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted, AutoEllipsis = true };
        c.Dock = DockStyle.Bottom;
        p.Controls.Add(c);
        p.Controls.Add(l);
        return p;
    }

    public static DataGridView NewGrid(bool readOnly = true)
    {
        var g = new DataGridView { Dock = DockStyle.Fill };
        Theme.Grid(g, readOnly);
        return g;
    }

    public static bool Confirm(string msg) =>
        MessageBox.Show(msg, "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static void Info(string msg) => MessageBox.Show(msg, "رصيد", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static void Warn(string msg) => MessageBox.Show(msg, "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static bool AskNumber(string title, string caption, double def, out double value)
    {
        using var f = new BaseForm { Text = title, Width = 380, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var n = Num(320, 2);
        n.Value = (decimal)def;
        var ok = Theme.Btn("موافق", Theme.Success, 120);
        ok.DialogResult = DialogResult.OK;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        flow.Controls.Add(Labeled(caption, n));
        flow.Controls.Add(ok);
        f.Controls.Add(flow);
        f.AcceptButton = ok;
        var r = f.ShowDialog();
        value = (double)n.Value;
        return r == DialogResult.OK;
    }

    public static Opt Pick(string title, string caption, string sql, params object[] p)
    {
        using var f = new BaseForm { Text = title, Width = 380, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var cb = Combo(320);
        FillCombo(cb, sql, false, "", p);
        var ok = Theme.Btn("موافق", Theme.Success, 120);
        ok.DialogResult = DialogResult.OK;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        flow.Controls.Add(Labeled(caption, cb));
        flow.Controls.Add(ok);
        f.Controls.Add(flow);
        f.AcceptButton = ok;
        return f.ShowDialog() == DialogResult.OK ? cb.SelectedItem as Opt : null;
    }

    /// <summary>تحويل الرقم العراقي إلى الصيغة الدولية لواتساب</summary>
    public static string Phone(string ph)
    {
        var d = new string((ph ?? "").Where(char.IsDigit).ToArray());
        if (d.StartsWith("00")) d = d[2..];
        if (d.StartsWith("0")) d = "964" + d[1..];
        else if (d.Length == 10 && d.StartsWith("7")) d = "964" + d;
        return d;
    }

    /// <summary>إضافة زري «طباعة» و«Excel» لأي جدول</summary>
    public static void GridTools(Control bar, DataGridView grid, Func<string> title, Func<string> subtitle = null)
    {
        if (!Session.Can("print")) return;
        var bPrint = Theme.Btn("طباعة", Theme.Gray, 90);
        var bXls = Theme.Btn("Excel", Theme.Success, 80);
        bPrint.Click += (s, e) => PrintDoc.PrintGrid(grid, title(), subtitle?.Invoke());
        bXls.Click += (s, e) => Excel.ExportGrid(grid, title());
        bar.Controls.Add(bPrint);
        bar.Controls.Add(bXls);
    }

    public static string Fill(string template, params (string Key, object Value)[] vals)
    {
        var s = template ?? "";
        foreach (var (k, v) in vals) s = s.Replace("{" + k + "}", Convert.ToString(v));
        return s;
    }

    public static double Rate(string currency) => currency == "USD" ? Settings.Dbl("usd_rate", 1500) : 1;
    public static string BoxCurrency(long boxId) => Db.S(Db.Scalar("SELECT currency FROM cashboxes WHERE id=@p0", boxId));
    public static double BoxRate(long boxId) => Rate(BoxCurrency(boxId));
    public static double PartyBalance(long partyId) => Db.D(Db.Scalar("SELECT balance FROM v_party_balance WHERE id=@p0", partyId));
}
