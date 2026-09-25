using System.Data;

namespace Raseed;

/// <summary>تفقيط: كتابة المبلغ بالحروف العربية (مثل: اثنان وسبعون ألفًا وخمسمائة دينار)</summary>
public static class Tafqeet
{
    static readonly string[] Ones = { "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة",
        "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر" };
    static readonly string[] Tens = { "", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون" };
    static readonly string[] Hundreds = { "", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة" };

    /// <summary>صيغ المعدود: (مفرد، مثنى، جمع 3-10، تمييز 11-99)</summary>
    record Noun(string One, string Two, string Few, string Many);
    static readonly Noun Thousand = new("ألف", "ألفان", "آلاف", "ألفًا"), Million = new("مليون", "مليونان", "ملايين", "مليونًا"),
                         Billion = new("مليار", "ملياران", "مليارات", "مليارًا");
    public static readonly (string One, string Two, string Few, string Many) Dinar = ("دينار", "ديناران", "دنانير", "دينارًا"),
                                                                              Dollar = ("دولار", "دولاران", "دولارات", "دولارًا");

    /// <summary>من 1 إلى 999</summary>
    static string Group(int n)
    {
        var parts = new List<string>();
        if (n >= 100) parts.Add(Hundreds[n / 100]);
        int r = n % 100;
        if (r > 0 && r < 20) parts.Add(Ones[r]);
        else if (r >= 20) parts.Add(r % 10 == 0 ? Tens[r / 10] : $"{Ones[r % 10]} و{Tens[r / 10]}");
        return string.Join(" و", parts);
    }

    static string Counted(int g, Noun n)
    {
        if (g == 1) return n.One;
        if (g == 2) return n.Two;
        int r = g % 100;
        string num = Group(g);
        if (g < 100) return r <= 10 ? $"{num} {n.Few}" : $"{num} {n.Many}";
        // مائة وأكثر: التمييز حسب آخر رقمين
        return r is >= 3 and <= 10 ? $"{num} {n.Few}" : r >= 11 ? $"{num} {n.Many}" : $"{num} {n.One}";
    }

    /// <summary>الرقم بالحروف (بدون عملة)</summary>
    public static string Words(long n)
    {
        if (n == 0) return "صفر";
        if (n < 0) return "سالب " + Words(-n);
        var parts = new List<string>();
        int b = (int)(n / 1_000_000_000 % 1000), m = (int)(n / 1_000_000 % 1000), t = (int)(n / 1000 % 1000), u = (int)(n % 1000);
        if (n >= 1_000_000_000_000) parts.Add(Words(n / 1_000_000_000_000) + " ترليون");
        if (b > 0) parts.Add(Counted(b, Billion));
        if (m > 0) parts.Add(Counted(m, Million));
        if (t > 0) parts.Add(Counted(t, Thousand));
        if (u > 0) parts.Add(Group(u));
        return string.Join(" و", parts);
    }

    /// <summary>المبلغ بالحروف مع العملة: «فقط اثنان وسبعون ألفًا وخمسمائة دينار لا غير»</summary>
    public static string Money(double amount, bool usd = false)
    {
        long n = (long)Math.Round(Math.Abs(amount));
        if (n == 0) return "";
        var c = usd ? Dollar : Dinar;
        if (n == 1) return $"فقط {c.One} واحد لا غير";
        if (n == 2) return $"فقط {c.Two} لا غير";
        // التمييز حسب آخر رقمين: 3-10 جمع، 11-99 مفرد منصوب، وغير ذلك مفرد
        int r = (int)(n % 100);
        string noun = r is >= 3 and <= 10 ? c.Few : r >= 11 ? c.Many : c.One;
        return $"فقط {Words(n)} {noun} لا غير";
    }
}

/// <summary>سند القبض / الدفع: مبلغ بالدينار وآخر بالدولار، وخصم لكل منهما، في سند واحد برقم واحد</summary>
public static class Vouchers
{
    public record Data(string Kind, DateTime Date, long Party, long Box, long BoxUsd, double Iqd, double Usd, double DiscIqd, double DiscUsd, string Note);

    /// <summary>القبض يُنقص رصيد الحساب (مدين/لنا)، والدفع يزيده</summary>
    public static double Sign(string kind) => kind == "قبض" ? -1 : 1;

    /// <summary>أثر السند على رصيد الحساب بالدينار (المبلغ + الخصم، والدولار بسعر الصرف)</summary>
    public static double Effect(Data d, double rate) => Sign(d.Kind) * (d.Iqd + d.DiscIqd + (d.Usd + d.DiscUsd) * rate);

    public static long Save(long id, Data d)
    {
        double rate = Ui.Rate("USD");
        using var tx = new Tx();
        if (id > 0) RemoveRows(tx, id);
        id = tx.Insert(@"INSERT INTO vouchers(id,kind,date,party_id,box_id,box_usd_id,iqd,usd,disc_iqd,disc_usd,rate,note,user_id)
                         VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12)",
            Db.N(id), d.Kind, d.Date.ToString(Ui.DtFmt), d.Party, Db.N(d.Box), Db.N(d.BoxUsd), d.Iqd, d.Usd, d.DiscIqd, d.DiscUsd, rate, d.Note, Session.UserId);
        double cash = d.Kind == "قبض" ? 1 : -1;
        string title = (d.Kind == "قبض" ? "سند قبض" : "سند دفع") + " رقم " + id;
        const string ins = "INSERT INTO cash_moves(date,kind,cashbox_id,amount,rate,party_id,note,user_id,voucher_id) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)";
        if (d.Iqd > 0) tx.Exec(ins, d.Date.ToString(Ui.DtFmt), d.Kind, d.Box, cash * d.Iqd, Ui.Rate(Ui.BoxCurrency(d.Box)), d.Party, $"{title} — {d.Note}", Session.UserId, id);
        if (d.Usd > 0) tx.Exec(ins, d.Date.ToString(Ui.DtFmt), d.Kind, d.BoxUsd, cash * d.Usd, rate, d.Party, $"{title} — {d.Note}", Session.UserId, id);
        // الخصم الممنوح (قبض) أو المكتسب (دفع): يسوّي الرصيد بلا حركة نقد
        if (d.DiscIqd > 0 || d.DiscUsd > 0)
            tx.Exec("INSERT INTO balance_entries(date,party_id,iqd,usd,rate,note,user_id,voucher_id) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                d.Date.ToString(Ui.DtFmt), d.Party, Sign(d.Kind) * d.DiscIqd, Sign(d.Kind) * d.DiscUsd, rate,
                (d.Kind == "قبض" ? "خصم ممنوح — " : "خصم مكتسب — ") + title, Session.UserId, id);
        tx.Commit();
        return id;
    }

    static void RemoveRows(Tx tx, long id)
    {
        tx.Exec("DELETE FROM cash_moves WHERE voucher_id=@p0", id);
        tx.Exec("DELETE FROM balance_entries WHERE voucher_id=@p0", id);
        tx.Exec("DELETE FROM vouchers WHERE id=@p0", id);
    }

    public static void Delete(long id)
    {
        using var tx = new Tx();
        RemoveRows(tx, id);
        tx.Commit();
        Db.Audit("حذف سند", $"سند قبض/دفع رقم {id}");
    }

    public static long NextId() => Db.L(Db.Scalar("SELECT IFNULL(MAX(id),0)+1 FROM vouchers"));
}

/// <summary>سقف الذمة (دينار + دولار بسعر الصرف) وطريقة التعامل عند التجاوز، وفترة التسديد</summary>
public static class Credit
{
    public record Info(double Limit, bool Block, int PayDays);

    public static Info Of(long partyId)
    {
        var dt = Db.Query("SELECT credit_limit, credit_limit_usd, credit_mode, pay_period_days FROM parties WHERE id=@p0", partyId);
        if (dt.Rows.Count == 0) return new Info(0, false, 0);
        var r = dt.Rows[0];
        return new Info(Db.D(r["credit_limit"]) + Db.D(r["credit_limit_usd"]) * Ui.Rate("USD"), Db.S(r["credit_mode"]) == "منع", (int)Db.L(r["pay_period_days"]));
    }

    public static bool Enabled => Features.On("feat_credit");

    /// <summary>تنبيهات الحسابات: تجاوز سقف الذمة وتأخر التسديد عن الفترة المحددة</summary>
    public const string AlertsSql = @"
        SELECT 'تجاوز سقف الذمة' AS [التنبيه], p.name AS [المادة / الجهة],
               'الرصيد: '||CAST(b.balance AS INTEGER)||'  —  السقف: '||CAST(p.credit_limit+p.credit_limit_usd*@p9 AS INTEGER) AS [التفاصيل]
        FROM parties p JOIN v_party_balance b ON b.id=p.id
        WHERE (p.credit_limit>0 OR p.credit_limit_usd>0) AND b.balance>p.credit_limit+p.credit_limit_usd*@p9+0.5
        UNION ALL
        SELECT 'تأخر التسديد', p.name, 'الرصيد: '||CAST(b.balance AS INTEGER)||'  —  آخر تسديد: '||
               IFNULL(substr((SELECT MAX(date) FROM cash_moves m WHERE m.party_id=p.id AND m.amount>0),1,10),'لا يوجد')
        FROM parties p JOIN v_party_balance b ON b.id=p.id
        WHERE p.pay_period_days>0 AND b.balance>0.5
          AND IFNULL((SELECT MAX(date) FROM cash_moves m WHERE m.party_id=p.id AND m.amount>0),
                     (SELECT MIN(date) FROM invoices i WHERE i.party_id=p.id AND i.type='Sale')) < datetime('now','localtime','-'||p.pay_period_days||' day')";

    public static DataTable Alerts() => Enabled ? Db.Query(AlertsSql.Replace("@p9", Rate)) : new DataTable();
    public static long AlertsCount() => Enabled ? Db.L(Db.Scalar($"SELECT COUNT(*) FROM ({AlertsSql.Replace("@p9", Rate)})")) : 0;
    static string Rate => Ui.Rate("USD").ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>أنظمة البرنامج التي يمكن إيقافها من الإعدادات لتبسيط القوائم (مثل محل لا يبيع بالأقساط)</summary>
public static class Features
{
    public static readonly (string Key, string Caption)[] All =
    {
        ("feat_three_prices", "إظهار ثلاث أسعار للمادة (مفرد، جملة، خاص)"),
        ("feat_warehouses", "إدارة المخازن (أكثر من مخزن والنقل بينها)"),
        ("feat_companies", "استخدام الشركات"),
        ("feat_serials", "استخدام الرقم التسلسلي"),
        ("feat_installments", "نظام الأقساط والكفلاء"),
        ("feat_boxes", "تعدد الصناديق والخزائن"),
        ("feat_credit", "سقف الذمة وفترة التسديد"),
        ("feat_analysis", "تحليل البيانات"),
        ("feat_followup", "تقارير المتابعة"),
        ("feat_repairs", "الصيانة"),
        ("feat_hr", "الموظفون والرواتب"),
        ("feat_quotes", "عروض الأسعار"),
    };

    public static bool On(string key) => Settings.Get(key, "1") != "0";
}
