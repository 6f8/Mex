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
        var n = o.Clone();
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
        bool overpaid = status == K.Cancelled && n.Paid > Calc.ChargeOf(n);
        if (overpaid) Dialogs.Warn($"{n.Device}: أُلغي وعليه دفعات {Txt.Money(n.Paid)}.\nافتح الطلب واكتب أجرة الفحص، أو احذف الدفعة التي أرجعتها للزبون.");
        else Toast.Show(rem > 0 ? $"{n.Device}: سُلّم وعليه متبقٍ {Txt.Money(rem)}" : $"{n.Device}: {status}", rem > 0 ? Tone.Warning : Tone.Success);
    }

    public static bool Delete(Order o)
    {
        o = Calc.Find(o?.Id);
        if (o == null) return false;
        var extra = o.Paid > 0 ? $"\nعليه دفعات مسجّلة بقيمة {Txt.Money(o.Paid)} وربح {Txt.Money(Calc.ProfitOf(o))} — ستخرج من التقارير." : "";
        if (!W.Confirm($"نقل {o.RefNo} إلى المحذوفات؟", $"{o.CustomerName} — {o.Device}{extra}\nيمكنك استعادته لاحقًا من المحذوفات.", "نقل إلى المحذوفات", true)) return false;
        Store.MoveToTrash(o);
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

    public static List<WaDialog.Template> Templates(Order o)
    {
        double rem = Calc.RemainingOf(o);
        var sign = Sign();
        var we = Calc.WarrantyEnd(o);
        string issue = o.Issue != "" ? o.Issue : o.IssueType;
        return new()
        {
            new("received", "استلام الجهاز", $"مرحباً {o.CustomerName}،\nاستلمنا جهازك ({o.Device}) للصيانة.\nالعطل: {issue}\nالرقم المرجعي: {o.RefNo}{(o.DateEstimated != "" ? $"\nالموعد المتوقع: {Txt.FmtDate(o.DateEstimated)}" : "")}\nسنبلغك فور انتهاء العمل.{sign}"),
            new("quote", "عرض السعر للموافقة", $"مرحباً {o.CustomerName}،\nفحصنا جهازك ({o.Device}).\nالعطل: {issue}\nكلفة الإصلاح: {Txt.Money(o.Price)}{(o.DateEstimated != "" ? $"\nيكون جاهزاً بتاريخ: {Txt.FmtDate(o.DateEstimated)}" : "")}{(o.CheckFee > 0 ? $"\nإذا لم ترغب بالإصلاح تكون أجرة الفحص {Txt.Money(o.CheckFee)}." : "")}\nهل نبدأ بالإصلاح؟ يرجى الرد بنعم أو لا.{sign}"),
            new("progress", "تحديث الحالة", $"مرحباً {o.CustomerName}،\nجهازك ({o.Device}) حالياً: {o.Status}.{(o.Status == K.Part ? "\nننتظر وصول القطعة المطلوبة وسنكمل العمل فور وصولها." : "\nنعمل عليه وسنبلغك عند جاهزيته.")}{sign}"),
            new("ready", "الجهاز جاهز", $"مرحباً {o.CustomerName}،\nجهازك ({o.Device}) جاهز للاستلام.\nالمبلغ الكلي: {Txt.Money(o.Price)}{(rem > 0 ? $"\nالمتبقي: {Txt.Money(rem)}" : "\nالمبلغ مسدد بالكامل")}\nبانتظارك، شكراً لثقتك.{sign}"),
            new("debt", "تذكير بالمتبقي", $"مرحباً {o.CustomerName}،\nنذكّرك بمبلغ متبقٍ قدره {Txt.Money(rem)} عن صيانة جهازك ({o.Device}) — المرجع {o.RefNo}.\nنشكر تعاونك.{sign}"),
            new("thanks", "شكر بعد التسليم", $"مرحباً {o.CustomerName}،\nشكراً لاختيارك ورشتنا لصيانة جهازك ({o.Device}).\nالضمان: {o.Warranty}{(we != "" ? $" — ساري حتى {Txt.FmtDate(we)}" : "")}.\nاحتفظ بالرقم المرجعي {o.RefNo} لأي مراجعة.\nلأي ملاحظة لا تتردد بمراسلتنا.{sign}"),
        };
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
        var lines = string.Join("\n", c.Unpaid.Select(o => $"- {o.Device} ({o.RefNo}): {Txt.Money(Calc.RemainingOf(o))}"));
        using var d = new WaDialog(c.Name, c.Phone, new() { new("debt", "تذكير", $"مرحباً {c.Name}،\nنذكّرك بالمبالغ المتبقية لدينا:\n{lines}\nالمجموع: {Txt.Money(c.Debt)}\nنشكر تعاونك.{Sign()}") }, 0);
        d.ShowModal();
    }

    /// <summary>بعد سداد كامل المبلغ: عرض وصل الاستلام للطباعة</summary>
    public static void OfferReceipt(Order o)
    {
        if (W.Confirm("تم سداد المبلغ بالكامل", $"{o.CustomerName} — {o.Device}\nالمرجع {o.RefNo}\nالمبلغ المستلم: {Txt.Money(o.Paid)}\n\nطباعة وصل الاستلام؟", "طباعة وصل الاستلام"))
            Printer.Receipt(o);
    }
}
