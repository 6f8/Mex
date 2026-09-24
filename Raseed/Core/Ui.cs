using System.Data;

namespace Raseed;

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
        AutoScaleMode = AutoScaleMode.None;
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

    public static ComboBox Combo(int width = 200) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = Theme.F(10) };

    public static NumericUpDown Num(int width = 140, int decimals = 0) => new()
    {
        Width = width, Maximum = 1_000_000_000_000m, Minimum = -1_000_000_000_000m,
        DecimalPlaces = decimals, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Center, Font = Theme.F(10)
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

    /// <summary>يلف حقول الإدخال بإطار حديث (النصوص، الأرقام، القوائم، التواريخ)</summary>
    public static Control Wrap(Control c, string icon = null) =>
        c is TextBox or NumericUpDown or ComboBox or DateTimePicker ? new InputBox(c, c.Width, icon) : c;

    /// <summary>حقل بعنوان صغير فوقه</summary>
    public static Control Labeled(string caption, Control c)
    {
        var field = Wrap(c, c is TextBox { PlaceholderText.Length: > 0 } t && t.PlaceholderText.StartsWith("بحث") ? "search" : null);
        // بلا لون خلفية صريح: يرث لون الحاوية (بطاقة بيضاء غالبًا)
        var p = new Panel { Width = field.Width + 4, Height = field.Height + 25, Margin = new Padding(6, 2, 6, 4) };
        var l = new Label
        {
            Text = caption, Dock = DockStyle.Top, Height = 25, ForeColor = Theme.Text2, Font = Theme.F(9), AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 2, 0)
        };
        field.Dock = DockStyle.Bottom;
        p.Controls.Add(field);
        p.Controls.Add(l);
        return p;
    }

    /// <summary>مربع بحث بأيقونة عدسة</summary>
    public static Control SearchBox(TextBox t, int width = 0)
    {
        if (width > 0) t.Width = width;
        return new InputBox(t, t.Width, "search") { Margin = new Padding(6, 27, 6, 4) };
    }

    public static DataGridView NewGrid(bool readOnly = true)
    {
        var g = new DataGridView { Dock = DockStyle.Fill };
        Theme.Grid(g, readOnly);
        return g;
    }

    public static bool Confirm(string msg) =>
        Dialogs.Confirm(msg, "تأكيد", msg.Contains("حذف") ? "نعم، احذف" : "نعم، متابعة", danger: msg.Contains("حذف") || msg.Contains("استبدال"));

    /// <summary>الرسائل القصيرة تظهر كإشعار عابر، والطويلة كنافذة</summary>
    public static void Info(string msg)
    {
        if (msg.Length <= 90 && !msg.Contains('\n')) Toast.Show(msg);
        else Dialogs.Info(msg);
    }

    public static void Warn(string msg) => Dialogs.Warn(msg);

    public static bool AskNumber(string title, string caption, double def, out double value)
    {
        using var f = new DialogShell(title, 420, 250, "calculator");
        var n = Num(372, 2);
        n.Value = (decimal)def;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        flow.Controls.Add(Labeled(caption, n));
        f.Body.Controls.Add(flow);
        f.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        f.AddButton("موافق", DialogResult.OK);
        f.Shown += (s, e) => { n.Focus(); n.Select(0, n.Text.Length); };
        var r = f.ShowModal();
        value = (double)n.Value;
        return r == DialogResult.OK;
    }

    public static Opt Pick(string title, string caption, string sql, params object[] p)
    {
        using var f = new DialogShell(title, 420, 250, "list");
        var cb = Combo(372);
        FillCombo(cb, sql, false, "", p);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0) };
        flow.Controls.Add(Labeled(caption, cb));
        f.Body.Controls.Add(flow);
        f.AddButton("إلغاء", DialogResult.Cancel, BtnKind.Secondary);
        f.AddButton("موافق", DialogResult.OK);
        return f.ShowModal() == DialogResult.OK ? cb.SelectedItem as Opt : null;
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
        var bXls = Theme.Btn("Excel", Theme.Gray, 80);
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
