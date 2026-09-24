using System.Data;
using System.Globalization;
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
        Dialogs.Warn("ليس لديك صلاحية لتنفيذ هذه العملية. اطلب من مدير النظام منحك الصلاحية.", "لا توجد صلاحية");
        return false;
    }

    /// <summary>كلمة المرور الافتراضية ما زالت مستخدمة؟ (تُطلب إعادة تعيينها عند أول دخول)</summary>
    public static bool UsingDefaultPassword;

    // PBKDF2-SHA256 مع ملح عشوائي لكل مستخدم: pbkdf2$التكرارات$الملح$الناتج
    const int Iterations = 120_000;

    public static string HashPassword(string pass)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Pbkdf2(pass, salt, Iterations, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    static byte[] Pbkdf2(string pass, byte[] salt, int iterations, int length)
    {
        try { return Rfc2898DeriveBytes.Pbkdf2(pass, salt, iterations, HashAlgorithmName.SHA256, length); }
        catch (CryptographicException) { return Pbkdf2Managed(pass, salt, iterations, length); }
    }

    /// <summary>نفس خوارزمية PBKDF2-HMAC-SHA256 (RFC 8018) بتنفيذ داخلي، احتياطًا إن لم يدعمها النظام</summary>
    static byte[] Pbkdf2Managed(string pass, byte[] salt, int iterations, int length)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pass));
        var result = new byte[length];
        var block = new byte[salt.Length + 4];
        salt.CopyTo(block, 0);
        for (int i = 1, offset = 0; offset < length; i++, offset += 32)
        {
            block[^4] = (byte)(i >> 24); block[^3] = (byte)(i >> 16); block[^2] = (byte)(i >> 8); block[^1] = (byte)i;
            var u = hmac.ComputeHash(block);
            var t = (byte[])u.Clone();
            for (int j = 1; j < iterations; j++)
            {
                u = hmac.ComputeHash(u);
                for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
            }
            Array.Copy(t, 0, result, offset, Math.Min(t.Length, length - offset));
        }
        return result;
    }

    /// <summary>التحقق من كلمة المرور (يدعم الصيغة القديمة SHA-256 للترقية التلقائية)</summary>
    public static bool Verify(string user, string pass, string stored, out bool legacy)
    {
        legacy = false;
        stored ??= "";
        var parts = stored.Split('$');
        if (parts.Length == 4 && parts[0] == "pbkdf2" && int.TryParse(parts[1], out int it))
        {
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Pbkdf2(pass, salt, it, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException) { return false; }
        }
        legacy = true;
        var old = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("raseed|" + user.Trim().ToLowerInvariant() + "|" + pass)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(old), Encoding.ASCII.GetBytes(stored));
    }

    public static bool Login(string user, string pass)
    {
        var dt = Db.Query("SELECT id,username,full_name,is_admin,pass_hash FROM users WHERE username=@p0 COLLATE NOCASE AND active=1", user.Trim());
        if (dt.Rows.Count == 0) return false;
        var r = dt.Rows[0];
        if (!Verify(Db.S(r["username"]), pass, Db.S(r["pass_hash"]), out bool legacy)) return false;
        UserId = Db.L(r["id"]);
        UserName = Db.S(r["full_name"]) != "" ? Db.S(r["full_name"]) : Db.S(r["username"]);
        IsAdmin = Db.L(r["is_admin"]) == 1;
        Perms = Db.Query("SELECT perm FROM user_perms WHERE user_id=@p0", UserId)
                  .Rows.Cast<DataRow>().Select(x => Db.S(x["perm"])).ToHashSet();
        if (legacy) Db.Exec("UPDATE users SET pass_hash=@p0 WHERE id=@p1", HashPassword(pass), UserId);
        UsingDefaultPassword = pass == "admin" || string.Equals(pass, Db.S(r["username"]), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static void Logout()
    {
        UserId = 0; UserName = ""; IsAdmin = false; Perms = new(); UsingDefaultPassword = false;
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
        ("invoice_footer", "تذييل الفاتورة المطبوعة", "شكرًا لتعاملكم معنا"),
        ("print_mode", "حجم ورق الفاتورة", "A4"),
        ("print_preview", "معاينة قبل الطباعة", "1"),
        ("print_after_save", "طباعة الفاتورة بعد الحفظ", "2"),
        ("printer_name", "طابعة الفواتير", ""),
        ("label_printer", "طابعة الملصقات", ""),
        ("label_w", "عرض الملصق (ملم)", "40"),
        ("label_h", "ارتفاع الملصق (ملم)", "25"),
        ("label_mode", "نوع ورق الملصقات", "roll"),
        ("label_price", "إظهار السعر على الملصق", "1"),
        ("repair_terms", "شروط وصل الصيانة", "الجهاز الذي لا يُستلم خلال 30 يومًا من إشعار الجاهزية لا يتحمل المحل مسؤوليته. البيانات مسؤولية الزبون."),
        ("repair_ready_msg", "رسالة جاهزية الجهاز",
            "عزيزي {name}، جهازكم {device} (وصل رقم {id}) جاهز للاستلام. المبلغ المطلوب: {price} د.ع. مع التحية — {shop}"),
        ("usd_rate", "سعر صرف الدولار (دينار)", "1500"),
        ("expiry_days", "التنبيه قبل انتهاء الصلاحية بـ (يوم)", "30"),
        ("reminder_days", "تذكير الأقساط قبل الاستحقاق بـ (يوم)", "3"),
        ("scale_prefix", "بادئة باركود الميزان", "2"),
        ("scale_code_len", "عدد أرقام رمز المادة في باركود الميزان", "6"),
        ("scale_value_len", "عدد أرقام الوزن في باركود الميزان", "5"),
        ("scale_divisor", "قسمة الوزن على (1000 = الوزن بالغرام)", "1000"),
        ("backup_dir", "مجلد النسخ الاحتياطي المحلي", ""),
        ("cloud_dir", "مجلد النسخ السحابي (Google Drive / OneDrive)", ""),
        ("backup_keep", "عدد النسخ المحتفظ بها", "30"),
        ("backup_on_exit", "نسخة تلقائية عند إغلاق البرنامج", "1"),
        ("wa_mode", "طريقة الإرسال", "link"),
        ("wa_token", "رمز الوصول (Access Token)", ""),
        ("wa_phone_id", "معرّف الرقم (Phone Number ID)", ""),
        ("wa_template", "نص تذكير القسط",
            "عزيزي {name}، نذكّركم بموعد القسط رقم {seq} بمبلغ {amount} د.ع المستحق بتاريخ {due}. مع التحية — {shop}"),
        ("api_enabled", "تفعيل ربط تطبيق الهاتف", "0"),
        ("api_port", "منفذ ربط الهاتف", "8085"),
        ("api_token", "رمز الربط", ""),
    };

    public static string Get(string key, string def = "")
    {
        var v = Db.Scalar("SELECT value FROM settings WHERE key=@p0", key);
        return v == null || v is DBNull ? def : Convert.ToString(v);
    }

    public static int Int(string key, int def) => int.TryParse(Get(key), out var v) ? v : def;
    public static double Dbl(string key, double def) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    public static void Set(string key, string value) => Db.Exec("INSERT OR REPLACE INTO settings(key,value) VALUES(@p0,@p1)", key, value);
}
