namespace Raseed;

/// <summary>سجل التحديثات: ما الجديد في كل إصدار</summary>
public class ChangelogForm : BaseForm
{
    static readonly (string Version, string[] Items)[] Log =
    {
        ("7", new[]
        {
            "سند قبض وسند دفع بالدينار والدولار مع الخصم والمبلغ كتابةً ولوحة الرصيد السابق والحالي، وطباعة نسخة للعميل.",
            "سقف الذمة بالدينار والدولار مع «التنبيه» أو «منع التعامل»، وفترة التسديد وتنبيه «تأخر التسديد».",
            "الإعدادات: الشعار وصورة ترويسة القوائم، المدينة والنشاط التجاري، نسب الربح لاقتراح أسعار البيع، وأنظمة البرنامج لإخفاء ما لا يلزم.",
            "قسم الأدوات: الطابعة الافتراضية، إعدادات التقارير، النسخ الاحتياطي، سجل التحديثات، الاختصارات، الدعم، الحاسبة.",
        }),
        ("6", new[]
        {
            "قائمة البيع بالترتيب المألوف: سطر إدخال المادة، الرقم التسلسلي والملاحظة لكل مادة، الرصيد السابق والحالي، «طباعة» و«تقسيط القائمة».",
            "قائمة عرض سعر وتحويلها إلى قائمة بيع، وسند تعديل الرصيد، والوصول السريع، وتقارير القوائم، وتحليل البيانات.",
        }),
        ("5", new[]
        {
            "شاشة المواد بتبويبات: الميزان، أكثر من باركود، الملاحظة عند البيع، الضمان، وتنبيهات الحد الأدنى والأعلى وحد الأمان والركود وهدف البيع.",
            "قسم الحسابات: الزبائن، المجهزون، الصناديق، الخزائن، الكفلاء، المصاريف، الموظفون.",
        }),
        ("4", new[] { "القائمة الجانبية بأقسام تُفتح وتُطوى، والشاشات في تبويبات، وإدخال وإخراج مخزني، والنقل بين المخازن، والأرقام التسلسلية." }),
        ("3", new[] { "إصلاح تجمّد حفظ القوائم، وتصميم جديد بالكامل، وكلمات مرور محمية، وفهارس تسرّع التقارير." }),
    };

    public ChangelogForm()
    {
        Text = "سجل التحديثات";
        AutoScroll = true;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg };
        foreach (var (v, items) in Log)
        {
            var card = new CardPanel { Title = $"الإصدار {v}", IconName = "sparkles", Width = 1000, Height = 70 + items.Length * 52, Margin = new Padding(0, 0, 0, 14) };
            var list = new Label
            {
                Dock = DockStyle.Fill, Font = Theme.F(10.5f), ForeColor = Theme.Text2, BackColor = Theme.Surface,
                Text = string.Join("\n\n", items.Select(x => "•  " + x))
            };
            card.Controls.Add(list);
            flow.Controls.Add(card);
        }
        Controls.Add(flow);
        Resize += (s, e) => { foreach (Control c in flow.Controls) c.Width = Math.Max(600, ClientSize.Width - 30); };
    }
}

/// <summary>اختصارات لوحة المفاتيح</summary>
public class ShortcutsForm : BaseForm
{
    public ShortcutsForm()
    {
        Text = "الاختصارات";
        var grid = Ui.NewGrid();
        var dt = new System.Data.DataTable();
        dt.Columns.Add("الاختصار"); dt.Columns.Add("الوظيفة"); dt.Columns.Add("الشاشة");
        foreach (var r in new[]
        {
            ("Ctrl + K", "البحث السريع عن أي شاشة والانتقال إليها", "كل الشاشات"),
            ("Ctrl + W", "إغلاق التبويب الحالي", "كل الشاشات"),
            ("Ctrl + Tab", "التنقل بين التبويبات المفتوحة", "كل الشاشات"),
            ("F1", "الدعم والمساعدة", "كل الشاشات"),
            ("F5", "تحديث الشاشة الحالية", "كل الشاشات"),
            ("F2", "الانتقال إلى «المادة / الباركود»", "القوائم"),
            ("Enter", "إضافة المادة الممسوحة، أو الانتقال إلى العدد ثم السعر", "القوائم"),
            ("Delete", "حذف السطر المحدد", "القوائم"),
            ("F10", "حفظ القائمة / المادة / السند", "القوائم، المواد، السندات"),
            ("F3", "البحث في قائمة المواد", "المواد"),
            ("Enter", "فتح السند بالرقم المكتوب", "سند قبض / دفع"),
            ("Esc", "إغلاق النوافذ والحوارات", "الحوارات"),
        }) dt.Rows.Add(r.Item1, r.Item2, r.Item3);
        grid.DataSource = dt;
        var card = new CardPanel { Dock = DockStyle.Fill, Title = "اختصارات لوحة المفاتيح", Subtitle = "تسرّع العمل اليومي", IconName = "keyboard" };
        card.Controls.Add(grid);
        Controls.Add(card);
    }
}
