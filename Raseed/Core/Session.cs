using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Raseed;

/// <summary>المستخدم الحالي والصلاحيات المتقدمة</summary>
public static class Session
{
    public static long UserId;
    public static string UserName = "";
    public static bool IsAdmin;
    public static HashSet<string> Perms = new();

    public static readonly (string Key, string Title)[] AllPerms =
    {
        ("sales", "فواتير البيع"), ("purchases", "فواتير الشراء"), ("returns", "المرتجعات"),
        ("damage", "إتلاف المواد"), ("items", "المواد"), ("parties", "العملاء والموردون"),
        ("stock", "المخازن والصلاحيات"), ("vouchers", "السندات المالية"), ("installments", "الأقساط"),
        ("hr", "الموارد البشرية"), ("reports", "التقارير"), ("profit", "الأرباح وتوزيعها"),
        ("tasks", "مدير المهام"), ("users", "المستخدمون"), ("settings", "الإعدادات والتعريفات"),
        ("backup", "النسخ الاحتياطي"), ("delete", "الحذف"), ("exceed_credit", "تجاوز سقف الذمة"),
        ("edit_price", "تعديل السعر داخل الفاتورة"), ("discount", "منح الخصم"),
        ("repairs", "الصيانة (استلام وتسليم الأجهزة)"), ("edit_invoice", "تعديل وحذف الفواتير"),
        ("print", "الطباعة والتصدير"), ("labels", "ملصقات الباركود"),
    };

    public static bool Can(string perm) => IsAdmin || perm == null || Perms.Contains(perm);

    public static bool Guard(string perm)
    {
        if (Can(perm)) return true;
        MessageBox.Show("ليس لديك صلاحية لتنفيذ هذه العملية.", "الصلاحيات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    public static string Hash(string user, string pass)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("raseed|" + user.Trim().ToLowerInvariant() + "|" + pass));
        return Convert.ToHexString(bytes);
    }

    public static bool Login(string user, string pass)
    {
        var dt = Db.Query("SELECT id,full_name,is_admin,pass_hash FROM users WHERE username=@p0 AND active=1", user.Trim());
        if (dt.Rows.Count == 0) return false;
        var r = dt.Rows[0];
        if (Db.S(r["pass_hash"]) != Hash(user, pass)) return false;
        UserId = Db.L(r["id"]);
        UserName = Db.S(r["full_name"]);
        IsAdmin = Db.L(r["is_admin"]) == 1;
        Perms = Db.Query("SELECT perm FROM user_perms WHERE user_id=@p0", UserId)
                  .Rows.Cast<DataRow>().Select(x => Db.S(x["perm"])).ToHashSet();
        return true;
    }
}

/// <summary>إعدادات البرنامج (مفتاح/قيمة)</summary>
public static class Settings
{
    public static readonly (string Key, string Caption, string Def)[] All =
    {
        ("shop_name", "اسم المحل", "محلي"),
        ("shop_phone", "هاتف المحل", ""),
        ("shop_address", "عنوان المحل (يظهر في الطباعة)", ""),
        ("invoice_footer", "تذييل الفاتورة المطبوعة", "شكراً لتعاملكم معنا"),
        ("print_mode", "حجم ورق الفاتورة (A4 أو 80mm)", "A4"),
        ("print_preview", "معاينة قبل الطباعة (1 = نعم، 0 = طباعة مباشرة)", "1"),
        ("print_after_save", "طباعة الفاتورة بعد الحفظ (0 = لا، 1 = نعم، 2 = اسأل)", "2"),
        ("printer_name", "طابعة الفواتير (فارغ = الافتراضية)", ""),
        ("label_printer", "طابعة الملصقات (فارغ = الافتراضية)", ""),
        ("label_w", "عرض الملصق (ملم)", "40"),
        ("label_h", "ارتفاع الملصق (ملم)", "25"),
        ("label_mode", "نمط الملصقات (roll = طابعة ملصقات / A4 = ورقة)", "roll"),
        ("label_price", "إظهار السعر على الملصق (1 = نعم)", "1"),
        ("repair_terms", "شروط وصل الصيانة", "الجهاز الذي لا يُستلم خلال 30 يوماً من إشعار الجاهزية لا يتحمل المحل مسؤوليته. البيانات مسؤولية الزبون."),
        ("repair_ready_msg", "رسالة جاهزية الجهاز ({name} {device} {id} {price} {shop})",
            "عزيزي {name}، جهازكم {device} (وصل رقم {id}) جاهز للاستلام. المبلغ المطلوب: {price} د.ع. مع التحية — {shop}"),
        ("usd_rate", "سعر صرف الدولار (دينار)", "1500"),
        ("expiry_days", "التنبيه قبل انتهاء الصلاحية بـ (يوم)", "30"),
        ("reminder_days", "تذكير الأقساط قبل الاستحقاق بـ (يوم)", "3"),
        ("backup_dir", "مجلد النسخ الاحتياطي المحلي", ""),
        ("cloud_dir", "مجلد النسخ السحابي (Google Drive / OneDrive)", ""),
        ("backup_keep", "عدد النسخ المحتفظ بها", "30"),
        ("backup_on_exit", "نسخة تلقائية عند الإغلاق (1 = نعم)", "1"),
        ("wa_mode", "طريقة واتساب (link = رابط يدوي / cloud = إرسال تلقائي)", "link"),
        ("wa_token", "WhatsApp Cloud API — Access Token", ""),
        ("wa_phone_id", "WhatsApp Cloud API — Phone Number ID", ""),
        ("wa_template", "نص تذكير القسط ({name} {seq} {amount} {due} {shop})",
            "عزيزي {name}، نذكّركم بموعد القسط رقم {seq} بمبلغ {amount} د.ع المستحق بتاريخ {due}. مع التحية — {shop}"),
        ("api_enabled", "تفعيل ربط تطبيق الهاتف (1 = نعم)", "0"),
        ("api_port", "منفذ ربط الهاتف", "8085"),
        ("api_token", "رمز الربط (Token)", ""),
    };

    public static string Get(string key, string def = "")
    {
        var v = Db.Scalar("SELECT value FROM settings WHERE key=@p0", key);
        return v == null || v is DBNull ? def : Convert.ToString(v);
    }

    public static int Int(string key, int def) => int.TryParse(Get(key), out var v) ? v : def;
    public static double Dbl(string key, double def) => double.TryParse(Get(key), out var v) ? v : def;
    public static void Set(string key, string value) => Db.Exec("INSERT OR REPLACE INTO settings(key,value) VALUES(@p0,@p1)", key, value);
}
