using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>التراجع عن آخر عملية (Ctrl+Z)</summary>
public static class UndoUi
{
    public static void Run()
    {
        var step = Undo.Last;
        if (step == null) { Toast.Show($"لا توجد عملية خلال آخر {Undo.Minutes} دقيقة للتراجع عنها", Tone.Info); return; }
        int mins = (int)(DateTime.Now - step.At).TotalMinutes;
        if (!W.Confirm("التراجع عن آخر عملية", $"{step.Label}\n{(mins < 1 ? "قبل أقل من دقيقة" : $"قبل {mins} دقيقة")}\n\nتعود البيانات كما كانت قبلها (مع المخزون وحسابات الموردين).", "تراجع")) return;
        if (Undo.Revert()) { Store.NotifyChanged(); Toast.Show("تم التراجع: " + step.Label); }
    }
}

/// <summary>قراءة IMEI من صورة ملف أو من الحافظة (لقطة شاشة)</summary>
public static class OcrUi
{
    public static void Pick(Control anchor, Action<string> done)
    {
        var m = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = Theme.F(10) };
        m.Items.Add("من صورة على الحاسوب...", null, async (_, _) =>
        {
            var f = W.OpenFile("صور|*.jpg;*.jpeg;*.png;*.bmp;*.webp");
            if (f == null) return;
            try { using var img = Image.FromFile(f); await Run(img, done); }
            catch (Exception ex) { Dialogs.Warn("تعذّرت قراءة الصورة: " + ex.Message); }
        });
        m.Items.Add("من الحافظة (لقطة شاشة أو صورة منسوخة)", null, async (_, _) =>
        {
            if (!Clipboard.ContainsImage()) { Toast.Show("لا توجد صورة في الحافظة — انسخ صورة أو خذ لقطة شاشة (Win+Shift+S)", Tone.Info); return; }
            using var img = Clipboard.GetImage();
            await Run(img, done);
        });
        m.Show(anchor, new Point(0, anchor.Height));
    }

    static async Task Run(Image img, Action<string> done)
    {
        Toast.Show("جارٍ قراءة الصورة...", Tone.Info);
        List<string> found;
        try { found = await ImeiOcr.FromImage(img); }
        catch (Exception ex) { Dialogs.Warn(ex.Message); return; }
        if (found.Count == 0) { Dialogs.Warn("لم أجد رقماً من 15 خانة في الصورة. قرّب الصورة من الملصق أو من شاشة ‎*#06#‎ وأعد المحاولة."); return; }
        string pick = found[0];
        if (found.Count > 1)
        {
            pick = Ask.Reason("اختر IMEI", "وُجد أكثر من رقم (الصحيحة أولاً):", found.Select(x => x + (Calc.ImeiValid(x) ? "  ✓" : "")), "استعمال");
            if (pick == null) return;
            pick = new string(pick.Where(char.IsDigit).ToArray());
        }
        done(pick);
        Toast.Show(Calc.ImeiValid(pick) ? "قُرئ IMEI صحيح: " + pick : "قُرئ الرقم — تحقق منه: " + pick, Calc.ImeiValid(pick) ? Tone.Success : Tone.Warning);
    }
}

/// <summary>تقرير مجمّع لعدة فروع: كل فرع يصدّر نسخة JSON، وتُجمع هنا مع بيانات هذا الفرع</summary>
public class BranchReportDialog : DialogShell
{
    readonly Seg range = new(("month", "هذا الشهر"), ("lastmonth", "الشهر الماضي"), ("year", "هذه السنة"), ("all", "الكل"));
    readonly DataGridView grid = W.Grid();
    readonly List<(string Name, List<Order> Orders, List<Expense> Exps)> branches = new();
    List<BranchReport.Row> rows = new();

    BranchReportDialog() : base("تقرير الفروع المجمّع", 1080, 620, "store", Pal.Primary)
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Surface };
        range.Margin = new Padding(4, 6, 4, 4);
        top.Controls.Add(range);
        foreach (var h in new[] { "الفرع", "استُلم", "سُلّم", "الإيراد", "تكلفة القطع", "المصاريف", "صافي الربح", "المقبوض", "الديون" }) grid.Columns.Add(h, h);
        Body.Controls.Add(grid);
        Body.Controls.Add(W.Note("كل فرع يصدّر ملف JSON من «الإعدادات ← النسخ الاحتياطي والبيانات ← تصدير JSON» ويرسله لك، ثم تضيفه هنا. الملفات تُقرأ فقط ولا تغيّر بياناتك.", 1000, 40));
        Body.Controls.Add(top);
        AddButton("إضافة ملف فرع", DialogResult.None, BtnKind.Primary, "plus").Click += (s, e) => AddFiles();
        AddButton("طباعة", DialogResult.None, BtnKind.Secondary, "printer").Click += (s, e) => Print();
        AddButton("إغلاق", DialogResult.Cancel, BtnKind.Ghost);
        branches.Add((Branches.Current == "" ? "هذا الفرع" : Branches.Current, Store.Orders, Store.Expenses));
        range.Value = "month";
        range.Changed += _ => Render();
        Render();
    }

    public static void Open() { using var d = new BranchReportDialog(); d.ShowModal(); }

    (string, string) Period()
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return range.Value switch
        {
            "month" => (Txt.Iso(first), Txt.Iso(first.AddMonths(1).AddDays(-1))),
            "lastmonth" => (Txt.Iso(first.AddMonths(-1)), Txt.Iso(first.AddDays(-1))),
            "year" => ($"{first.Year}-01-01", $"{first.Year}-12-31"),
            _ => ("0000-01-01", "9999-12-31"),
        };
    }

    void AddFiles()
    {
        using var d = new OpenFileDialog { Filter = "JSON|*.json", Multiselect = true };
        if (d.ShowDialog() != DialogResult.OK) return;
        foreach (var f in d.FileNames)
        {
            try
            {
                var p = WebBackup.Read(f);
                var name = p.Branch != "" ? p.Branch : Path.GetFileNameWithoutExtension(f);
                branches.RemoveAll(b => b.Name == name && b.Orders != Store.Orders);
                branches.Add((name, p.Orders, p.Expenses));
            }
            catch (Exception ex) { Dialogs.Warn($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        Render();
    }

    void Render()
    {
        var (a, b) = Period();
        rows = branches.Select(x => BranchReport.For(x.Name, x.Orders, x.Exps, a, b)).ToList();
        grid.Rows.Clear();
        foreach (var r in rows.Append(BranchReport.Total(rows)))
        {
            int i = grid.Rows.Add(r.Branch, r.Received, r.Delivered, Txt.Money(r.Revenue), Txt.Money(r.Parts), Txt.Money(r.Expenses), Txt.Money(r.Profit), Txt.Money(r.Cash), Txt.Money(r.Debt));
            if (r.Branch == "المجموع") grid.Rows[i].DefaultCellStyle.Font = Theme.FS(10);
        }
    }

    void Print()
    {
        var all = rows.Append(BranchReport.Total(rows));
        var body = Printer.Header("تقرير الفروع المجمّع") +
            Printer.Table(new[] { "الفرع", "استُلم", "سُلّم", "الإيراد", "تكلفة القطع", "المصاريف", "صافي الربح", "المقبوض", "الديون" },
                all.Select(r => new[] { r.Branch, r.Received.ToString(), r.Delivered.ToString(), Txt.Money(r.Revenue), Txt.Money(r.Parts), Txt.Money(r.Expenses), Txt.Money(r.Profit), Txt.Money(r.Cash), Txt.Money(r.Debt) }));
        Printer.Doc(body, "تقرير الفروع", "960px");
    }
}
