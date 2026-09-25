using System.Text.Json.Nodes;

namespace Workshop;

/// <summary>الثوابت: الحالات، طرق الدفع، أنواع الأعطال، الضمان، الملحقات، فحص الاستلام</summary>
public static class K
{
    public const string Check = "قيد الفحص", Approval = "بانتظار الموافقة", Repair = "قيد الإصلاح", Part = "بانتظار قطعة",
                        Ready = "جاهز للاستلام", Done = "تم التسليم", Cancelled = "ملغى";
    public static readonly string[] Statuses = { Check, Approval, Repair, Part, Ready, Done, Cancelled };
    public static readonly string[] OpenStatuses = Statuses.Where(s => s != Done && s != Cancelled).ToArray();
    /// <summary>ما زال على طاولة العمل: تجاوز الموعد هنا تأخير من الورشة (انتظار موافقة الزبون أو استلامه ليس تأخيرًا)</summary>
    public static readonly string[] WorkStatuses = { Check, Repair, Part };

    // القوائم قابلة للتعديل من الإعدادات (انظر Lists)
    public static string[] PayMethods => Lists.Get("pay_methods");
    public const string PayNone = "غير مدفوع", PayPart = "مدفوع جزئياً", PayFull = "مدفوع بالكامل";
    public static readonly string[] PayList = { PayNone, PayPart, PayFull };

    public static string[] IssueTypes => Lists.Get("issue_types");
    public static string[] Warranties => Lists.Get("warranties");
    public static string[] Accessories => Lists.Get("accessories");
    public static string[] InvCats => Lists.Get("inv_cats");
    /// <summary>فحص الجهاز عند الاستلام: ما يعمل قبل فتحه</summary>
    public static (string Key, string Title)[] Checks => Lists.Checks();

    public static readonly (string Key, string Title)[] TableCols =
    {
        ("ref", "المرجع"), ("customer", "الزبون"), ("device", "الجهاز"), ("issue", "نوع العطل"), ("status", "الحالة"), ("tech", "الفني"), ("price", "السعر"),
        ("profit", "الربح"), ("payment", "الدفع"), ("received", "الاستلام"), ("estimated", "التسليم المتوقع"), ("duration", "مدة العمل"),
    };

    public static readonly (string Key, string Title)[] DashCells =
        { ("revenue", "الإيراد"), ("cash", "المقبوض"), ("cost", "تكلفة القطع"), ("profit", "صافي الربح"), ("loss", "خسائر الملغاة") };

    /// <summary>ألوان الحالات (نص، خلفية)</summary>
    public static (Color Fg, Color Bg) StatusColors(string s) => s switch
    {
        Check => (C("#5E6A7E"), C("#ECEFF4")),
        Approval => (C("#6A4CC2"), C("#EEE9FA")),
        Repair => (C("#2B55C9"), C("#E6ECFB")),
        Part => (C("#94700F"), C("#FAF1D6")),
        Ready => (C("#0D7C86"), C("#DCF2F3")),
        Done => (C("#1D8657"), C("#E1F3EA")),
        Cancelled => (C("#C43F2C"), C("#FBE8E4")),
        PayFull => (C("#1D8657"), C("#E1F3EA")),
        PayPart => (C("#94700F"), C("#FAF1D6")),
        PayNone => (C("#C43F2C"), C("#FBE8E4")),
        _ => (Color.Empty, Color.Empty)
    };
    static Color C(string h) => ColorTranslator.FromHtml(h);
}

public class Part
{
    public string Name = "", Supplier = "";
    public double Cost;
    public string InventoryItemId;
}

public class Payment
{
    public string Id, Date, Note = "", Method = Lists.Cash;
    public double Amount;
}

/// <summary>طلب صيانة (جهاز في الورشة)</summary>
public class Order
{
    public string Id, RefNo = "", CustomerName = "", Phone = "", Device = "", Status = K.Check, Issue = "", IssueType = "أخرى", Passcode = "", Imei = "";
    /// <summary>الفني المسؤول عن الإصلاح (فارغ = بدون فني)</summary>
    public string Technician = "";
    public Dictionary<string, string> Checks = new();
    public bool ChecksNA;
    public string WarrantyOf;
    public List<string> Accessories = new();
    public string Warranty = K.Warranties[0];
    public List<Part> Parts = new();
    public double Price, Paid, CheckFee;
    public List<Payment> PaymentHistory = new();
    public string PaymentStatus = K.PayNone;
    public string DateReceived = "", DateEstimated = "", DateDelivered = "", Notes = "";
    /// <summary>مفتاح صورة الجهاز في جدول الصور (الصورة نفسها لا تُحمَّل إلا عند عرضها)</summary>
    public string PhotoRef;
    public string StartedAt, CompletedAt, CancelledAt, ReadyAt, StatusAt, CreatedAt, UpdatedAt, DeletedAt;

    public Order Clone()
    {
        var o = (Order)MemberwiseClone();
        o.Checks = new(Checks);
        o.Accessories = new(Accessories);
        o.Parts = Parts.Select(p => new Part { Name = p.Name, Supplier = p.Supplier, Cost = p.Cost, InventoryItemId = p.InventoryItemId }).ToList();
        o.PaymentHistory = PaymentHistory.Select(p => new Payment { Id = p.Id, Date = p.Date, Note = p.Note, Method = p.Method, Amount = p.Amount }).ToList();
        return o;
    }
}

/// <summary>قطعة في قائمة الأسعار (مع كمية اختيارية)</summary>
public class InvItem
{
    public string Id, Name = "", Category = K.InvCats[0], Compatible = "", Supplier = "", Notes = "", UpdatedAt;
    public double Cost, SalePrice;
    /// <summary>null = الكمية غير متابَعة</summary>
    public int? Qty, MinQty;
}

public class Expense { public string Id, Description = "مصروف", Date; public double Amount; }

/// <summary>حركة مورد: شراء بالدَّين، دفعة، أو مرتجع قطعة معيبة (يُخصم من حسابه)</summary>
public class SupplierTx { public string Id, Supplier = "", Type = "purchase", Date, Note = ""; public double Amount; }

public class Driver { public string Id, Printer = "", Brand = "", Os = "", Url = "", Note = "", UpdatedAt; }

/// <summary>
/// تحويل البيانات من/إلى JSON بنفس المفاتيح التي تستعملها النسخة السابقة (نسخة المتصفح)،
/// فتُستورد نسخها الاحتياطية كما هي، مع تنظيف أي قيمة ناقصة أو غير صالحة.
/// </summary>
public static class Json
{
    static string S(JsonNode n, string k) => n?[k] is JsonValue v ? Txt.Str(v.ToString()) : "";
    static double M(JsonNode n, string k) => n?[k] is JsonValue v ? Txt.ParseMoney(v.TryGetValue<double>(out var d) ? d : (object)v.ToString()) : 0;
    static bool B(JsonNode n, string k) => n?[k] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.ToString() is "true" or "1");
    static string OrNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    static JsonValue V(string s) => s == null ? null : JsonValue.Create(s);

    public static string DerivePay(double price, double paid)
    {
        if (price <= 0) return paid > 0 ? K.PayFull : K.PayNone;
        if (paid >= price) return K.PayFull;
        return paid > 0 ? K.PayPart : K.PayNone;
    }

    public static Order Order(JsonNode o)
    {
        if (o is not JsonObject) return null;
        var price = Math.Max(0, M(o, "price"));
        var paid = Math.Max(0, M(o, "paid"));
        string received = Txt.Cut10(S(o, "dateReceived"));
        var hist = new List<Payment>();
        if (o["paymentHistory"] is JsonArray ph)
            foreach (var p in ph)
            {
                double a = M(p, "amount");
                if (a <= 0) continue;
                var method = S(p, "method");
                hist.Add(new Payment
                {
                    Id = OrNull(S(p, "id")) ?? Txt.Uid("inst"), Amount = a,
                    Date = OrNull(Txt.Cut10(S(p, "date"))) ?? OrNull(received) ?? Txt.Today, Note = S(p, "note"),
                    Method = OrNull(method) ?? Lists.Cash
                });
            }
        // النسخ القديمة سمحت بتعليم «مدفوع بالكامل» بدون إدخال المبلغ: يبقى هذا المعنى
        if (S(o, "paymentStatus") == K.PayFull && price > 0 && paid < price) paid = price;
        double histSum = hist.Sum(p => p.Amount);
        if (hist.Count > 0 && histSum > paid) paid = histSum;
        var status = K.Statuses.Contains(S(o, "status")) ? S(o, "status") : K.Statuses[0];
        var checkFee = Math.Max(0, M(o, "checkFee"));
        double charge = status == K.Cancelled ? checkFee : price;
        var createdAt = OrNull(S(o, "createdAt")) ?? (received != "" ? received + "T09:00:00" : Txt.Now);
        var updatedAt = OrNull(S(o, "updatedAt"));
        string delivered = Txt.Cut10(S(o, "dateDelivered"));
        string completedAt = status == K.Done ? OrNull(S(o, "completedAt")) ?? (delivered != "" ? delivered + "T12:00:00" : null) ?? updatedAt ?? createdAt : null;
        string cancelledAt = status == K.Cancelled ? OrNull(S(o, "cancelledAt")) ?? updatedAt ?? createdAt : null;

        var checks = new Dictionary<string, string>();
        if (o["checks"] is JsonObject co)
            foreach (var (k, v) in co)
                if (k != "" && v?.ToString() is "ok" or "bad") checks[k] = v.ToString();

        var issueType = S(o, "issueType");
        var warranty = S(o, "warranty");
        var r = new Order
        {
            Id = OrNull(S(o, "id")) ?? Txt.Uid(),
            RefNo = S(o, "refNo"),
            CustomerName = OrNull(S(o, "customerName")) ?? "بدون اسم",
            Phone = Txt.LatinDigits(S(o, "phone")),
            Device = OrNull(S(o, "device")) ?? "جهاز غير محدد",
            Status = status,
            Issue = S(o, "issue"),
            IssueType = K.IssueTypes.Contains(issueType) ? issueType : OrNull(issueType) ?? "أخرى",
            Passcode = S(o, "passcode"),
            Technician = S(o, "technician"),
            Imei = new string(Txt.LatinDigits(S(o, "imei")).Where(c => !char.IsWhiteSpace(c)).ToArray()),
            Checks = checks,
            ChecksNA = B(o, "checksNA"),
            WarrantyOf = OrNull(S(o, "warrantyOf")),
            Accessories = o["accessories"] is JsonArray acc ? acc.Select(x => Txt.Str(x?.ToString())).Where(x => x != "").ToList() : new(),
            Warranty = OrNull(warranty) ?? K.Warranties[0],
            Parts = o["parts"] is JsonArray pa ? pa.Where(p => p != null && (S(p, "name") != "" || M(p, "cost") != 0))
                .Select(p => new Part { Name = S(p, "name"), Supplier = S(p, "supplier"), Cost = Math.Max(0, M(p, "cost")), InventoryItemId = OrNull(S(p, "inventoryItemId")) }).ToList() : new(),
            Price = price, Paid = paid, PaymentHistory = hist,
            PaymentStatus = DerivePay(charge, paid),
            CheckFee = checkFee,
            DateReceived = received != "" ? received : Txt.Cut10(createdAt),
            DateEstimated = Txt.Cut10(S(o, "dateEstimated")),
            DateDelivered = delivered != "" ? delivered : Txt.Cut10(completedAt ?? ""),
            Notes = S(o, "notes"),
            PhotoRef = OrNull(S(o, "photoRef")),
            StartedAt = OrNull(S(o, "startedAt")) ?? createdAt,
            CompletedAt = completedAt,
            CancelledAt = cancelledAt,
            ReadyAt = status == K.Ready ? OrNull(S(o, "readyAt")) ?? updatedAt ?? createdAt : null,
            StatusAt = OrNull(S(o, "statusAt")) ?? (status == K.Ready ? OrNull(S(o, "readyAt")) : null) ?? updatedAt ?? createdAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt ?? createdAt,
            DeletedAt = OrNull(S(o, "deletedAt")),
        };
        return r;
    }

    public static JsonObject ToJson(Order o)
    {
        var checks = new JsonObject();
        foreach (var (k, v) in o.Checks) checks[k] = v;
        return new JsonObject
        {
            ["id"] = o.Id, ["refNo"] = o.RefNo, ["customerName"] = o.CustomerName, ["phone"] = o.Phone, ["device"] = o.Device,
            ["status"] = o.Status, ["technician"] = o.Technician, ["issue"] = o.Issue, ["issueType"] = o.IssueType, ["passcode"] = o.Passcode, ["imei"] = o.Imei,
            ["checks"] = checks, ["checksNA"] = o.ChecksNA, ["warrantyOf"] = V(o.WarrantyOf),
            ["accessories"] = new JsonArray(o.Accessories.Select(a => (JsonNode)a).ToArray()),
            ["warranty"] = o.Warranty,
            ["parts"] = new JsonArray(o.Parts.Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["supplier"] = p.Supplier, ["cost"] = p.Cost, ["inventoryItemId"] = V(p.InventoryItemId) }).ToArray()),
            ["price"] = o.Price, ["paid"] = o.Paid,
            ["paymentHistory"] = new JsonArray(o.PaymentHistory.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["amount"] = p.Amount, ["date"] = p.Date, ["note"] = p.Note, ["method"] = p.Method }).ToArray()),
            ["paymentStatus"] = o.PaymentStatus, ["checkFee"] = o.CheckFee,
            ["dateReceived"] = o.DateReceived, ["dateEstimated"] = o.DateEstimated, ["dateDelivered"] = o.DateDelivered, ["notes"] = o.Notes,
            ["photo"] = null, ["photoRef"] = V(o.PhotoRef),
            ["startedAt"] = V(o.StartedAt), ["completedAt"] = V(o.CompletedAt), ["cancelledAt"] = V(o.CancelledAt), ["readyAt"] = V(o.ReadyAt),
            ["statusAt"] = V(o.StatusAt), ["createdAt"] = V(o.CreatedAt), ["updatedAt"] = V(o.UpdatedAt),
            ["deletedAt"] = V(o.DeletedAt),
        };
    }

    public static InvItem Inv(JsonNode i)
    {
        if (i is not JsonObject || S(i, "name") == "") return null;
        return new InvItem
        {
            Id = OrNull(S(i, "id")) ?? Txt.Uid("inv"), Name = S(i, "name"), Category = OrNull(S(i, "category")) ?? K.InvCats[0],
            Compatible = S(i, "compatible"), Supplier = S(i, "supplier"),
            Cost = Math.Max(0, M(i, "cost")), SalePrice = Math.Max(0, M(i, "salePrice")),
            Qty = i["qty"] is JsonValue q ? Txt.OptInt(q.ToString()) : null, MinQty = i["minQty"] is JsonValue mq ? Txt.OptInt(mq.ToString()) : null,
            Notes = S(i, "notes"), UpdatedAt = OrNull(S(i, "updatedAt")) ?? Txt.Now
        };
    }

    public static JsonObject ToJson(InvItem i) => new()
    {
        ["id"] = i.Id, ["name"] = i.Name, ["category"] = i.Category, ["compatible"] = i.Compatible, ["supplier"] = i.Supplier,
        ["cost"] = i.Cost, ["salePrice"] = i.SalePrice, ["qty"] = i.Qty, ["minQty"] = i.MinQty, ["notes"] = i.Notes, ["updatedAt"] = i.UpdatedAt
    };

    public static Expense Expense(JsonNode e)
    {
        if (e is not JsonObject) return null;
        double a = M(e, "amount");
        if (a <= 0) return null;
        return new Expense { Id = OrNull(S(e, "id")) ?? Txt.Uid("e"), Description = OrNull(S(e, "description")) ?? "مصروف", Amount = a, Date = OrNull(Txt.Cut10(S(e, "date"))) ?? Txt.Today };
    }

    public static JsonObject ToJson(Expense e) => new() { ["id"] = e.Id, ["description"] = e.Description, ["amount"] = e.Amount, ["date"] = e.Date };

    public static SupplierTx Stx(JsonNode t)
    {
        if (t is not JsonObject || S(t, "supplier") == "") return null;
        double a = M(t, "amount");
        if (a <= 0) return null;
        return new SupplierTx
        {
            Id = OrNull(S(t, "id")) ?? Txt.Uid("st"), Supplier = S(t, "supplier"), Type = S(t, "type") is "payment" or "return" ? S(t, "type") : "purchase",
            Amount = a, Date = OrNull(Txt.Cut10(S(t, "date"))) ?? Txt.Today, Note = S(t, "note")
        };
    }

    public static JsonObject ToJson(SupplierTx t) => new() { ["id"] = t.Id, ["supplier"] = t.Supplier, ["type"] = t.Type, ["amount"] = t.Amount, ["date"] = t.Date, ["note"] = t.Note };

    public static Driver Driver(JsonNode d)
    {
        if (d is not JsonObject || S(d, "printer") == "") return null;
        var url = S(d, "url");
        return new Driver
        {
            Id = OrNull(S(d, "id")) ?? Txt.Uid("drv"), Printer = S(d, "printer"), Brand = S(d, "brand"), Os = S(d, "os"),
            Url = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : "",
            Note = S(d, "note"), UpdatedAt = OrNull(S(d, "updatedAt")) ?? Txt.Now
        };
    }

    public static Defect Defect(JsonNode d)
    {
        if (d is not JsonObject || S(d, "partName") == "") return null;
        var res = S(d, "resolution");
        return new Defect
        {
            Id = OrNull(S(d, "id")) ?? Txt.Uid("df"), Date = OrNull(Txt.Cut10(S(d, "date"))) ?? Txt.Today, PartName = S(d, "partName"), Supplier = S(d, "supplier"),
            Cost = Math.Max(0, M(d, "cost")), InventoryItemId = OrNull(S(d, "inventoryItemId")), OrderId = OrNull(S(d, "orderId")), RefNo = S(d, "refNo"),
            Device = S(d, "device"), Technician = S(d, "technician"), Note = S(d, "note"),
            Status = S(d, "status") == "done" && res is "credit" or "replaced" or "rejected" ? "done" : "pending",
            Resolution = S(d, "status") == "done" && res is "credit" or "replaced" or "rejected" ? res : "",
            ResolvedAt = OrNull(S(d, "resolvedAt")), StxId = OrNull(S(d, "stxId")), Credited = Math.Max(0, M(d, "credited")),
        };
    }

    public static JsonObject ToJson(Defect d) => new()
    {
        ["id"] = d.Id, ["date"] = d.Date, ["partName"] = d.PartName, ["supplier"] = d.Supplier, ["cost"] = d.Cost, ["inventoryItemId"] = V(d.InventoryItemId),
        ["orderId"] = V(d.OrderId), ["refNo"] = d.RefNo, ["device"] = d.Device, ["technician"] = d.Technician, ["note"] = d.Note,
        ["status"] = d.Status, ["resolution"] = d.Resolution, ["resolvedAt"] = V(d.ResolvedAt), ["stxId"] = V(d.StxId), ["credited"] = d.Credited,
    };

    public static JsonObject ToJson(Driver d) => new() { ["id"] = d.Id, ["printer"] = d.Printer, ["brand"] = d.Brand, ["os"] = d.Os, ["url"] = d.Url, ["note"] = d.Note, ["updatedAt"] = d.UpdatedAt };
}
