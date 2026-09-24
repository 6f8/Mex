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
    /// <summary>قيم ثابتة تُحفظ مع كل سجل جديد (مثل نوع الصندوق)</summary>
    public Dictionary<string, object> Fixed = new();
    /// <summary>بعد الحفظ (مثل: إلغاء «الافتراضي» عن بقية المخازن)</summary>
    public Action<long> AfterSave;
    /// <summary>زر في كل سطر من الجدول (مثل «تعديل رصيد»)</summary>
    public (string Caption, Action<long> Run)? RowAction;
}

/// <summary>
/// شاشة عامة للإضافة والتعديل والحذف والبحث — تُبنى من تعريف الكيان.
/// الحقول في جهة (العنوان بجانب الحقل)، والجدول مع البحث في الجهة الأخرى، وأزرار جديد / حفظ / حذف.
/// </summary>
public class CrudForm : BaseForm
{
    const int CaptionW = 140, InputW = 300;
    readonly EntityDef def;
    readonly DataGridView grid = Ui.NewGrid();
    readonly TextBox search = new() { Width = 300, PlaceholderText = "بحث..." };
    readonly Dictionary<string, Control> inputs = new();
    readonly CardPanel card;
    readonly ModernButton bDel;
    long currentId;

    public CrudForm(EntityDef d)
    {
        def = d;
        Text = d.Title;

        // ---------- الحقول ----------
        card = new CardPanel { Dock = DockStyle.Fill, Title = d.Title, Subtitle = "سجل جديد", IconName = d.Icon };
        var editor = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 4, 0, 8)
        };
        foreach (var f in def.Fields) editor.Controls.Add(MakeInput(f));

        // أزرار جديد / حفظ / حذف من اليمين، بعرض متساوٍ
        var actions = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Surface };
        var bNew = new ModernButton { Text = "جديد", IconName = "plus", Height = 44 };
        var bSave = new ModernButton { Text = "حفظ", IconName = "save", Height = 44 };
        bDel = new ModernButton { Text = "حذف", IconName = "trash-2", Kind = BtnKind.Coral, Height = 44 };
        var order = new[] { bNew, bSave, bDel };
        actions.Controls.AddRange(order);
        actions.Resize += (s, e) =>
        {
            int w = Math.Min(150, (actions.ClientSize.Width - 16) / 3), x = actions.ClientSize.Width;
            foreach (var b in order) { x -= w; b.SetBounds(x, 12, w - 8, 44); }
        };
        card.Controls.Add(editor);
        card.Controls.Add(actions);

        // ---------- الجدول والبحث ----------
        // الجدول بعرض ما يتبقى بعد بطاقة الحقول (البطاقة بعرض ثابت في الجهة اليمنى)
        var listCard = new CardPanel { Dock = DockStyle.Right, Title = "السجلات", IconName = "list" };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Surface, WrapContents = false };
        top.Controls.Add(new Label { Text = "البحث", AutoSize = false, Width = 60, Height = 42, Font = Theme.FS(10.5f), ForeColor = Theme.Brand, TextAlign = ContentAlignment.MiddleLeft });
        var sb = new InputBox(search, 320, "search") { Height = 42, Margin = new Padding(4, 0, 12, 0) };
        top.Controls.Add(sb);
        Ui.GridTools(top, grid, () => def.Title);
        foreach (Control c in top.Controls) if (c is ModernButton mb) { mb.Height = 42; mb.Margin = new Padding(4, 0, 4, 0); }
        top.Resize += (s, e) =>
        {
            int tools = top.Controls.OfType<ModernButton>().Sum(b => b.Width + 8);
            sb.Width = Math.Max(200, top.ClientSize.Width - 80 - tools - 16);
        };
        listCard.Controls.Add(grid);
        listCard.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Surface });
        listCard.Controls.Add(top);

        Controls.Add(card);
        Controls.Add(new Panel { Dock = DockStyle.Right, Width = 14 });
        Controls.Add(listCard);
        Resize += (s, e) => listCard.Width = Math.Max(420, ClientSize.Width - (CaptionW + InputW + 80) - 14);

        search.TextChanged += (s, e) => LoadList();
        bNew.Click += (s, e) => NewRecord();
        bSave.Click += (s, e) => Save();
        bDel.Click += (s, e) => Delete();
        grid.CellClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "__action" && def.RowAction is { } act)
            {
                long rid = Db.L(grid.Rows[e.RowIndex].Cells["id"].Value);
                act.Run(rid);
                LoadList();
                return;
            }
            LoadSelected();
        };
        grid.SelectionChanged += (s, e) => { if (grid.Focused) LoadSelected(); };
        grid.DataBindingComplete += (s, e) => AddActionColumn();

        LoadList();
        NewRecord();
    }

    void AddActionColumn()
    {
        if (def.RowAction is not { } act || grid.Columns.Contains("__action")) return;
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "__action", HeaderText = act.Caption, Text = act.Caption, UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat, FillWeight = 60, MinimumWidth = 96,
            DefaultCellStyle = { BackColor = Theme.BrandSoft, ForeColor = Theme.BrandDark, SelectionBackColor = Theme.BrandSoft2, SelectionForeColor = Theme.BrandDark }
        });
    }

    static Label Caption(string text) => new()
    {
        Text = text, AutoSize = false, Width = CaptionW, Height = 40, Font = Theme.FS(10), ForeColor = Theme.Ink,
        TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 4, 0)
    };

    Control MakeInput(Field f)
    {
        Control c;
        switch (f.Type)
        {
            case FType.Memo: c = new TextBox { Multiline = true, Height = 76, ScrollBars = ScrollBars.Vertical }; break;
            case FType.Number: c = Ui.Num(200, 2); break;
            case FType.Bool: c = new Toggle { Text = f.Caption, Height = 38 }; break;
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
        c.Width = f.Type == FType.Bool ? CaptionW + InputW : InputW;
        inputs[f.Name] = c;
        if (f.Type == FType.Bool) { c.Margin = new Padding(8, 6, 6, 4); return c; }
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(Caption(f.Caption));
        var field = Ui.Wrap(c);
        field.Margin = new Padding(4, 0, 4, 0);
        row.Controls.Add(field);
        return row;
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
        long keep = currentId;
        grid.DataSource = q == ""
            ? Db.Query($"SELECT * FROM ({def.ListSql}) ORDER BY id DESC")
            : Db.Query($"SELECT * FROM ({def.ListSql}) WHERE {def.SearchWhere} ORDER BY id DESC", "%" + q + "%");
        card.Subtitle = currentId > 0 ? $"تعديل السجل رقم {currentId}" : "سجل جديد";
        if (keep > 0)
            foreach (DataGridViewRow r in grid.Rows)
                if (Db.L(r.Cells["id"].Value) == keep) { r.Selected = true; break; }
    }

    void NewRecord()
    {
        currentId = 0;
        foreach (var f in def.Fields) SetValue(f, f.Default);
        card.Subtitle = "سجل جديد";
        bDel.Enabled = false;
        inputs[def.Fields[0].Name].Focus();
    }

    void LoadSelected()
    {
        if (grid.CurrentRow == null) return;
        long id = Db.L(grid.CurrentRow.Cells["id"].Value);
        var dt = Db.Query($"SELECT * FROM {def.Table} WHERE id=@p0", id);
        if (dt.Rows.Count == 0) return;
        currentId = id;
        foreach (var f in def.Fields) SetValue(f, dt.Rows[0][f.Name]);
        card.Subtitle = $"تعديل السجل رقم {id}";
        bDel.Enabled = true;
    }

    void Save()
    {
        if (!Session.Guard(def.Perm)) return;
        var first = def.Fields[0];
        var required = def.Fields.FirstOrDefault(f => f.Type == FType.Text && f.Caption.StartsWith("*")) ?? first;
        if (GetValue(required) is string s0 && s0 == "") { Ui.Warn($"يرجى إدخال: {required.Caption.TrimStart('*', ' ')}"); inputs[required.Name].Focus(); return; }

        var cols = def.Fields.Select(f => f.Name).ToList();
        var vals = def.Fields.Select(GetValue).ToList();
        if (currentId == 0)
        {
            foreach (var (k, v) in def.Fixed) { cols.Add(k); vals.Add(v); }
            var sql = $"INSERT INTO {def.Table}({string.Join(",", cols)}) VALUES({string.Join(",", cols.Select((c, i) => "@p" + i))})";
            currentId = Db.Insert(sql, vals.ToArray());
        }
        else
        {
            var sql = $"UPDATE {def.Table} SET {string.Join(",", cols.Select((c, i) => $"{c}=@p{i}"))} WHERE id=@p{cols.Count}";
            vals.Add(currentId);
            Db.Exec(sql, vals.ToArray());
        }
        def.AfterSave?.Invoke(currentId);
        bDel.Enabled = true;
        LoadList();
        Toast.Show("تم الحفظ بنجاح");
    }

    void Delete()
    {
        if (currentId == 0 || !Session.Guard(def.Perm) || !Session.Guard("delete")) return;
        if (!Ui.Confirm("هل تريد حذف السجل المحدد؟")) return;
        try
        {
            Db.Exec($"DELETE FROM {def.Table} WHERE id=@p0", currentId);
            def.AfterSave?.Invoke(0);
            currentId = 0;
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
    static Field With(this Field f, object def) { f.Default = def; return f; }

    public static EntityDef Companies() => new()
    {
        Table = "companies", Title = "الشركات", Icon = "building-2", Perm = "items",
        ListSql = @"SELECT c.id, c.name AS [الاسم], c.phone AS [الهاتف],
            (SELECT COUNT(*) FROM items i WHERE i.company_id=c.id) AS [عدد المواد] FROM companies c",
        Fields = { F("name", "اسم الشركة / الماركة"), F("phone", "الهاتف"), F("notes", "ملاحظات", FType.Memo) }
    };

    /// <summary>حساب الزبائن / حساب المجهزين: نوع الحساب، الاسم، نوع السعر، العنوان والهاتف والبريد والمدينة، وزر «تعديل رصيد»</summary>
    public static EntityDef Accounts(bool suppliers) => new()
    {
        Table = "parties", Title = suppliers ? "حساب المجهزين" : "حساب الزبائن", Icon = suppliers ? "truck" : "users", Perm = "parties",
        ListSql = $@"SELECT p.id, p.name AS [الاسم], p.address AS [العنوان], p.phone AS [رقم الهاتف], p.email AS [البريد الإلكتروني],
            p.city AS [المدينة], p.notes AS [الملاحظات], b.balance AS [الرصيد]
            FROM parties p JOIN v_party_balance b ON b.id=p.id WHERE p.kind IN ('{(suppliers ? "مورد" : "عميل")}','عميل ومورد')",
        SearchWhere = "[الاسم] LIKE @p0 OR [رقم الهاتف] LIKE @p0 OR [المدينة] LIKE @p0",
        Fields =
        {
            C("kind", "نوع الحساب", false, "عميل", "مورد", "عميل ومورد").With(suppliers ? "مورد" : "عميل"),
            F("name", "* اسم الحساب"),
            C("price_level", "نوع السعر", false, "مفرد", "جملة", "خاص"),
            F("address", "العنوان"), F("phone", "رقم الهاتف"), F("email", "البريد الإلكتروني"), F("city", "المدينة"),
            F("credit_limit", "سقف الذمة", FType.Number),
            F("notes", "الملاحظات", FType.Memo),
        },
        RowAction = ("تعديل رصيد", AdjustBalance),
    };

    /// <summary>تعديل رصيد الحساب إلى قيمة جديدة (موجب = عليه لنا، سالب = له علينا)</summary>
    static void AdjustBalance(long partyId)
    {
        if (!Session.Guard("parties")) return;
        double cur = Ui.PartyBalance(partyId);
        var name = Db.S(Db.Scalar("SELECT name FROM parties WHERE id=@p0", partyId));
        if (!Ui.AskNumber("تعديل رصيد", $"الرصيد الجديد لـ «{name}»", cur, out var target,
                $"الرصيد الحالي {Ui.M(cur)} — موجب = عليه لنا، سالب = له علينا")) return;
        double diff = Ledger.AdjustTo(partyId, target);
        if (Math.Abs(diff) > 0.001) Toast.Show($"تم تعديل رصيد «{name}» إلى {Ui.M(target)}");
    }

    public static EntityDef Guarantors() => new()
    {
        Table = "guarantors", Title = "حساب الكفلاء", Icon = "shield-check", Perm = "installments",
        ListSql = @"SELECT g.id, g.name AS [الاسم], g.phone AS [رقم الهاتف], g.address AS [العنوان], g.work AS [جهة العمل],
            (SELECT COUNT(*) FROM invoices v WHERE v.guarantor_id=g.id) AS [الفواتير المكفولة],
            IFNULL((SELECT SUM(t.amount-t.paid) FROM installments t JOIN invoices v ON v.id=t.invoice_id WHERE v.guarantor_id=g.id),0) AS [المتبقي من الأقساط]
            FROM guarantors g",
        SearchWhere = "[الاسم] LIKE @p0 OR [رقم الهاتف] LIKE @p0",
        Fields =
        {
            F("name", "* اسم الكفيل"), F("phone", "رقم الهاتف"), F("address", "العنوان"), F("id_number", "رقم الهوية"),
            F("work", "جهة العمل"), F("notes", "الملاحظات", FType.Memo),
        }
    };

    public static EntityDef ExpenseTypes() => new()
    {
        Table = "expense_types", Title = "حساب المصاريف", Icon = "receipt", Perm = "vouchers",
        ListSql = @"SELECT t.id, t.name AS [الاسم],
            IFNULL((SELECT -SUM(m.amount*m.rate) FROM cash_moves m WHERE m.expense_type_id=t.id AND m.date>=strftime('%Y-%m-01','now','localtime')),0) AS [مصروف هذا الشهر],
            IFNULL((SELECT -SUM(m.amount*m.rate) FROM cash_moves m WHERE m.expense_type_id=t.id),0) AS [الإجمالي], t.notes AS [الملاحظات]
            FROM expense_types t",
        Fields = { F("name", "* نوع المصروف"), F("notes", "الملاحظات", FType.Memo) }
    };

    public static EntityDef Employees() => new()
    {
        Table = "employees", Title = "حساب الموظفين", Icon = "id-card", Perm = "hr",
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
        ListSql = @"SELECT w.id, w.name AS [اسم المخزن], CASE w.is_default WHEN 1 THEN 'نعم' ELSE '' END AS [افتراضي], w.location AS [الموقع],
            IFNULL((SELECT SUM(qty*cost) FROM batches b WHERE b.warehouse_id=w.id),0) AS [قيمة المخزون] FROM warehouses w",
        SearchWhere = "[اسم المخزن] LIKE @p0",
        Fields = { F("name", "* اسم المخزن"), F("is_default", "المخزن الافتراضي (يُختار تلقائيًا في الفواتير)", FType.Bool), F("location", "الموقع"), F("notes", "ملاحظات", FType.Memo) },
        AfterSave = id =>
        {
            // مخزن افتراضي واحد فقط، ودائمًا يوجد واحد
            if (id > 0 && Db.L(Db.Scalar("SELECT is_default FROM warehouses WHERE id=@p0", id)) == 1)
                Db.Exec("UPDATE warehouses SET is_default=0 WHERE id<>@p0", id);
            Db.Exec("UPDATE warehouses SET is_default=1 WHERE id=(SELECT MIN(id) FROM warehouses) AND NOT EXISTS(SELECT 1 FROM warehouses WHERE is_default=1)");
        },
    };

    /// <summary>حساب الصناديق (نقد المحل) أو حساب الخزائن (خزائن وحسابات مصرفية ومحافظ)</summary>
    public static EntityDef Cashboxes(bool safes) => new()
    {
        Table = "cashboxes", Title = safes ? "حساب الخزائن" : "حساب الصناديق", Icon = safes ? "landmark" : "wallet", Perm = "settings",
        ListSql = $@"SELECT c.id, c.name AS [الاسم], c.kind AS [النوع], c.currency AS [العملة],
            IFNULL((SELECT SUM(amount) FROM cash_moves m WHERE m.cashbox_id=c.id),0) AS [الرصيد],
            IFNULL((SELECT SUM(amount) FROM cash_moves m WHERE m.cashbox_id=c.id AND m.date LIKE strftime('%Y-%m-%d','now','localtime')||'%'),0) AS [حركة اليوم]
            FROM cashboxes c WHERE {(safes ? "c.kind<>'صندوق'" : "c.kind='صندوق'")}",
        Fields = safes
            ? new() { F("name", "* الاسم"), C("kind", "النوع", false, "خزينة", "حساب مصرفي", "محفظة إلكترونية"), C("currency", "العملة", false, "IQD", "USD"), F("notes", "ملاحظات", FType.Memo) }
            : new() { F("name", "* اسم الصندوق"), C("currency", "العملة", false, "IQD", "USD"), F("notes", "ملاحظات", FType.Memo) },
        Fixed = safes ? new() : new() { ["kind"] = "صندوق" },
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
