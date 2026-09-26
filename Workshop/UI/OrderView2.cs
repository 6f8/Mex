using Raseed;
using static Raseed.Dpi;

namespace Workshop;

/// <summary>أقسام إضافية في نافذة الطلب: الخدمة، فحص الجودة، توقيت العمل، الخدوش، التقسيط، ملاحظات الموظفين</summary>
public partial class OrderView
{
    void BuildExtras(FlowLayoutPanel flow, Order o)
    {
        var x = o.X;
        var g = W.Flow();
        g.MaximumSize = new Size(920, 0);
        if (x.Source != "" || x.Area != "") g.Controls.Add(KV("مصدر الزبون / المنطقة", string.Join(" — ", new[] { x.Source, x.Area }.Where(v => v != ""))));
        if (x.Service != "shop")
            g.Controls.Add(KV(Extra.ServiceText(x.Service), string.Join(" — ", new[] { x.Address, Txt.ParseTime(x.VisitAt)?.ToString("yyyy/MM/dd HH:mm") ?? "", x.ServiceFee > 0 ? "أجرة " + Txt.Money(x.ServiceFee) : "" }.Where(v => v != "")), 890));
        if (x.Service == "ship")
            g.Controls.Add(KV("الشحن", string.Join(" — ", new[] { x.ShipCompany, x.ShipTracking != "" ? "رقم " + x.ShipTracking : "", x.ShipStatus, x.ShipFee > 0 ? "أجرة " + Txt.Money(x.ShipFee) : "" }.Where(v => v != "")), 890));
        if (x.MaxBudget is double b)
            g.Controls.Add(KV("سقف الزبون", Txt.Money(b) + (o.Price > b ? "  — ⚠ السعر تجاوزه" : ""), 440, o.Price > b ? Pal.Bad : null));
        if (x.SealNo != "") g.Controls.Add(KV("رقم ملصق الضمان", x.SealNo));
        if (x.Branch != "" && x.Branch != Branches.Current) g.Controls.Add(KV("الفرع", x.Branch));
        if (x.InvoiceNo != "") g.Controls.Add(KV("رقم الفاتورة", x.InvoiceNo));
        if (x.ExtWarranty != "") g.Controls.Add(KV("ضمان ممتد", $"{x.ExtWarranty} — {Txt.Money(x.ExtWarrantyFee)}"));
        if (x.ReferredBy != "") g.Controls.Add(KV("أحاله", x.ReferredBy + (x.ReferredPhone != "" ? " — " + x.ReferredPhone : "")));
        if (x.Kiosk) g.Controls.Add(KV("سُجّل من", x.KioskReviewed ? "شاشة الزبون (رُوجع)" : "شاشة الزبون — لم يُراجع بعد", 440, x.KioskReviewed ? null : Pal.Amber));
        if (g.Controls.Count > 0) flow.Controls.Add(g);

        // ---------- قرار الضمان ----------
        if (o.WarrantyOf != null)
        {
            flow.Controls.Add(W.Head("قرار الضمان", 900));
            var src = Calc.Find(o.WarrantyOf);
            if (src != null)
                flow.Controls.Add(Line($"الإصلاح الأصلي {src.RefNo} — {src.IssueType} — سُلّم {Txt.FmtDate(src.DateDelivered)}",
                    Calc.InWarranty(src, Txt.Cut10(o.CreatedAt ?? Txt.Today)) ? "كان ضمن الضمان عند الاستلام" : "انتهى ضمانه عند الاستلام", Calc.InWarranty(src, Txt.Cut10(o.CreatedAt ?? Txt.Today)) ? Pal.Good : Pal.Bad));
            var wr = W.Flow(false);
            if (x.WarrantyDecision != "")
                flow.Controls.Add(Line((x.WarrantyDecision == "covered" ? "✓ يشمله الضمان — " : "✕ لا يشمله الضمان — ") + x.WarrantyReason, Txt.FmtShortDate(Txt.Cut10(x.WarrantyDecidedAt)), x.WarrantyDecision == "covered" ? Pal.Good : Pal.Bad));
            var bYes = W.Btn("يشمله الضمان", "badge-check", BtnKind.Success, 130);
            var bNo = W.Btn("لا يشمله (مع السبب)", "ban", BtnKind.Danger, 160);
            bYes.Click += (_, _) => { if (Calc.Find(id) is Order cur) WarrantyDecision.Decide(cur, true); };
            bNo.Click += (_, _) => { if (Calc.Find(id) is Order cur) WarrantyDecision.Decide(cur, false); };
            wr.Controls.Add(bYes);
            wr.Controls.Add(bNo);
            if (x.WarrantyDecision != "")
            {
                var bLetter = W.Btn("طباعة خطاب القرار", "printer", BtnKind.Soft, 150);
                bLetter.Click += (_, _) => { if (Calc.Find(id) is Order cur) Printer.WarrantyLetter(cur); };
                wr.Controls.Add(bLetter);
            }
            flow.Controls.Add(wr);
        }

        if (x.Marks.Count > 0)
        {
            flow.Controls.Add(W.Head("الخدوش والكسور عند الاستلام", 900));
            var r = W.Flow(false);
            r.Controls.Add(new DamageMap { Marks = x.Marks, ReadOnly = true, Size = new Size(360, 220), Cursor = Cursors.Default, Margin = new Padding(6) });
            r.Controls.Add(W.Note(DamageMap.Describe(x.Marks), 500, 120));
            flow.Controls.Add(r);
        }

        // ---------- فحص الجودة ----------
        flow.Controls.Add(W.Head("فحص الجودة قبل التسليم", 900));
        if (QC.Done(o))
        {
            var bad = x.QC.Where(q => q.Value == "bad").Select(q => q.Key).ToList();
            flow.Controls.Add(Line(bad.Count == 0 ? "✓ كل البنود تعمل" : "✕ لا يعمل: " + string.Join("، ", bad),
                $"{x.QcBy} {Txt.FmtShortDate(Txt.Cut10(x.QcAt ?? ""))}", bad.Count == 0 ? Pal.Good : Pal.Bad));
        }
        else flow.Controls.Add(W.Note(x.QC.Count == 0 ? "لم يُفحص بعد" : "الفحص غير مكتمل", 900));
        var bQc = W.Btn(QC.Done(o) ? "إعادة الفحص" : "فحص الجودة", "shield-check", BtnKind.Soft, 130);
        bQc.Click += (_, _) =>
        {
            if (Calc.Find(id) is not Order cur) return;
            var n = cur.Clone();
            if (!QcDialog.Run(n)) return;
            n.UpdatedAt = Txt.Now;
            Store.SaveOrder(n);
            Store.NotifyChanged();
        };
        flow.Controls.Add(bQc);

        // ---------- توقيت العمل ----------
        var total = WorkTimer.Total(o);
        bool running = WorkTimer.IsRunning(o);
        flow.Controls.Add(W.Head("وقت العمل الفعلي", 900));
        flow.Controls.Add(Line(running ? $"⏱ يعمل الآن — بدأ {Txt.ParseTime(WorkTimer.Running(o).Start):HH:mm}" : x.Work.Count == 0 ? "لم يُسجَّل وقت بعد" : $"{x.Work.Count} فترة عمل",
            WorkTimer.Text(total), running ? Pal.Good : null));
        var bTimer = W.Btn(running ? "إيقاف" : "بدء العمل", running ? "square" : "clock", running ? BtnKind.Danger : BtnKind.Success, 120);
        bTimer.Click += (_, _) => { if (Calc.Find(id) is Order cur) { WorkTimer.Toggle(cur, cur.Technician); Store.NotifyChanged(); } };
        flow.Controls.Add(bTimer);

        // جهاز تركه صاحبه: يتحول إلى جهاز قطع
        if (o.Status == K.Cancelled || (o.Status == K.Ready && Txt.ParseTime(o.ReadyAt) is DateTime ra && (DateTime.Now - ra).TotalDays > 60))
        {
            var bScrap = W.Btn("تحويل إلى جهاز قطع", "boxes", BtnKind.Ghost, 160);
            bScrap.Click += (_, _) => { if (Calc.Find(id) is Order cur) ScrapDialog.FromOrder(cur); };
            flow.Controls.Add(bScrap);
        }
    }

    void BuildAfterPayments(FlowLayoutPanel flow, Order o)
    {
        var x = o.X;
        if (x.Plan.Count > 0)
        {
            flow.Controls.Add(W.Head("خطة التقسيط", 900));
            foreach (var (i, _, paid, late) in Installments.Status(o))
                flow.Controls.Add(Line($"{Txt.FmtDate(i.Date)}{(late ? "   — متأخر" : "")}", (paid ? "✓ " : "") + Txt.Money(i.Amount), paid ? Pal.Good : late ? Pal.Bad : null));
        }
        if (Credits.Balance(o) is double c && c > 0)
            flow.Controls.Add(Line("💳 رصيد الزبون عندنا", Txt.Money(c), Pal.Good));

        // ---------- ملاحظات الموظفين ----------
        flow.Controls.Add(W.Head("ملاحظات الموظفين", 900));
        foreach (var n in x.Chat)
            flow.Controls.Add(Line($"{(n.Author == "" ? "" : n.Author + ": ")}{n.Text}", $"{Txt.FmtShortDate(Txt.Cut10(n.At))} {Txt.ParseTime(n.At):HH:mm}", Theme.Muted));
        var row = W.Flow(false);
        var who = W.Combo(170, Techs.All.Select(t => t.Name).Prepend("الاستقبال"), true);
        who.Text = Store.Get("last_author", o.Technician != "" ? o.Technician : "الاستقبال");
        var text = new TextBox { Width = 520, PlaceholderText = "اكتب ملاحظة للزملاء..." };
        row.Controls.Add(W.Wrap(who, "user"));
        row.Controls.Add(W.Wrap(text));
        var bAdd = W.Btn("إضافة", "send", BtnKind.Secondary, 90);
        row.Controls.Add(bAdd);
        void Add()
        {
            var t = text.Text.Trim();
            if (t == "" || Calc.Find(id) is not Order cur) return;
            var n = cur.Clone();
            n.X.Chat.Add(new Note { At = Txt.Now, Author = who.Text.Trim(), Text = t });
            n.UpdatedAt = Txt.Now;
            Store.SaveOrder(n);
            Store.Set("last_author", who.Text.Trim());
            Store.NotifyChanged();
        }
        bAdd.Click += (_, _) => Add();
        text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); } };
        flow.Controls.Add(row);
    }
}
