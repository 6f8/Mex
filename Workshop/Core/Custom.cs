using System.Text.Json.Nodes;

namespace Workshop;

/// <summary>
/// القوائم التي يعدّلها صاحب المحل من الإعدادات (أنواع الأعطال، الملحقات، الضمان، بنود الفحص، التصنيفات، طرق الدفع).
/// تُحفظ في جدول الإعدادات، والقيم المحفوظة في الطلبات القديمة لا تتغير عند حذف عنصر من القائمة.
/// </summary>
public static class Lists
{
    public record Def(string Key, string Title, string Note, string[] Defaults);

    public const string Cash = "نقد";
    /// <summary>طريقة خاصة: استعمال رصيد الزبون (لا تدخل الصندوق)</summary>
    public const string Credit = "رصيد الزبون";
    public static readonly (string Key, string Title)[] DefChecks =
    {
        ("screen", "الشاشة واللمس"), ("faceid", "Face ID / البصمة"), ("camF", "الكاميرا الأمامية"), ("camB", "الكاميرا الخلفية"),
        ("charge", "الشحن"), ("speaker", "السماعة"), ("mic", "المايك"), ("network", "الشبكة والاتصال"),
        ("wifi", "واي فاي / بلوتوث"), ("buttons", "الأزرار"), ("vibrate", "الاهتزاز"), ("sensors", "الحساسات"),
    };

    public static readonly Def[] All =
    {
        new("issue_types", "أنواع الأعطال", "تظهر في الطلب وفلاتر الطلبات والتقارير.",
            new[] { "شاشة", "بطارية", "منفذ شحن", "كاميرا", "صوت / سماعة", "مياه / رطوبة", "برمجيات", "أخرى" }),
        new("accessories", "الملحقات المستلمة مع الجهاز", "تظهر كأزرار تشغيل في نموذج الطلب.",
            new[] { "شاحن", "كفر / جراب", "شريحة SIM", "بطاقة ذاكرة", "بدون ملحقات" }),
        new("warranties", "مدد الضمان", "اكتب المدة بالأرقام ليُحسب تاريخ انتهاء الضمان: «10 أيام»، «2 أسبوع»، «شهر واحد»، «3 أشهر»، «سنة». الأول في القائمة هو الافتراضي للطلب الجديد.",
            new[] { "بدون ضمان", "7 أيام", "15 يوماً", "شهر واحد", "3 أشهر", "6 أشهر" }),
        new("checks", "بنود فحص الجهاز عند الاستلام", "ما يُفحص قبل فتح الجهاز (يعمل / لا يعمل).",
            DefChecks.Select(c => c.Title).ToArray()),
        new("inv_cats", "تصنيفات قطع الغيار", "تُستعمل في شاشة قطع الغيار والأسعار.",
            new[] { "شاشة", "بطارية", "منفذ شحن", "كاميرا", "سماعة", "ظهر / كفر", "أخرى" }),
        new("pay_methods", "طرق الدفع", "«نقد» ثابت في أول القائمة لأنه ما يُحسب في صندوق اليوم.",
            new[] { Cash, "زين كاش", "FastPay", "بطاقة كي", "تحويل مصرفي" }),
        new("statuses", "حالات إضافية للطلب", "حالات مفتوحة تُضاف قبل «جاهز للاستلام» — مثل «معلّق بطلب الزبون» أو «مُرسل لورشة أخرى». الحالات الأساسية ثابتة.",
            Array.Empty<string>()),
        new("qc_checks", "فحص الجودة قبل التسليم", "ما يفحصه الفني بعد الإصلاح قبل أن يصبح الجهاز «جاهز للاستلام».",
            new[] { "الشاشة واللمس", "الشحن", "الكاميرات", "السماعة والمايك", "الشبكة والاتصال", "الأزرار", "Face ID / البصمة", "لا توجد حرارة زائدة" }),
        new("sources", "كيف عرف الزبون بالمحل", "يظهر في الطلب الجديد، ويبيّن في التقارير أين يفيد إعلانك.",
            new[] { "زبون سابق", "صديق أو قريب", "إنستغرام", "فيسبوك", "تيك توك", "مرّ من أمام المحل", "خرائط Google", "أخرى" }),
    };

    static readonly Dictionary<string, (string Raw, string[] Items)> cache = new();

    public static Def Find(string key) => All.First(d => d.Key == key);

    public static string[] Get(string key)
    {
        var raw = Store.Get("list_" + key);
        if (cache.TryGetValue(key, out var c) && c.Raw == raw) return c.Items;
        string[] items = null;
        if (raw != "")
            try { items = (JsonNode.Parse(raw) as JsonArray)?.Select(x => Txt.Str(x?.ToString())).Where(x => x != "").Distinct().ToArray(); }
            catch { items = null; }
        if (items == null || items.Length == 0) items = Find(key).Defaults;
        if (key == "pay_methods") items = new[] { Cash }.Concat(items.Where(x => x != Cash && x != Credit)).ToArray();
        cache[key] = (raw, items);
        return items;
    }

    public static void Set(string key, IEnumerable<string> items)
    {
        var list = items.Select(Txt.Str).Where(x => x != "").Distinct().ToList();
        if (key == "pay_methods") { list.Remove(Cash); list.Insert(0, Cash); }
        var def = Find(key).Defaults;
        Store.Set("list_" + key, list.Count == 0 || list.SequenceEqual(def) ? "" : new JsonArray(list.Select(x => (JsonNode)x).ToArray()).ToJsonString());
    }

    /// <summary>بنود الفحص: البنود الأصلية تحتفظ بمفاتيحها القديمة، والبند الجديد مفتاحه اسمه</summary>
    public static (string Key, string Title)[] Checks() =>
        Get("checks").Select(t => (DefChecks.FirstOrDefault(d => d.Title == t).Key ?? t, t)).ToArray();

    /// <summary>نتيجة فحص الطلب بترتيب القائمة الحالية، ثم أي بند قديم لم يعد فيها</summary>
    public static List<(string Title, string State)> OrderChecks(Order o)
    {
        var order = Checks().Select(c => c.Key).ToList();
        return o.Checks.OrderBy(kv => order.IndexOf(kv.Key) is var i && i >= 0 ? i : int.MaxValue).Select(kv => (CheckTitle(kv.Key), kv.Value)).ToList();
    }

    /// <summary>اسم بند الفحص حتى لو حُذف من القائمة (للطلبات القديمة)</summary>
    public static string CheckTitle(string key) =>
        Checks().FirstOrDefault(c => c.Key == key).Title ?? DefChecks.FirstOrDefault(c => c.Key == key).Title ?? key;
}

// ============================== الفنيون ==============================
public class Tech
{
    public string Name = "";
    /// <summary>profit: نسبة من ربح الطلب، price: نسبة من سعره، fixed: مبلغ ثابت لكل جهاز مُسلَّم</summary>
    public string Basis = "profit";
    public double Value;
}

public static class Techs
{
    public static readonly (string Key, string Title)[] Bases = { ("profit", "نسبة من الربح"), ("price", "نسبة من السعر"), ("fixed", "مبلغ ثابت لكل جهاز") };
    public const string NoTech = "بدون فني";

    static string raw;
    static List<Tech> cached = new();

    public static List<Tech> All
    {
        get
        {
            var r = Store.Get("technicians");
            if (r == raw) return cached;
            var list = new List<Tech>();
            try
            {
                if (r != "" && JsonNode.Parse(r) is JsonArray a)
                    foreach (var n in a.OfType<JsonObject>())
                    {
                        var name = Txt.Str(n["name"]?.ToString());
                        if (name == "" || list.Any(t => Txt.Fold(t.Name) == Txt.Fold(name))) continue;
                        var basis = n["basis"]?.ToString();
                        list.Add(new Tech { Name = name, Basis = Bases.Any(b => b.Key == basis) ? basis : "profit", Value = Math.Max(0, Txt.ParseMoney(n["value"]?.ToString())) });
                    }
            }
            catch { }
            raw = r;
            cached = list;
            return list;
        }
    }

    public static void Save(IEnumerable<Tech> techs) =>
        Store.Set("technicians", new JsonArray(techs.Where(t => Txt.Str(t.Name) != "").Select(t => (JsonNode)new JsonObject
        { ["name"] = Txt.Str(t.Name), ["basis"] = t.Basis, ["value"] = t.Value }).ToArray()).ToJsonString());

    public static Tech Find(string name) => string.IsNullOrWhiteSpace(name) ? null : All.FirstOrDefault(t => Txt.Fold(t.Name) == Txt.Fold(name));

    public static string BasisText(Tech t) => t.Basis switch
    {
        "price" => $"{Txt.Num(t.Value)}% من السعر",
        "fixed" => $"{Txt.Money(t.Value)} لكل جهاز",
        _ => $"{Txt.Num(t.Value)}% من الربح",
    };

    /// <summary>العمولة على طلب مُسلَّم بسعر (طلبات الضمان المجانية بلا عمولة)</summary>
    public static double Commission(Order o)
    {
        if (o.Status != K.Done || o.Price <= 0) return 0;
        var t = Find(o.Technician);
        if (t == null) return 0;
        return t.Basis switch
        {
            "price" => o.Price * t.Value / 100,
            "fixed" => t.Value,
            _ => Math.Max(0, Calc.ProfitOf(o)) * t.Value / 100,
        };
    }

    public class Row
    {
        public string Name;
        public int Received, Delivered, Returns;
        public double Revenue, Parts, Profit, Commission;
        public TimeSpan? AvgTime;
        public List<Order> Orders = new();
        public double ReturnRate => Delivered > 0 ? Returns * 100.0 / Delivered : 0;
    }

    /// <summary>أداء الفنيين في الفترة: المُسلَّم بيوم تسليمه، والمرتجع بالضمان يُحسب على فني الطلب الأصلي</summary>
    public static List<Row> Report(string a, string b)
    {
        var rows = new Dictionary<string, Row>();
        Row R(string name)
        {
            var n = Txt.Str(name);
            var k = n == "" ? "" : Txt.Fold(n);
            if (!rows.TryGetValue(k, out var r)) rows[k] = r = new Row { Name = n == "" ? NoTech : Find(n)?.Name ?? n };
            return r;
        }
        foreach (var t in All) R(t.Name);
        foreach (var o in Store.Orders)
        {
            if (Calc.InRange(o.DateReceived, a, b)) R(o.Technician).Received++;
            if (o.Status == K.Done && Calc.InRange(Calc.ClosedDate(o), a, b))
            {
                var r = R(o.Technician);
                r.Delivered++;
                r.Revenue += o.Price;
                r.Parts += Calc.PartsCost(o);
                r.Profit += Calc.ProfitOf(o);
                r.Commission += Commission(o);
                r.Orders.Add(o);
            }
            if (o.WarrantyOf != null && Calc.InRange(o.DateReceived, a, b) && Calc.Find(o.WarrantyOf) is Order src) R(src.Technician).Returns++;
        }
        foreach (var r in rows.Values)
        {
            var times = r.Orders.Select(Calc.Duration).Where(t => t != null && t.Value.TotalMinutes > 0).Select(t => t.Value.TotalMinutes).ToList();
            if (times.Count > 0) r.AvgTime = TimeSpan.FromMinutes(times.Average());
        }
        // «بدون فني» يظهر فقط إذا كان فيه عمل فعلي
        return rows.Values.Where(r => r.Name != NoTech || r.Received + r.Delivered + r.Returns > 0)
            .OrderByDescending(r => r.Delivered).ThenBy(r => r.Name == NoTech).ToList();
    }
}

// ============================== القطع المعيبة ==============================
/// <summary>قطعة معيبة: تعطلت بعد تركيبها (رجع الجهاز بالضمان) أو وصلت معيبة من المورد</summary>
public class Defect
{
    public string Id, Date, PartName = "", Supplier = "", InventoryItemId, OrderId, RefNo = "", Device = "", Technician = "", Note = "";
    public double Cost;
    /// <summary>pending: عندك بانتظار الإرجاع، done: أُغلقت</summary>
    public string Status = "pending";
    /// <summary>credit: خُصمت من حساب المورد، replaced: استبدلها المورد، rejected: رفضها (خسارة)</summary>
    public string Resolution = "";
    public string ResolvedAt, StxId;
    public double Credited;
    /// <summary>نهاية ضمان المورد على القطعة (يُحسب عند التسجيل)</summary>
    public string SupWarrantyEnd = "";
}

public static class Defects
{
    public static readonly (string Key, string Title)[] Resolutions =
        { ("credit", "خصم قيمتها من حساب المورد"), ("replaced", "استبدلها المورد بقطعة سليمة"), ("rejected", "رفض المورد إرجاعها (خسارة)") };

    public static string StateText(Defect d) => d.Status == "pending" ? "بانتظار الإرجاع" : d.Resolution switch
    {
        "credit" => "خُصمت من الحساب",
        "replaced" => "استبدلها المورد",
        "rejected" => "رفضها المورد",
        _ => "مغلقة",
    };

    public static int PendingCount => Store.Defects.Count(d => d.Status == "pending");

    /// <summary>إغلاق القطعة المعيبة وتطبيق أثرها: حركة «مرتجع» على حساب المورد، أو قطعة سليمة تعود للمخزون</summary>
    public static void Resolve(Defect d, string resolution, double credit, string supplier)
    {
        if (d.Status != "pending") Reopen(d);
        if (!string.IsNullOrWhiteSpace(supplier)) d.Supplier = supplier.Trim();
        d.Status = "done";
        d.Resolution = resolution;
        d.ResolvedAt = Txt.Now;
        d.Credited = 0;
        if (resolution == "credit" && credit > 0 && d.Supplier != "")
        {
            var known = Calc.SupplierBalances().FirstOrDefault(x => x.Key == Txt.Fold(d.Supplier));
            var t = new SupplierTx
            {
                Id = Txt.Uid("st"), Supplier = known?.Name ?? d.Supplier, Type = "return", Amount = credit, Date = Txt.Today,
                Note = $"مرتجع قطعة معيبة: {d.PartName}{(d.RefNo != "" ? " — " + d.RefNo : "")}"
            };
            Store.AddSupplierTx(t);
            d.StxId = t.Id;
            d.Credited = credit;
        }
        if (resolution == "replaced" && Store.Inventory.FirstOrDefault(i => i.Id == d.InventoryItemId) is InvItem item && item.Qty != null)
        {
            item.Qty++;
            item.UpdatedAt = Txt.Now;
            Store.SaveInv(item);
        }
        Store.SaveDefect(d);
    }

    /// <summary>التراجع عن الإغلاق (حذف حركة المورد أو إعادة خصم القطعة من المخزون)</summary>
    public static void Reopen(Defect d)
    {
        if (d.Resolution == "credit" && Store.SupplierTx.FirstOrDefault(t => t.Id == d.StxId) is SupplierTx t) Store.DeleteSupplierTx(t);
        if (d.Resolution == "replaced" && Store.Inventory.FirstOrDefault(i => i.Id == d.InventoryItemId) is InvItem item && item.Qty != null)
        {
            item.Qty--;
            item.UpdatedAt = Txt.Now;
            Store.SaveInv(item);
        }
        d.Status = "pending";
        d.Resolution = "";
        d.ResolvedAt = null;
        d.StxId = null;
        d.Credited = 0;
        Store.SaveDefect(d);
    }

    public static void Delete(Defect d)
    {
        if (d.Status != "pending") Reopen(d);
        Store.DeleteDefect(d);
    }

    public class Quality
    {
        public string Name;
        public int Parts, Orders, WarrantyReturns, DefectCount;
        public double DefectCost, Recovered, Lost;
        public double DefectRate => Parts > 0 ? DefectCount * 100.0 / Parts : 0;
        public double ReturnRate => Orders > 0 ? WarrantyReturns * 100.0 / Orders : 0;
    }

    /// <summary>جودة الموردين: كم قطعة ركّبت من كل مورد، وكم منها تعطل أو رجع جهازه بالضمان، وكم استرددت</summary>
    public static List<Quality> SupplierQuality()
    {
        var map = new Dictionary<string, Quality>();
        Quality Q(string name)
        {
            var k = Txt.Fold(name);
            if (!map.TryGetValue(k, out var q)) map[k] = q = new Quality { Name = Txt.Str(name) };
            return q;
        }
        foreach (var o in Store.Orders.Where(o => o.Status != K.Cancelled))
        {
            foreach (var p in o.Parts.Where(p => Txt.Str(p.Supplier) != "")) Q(p.Supplier).Parts++;
            if (o.Status == K.Done && o.WarrantyOf == null)
                foreach (var s in o.Parts.Select(p => Txt.Str(p.Supplier)).Where(x => x != "").Distinct(StringComparer.Ordinal)) Q(s).Orders++;
        }
        foreach (var r in Store.Orders.Where(o => o.WarrantyOf != null))
            if (Calc.Find(r.WarrantyOf) is Order src)
                foreach (var s in src.Parts.Select(p => Txt.Fold(p.Supplier)).Where(x => x != "").Distinct())
                    if (map.TryGetValue(s, out var q)) q.WarrantyReturns++;
        foreach (var d in Store.Defects.Where(d => Txt.Str(d.Supplier) != ""))
        {
            var q = Q(d.Supplier);
            q.DefectCount++;
            q.DefectCost += d.Cost;
            if (d.Resolution == "credit") q.Recovered += d.Credited;
            else if (d.Resolution == "replaced") q.Recovered += d.Cost;
            else if (d.Resolution == "rejected") q.Lost += d.Cost;
        }
        return map.Values.Where(q => q.Name != "")
            .OrderByDescending(q => q.DefectCount + q.WarrantyReturns).ThenByDescending(q => q.Parts).ToList();
    }
}

// ============================== رسائل واتساب ==============================
/// <summary>
/// قوالب الرسائل القابلة للتعديل. المتغيرات بين قوسين {الزبون}، والسطر الذي فيه متغير فارغ
/// (مثل موعد غير محدد أو متبقٍ صفر) يُحذف تلقائياً من الرسالة.
/// </summary>
public static class Msg
{
    public record Tpl(string Id, string Title, string Default);

    public static readonly Tpl[] All =
    {
        new("received", "استلام الجهاز", "مرحباً {الزبون}،\nاستلمنا جهازك ({الجهاز}) للصيانة.\nالعطل: {العطل}\nالرقم المرجعي: {المرجع}\nالموعد المتوقع: {الموعد}\nسنبلغك فور انتهاء العمل.\n\n{التوقيع}"),
        new("quote", "عرض السعر للموافقة", "مرحباً {الزبون}،\nفحصنا جهازك ({الجهاز}).\nالعطل: {العطل}\n{البنود}\nكلفة الإصلاح: {السعر}\nيكون جاهزاً بتاريخ: {الموعد}\nإذا لم ترغب بالإصلاح تكون أجرة الفحص {أجرة_الفحص}.\nهل نبدأ بالإصلاح؟ يرجى الرد بنعم أو لا.\n\n{التوقيع}"),
        new("progress", "تحديث الحالة", "مرحباً {الزبون}،\nجهازك ({الجهاز}) حالياً: {الحالة}.\n{ملاحظة_الحالة}\n\n{التوقيع}"),
        new("ready", "الجهاز جاهز", "مرحباً {الزبون}،\nجهازك ({الجهاز}) جاهز للاستلام.\nالمبلغ الكلي: {السعر}\nالمتبقي: {المتبقي}\n{مسدد}\nبانتظارك، شكراً لثقتك.\n\n{التوقيع}"),
        new("debt", "تذكير بالمتبقي", "مرحباً {الزبون}،\nنذكّرك بمبلغ متبقٍ قدره {المتبقي} عن صيانة جهازك ({الجهاز}) — المرجع {المرجع}.\nنشكر تعاونك.\n\n{التوقيع}"),
        new("thanks", "شكر بعد التسليم", "مرحباً {الزبون}،\nشكراً لاختيارك ورشتنا لصيانة جهازك ({الجهاز}).\nالضمان: {الضمان}\nساري حتى {نهاية_الضمان}\nاحتفظ بالرقم المرجعي {المرجع} لأي مراجعة.\nلأي ملاحظة لا تتردد بمراسلتنا.\n\n{التوقيع}"),
        new("stale", "إلغاء لعدم الرد على السعر", "مرحباً {الزبون}،\nلم يصلنا ردك على عرض سعر إصلاح جهازك ({الجهاز}) — المرجع {المرجع}.\nألغينا الطلب، ويمكنك استلام جهازك في أي وقت.\nأجرة الفحص: {أجرة_الفحص}\nإذا رغبت بالإصلاح لاحقاً يسعدنا ذلك.\n\n{التوقيع}"),
        new("debts", "تذكير بكل ديون الزبون", "مرحباً {الزبون}،\nنذكّرك بالمبالغ المتبقية لدينا:\n{قائمة_الديون}\nالمجموع: {المجموع}\nنشكر تعاونك.\n\n{التوقيع}"),
    };

    public static readonly (string Name, string Hint)[] Vars =
    {
        ("الزبون", "اسم الزبون"), ("الجهاز", "الجهاز والموديل"), ("العطل", "وصف العطل"), ("البنود", "بنود الإصلاح وأسعارها"), ("المرجع", "الرقم المرجعي"), ("الحالة", "حالة الطلب"),
        ("السعر", "سعر الإصلاح"), ("المدفوع", "ما دُفع"), ("المتبقي", "المتبقي (فارغ إذا مسدد)"), ("مسدد", "«المبلغ مسدد بالكامل» إذا لا يوجد متبقٍ"),
        ("الموعد", "التسليم المتوقع"), ("أجرة_الفحص", "أجرة الفحص إن وُجدت"), ("الضمان", "مدة الضمان"), ("نهاية_الضمان", "تاريخ انتهاء الضمان"),
        ("ملاحظة_الحالة", "جملة حسب الحالة"), ("الفني", "اسم الفني"), ("المحل", "اسم المحل"), ("هاتف_المحل", "هاتف المحل"), ("التوقيع", "اسم المحل — هاتفه"),
        ("قائمة_الديون", "أجهزة الزبون المدينة (رسالة الديون)"), ("المجموع", "مجموع الديون (رسالة الديون)"),
    };

    public static string Text(string id) => Store.Get("tpl_" + id) is var t && t.Trim() != "" ? t : All.First(x => x.Id == id).Default;
    public static void Save(string id, string text) => Store.Set("tpl_" + id, text.Replace("\r\n", "\n") == All.First(x => x.Id == id).Default ? "" : text.Replace("\r\n", "\n"));

    static Dictionary<string, string> Common() => new()
    {
        ["المحل"] = Store.ShopName, ["هاتف_المحل"] = Store.ShopPhone,
        ["التوقيع"] = Store.ShopName + (Store.ShopPhone != "" ? " — " + Store.ShopPhone : ""),
    };

    public static Dictionary<string, string> VarsFor(Order o)
    {
        double rem = Calc.RemainingOf(o);
        var we = Calc.WarrantyEnd(o);
        var v = Common();
        v["الزبون"] = o.CustomerName; v["الجهاز"] = o.Device; v["العطل"] = o.Issue != "" ? o.Issue : o.IssueType; v["المرجع"] = o.RefNo;
        v["الحالة"] = o.Status; v["السعر"] = o.Price > 0 ? Txt.Money(o.Price) : ""; v["المدفوع"] = o.Paid > 0 ? Txt.Money(o.Paid) : "";
        v["المتبقي"] = rem > 0 ? Txt.Money(rem) : ""; v["مسدد"] = rem > 0 ? "" : "المبلغ مسدد بالكامل";
        v["الموعد"] = o.DateEstimated != "" ? Txt.FmtDate(o.DateEstimated) : ""; v["أجرة_الفحص"] = o.CheckFee > 0 ? Txt.Money(o.CheckFee) : "";
        v["الضمان"] = o.Warranty; v["نهاية_الضمان"] = we != "" ? Txt.FmtDate(we) : "";
        v["ملاحظة_الحالة"] = o.Status == K.Part ? "ننتظر وصول القطعة المطلوبة وسنكمل العمل فور وصولها." : "نعمل عليه وسنبلغك عند جاهزيته.";
        v["الفني"] = o.Technician;
        v["البنود"] = string.Join("\n", o.X.Items.Select(i => $"- {i.Desc}: {Txt.Money(i.Price)}"));
        return v;
    }

    public static Dictionary<string, string> VarsFor(Calc.Customer c)
    {
        var v = Common();
        v["الزبون"] = c.Name;
        v["قائمة_الديون"] = string.Join("\n", c.Unpaid.Select(o => $"- {o.Device} ({o.RefNo}): {Txt.Money(Calc.RemainingOf(o))}"));
        v["المجموع"] = Txt.Money(c.Debt);
        return v;
    }

    /// <summary>تعبئة القالب: كل سطر فيه متغير فارغ يُحذف، والأسطر الفارغة المتكررة تُختصر</summary>
    public static string Fill(string template, Dictionary<string, string> vars)
    {
        var lines = new List<string>();
        foreach (var line in (template ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            bool drop = false;
            var outLine = System.Text.RegularExpressions.Regex.Replace(line, @"\{([^{}\s]+)\}", m =>
            {
                if (!vars.TryGetValue(m.Groups[1].Value, out var val)) return m.Value;
                if (string.IsNullOrWhiteSpace(val)) drop = true;
                return val ?? "";
            });
            if (drop) continue;
            if (outLine.Trim() == "" && lines.Count > 0 && lines[^1].Trim() == "") continue;
            lines.Add(outLine);
        }
        while (lines.Count > 0 && lines[^1].Trim() == "") lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    public static string For(string id, Order o) => Fill(Text(id), VarsFor(o));
}
