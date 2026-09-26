using System.Diagnostics;
using System.Text;
using static Workshop.Txt;

namespace Workshop;

/// <summary>
/// الطباعة: الفاتورة وملصق الجهاز ووصل الاستلام والتقارير تُكتب كصفحة HTML بنفس تصميم النسخة السابقة
/// وتُفتح في المتصفح مع نافذة الطباعة مباشرة (تدعم أي طابعة، والطباعة إلى PDF).
/// </summary>
public static class Printer
{
    static string FontFace()
    {
        var dir = Path.Combine(Store.DataDir, "fonts");
        string Url(string f) => new Uri(Path.Combine(dir, f)).AbsoluteUri;
        return $@"@font-face{{font-family:'Plex';src:url('{Url("IBMPlexSansArabic-Regular.ttf")}');font-weight:400}}
@font-face{{font-family:'Plex';src:url('{Url("IBMPlexSansArabic-SemiBold.ttf")}');font-weight:600}}
@font-face{{font-family:'Plex';src:url('{Url("IBMPlexSansArabic-Bold.ttf")}');font-weight:700}}";
    }

    public static void Doc(string bodyHtml, string title, string width = "680px", string extraCss = "")
    {
        var html = $@"<!DOCTYPE html><html lang=""ar"" dir=""rtl""><head><meta charset=""UTF-8""><title>{Esc(title)}</title>
<style>
{FontFace()}
*{{box-sizing:border-box}}
body{{font-family:'Plex','IBM Plex Sans Arabic',Tahoma,Arial,sans-serif;margin:0;padding:22px;color:#172033;background:#fff;-webkit-print-color-adjust:exact;print-color-adjust:exact;font-variant-numeric:tabular-nums}}
.doc{{max-width:{width};margin:0 auto}}
.hdr{{display:flex;justify-content:space-between;align-items:flex-start;gap:16px;padding-bottom:14px;border-bottom:3px solid #2B55C9;margin-bottom:16px}}
.shop{{font-size:21px;font-weight:700;color:#1C3C95}}
.shop-sub{{font-size:12px;color:#5A6478;margin-top:2px}}
.doc-title{{font-size:15px;font-weight:700;text-align:left}}
.ref{{display:inline-block;margin-top:6px;background:#FCF0DC;color:#8A5610;border:1px solid #E9C58F;border-radius:4px 10px 10px 4px;padding:3px 10px;font-weight:700;direction:ltr;letter-spacing:.03em}}
.grid{{display:grid;grid-template-columns:1fr 1fr;gap:8px 18px;background:#F5F7FA;border-radius:10px;padding:12px 14px;font-size:13px;margin-bottom:14px}}
.grid b{{font-weight:600}}
.k{{color:#5A6478}}
.row{{display:flex;justify-content:space-between;gap:12px;padding:8px 0;border-bottom:1px dashed #DCE1EA;font-size:13.5px}}
.row.total{{border-bottom:2px solid #172033;font-size:16px;font-weight:700;padding:10px 0}}
.box{{border:1px solid #DCE1EA;border-radius:8px;padding:10px 12px;font-size:13px;line-height:1.7;margin-bottom:12px}}
.foot{{margin-top:18px;padding-top:10px;border-top:1px solid #DCE1EA;font-size:11.5px;color:#5A6478;line-height:1.8}}
.thanks{{text-align:center;font-weight:700;color:#1C3C95;margin-top:14px}}
.good{{color:#1D8657}}.bad{{color:#C43F2C}}
table.t{{width:100%;border-collapse:collapse;font-size:11.5px}}
table.t th{{background:#2B55C9;color:#fff;padding:6px;text-align:right}}
table.t td{{border-bottom:1px solid #DCE1EA;padding:6px}}
.bar{{display:flex;gap:8px;justify-content:center;margin-bottom:16px}}
.bar button{{font:inherit;padding:8px 18px;border-radius:8px;border:0;cursor:pointer;font-weight:700}}
.bar .p{{background:#2B55C9;color:#fff}}.bar .c{{background:#E9EDF3}}
@media print{{ body{{padding:0}} .bar{{display:none}} @page{{margin:10mm}} }}
{extraCss}
</style></head><body>
<div class=""bar""><button class=""p"" onclick=""window.print()"">طباعة</button><button class=""c"" onclick=""window.close()"">إغلاق</button></div>
<div class=""doc"">{bodyHtml}</div>
<script>window.addEventListener('load',function(){{setTimeout(function(){{window.print()}},350)}});</script>
</body></html>";
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "WorkshopPrint");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.html").Where(f => File.GetCreationTime(f) < DateTime.Now.AddDays(-1)))
                try { File.Delete(old); } catch { }
            var safe = string.Concat(title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var file = Path.Combine(dir, $"{safe}_{DateTime.Now:HHmmssfff}.html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex) { Raseed.Dialogs.Warn("تعذّرت الطباعة: " + ex.Message); }
    }

    // ---------- باركود Code 128 (B) كصورة SVG يقرؤها أي قارئ باركود ----------
    static readonly string[] C128 = ("212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 112232 122132 122231 113222 123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 231113 231311 112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 122411 142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 114131 311141 411131 211412 211214 211232 2331112").Split(' ');

    public static string Code128Svg(string text, int height = 44, double module = 2, double maxWidth = 0)
    {
        var chars = (text ?? "").Where(c => c >= 32 && c <= 126).ToList();
        var codes = new List<int> { 104 };
        codes.AddRange(chars.Select(c => c - 32));
        int check = 104;
        for (int i = 1; i < codes.Count; i++) check += codes[i] * i;
        codes.Add(check % 103);
        codes.Add(106);
        int units = 11 * (codes.Count - 1) + 13 + 20;
        if (maxWidth > 0 && units * module > maxWidth) module = Math.Max(1, Math.Floor(maxWidth / units * 4) / 4);
        double x = 10 * module;
        var bars = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var c in codes)
        {
            var w = C128[c];
            for (int i = 0; i < w.Length; i++)
            {
                double bw = (w[i] - '0') * module;
                if (i % 2 == 0) bars.Append($"<rect x=\"{x.ToString(inv)}\" y=\"0\" width=\"{bw.ToString(inv)}\" height=\"{height}\"/>");
                x += bw;
            }
        }
        double W = x + 10 * module;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W.ToString(inv)} {height}\" width=\"{W.ToString(inv)}\" height=\"{height}\" style=\"display:block;margin:0 auto;max-width:100%;height:auto\"><rect width=\"{W.ToString(inv)}\" height=\"{height}\" fill=\"#fff\"/><g fill=\"#000\">{bars}</g></svg>";
    }

    public static string Header(string title, string refNo = null)
    {
        var sub = string.Join(" — ", new[] { Store.ShopAddress, Store.ShopPhone }.Where(x => x != ""));
        return $@"<div class=""hdr""><div><div class=""shop"">{Esc(Store.ShopName)}</div><div class=""shop-sub"">{Esc(sub)}</div></div>
<div style=""text-align:left""><div class=""doc-title"">{Esc(title)}</div>{(refNo != null ? $"<div class=\"ref\">{Esc(refNo)}</div>" : "")}<div class=""shop-sub"">{Esc(FmtDate(Today))}</div></div></div>";
    }

    public static string Row(string k, string v, string cls = "") => $"<div class=\"row\"><span class=\"k\">{k}</span><b class=\"{cls}\">{Esc(v)}</b></div>";

    // ---------- فاتورة الصيانة ----------
    public static void Invoice(Order o)
    {
        if (o.Status == K.Done) o = InvoiceNumbers.Ensure(o);
        double rem = Calc.RemainingOf(o);
        var we = Calc.WarrantyEnd(o);
        var src = Calc.Find(o.WarrantyOf);
        var sb = new StringBuilder(Header("فاتورة صيانة", o.X.InvoiceNo != "" ? o.X.InvoiceNo : o.RefNo));
        if (o.X.InvoiceNo != "") sb.Append($"<div class=\"k\" style=\"text-align:left;margin:-6px 0 6px\">رقم الطلب: <b dir=\"ltr\">{Esc(o.RefNo)}</b></div>");
        sb.Append($@"<div class=""grid"">
<div><span class=""k"">الزبون: </span><b>{Esc(o.CustomerName)}</b></div><div><span class=""k"">الهاتف: </span><b dir=""ltr"">{Esc(o.Phone == "" ? "—" : o.Phone)}</b></div>
<div><span class=""k"">الجهاز: </span><b>{Esc(o.Device)}</b></div><div><span class=""k"">نوع العطل: </span><b>{Esc(o.IssueType)}</b></div>
{(o.Imei != "" ? $"<div><span class=\"k\">IMEI: </span><b dir=\"ltr\">{Esc(o.Imei)}</b></div><div></div>" : "")}
<div><span class=""k"">تاريخ الاستلام: </span><b>{FmtDate(o.DateReceived)}</b></div><div><span class=""k"">{(o.DateDelivered != "" ? "تاريخ التسليم" : "التسليم المتوقع")}: </span><b>{FmtDate(o.DateDelivered != "" ? o.DateDelivered : o.DateEstimated)}</b></div>
</div>
<div class=""box""><span class=""k"">وصف العطل: </span>{Esc(o.Issue == "" ? "—" : o.Issue)}</div>");
        if (o.ChecksNA || o.Checks.Count > 0)
            sb.Append($"<div class=\"box\"><span class=\"k\">حالة الجهاز عند الاستلام: </span>{(o.ChecksNA ? "الجهاز لا يعمل، لم يُفحص" : string.Join(" &nbsp;·&nbsp; ", Lists.OrderChecks(o).Select(c => (c.State == "ok" ? "✓ " : "✕ ") + Esc(c.Title))))}</div>");
        if (o.X.Marks.Count > 0)
            sb.Append($"<div class=\"box\" style=\"display:flex;gap:12px;align-items:center\">{DamageMap.Svg(o.X.Marks)}<div><span class=\"k\">خدوش وكسور عند الاستلام: </span>{Esc(DamageMap.Describe(o.X.Marks))}</div></div>");
        if (o.X.Items.Count > 0)
            sb.Append(Table(new[] { "بند الإصلاح", "السعر", "" }, o.X.Items.Select(i => new[] { i.Desc, Money(i.Price), i.Approved ? "✓ موافق" : "لم يوافق عليه الزبون" })));
        if (o.Parts.Count > 0) sb.Append(Row("القطع المستبدلة", string.Join("، ", o.Parts.Where(p => p.Name != "").Select(p => p.Name + (p.Serial != "" ? $" (رقم {p.Serial})" : "")))).Replace("<b class=\"\">", "<span>").Replace("</b></div>", "</span></div>"));
        if (o.X.SealNo != "") sb.Append(Row("رقم ملصق الضمان", o.X.SealNo));
        if (o.X.Service != "shop") sb.Append(Row("الخدمة", Extra.ServiceText(o.X.Service) + (o.X.Address != "" ? " — " + o.X.Address : "")));
        if (o.X.ShipTracking != "") sb.Append(Row("الشحن", $"{o.X.ShipCompany} — رقم الشحنة {o.X.ShipTracking}"));
        if (QC.Done(o)) sb.Append(Row("فحص الجودة قبل التسليم", QC.Passed(o) ? "✓ كل البنود تعمل" : "لا يعمل: " + string.Join("، ", o.X.QC.Where(q => q.Value == "bad").Select(q => q.Key)), QC.Passed(o) ? "good" : "bad"));
        if (o.Accessories.Count > 0) sb.Append(Row("الملحقات المستلمة", string.Join("، ", o.Accessories)).Replace("<b class=\"\">", "<span>").Replace("</b></div>", "</span></div>"));
        sb.Append(Row("الضمان", o.Warranty + (we != "" ? " — حتى " + FmtDate(we) : "")));
        if (o.X.ExtWarranty != "") sb.Append(Row("ضمان ممتد", $"{o.X.ExtWarranty} — {Money(o.X.ExtWarrantyFee)}"));
        if (src != null) sb.Append(Row("طلب ضمان", "للطلب " + src.RefNo));
        sb.Append(Row(o.Status == K.Cancelled ? "أجرة الفحص (الطلب ملغى)" : "مبلغ الصيانة", Money(Calc.ChargeOf(o))));
        if (o.Status != K.Cancelled && o.CheckFee > 0) sb.Append(Row("أجرة الفحص", "لا تُضاف — مشمولة بسعر الإصلاح"));
        double refunded = -o.PaymentHistory.Where(p => p.IsRefund).Sum(p => p.Amount);
        if (refunded > 0) { sb.Append(Row("المدفوع", Money(o.Paid + refunded), "good")); sb.Append(Row("مُرجَع للزبون", Money(refunded), "bad")); }
        else sb.Append(Row("المدفوع", Money(o.Paid), "good"));
        sb.Append($"<div class=\"row total\"><span>المتبقي</span><span class=\"{(rem > 0 ? "bad" : "good")}\">{(rem > 0 ? Esc(Money(rem)) : "مسدد بالكامل")}</span></div>");
        var terms = string.Join("\n", new[] { Store.Terms, IssueTerms.For(o.IssueType) }.Where(t => t != ""));
        if (terms != "") sb.Append($"<div class=\"foot\" style=\"white-space:pre-line\">{Esc(terms)}</div>");
        if (Loyalty.On && o.AccountId == null && o.Phone != "")
        {
            int pts = Loyalty.Balance(Calc.CustomerKey(o));
            sb.Append($"<div class=\"box\" style=\"text-align:center\">نقاط الولاء: <b>{pts}</b> نقطة{(pts >= Loyalty.MinRedeem ? $" — تساوي {Esc(Money(Loyalty.Worth(pts)))} تستعملها في زيارتك القادمة" : "")}</div>");
        }
        var review = Store.Get("google_review_url");
        if (review != "" && o.Status == K.Done) sb.Append($"<div class=\"foot\" style=\"text-align:center\">رأيك يهمنا — قيّمنا على Google: <span dir=\"ltr\">{Esc(review)}</span></div>");
        sb.Append("<div class=\"thanks\">شكراً لثقتكم</div>");
        sb.Append($"<div style=\"margin-top:14px\">{Code128Svg(o.RefNo, 40, 2, 320)}</div>");
        Doc(sb.ToString(), "فاتورة " + (o.X.InvoiceNo != "" ? o.X.InvoiceNo : o.RefNo));
    }

    // ---------- ملصق الجهاز (يُلصق على الجهاز داخل الورشة) ----------
    public static void Label(Order o)
    {
        var sb = new StringBuilder($@"<div style=""border:2px dashed #172033;border-radius:10px;padding:12px"">
<div style=""display:flex;justify-content:space-between;align-items:center""><b style=""font-size:13px"">{Esc(Store.ShopName)}</b><span style=""font-size:11px;color:#5A6478"">{FmtDate(o.DateReceived)}</span></div>
<div style=""text-align:center;margin:8px 0;padding:6px;border:2px solid #172033;border-radius:8px"">{Code128Svg(o.RefNo, 46, 2, 250)}
<div style=""font-size:24px;font-weight:700;letter-spacing:.06em;direction:ltr;margin-top:4px"">{Esc(o.RefNo)}</div></div>");
        sb.Append(Row("الزبون", o.CustomerName));
        sb.Append(Row("الهاتف", o.Phone == "" ? "—" : o.Phone).Replace("<b class=\"\">", "<b dir=\"ltr\">"));
        sb.Append(Row("الجهاز", o.Device));
        sb.Append(Row("العطل", o.IssueType));
        if (o.Imei != "") sb.Append(Row("IMEI", o.Imei).Replace("<b class=\"\">", "<b dir=\"ltr\">"));
        if (o.Passcode != "" && Store.LabelPasscode) sb.Append(Row("رمز القفل", o.Passcode).Replace("<b class=\"\">", "<b dir=\"ltr\">"));
        if (o.Accessories.Count > 0) sb.Append(Row("ملحقات", string.Join("، ", o.Accessories)));
        sb.Append($"<div class=\"row\" style=\"border:0\"><span class=\"k\">الموعد</span><b>{FmtDate(o.DateEstimated)}</b></div></div>");
        Doc(sb.ToString(), "ملصق " + o.RefNo, "300px", ".row{font-size:12px;padding:5px 0}");
    }

    // ---------- وصل استلام مبلغ ----------
    public static void Receipt(Order o)
    {
        double legacy = o.Paid - o.PaymentHistory.Sum(p => p.Amount);
        var sb = new StringBuilder(Header("وصل استلام مبلغ", o.RefNo));
        sb.Append($"<div class=\"grid\"><div><span class=\"k\">الزبون: </span><b>{Esc(o.CustomerName)}</b></div><div><span class=\"k\">الجهاز: </span><b>{Esc(o.Device)}</b></div></div>");
        if (legacy > 0) sb.Append(Row("دفعة سابقة " + FmtDate(o.DateReceived), Money(legacy)));
        foreach (var p in o.PaymentHistory)
            sb.Append(p.IsRefund
                ? Row($"مُرجَع {FmtDate(p.Date)} — {Esc(p.Method)} — {Esc(p.Note.Replace("استرجاع: ", ""))}", "- " + Money(-p.Amount), "bad")
                : Row($"دفعة {FmtDate(p.Date)} — {Esc(p.Method)}{(p.Note != "" ? " — " + Esc(p.Note) : "")}", Money(p.Amount)));
        sb.Append(Row(o.Status == K.Cancelled ? "أجرة الفحص" : "المبلغ الكلي", Money(Calc.ChargeOf(o))));
        sb.Append($"<div class=\"row total\"><span>المبلغ المستلم</span><span class=\"good\">{Esc(Money(o.Paid))}</span></div>");
        sb.Append("<div style=\"text-align:center;margin-top:14px;padding:10px;border-radius:8px;background:#E1F3EA;color:#1D8657;font-weight:700\">تم سداد كامل المبلغ</div>");
        sb.Append("<div class=\"thanks\">شكراً لتعاملكم معنا</div>");
        Doc(sb.ToString(), "وصل " + o.RefNo, "460px");
    }

    // ---------- بطاقة عمل للفني (بدون أسعار) ----------
    public static void JobCard(Order o)
    {
        var sb = new StringBuilder(Header("بطاقة عمل", o.RefNo));
        sb.Append($"<div class=\"grid\"><div><span class=\"k\">الجهاز: </span><b>{Esc(o.Device)}</b></div><div><span class=\"k\">الفني: </span><b>{Esc(o.Technician == "" ? "—" : o.Technician)}</b></div>" +
                  $"<div><span class=\"k\">الاستلام: </span><b>{FmtDate(o.DateReceived)}</b></div><div><span class=\"k\">الموعد: </span><b>{FmtDate(o.DateEstimated)}</b></div>" +
                  (o.Passcode != "" ? $"<div><span class=\"k\">رمز القفل: </span><b dir=\"ltr\">{Esc(o.Passcode)}</b></div>" : "") +
                  (o.Imei != "" ? $"<div><span class=\"k\">IMEI: </span><b dir=\"ltr\">{Esc(o.Imei)}</b></div>" : "") + "</div>");
        sb.Append($"<div class=\"box\"><span class=\"k\">العطل ({Esc(o.IssueType)}): </span>{Esc(o.Issue)}</div>");
        if (o.X.Items.Count > 0) sb.Append("<div class=\"box\"><span class=\"k\">البنود: </span>" + Esc(string.Join("، ", o.X.Items.Where(i => i.Approved).Select(i => i.Desc))) + "</div>");
        if (o.Checks.Count > 0) sb.Append("<div class=\"box\"><span class=\"k\">حالته عند الاستلام: </span>" + string.Join(" · ", Lists.OrderChecks(o).Select(c => (c.State == "ok" ? "✓ " : "✕ ") + Esc(c.Title))) + "</div>");
        if (o.X.Marks.Count > 0) sb.Append($"<div class=\"box\" style=\"display:flex;gap:12px;align-items:center\">{DamageMap.Svg(o.X.Marks)}<div>{Esc(DamageMap.Describe(o.X.Marks))}</div></div>");
        if (o.Parts.Count > 0) sb.Append(Row("القطع", string.Join("، ", o.Parts.Select(p => p.Name).Where(n => n != ""))));
        if (o.Notes != "") sb.Append($"<div class=\"box\"><span class=\"k\">ملاحظات: </span>{Esc(o.Notes)}</div>");
        foreach (var n in o.X.Chat.TakeLast(5)) sb.Append(Row(Esc(n.Author == "" ? "ملاحظة" : n.Author), n.Text));
        sb.Append("<div class=\"box\" style=\"min-height:90px\"><span class=\"k\">ما تم عمله: </span></div>");
        sb.Append($"<div style=\"margin-top:10px\">{Code128Svg(o.RefNo, 40, 2, 300)}</div>");
        Doc(sb.ToString(), "بطاقة عمل " + o.RefNo, "620px");
    }

    // ---------- بطاقة الضمان ----------
    public static void WarrantyCard(Order o)
    {
        var we = Calc.WarrantyEnd(o);
        var body = $@"<div style=""border:2px solid #2B55C9;border-radius:14px;padding:16px"">
<div style=""display:flex;justify-content:space-between;align-items:center""><div class=""shop"">{Esc(Store.ShopName)}</div><div class=""ref"">{Esc(o.RefNo)}</div></div>
<div style=""text-align:center;font-size:20px;font-weight:700;margin:12px 0;color:#1C3C95"">بطاقة ضمان</div>" +
            Row("الزبون", o.CustomerName) + Row("الجهاز", o.Device) + Row("الإصلاح", o.X.Items.Count > 0 ? string.Join("، ", o.X.Items.Where(i => i.Approved).Select(i => i.Desc)) : o.IssueType) +
            (o.Parts.Any(p => p.Serial != "") ? Row("أرقام القطع", string.Join("، ", o.Parts.Where(p => p.Serial != "").Select(p => p.Serial))) : "") +
            (o.X.SealNo != "" ? Row("رقم الملصق", o.X.SealNo) : "") +
            Row("تاريخ التسليم", FmtDate(o.DateDelivered)) + Row("مدة الضمان", o.Warranty) +
            $"<div class=\"row total\"><span>ساري حتى</span><span class=\"good\">{FmtDate(we)}</span></div>" +
            (Store.Terms != "" ? $"<div class=\"foot\">{Esc(Store.Terms)}</div>" : "") +
            $"<div style=\"text-align:center;margin-top:10px\">{Code128Svg(o.RefNo, 34, 1.6, 240)}</div>" +
            $"<div style=\"text-align:center;font-size:11px;color:#5A6478;margin-top:4px\">{Esc(Store.ShopPhone)}</div></div>";
        Doc(body, "ضمان " + o.RefNo, "420px");
    }

    // ---------- خطاب قرار الضمان ----------
    public static void WarrantyLetter(Order o)
    {
        var src = Calc.Find(o.WarrantyOf);
        bool ok = o.X.WarrantyDecision == "covered";
        var sb = new StringBuilder(Header(ok ? "قبول طلب ضمان" : "رفض طلب ضمان", o.RefNo));
        sb.Append($"<div class=\"grid\"><div><span class=\"k\">الزبون: </span><b>{Esc(o.CustomerName)}</b></div><div><span class=\"k\">الجهاز: </span><b>{Esc(o.Device)}</b></div>" +
                  (src != null ? $"<div><span class=\"k\">الإصلاح الأصلي: </span><b>{Esc(src.RefNo)} — {FmtDate(src.DateDelivered)}</b></div><div><span class=\"k\">الضمان: </span><b>{Esc(src.Warranty)} حتى {FmtDate(Calc.WarrantyEnd(src))}</b></div>" : "") + "</div>");
        sb.Append($"<div class=\"box\"><span class=\"k\">الشكوى: </span>{Esc(o.Issue == "" ? o.IssueType : o.Issue)}</div>");
        sb.Append($"<div class=\"box\" style=\"font-size:15px;border-color:{(ok ? "#1D8657" : "#C8374A")}\"><b style=\"color:{(ok ? "#1D8657" : "#C8374A")}\">{(ok ? "القرار: يشمله الضمان — يُصلح دون مقابل" : "القرار: لا يشمله الضمان")}</b><br>" +
                  $"<span class=\"k\">السبب: </span>{Esc(o.X.WarrantyReason)}</div>");
        if (!ok) sb.Append("<div class=\"foot\">يمكننا إصلاح الجهاز بأجرة عادية بعد موافقتكم على السعر.</div>");
        if (Store.Terms != "") sb.Append($"<div class=\"foot\" style=\"white-space:pre-line\">{Esc(Store.Terms)}</div>");
        sb.Append($"<div style=\"display:flex;justify-content:space-between;margin-top:40px\"><div>توقيع المحل: ..................</div><div>توقيع الزبون: ..................</div></div>");
        Doc(sb.ToString(), "قرار ضمان " + o.RefNo, "620px");
    }

    // ---------- ملف الزبون الكامل ----------
    public static void CustomerFile(string key)
    {
        var c = Calc.GetCustomers().FirstOrDefault(x => x.Key == key);
        if (c == null) return;
        double credit = Credits.Balance(key);
        var sb = new StringBuilder(Header("ملف الزبون"));
        sb.Append($"<div class=\"grid\"><div><span class=\"k\">الزبون: </span><b>{Esc(c.Name)}</b></div><div><span class=\"k\">الهاتف: </span><b dir=\"ltr\">{Esc(c.Phone == "" ? "—" : c.Phone)}</b></div>" +
                  $"<div><span class=\"k\">عدد الأجهزة: </span><b>{c.Orders.Count}</b></div><div><span class=\"k\">مجموع ما دفعه: </span><b>{Esc(Money(c.Orders.Sum(o => o.Paid)))}</b></div>" +
                  $"<div><span class=\"k\">الدين: </span><b class=\"{(c.Debt > 0 ? "bad" : "good")}\">{Esc(Money(c.Debt))}</b></div>" +
                  (credit > 0 ? $"<div><span class=\"k\">رصيده عندنا: </span><b class=\"good\">{Esc(Money(credit))}</b></div>" : "") + "</div>");
        sb.Append(Table(new[] { "المرجع", "الاستلام", "الجهاز", "العطل", "الحالة", "السعر", "المدفوع", "المتبقي", "الضمان حتى" },
            c.Orders.OrderByDescending(o => o.DateReceived, StringComparer.Ordinal).Select(o => new[]
            {
                o.RefNo, FmtDate(o.DateReceived), o.Device, o.IssueType, o.Status, Money(Calc.ChargeOf(o)), Money(o.Paid), Money(Calc.RemainingOf(o)),
                Calc.WarrantyEnd(o) is var we && we != "" ? FmtDate(we) : "—"
            })));
        var pays = c.Orders.SelectMany(o => o.PaymentHistory.Select(p => (o, p))).OrderBy(x => x.p.Date, StringComparer.Ordinal).ToList();
        if (pays.Count > 0)
        {
            sb.Append("<h3 style=\"font-size:14px;margin-top:16px\">الدفعات</h3>");
            sb.Append(Table(new[] { "التاريخ", "المرجع", "الطريقة", "ملاحظة", "المبلغ" }, pays.Select(x => new[] { FmtDate(x.p.Date), x.o.RefNo, x.p.Method, x.p.Note, Money(x.p.Amount) })));
        }
        Doc(sb.ToString(), "ملف " + c.Name, "900px");
    }

    public static string Table(string[] head, IEnumerable<string[]> rows) =>
        $"<table class=\"t\"><thead><tr>{string.Concat(head.Select(h => $"<th>{Esc(h)}</th>"))}</tr></thead><tbody>{string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select(c => $"<td>{Esc(c)}</td>")) + "</tr>"))}</tbody></table>";
}
