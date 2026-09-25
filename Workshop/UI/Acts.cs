using Raseed;

namespace Workshop;

/// <summary>إجراءات الطلب المشتركة بين كل الشاشات (فتح، تعديل، تغيير الحالة، حذف، واتساب)</summary>
public static class Acts
{
    public static void View(Order o)
    {
        if (o == null) return;
        using var d = new OrderView(o.Id);
        d.ShowModal();
    }

    public static void View(string id) => View(Calc.Find(id));

    public static void New(Order prefill = null)
    {
        using var f = new OrderForm(null, prefill);
        f.ShowModal();
    }

    public static void Edit(Order o)
    {
        if (o == null || Calc.Find(o.Id) == null) { Toast.Show("الطلب غير موجود — ربما حُذف.", Tone.Warning); return; }
        using var f = new OrderForm(o.Id, null);
        f.ShowModal();
    }

    /// <summary>المكان الوحيد الذي يغيّر حالة الطلب حتى تبقى التواريخ والمؤقتات متسقة</summary>
    public static void SetStatus(Order o, string status)
    {
        o = Calc.Find(o?.Id);
        if (o == null || o.Status == status || !K.Statuses.Contains(status)) return;
        var prev = o.Status;
        string reason = null;
        if (Locking.IsLocked(o))
        {
            reason = Ask.Reason("تغيير حالة طلب " + prev, $"{o.RefNo} — {o.CustomerName} — {o.Device}\nنقله من «{prev}» إلى «{status}» يغيّر حسابات يوم {Txt.FmtDate(Calc.ClosedDate(o))}. اكتب السبب:",
                new[] { "رجع الجهاز للإصلاح", "خطأ في الإدخال", "لم يُسلَّم فعلاً" }, "تغيير الحالة");
            if (reason == null) return;
        }
        var n = o.Clone();
        if (reason != null) Locking.Log(n, $"تغيير الحالة {prev} ← {status} — السبب: {reason}");
        var now = Txt.Now;
        n.Status = status; n.UpdatedAt = now; n.StatusAt = now;
        n.ReadyAt = status == K.Ready ? now : null;
        n.PaymentStatus = Json.DerivePay(Calc.ChargeOf(n), n.Paid);
        n.CancelledAt = status == K.Cancelled ? now : null;
        if (status == K.Done)
        {
            n.CompletedAt ??= now;
            if (n.DateDelivered == "") n.DateDelivered = Txt.Today;
            if (Store.ClearPasscodeOnDelivery) n.Passcode = "";
        }
        else
        {
            n.CompletedAt = null;
            if (prev == K.Done) n.DateDelivered = "";
        }
        Store.SaveOrder(n);
        Store.NotifyChanged();
        double rem = status == K.Done ? Calc.RemainingOf(n) : 0;
        if (reason != null) Notify.Alert("locked", $"🔒 تغيير حالة طلب {prev}\n{n.RefNo} — {n.CustomerName} — {n.Device}\n{prev} ← {status}\nالسبب: {reason}");
        if (rem > 0 && n.AccountId == null)
            Notify.Alert("debt", $"⚠️ سُلّم جهاز وعليه دين\n{n.RefNo} — {n.CustomerName} ({n.Phone})\n{n.Device}\nالمتبقي: {Txt.Money(rem)} من {Txt.Money(n.Price)}");
        bool overpaid = status == K.Cancelled && n.Paid > Calc.ChargeOf(n);
        if (overpaid) Dialogs.Warn($"{n.Device}: أُلغي وعليه دفعات {Txt.Money(n.Paid)}.\nافتح الطلب واكتب أجرة الفحص، أو سجّل المبلغ الذي أرجعته للزبون بزر «إرجاع مبلغ».");
        else Toast.Show(rem > 0 ? $"{n.Device}: سُلّم وعليه متبقٍ {Txt.Money(rem)}" : $"{n.Device}: {status}", rem > 0 ? Tone.Warning : Tone.Success);
    }

    public static bool Delete(Order o)
    {
        o = Calc.Find(o?.Id);
        if (o == null) return false;
        var extra = o.Paid > 0 ? $"\nعليه دفعات مسجّلة بقيمة {Txt.Money(o.Paid)} وربح {Txt.Money(Calc.ProfitOf(o))} — ستخرج من التقارير." : "";
        string reason = null;
        if (Locking.IsLocked(o))
        {
            reason = Ask.Reason($"حذف طلب {o.Status}", $"{o.RefNo} — {o.CustomerName} — {o.Device}{extra}\nاكتب سبب الحذف (يُسجَّل ويُرسل تنبيه):",
                new[] { "طلب مكرر", "أُدخل بالخطأ" }, "نقل إلى المحذوفات");
            if (reason == null) return false;
            o = o.Clone();
            Locking.Log(o, "حُذف — السبب: " + reason);
        }
        else if (!W.Confirm($"نقل {o.RefNo} إلى المحذوفات؟", $"{o.CustomerName} — {o.Device}{extra}\nيمكنك استعادته لاحقًا من المحذوفات.", "نقل إلى المحذوفات", true)) return false;
        Store.MoveToTrash(o);
        Notify.Alert("delete", $"🗑 حُذف طلب\n{o.RefNo} — {o.CustomerName} — {o.Device}\nالحالة: {o.Status} — السعر: {Txt.Money(o.Price)} — المدفوع: {Txt.Money(o.Paid)}" + (reason != null ? "\nالسبب: " + reason : ""));
        Calc.ApplyStockChange(o.Parts, null);
        Store.NotifyChanged();
        Toast.Show($"نُقل {o.RefNo} إلى المحذوفات");
        return true;
    }

    public static void Restore(string id)
    {
        var o = Store.RestoreFromTrash(id);
        if (o == null) return;
        var ranOut = Calc.ApplyStockChange(null, o.Parts);
        Store.NotifyChanged();
        Toast.Show($"استُعيد الطلب {o.RefNo}");
        if (ranOut.Count > 0) Toast.Show("نفدت من المخزون: " + string.Join("، ", ranOut), Tone.Warning);
    }

    // ---------------- واتساب ----------------
    public static string Sign() => $"\n\n{Store.ShopName}{(Store.ShopPhone != "" ? " — " + Store.ShopPhone : "")}";

    /// <summary>قوالب الرسائل (قابلة للتعديل من الإعدادات ← رسائل واتساب)</summary>
    public static List<WaDialog.Template> Templates(Order o)
    {
        var vars = Msg.VarsFor(o);
        return Msg.All.Where(t => t.Id != "debts").Select(t => new WaDialog.Template(t.Id, t.Title, Msg.Fill(Msg.Text(t.Id), vars))).ToList();
    }

    public static void WhatsApp(Order o)
    {
        if (o == null) return;
        var t = Templates(o);
        string want = o.Status == K.Ready ? "ready" : o.Status == K.Approval ? "quote" : o.Status == K.Done ? (Calc.RemainingOf(o) > 0 ? "debt" : "thanks") : o.Status == K.Check ? "received" : "progress";
        using var d = new WaDialog(o.CustomerName, o.Phone, t, Math.Max(0, t.FindIndex(x => x.Id == want)));
        d.ShowModal();
    }

    public static void DebtReminder(Calc.Customer c)
    {
        using var d = new WaDialog(c.Name, c.Phone, new() { new("debts", "تذكير", Msg.Fill(Msg.Text("debts"), Msg.VarsFor(c))) }, 0);
        d.ShowModal();
    }

    /// <summary>بعد سداد كامل المبلغ: عرض وصل الاستلام للطباعة</summary>
    public static void OfferReceipt(Order o)
    {
        if (W.Confirm("تم سداد المبلغ بالكامل", $"{o.CustomerName} — {o.Device}\nالمرجع {o.RefNo}\nالمبلغ المستلم: {Txt.Money(o.Paid)}\n\nطباعة وصل الاستلام؟", "طباعة وصل الاستلام"))
            Printer.Receipt(o);
    }
}
