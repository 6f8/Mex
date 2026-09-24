using System.Data;

namespace Raseed;

public enum FType { Text, Memo, Number, Bool, Lookup, Choice, Date, DateTime }

public class Field
{
    public string Name, Caption, Lookup;
    public FType Type = FType.Text;
    public string[] Choices;
    public bool Free;          // قائمة اختيار تقبل الكتابة الحرة
    public object Default;
    public int Width = 400;
}

public class EntityDef
{
    public string Table, Title, ListSql, Perm, Icon = "file-text";
    public string SearchWhere = "[الاسم] LIKE @p0";
    public List<Field> Fields = new();
}

/// <summary>شاشة عامة للإضافة والتعديل والحذف والبحث — تُبنى من تعريف الكيان</summary>
public class CrudForm : BaseForm
{
    readonly EntityDef def;
    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox search = new() { Width = 280, PlaceholderText = "بحث..." };
    readonly Dictionary<string, Control> inputs = new();
    readonly Label lblMode = new() { AutoSize = true, ForeColor = Theme.Accent, Font = Theme.F(10, FontStyle.Bold), Margin = new Padding(8, 14, 8, 0) };
    long currentId;

    public CrudForm(EntityDef d)
    {
        def = d;
        Text = d.Title;

        var tool = Theme.Bar();
        var sb = Ui.SearchBox(search);
        sb.Margin = new Padding(6, 5, 6, 4);
        tool.Controls.Add(sb);
        var bSave = Theme.Btn("حفظ", Theme.Success, 100);
        var bNew = Theme.Btn("جديد", Theme.Gray, 100);
        var bDel = Theme.Btn("حذف", Theme.Danger, 100);
        tool.Controls.AddRange(new Control[] { bSave, bNew, bDel });
        Ui.GridTools(tool, grid, () => def.Title);
        search.TextChanged += (s, e) => LoadList();
        bNew.Click += (s, e) => NewRecord();
        bSave.Click += (s, e) => Save();
        bDel.Click += (s, e) => Delete();

        var card = new CardPanel { Dock = DockStyle.Right, Width = 462, Title = "تفاصيل السجل", Subtitle = "سجل جديد", IconName = d.Icon };
        var editor = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 0, 0, 8)
        };
        foreach (var f in def.Fields) editor.Controls.Add(MakeInput(f));
        card.Controls.Add(editor);
        lblMode.TextChanged += (s, e) => card.Subtitle = lblMode.Text;

        var split = new Panel { Dock = DockStyle.Right, Width = 14 };
        Controls.Add(grid);
        Controls.Add(split);
        Controls.Add(card);
        Controls.Add(tool);
        Controls.Add(Theme.Title(def.Title));

        grid.CellClick += (s, e) => LoadSelected();
        grid.SelectionChanged += (s, e) => { if (grid.Focused) LoadSelected(); };

        LoadList();
        NewRecord();
    }

    Control MakeInput(Field f)
    {
        Control c;
        switch (f.Type)
        {
            case FType.Memo: c = new TextBox { Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical }; break;
            case FType.Number: c = Ui.Num(200, 2); break;
            case FType.Bool: c = new Toggle { Text = f.Caption, Height = 36 }; break;
            case FType.Lookup:
                {
                    var cb = Ui.Combo();
                    Ui.FillCombo(cb, f.Lookup, true);
                    c = cb; break;
                }
            case FType.Choice:
                {
                    var cb = Ui.Combo();
                    if (f.Free) cb.DropDownStyle = ComboBoxStyle.DropDown;
                    cb.Items.AddRange(f.Choices);
                    c = cb; break;
                }
            case FType.Date: c = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true }; break;
            case FType.DateTime: c = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm" }; break;
            default: c = new TextBox(); break;
        }
        c.Width = Math.Min(f.Width, 384);
        inputs[f.Name] = c;
        if (f.Type == FType.Bool) { c.Margin = new Padding(6, 8, 6, 4); return c; }
        return Ui.Labeled(f.Caption, c);
    }

    object GetValue(Field f)
    {
        var c = inputs[f.Name];
        switch (f.Type)
        {
            case FType.Number: return (double)((NumericUpDown)c).Value;
            case FType.Bool: return ((CheckBox)c).Checked ? 1 : 0;
            case FType.Lookup: return Db.N(Ui.GetId((ComboBox)c));
            case FType.Choice: return ((ComboBox)c).Text;
            case FType.Date:
                {
                    var d = (DateTimePicker)c;
                    return d.Checked ? (object)d.Value.ToString(Ui.DFmt) : DBNull.Value;
                }
            case FType.DateTime: return ((DateTimePicker)c).Value.ToString(Ui.TFmt);
            default: return c.Text.Trim();
        }
    }

    void SetValue(Field f, object v)
    {
        var c = inputs[f.Name];
        bool isNull = v == null || v is DBNull;
        switch (f.Type)
        {
            case FType.Number: ((NumericUpDown)c).Value = isNull ? 0 : (decimal)Db.D(v); break;
            case FType.Bool: ((CheckBox)c).Checked = !isNull && Db.L(v) == 1; break;
            case FType.Lookup: Ui.SelectId((ComboBox)c, isNull ? 0 : Db.L(v)); break;
            case FType.Choice:
                {
                    var cb = (ComboBox)c;
                    var s = Db.S(v);
                    if (cb.DropDownStyle == ComboBoxStyle.DropDown) cb.Text = s == "" && cb.Items.Count > 0 ? cb.Items[0].ToString() : s;
                    else
                    {
                        int i = cb.Items.IndexOf(s);
                        cb.SelectedIndex = i >= 0 ? i : (cb.Items.Count > 0 ? 0 : -1);
                    }
                    break;
                }
            case FType.Date:
                {
                    var d = (DateTimePicker)c;
                    if (!isNull && DateTime.TryParse(Db.S(v), out var dt)) { d.Value = dt; d.Checked = true; }
                    else { d.Value = DateTime.Today; d.Checked = false; }
                    break;
                }
            case FType.DateTime:
                {
                    var d = (DateTimePicker)c;
                    d.Value = !isNull && DateTime.TryParse(Db.S(v), out var dt) ? dt : DateTime.Now.AddMinutes(5);
                    break;
                }
            default: c.Text = Db.S(v); break;
        }
    }

    void LoadList()
    {
        var q = search.Text.Trim();
        grid.DataSource = q == ""
            ? Db.Query($"SELECT * FROM ({def.ListSql}) ORDER BY id DESC")
            : Db.Query($"SELECT * FROM ({def.ListSql}) WHERE {def.SearchWhere} ORDER BY id DESC", "%" + q + "%");
    }

    void NewRecord()
    {
        currentId = 0;
        foreach (var f in def.Fields) SetValue(f, f.Default);
        lblMode.Text = "سجل جديد";
    }

    void LoadSelected()
    {
        if (grid.CurrentRow == null) return;
        long id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var dt = Db.Query($"SELECT * FROM {def.Table} WHERE id=@p0", id);
        if (dt.Rows.Count == 0) return;
        currentId = id;
        foreach (var f in def.Fields) SetValue(f, dt.Rows[0][f.Name]);
        lblMode.Text = $"تعديل السجل رقم {id}";
    }

    void Save()
    {
        if (!Session.Guard(def.Perm)) return;
        var first = def.Fields[0];
        if (GetValue(first) is string s0 && s0 == "") { Ui.Warn($"يرجى إدخال: {first.Caption}"); inputs[first.Name].Focus(); return; }

        var cols = def.Fields.Select(f => f.Name).ToList();
        var vals = def.Fields.Select(GetValue).ToList();
        if (currentId == 0)
        {
            var sql = $"INSERT INTO {def.Table}({string.Join(",", cols)}) VALUES({string.Join(",", cols.Select((c, i) => "@p" + i))})";
            currentId = Db.Insert(sql, vals.ToArray());
        }
        else
        {
            var sql = $"UPDATE {def.Table} SET {string.Join(",", cols.Select((c, i) => $"{c}=@p{i}"))} WHERE id=@p{cols.Count}";
            vals.Add(currentId);
            Db.Exec(sql, vals.ToArray());
        }
        LoadList();
        lblMode.Text = $"تعديل السجل رقم {currentId}";
        Toast.Show("تم الحفظ بنجاح");
    }

    void Delete()
    {
        if (currentId == 0 || !Session.Guard(def.Perm) || !Session.Guard("delete")) return;
        if (!Ui.Confirm("هل تريد حذف السجل المحدد؟")) return;
        try
        {
            Db.Exec($"DELETE FROM {def.Table} WHERE id=@p0", currentId);
            LoadList();
            NewRecord();
            Toast.Show("تم الحذف");
        }
        catch { Ui.Warn("لا يمكن حذف هذا السجل لوجود حركات مرتبطة به."); }
    }
}

/// <summary>تعريفات الشاشات العامة</summary>
public static class Defs
{
    static Field F(string name, string caption, FType type = FType.Text, object def = null) =>
        new() { Name = name, Caption = caption, Type = type, Default = def };
    static Field C(string name, string caption, bool free, params string[] choices) =>
        new() { Name = name, Caption = caption, Type = FType.Choice, Choices = choices, Free = free };

    public static EntityDef Items() => new()
    {
        Table = "items", Title = "المواد", Icon = "package", Perm = "items",
        ListSql = @"SELECT i.id, i.code AS [الرمز], i.barcode AS [الباركود], i.name AS [الاسم], i.category AS [الصنف], i.unit AS [الوحدة],
            i.price_retail AS [مفرد], i.price_wholesale AS [جملة], i.price_special AS [خاص],
            IFNULL((SELECT SUM(qty) FROM batches b WHERE b.item_id=i.id),0) AS [الرصيد], i.min_qty AS [حد الطلب] FROM items i",
        SearchWhere = "[الاسم] LIKE @p0 OR [الباركود] LIKE @p0 OR [الرمز] LIKE @p0 OR [الصنف] LIKE @p0",
        Fields =
        {
            F("name", "اسم المادة"), F("code", "الرمز"), F("barcode", "الباركود"), F("category", "الصنف / المجموعة"),
            C("unit", "الوحدة", true, "قطعة", "علبة", "كارتون", "شريط", "متر", "متر مربع", "كغم", "لتر"),
            F("price_retail", "سعر المفرد", FType.Number), F("price_wholesale", "سعر الجملة", FType.Number),
            F("price_special", "السعر الخاص", FType.Number), F("min_qty", "حد الطلب (تنبيه النفاد)", FType.Number),
            F("by_measure", "تُباع بالقياس (الطول × العرض)", FType.Bool),
            F("medical_info", "البيانات الطبية (الاسم العلمي، التركيز، الجرعة، التحذيرات)", FType.Memo),
            F("alert_note", "تنبيه يظهر عند إضافة المادة للفاتورة", FType.Memo),
            F("active", "مادة فعّالة", FType.Bool, 1),
        }
    };

    public static EntityDef Companies() => new()
    {
        Table = "companies", Title = "الشركات", Icon = "building-2", Perm = "items",
        ListSql = @"SELECT c.id, c.name AS [الاسم], c.phone AS [الهاتف],
            (SELECT COUNT(*) FROM items i WHERE i.company_id=c.id) AS [عدد المواد] FROM companies c",
        Fields = { F("name", "اسم الشركة / الماركة"), F("phone", "الهاتف"), F("notes", "ملاحظات", FType.Memo) }
    };

    public static EntityDef Parties() => new()
    {
        Table = "parties", Title = "العملاء والموردون", Icon = "users", Perm = "parties",
        ListSql = @"SELECT p.id, p.name AS [الاسم], p.kind AS [النوع], p.phone AS [الهاتف], p.price_level AS [مستوى السعر],
            p.credit_limit AS [سقف الذمة], b.balance AS [الرصيد] FROM parties p JOIN v_party_balance b ON b.id=p.id",
        SearchWhere = "[الاسم] LIKE @p0 OR [الهاتف] LIKE @p0",
        Fields =
        {
            F("name", "الاسم"), C("kind", "النوع", false, "عميل", "مورد", "عميل ومورد"),
            F("phone", "الهاتف (واتساب)"), F("address", "العنوان"),
            C("price_level", "مستوى السعر الافتراضي", false, "مفرد", "جملة", "خاص"),
            F("credit_limit", "سقف الذمة (0 = بلا حد)", FType.Number),
            F("opening_balance", "رصيد افتتاحي (موجب = عليه، سالب = له)", FType.Number),
            F("notes", "ملاحظات", FType.Memo),
        }
    };

    public static EntityDef Employees() => new()
    {
        Table = "employees", Title = "الموارد البشرية — الموظفون", Icon = "id-card", Perm = "hr",
        ListSql = @"SELECT e.id, e.name AS [الاسم], e.job AS [الوظيفة], e.phone AS [الهاتف], e.salary AS [الراتب], e.hire_date AS [تاريخ التعيين],
            IFNULL((SELECT -SUM(amount*rate) FROM cash_moves m WHERE m.employee_id=e.id AND m.date LIKE strftime('%Y-%m','now','localtime')||'%'),0) AS [المصروف هذا الشهر],
            CASE e.active WHEN 1 THEN 'على الملاك' ELSE 'منفك' END AS [الحالة] FROM employees e",
        Fields =
        {
            F("name", "اسم الموظف"), F("job", "الوظيفة"), F("phone", "الهاتف"), F("salary", "الراتب الشهري", FType.Number),
            F("hire_date", "تاريخ التعيين", FType.Date), F("active", "على الملاك", FType.Bool, 1), F("notes", "ملاحظات", FType.Memo),
        }
    };

    public static EntityDef Warehouses() => new()
    {
        Table = "warehouses", Title = "المخازن", Icon = "warehouse", Perm = "settings",
        ListSql = @"SELECT w.id, w.name AS [الاسم], w.location AS [الموقع],
            IFNULL((SELECT SUM(qty*cost) FROM batches b WHERE b.warehouse_id=w.id),0) AS [قيمة المخزون] FROM warehouses w",
        Fields = { F("name", "اسم المخزن"), F("location", "الموقع"), F("notes", "ملاحظات", FType.Memo) }
    };

    public static EntityDef Cashboxes() => new()
    {
        Table = "cashboxes", Title = "الصناديق والخزائن", Icon = "wallet", Perm = "settings",
        ListSql = @"SELECT c.id, c.name AS [الاسم], c.kind AS [النوع], c.currency AS [العملة],
            IFNULL((SELECT SUM(amount) FROM cash_moves m WHERE m.cashbox_id=c.id),0) AS [الرصيد] FROM cashboxes c",
        Fields =
        {
            F("name", "الاسم"), C("kind", "النوع", false, "صندوق", "خزينة", "حساب مصرفي", "محفظة إلكترونية"),
            C("currency", "العملة", false, "IQD", "USD"), F("notes", "ملاحظات", FType.Memo),
        }
    };

    public static EntityDef CostCenters() => new()
    {
        Table = "cost_centers", Title = "مراكز الكلفة", Icon = "layers", Perm = "settings",
        ListSql = "SELECT id, name AS [الاسم], notes AS [ملاحظات] FROM cost_centers",
        Fields = { F("name", "اسم مركز الكلفة"), F("notes", "ملاحظات", FType.Memo) }
    };

    public static EntityDef Delivery() => new()
    {
        Table = "delivery_companies", Title = "شركات التوصيل", Icon = "truck", Perm = "settings",
        ListSql = "SELECT id, name AS [الاسم], phone AS [الهاتف], fee AS [أجور التوصيل] FROM delivery_companies",
        Fields = { F("name", "اسم الشركة"), F("phone", "الهاتف"), F("fee", "أجور التوصيل الافتراضية", FType.Number), F("notes", "ملاحظات", FType.Memo) }
    };

    public static EntityDef Partners() => new()
    {
        Table = "partners", Title = "الشركاء (توزيع الأرباح)", Icon = "handshake", Perm = "profit",
        ListSql = "SELECT id, name AS [الاسم], phone AS [الهاتف], share AS [النسبة %] FROM partners",
        Fields = { F("name", "اسم الشريك"), F("phone", "الهاتف"), F("share", "نسبة الربح %", FType.Number) }
    };

    public static EntityDef Tasks() => new()
    {
        Table = "tasks", Title = "مدير المهام والتنبيهات", Icon = "list-todo", Perm = "tasks",
        ListSql = @"SELECT id, title AS [الاسم], kind AS [النوع], run_at AS [موعد التنفيذ], repeat AS [التكرار],
            CASE active WHEN 1 THEN 'فعّالة' ELSE 'متوقفة' END AS [الحالة], last_run AS [آخر تنفيذ] FROM tasks",
        Fields =
        {
            F("title", "عنوان المهمة"),
            C("kind", "نوع المهمة", false, "تذكير", "نسخ احتياطي", "تذكير الأقساط", "فحص الصلاحية"),
            F("run_at", "موعد التنفيذ", FType.DateTime),
            C("repeat", "التكرار", false, "بدون", "يومي", "أسبوعي", "شهري"),
            F("message", "نص التنبيه (للتذكير)", FType.Memo),
            F("active", "مهمة فعّالة", FType.Bool, 1),
        }
    };
}
