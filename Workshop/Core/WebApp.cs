using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Workshop;

/// <summary>
/// صفحة الفني على الهاتف: خادم صغير داخل البرنامج على شبكة المحل. يفتحها الفني من متصفح هاتفه
/// (http://عنوان-الحاسوب:8095) برمز PIN، فيرى الأجهزة المفتوحة ويغيّر حالتها، ويضيف ملاحظة، ويشغّل التوقيت،
/// ويصوّر الجهاز، ويسجّل فحص الجودة. كل تعديل يُنفَّذ داخل البرنامج نفسه فيبقى متسقاً.
/// </summary>
public static class WebApp
{
    public static bool Enabled => Store.Flag("web_on");
    public static int Port => int.TryParse(Store.Get("web_port", "8095"), out var p) && p is > 1024 and < 65535 ? p : 8095;
    public static string Pin => Store.Get("web_pin");
    /// <summary>رمز صاحب المحل: يفتح لوحة الأرقام (الإيراد، الربح، الصناديق) إضافة لصفحة الفني</summary>
    public static string OwnerPin => Store.Get("web_owner_pin");
    public static bool Running => listener != null;
    public static string LastError { get; private set; } = "";

    static TcpListener listener;
    static CancellationTokenSource cts;
    static SynchronizationContext ui;
    static readonly Dictionary<string, string> tokens = new();   // الرمز ← الدور: tech / owner / kiosk
    static readonly Dictionary<string, (int Fails, DateTime Until)> guard = new();

    public static void Start(SynchronizationContext context)
    {
        Stop();
        if (!Enabled || Pin.Length < 4 || Training.Active) return;
        ui = context;
        try
        {
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            cts = new CancellationTokenSource();
            _ = Loop(cts.Token);
            LastError = "";
        }
        catch (Exception ex) { LastError = ex.Message; listener = null; }
    }

    public static void Stop()
    {
        try { cts?.Cancel(); listener?.Stop(); } catch { }
        listener = null;
        cts = null;
    }

    /// <summary>العناوين التي يكتبها الفني في متصفح هاتفه</summary>
    public static List<string> Urls()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => $"http://{a}:{Port}").ToList();
        }
        catch { return new(); }
    }

    static async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => Handle(client));
        }
    }

    // ---------------- HTTP ----------------
    record Req(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, byte[] Body, string Ip)
    {
        public Dictionary<string, string> Form => ParseQuery(Encoding.UTF8.GetString(Body));
        public string Cookie(string name) =>
            Headers.GetValueOrDefault("cookie", "").Split(';').Select(p => p.Trim().Split('=', 2)).Where(p => p.Length == 2 && p[0] == name).Select(p => p[1]).FirstOrDefault();
    }

    static Dictionary<string, string> ParseQuery(string q)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in (q ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            string Dec(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
            try { d[Dec(kv[0])] = kv.Length > 1 ? Dec(kv[1]) : ""; } catch { }
        }
        return d;
    }

    static async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = client.SendTimeout = 15000;
                var stream = client.GetStream();
                var req = await Read(stream, ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
                if (req == null) return;
                var (status, type, body, extra) = Route(req);
                var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n{extra}\r\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(head));
                await stream.WriteAsync(body);
            }
            catch { }
        }
    }

    static async Task<Req> Read(NetworkStream s, string ip)
    {
        var buf = new List<byte>();
        var one = new byte[4096];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await s.ReadAsync(one);
            if (n <= 0) return null;
            buf.AddRange(one.Take(n));
            if (buf.Count > 32_000) return null;
            for (int i = 3; i < buf.Count; i++) if (buf[i - 3] == 13 && buf[i - 2] == 10 && buf[i - 1] == 13 && buf[i] == 10) { headerEnd = i + 1; break; }
        }
        var lines = Encoding.UTF8.GetString(buf.Take(headerEnd).ToArray()).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        var headers = lines.Skip(1).Where(l => l.Contains(':')).Select(l => l.Split(':', 2)).ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
        int len = int.TryParse(headers.GetValueOrDefault("content-length"), out var cl) ? cl : 0;
        if (len > 12_000_000) return null;
        var body = buf.Skip(headerEnd).ToList();
        while (body.Count < len)
        {
            int n = await s.ReadAsync(one);
            if (n <= 0) break;
            body.AddRange(one.Take(n));
        }
        var target = first[1];
        var qi = target.IndexOf('?');
        return new Req(first[0].ToUpperInvariant(), qi >= 0 ? target[..qi] : target, ParseQuery(qi >= 0 ? target[(qi + 1)..] : ""), headers, body.Take(len).ToArray(), ip);
    }

    static T OnUi<T>(Func<T> f)
    {
        T result = default;
        Exception error = null;
        ui.Send(_ => { try { result = f(); } catch (Exception ex) { error = ex; } }, null);
        if (error != null) throw error;
        return result;
    }

    static (string, string, byte[], string) Html(string html, string extra = "") => ("200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), extra);
    static (string, string, byte[], string) Redirect(string to, string extra = "") => ("303 See Other", "text/plain", Array.Empty<byte>(), $"Location: {to}\r\n{extra}");

    static (string, string, byte[], string) Route(Req r)
    {
        if (r.Path == "/login" && r.Method == "POST")
        {
            lock (guard) if (guard.TryGetValue(r.Ip, out var g) && g.Until > DateTime.Now) return Html(Page("الدخول", LoginForm("محاولات كثيرة — انتظر خمس دقائق")));
            var pin = r.Form.GetValueOrDefault("pin");
            string role = pin == null || pin == "" ? null : OwnerPin.Length >= 4 && pin == OwnerPin ? "owner" : pin == Pin ? "tech" : null;
            if (role != null)
            {
                lock (guard) guard.Remove(r.Ip);
                return Redirect(role == "owner" ? "/owner" : "/", Cookie(role));
            }
            lock (guard)
            {
                var f = guard.GetValueOrDefault(r.Ip);
                guard[r.Ip] = (f.Fails + 1, f.Fails + 1 >= 5 ? DateTime.Now.AddMinutes(5) : DateTime.MinValue);
            }
            return Html(Page("الدخول", LoginForm("الرمز غير صحيح")));
        }
        string who = null;
        lock (tokens) if (r.Cookie("wt") is string tk) tokens.TryGetValue(tk, out who);
        if (who == null) return Html(Page("الدخول", LoginForm("")));

        // شاشة الزبون: لا يرى الزبون غير نموذج التسجيل، والخروج منها يحتاج رمز الدخول
        if (who == "kiosk")
        {
            if (r.Path == "/kiosk" && r.Method == "POST")
            {
                if (r.Form.ContainsKey("exit"))
                {
                    if (r.Form.GetValueOrDefault("pin") is string ep && ep != "" && (ep == Pin || ep == OwnerPin)) { lock (tokens) tokens.Remove(r.Cookie("wt")); return Redirect("/"); }
                    return Html(KioskPage("", "الرمز غير صحيح"));
                }
                var (ok, text) = OnUi(() => KioskSubmit(r.Form));
                return Html(KioskPage(ok ? text : "", ok ? "" : text, ok ? null : r.Form));
            }
            return Html(KioskPage("", ""));
        }
        if (r.Path == "/logout") { lock (tokens) tokens.Remove(r.Cookie("wt")); return Redirect("/"); }
        if (r.Path == "/kiosk") { lock (tokens) tokens.Remove(r.Cookie("wt")); return Redirect("/kiosk", Cookie("kiosk")); }
        if (r.Path == "/owner")
            return who == "owner" ? Html(OnUi(OwnerPage)) : Html(Page("غير مسموح", "<div class=card>هذه الصفحة لصاحب المحل فقط — ادخل برمز صاحب المحل.</div>"));

        if (r.Path == "/" && r.Method == "GET") return Html(OnUi(() => ListPage(r.Query.GetValueOrDefault("q", ""), r.Query.GetValueOrDefault("tech", ""))));
        var parts = r.Path.Trim('/').Split('/');
        if (parts.Length >= 2 && parts[0] == "o")
        {
            var id = parts[1];
            if (parts.Length == 2 && r.Method == "GET") return Html(OnUi(() => OrderPage(id, r.Query.GetValueOrDefault("m", ""))));
            if (r.Method == "POST" && parts.Length == 3)
            {
                var msg = OnUi(() => Act(id, parts[2], r));
                return Redirect($"/o/{Uri.EscapeDataString(id)}?m={Uri.EscapeDataString(msg)}");
            }
        }
        return ("404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("غير موجود"), "");
    }

    static string Cookie(string role)
    {
        var t = Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        lock (tokens) tokens[t] = role;
        return $"Set-Cookie: wt={t}; HttpOnly; Path=/; Max-Age=2592000\r\n";
    }

    // ---------------- لوحة صاحب المحل ----------------
    static string OwnerPage()
    {
        var today = Txt.Today;
        var month = today[..7] + "-01";
        var d = Calc.Summarize(today, today);
        var m = Calc.Summarize(month, today);
        var c = Calc.Close(today);
        var open = Store.Orders.Where(Calc.IsOpen).ToList();
        double debts = Calc.GetDebts().Sum(x => x.Debt);
        string Box(string k, string v, string color = "#172033", string sub = "") =>
            $"<div class=card style=\"margin:0\"><div class=k>{E(k)}</div><div style=\"font-size:20px;font-weight:700;color:{color}\">{E(v)}</div>{(sub != "" ? $"<div class=k>{E(sub)}</div>" : "")}</div>";
        var sb = new StringBuilder($"<h3>اليوم — {E(Txt.FmtDate(today))}</h3><div class=grid>");
        sb.Append(Box("المقبوض اليوم", Txt.Money(d.Cash), "#1D8657"));
        sb.Append(Box("في الدرج (نقداً)", Txt.Money(c.Drawer), "#2B55C9", c.Withdrawn > 0 ? "بعد مسحوبات " + Txt.Money(c.Withdrawn) : ""));
        sb.Append(Box("مُسلّم اليوم", d.Active.Count.ToString(), "#172033", "ربح " + Txt.Money(d.Profit)));
        sb.Append(Box("مُستلم اليوم", d.Received.Count.ToString()));
        sb.Append("</div><h3>هذا الشهر</h3><div class=grid>");
        sb.Append(Box("الإيراد", Txt.Money(m.Revenue), "#2B55C9"));
        sb.Append(Box("صافي الربح", Txt.Money(m.Profit), m.Profit >= 0 ? "#1D8657" : "#C43F2C"));
        sb.Append(Box("المسحوبات", Txt.Money(Boxes.WithdrawnIn(month, today)), "#5E6A7E"));
        sb.Append(Box("المصاريف", Txt.Money(m.Expenses), "#C43F2C"));
        sb.Append("</div><h3>الآن</h3><div class=grid>");
        sb.Append(Box("أجهزة في الورشة", open.Count.ToString(), "#172033", $"متأخر {open.Count(Calc.IsLate)} — جاهز {open.Count(o => o.Status == K.Ready)}"));
        sb.Append(Box("ديون الزبائن", Txt.Money(debts), debts > 0 ? "#C43F2C" : "#1D8657"));
        foreach (var (box, bal) in Boxes.Balances().Where(kv => Math.Abs(kv.Value) > 0.001).Select(kv => (kv.Key, kv.Value)))
            sb.Append(Box("صندوق " + box, Txt.Money(bal), "#0D7C86"));
        sb.Append("</div>");
        int kiosk = Store.Orders.Count(o => o.X.Kiosk && !o.X.KioskReviewed);
        if (kiosk > 0) sb.Append($"<div class=card style=\"margin-top:10px\">📝 {kiosk} طلب من شاشة الزبون بانتظار المراجعة</div>");
        var last = Store.Orders.Where(o => o.Status == K.Done && o.DateDelivered == today).OrderByDescending(o => o.CompletedAt, StringComparer.Ordinal).Take(10).ToList();
        if (last.Count > 0)
        {
            sb.Append("<h3>تسليمات اليوم</h3>");
            foreach (var o in last) sb.Append($"<div class=card><div class=row><b>{E(o.Device)}</b><span>{E(Txt.Money(o.Price))}</span></div><div class=k>{E(o.CustomerName)} — {E(o.Technician)}</div></div>");
        }
        sb.Append("<p><a class=btn href=\"/\">الأجهزة</a> <a class=btn href=\"/owner\">تحديث</a></p>");
        return Page("لوحة صاحب المحل", sb.ToString());
    }

    // ---------------- شاشة الزبون (تابلت عند الاستقبال) ----------------
    static string KioskPage(string done, string err, Dictionary<string, string> keep = null)
    {
        string V(string k) => E(keep?.GetValueOrDefault(k, "") ?? "");
        var types = string.Concat(K.IssueTypes.Select(t => $"<option{(keep?.GetValueOrDefault("type") == t ? " selected" : "")}>{E(t)}</option>"));
        var sb = new StringBuilder();
        if (done != "") sb.Append($"<div class=msg style=\"font-size:18px;text-align:center\">{E(done)}</div>");
        if (err != "") sb.Append($"<div class=card><p class=late>{E(err)}</p></div>");
        sb.Append($@"<form class=card method=post action=/kiosk><h3>تسجيل جهاز للصيانة</h3><p class=k>املأ بياناتك وسيراجعها الموظف ويعطيك وصل الاستلام.</p>
<input name=name required placeholder=""الاسم *"" value=""{V("name")}""><input name=phone required inputmode=tel placeholder=""رقم الهاتف *"" value=""{V("phone")}"">
<input name=device required placeholder=""الجهاز والموديل * (مثل: iPhone 13 Pro)"" value=""{V("device")}""><select name=type>{types}</select>
<textarea name=issue rows=3 required placeholder=""ما المشكلة؟ *"">{V("issue")}</textarea>
<input name=ref placeholder=""من أرسلك إلينا؟ (اختياري)"" value=""{V("ref")}"">
<button class=p style=""width:100%;font-size:18px"">إرسال</button></form>
<details class=card><summary class=k>للموظف: خروج من شاشة الزبون</summary><form method=post action=/kiosk><input type=hidden name=exit value=1><input name=pin type=password inputmode=numeric placeholder=""رمز الدخول""><button>خروج</button></form></details>");
        return Page("تسجيل جهاز", sb.ToString());
    }

    /// <summary>ينشئ الطلب من نموذج شاشة الزبون (قيد الفحص، بانتظار مراجعة الموظف)</summary>
    static (bool, string) KioskSubmit(Dictionary<string, string> f)
    {
        string name = f.GetValueOrDefault("name", "").Trim(), phone = Txt.LatinDigits(f.GetValueOrDefault("phone", "").Trim()), device = f.GetValueOrDefault("device", "").Trim(), issue = f.GetValueOrDefault("issue", "").Trim();
        if (name == "" || phone == "" || device == "" || issue == "") return (false, "أكمل الحقول المطلوبة");
        if (name.Length > 80 || device.Length > 80 || issue.Length > 600) return (false, "النص طويل جداً");
        var type = f.GetValueOrDefault("type", "");
        var now = Txt.Now;
        var o = new Order
        {
            Id = Txt.Uid(), RefNo = Txt.GenRef(Calc.UsedRefs()), CustomerName = name, Phone = phone, Device = Models.Similar(device) ?? device,
            IssueType = K.IssueTypes.Contains(type) ? type : K.IssueTypes.LastOrDefault() ?? "أخرى", Issue = issue, Status = K.Check,
            DateReceived = Txt.Today, CreatedAt = now, StartedAt = now, StatusAt = now, UpdatedAt = now, Warranty = K.Warranties.FirstOrDefault() ?? "",
            PaymentStatus = K.PayNone,
        };
        o.X.Kiosk = true;
        o.X.Source = "شاشة الزبون";
        o.X.Branch = Branches.Current;
        var rf = f.GetValueOrDefault("ref", "").Trim();
        if (rf != "" && Txt.Fold(rf) != Txt.Fold(name))
        {
            o.X.ReferredBy = rf;
            o.X.ReferredPhone = Store.Orders.Where(p => Txt.Fold(p.CustomerName) == Txt.Fold(rf) && p.Phone != "").Select(p => p.Phone).FirstOrDefault() ?? "";
        }
        if (AutoAssign.On) o.Technician = AutoAssign.Pick(o.IssueType);
        Store.SaveOrder(o);
        Store.NotifyChanged();
        Notify.Alert("kiosk", $"📝 طلب جديد من شاشة الزبون\n{o.RefNo} — {name} ({phone})\n{o.Device} — {o.IssueType}");
        return (true, $"شكراً {name}! رقم طلبك {o.RefNo} — تفضّل عند الموظف لتسليم الجهاز.");
    }

    // ---------------- الإجراءات (داخل البرنامج) ----------------
    static string Act(string id, string action, Req r)
    {
        var o = Calc.Find(id);
        if (o == null) return "الطلب غير موجود";
        var f = action == "photo" ? null : r.Form;
        switch (action)
        {
            case "status":
            {
                var s = f.GetValueOrDefault("s", "");
                if (!K.OpenStatuses.Contains(s) || !Calc.IsOpen(o)) return "لا يمكن تغيير هذه الحالة من الهاتف";
                if (s == K.Ready && QC.Required && !QC.Done(o)) return "أكمل فحص الجودة أولاً (أسفل الصفحة)";
                Store.SaveOrder(StatusOps.Apply(o, s));
                Store.NotifyChanged();
                return "تم: " + s;
            }
            case "note":
            {
                var t = f.GetValueOrDefault("text", "").Trim();
                if (t == "") return "اكتب الملاحظة";
                var n = o.Clone();
                n.X.Chat.Add(new Note { At = Txt.Now, Author = f.GetValueOrDefault("author", "").Trim(), Text = t });
                n.UpdatedAt = Txt.Now;
                Store.SaveOrder(n);
                Store.NotifyChanged();
                return "أُضيفت الملاحظة";
            }
            case "timer":
            {
                var n = WorkTimer.Toggle(o, f.GetValueOrDefault("tech", o.Technician));
                Store.NotifyChanged();
                return WorkTimer.IsRunning(n) ? "بدأ التوقيت" : "أُوقف التوقيت";
            }
            case "qc":
            {
                var n = o.Clone();
                foreach (var item in QC.Items)
                    if (f.GetValueOrDefault("q" + Array.IndexOf(QC.Items, item)) is "ok" or "bad") n.X.QC[item] = f["q" + Array.IndexOf(QC.Items, item)];
                if (!QC.Done(n)) return "علّم كل البنود";
                n.X.QcAt = Txt.Now;
                n.X.QcBy = f.GetValueOrDefault("by", "").Trim();
                n.UpdatedAt = Txt.Now;
                Store.SaveOrder(n);
                Store.NotifyChanged();
                return QC.Passed(n) ? "سُجّل الفحص: كل البنود تعمل" : "سُجّل الفحص (فيه بنود لا تعمل)";
            }
            case "photo":
            {
                var json = JsonNode.Parse(Encoding.UTF8.GetString(r.Body));
                var data = json?["data"]?.ToString() ?? "";
                int i = data.IndexOf("base64,", StringComparison.Ordinal);
                if (i < 0) return "صورة غير صالحة";
                var bytes = Convert.FromBase64String(data[(i + 7)..]);
                if (bytes.Length > 6_000_000) return "الصورة كبيرة جداً";
                var n = o.Clone();
                n.PhotoRef = "ph_" + n.Id;
                Store.SetPhoto(n.PhotoRef, bytes);
                n.UpdatedAt = Txt.Now;
                Store.SaveOrder(n);
                Store.NotifyChanged();
                return "حُفظت الصورة";
            }
        }
        return "";
    }

    // ---------------- الصفحات ----------------
    static string E(string s) => Txt.Esc(s);

    static string Page(string title, string body) => $@"<!DOCTYPE html><html lang=""ar"" dir=""rtl""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>{E(title)}</title><style>
*{{box-sizing:border-box}}body{{font-family:Tahoma,Arial,sans-serif;margin:0;background:#EEF1F6;color:#172033}}
header{{background:#2B55C9;color:#fff;padding:12px 14px;display:flex;justify-content:space-between;align-items:center;position:sticky;top:0}}
header a{{color:#fff;text-decoration:none}} main{{padding:12px;max-width:720px;margin:auto}}
.card{{background:#fff;border-radius:12px;padding:12px;margin-bottom:10px;box-shadow:0 1px 3px #0001;display:block;color:inherit;text-decoration:none}}
.row{{display:flex;justify-content:space-between;gap:8px}} .k{{color:#5A6478;font-size:13px}} .tag{{background:#FCF0DC;color:#8A5610;border-radius:6px;padding:1px 8px;font-weight:700;direction:ltr}}
.st{{border-radius:20px;padding:2px 10px;font-size:12px;background:#E6ECFB;color:#2B55C9}} .late{{color:#C43F2C;font-weight:700}}
button,.btn{{font:inherit;border:0;border-radius:10px;padding:10px 14px;background:#E9EDF3;color:#172033;margin:3px 0}} .p{{background:#2B55C9;color:#fff}} .g{{background:#1D8657;color:#fff}} .r{{background:#C43F2C;color:#fff}}
input,select,textarea{{font:inherit;width:100%;padding:10px;border:1px solid #CDD3DF;border-radius:10px;margin:4px 0}} .msg{{background:#E1F3EA;color:#1D8657;padding:10px;border-radius:10px;margin-bottom:10px}}
.grid{{display:grid;grid-template-columns:1fr 1fr;gap:6px}} h3{{margin:6px 0}} img{{max-width:100%;border-radius:10px}}
</style></head><body><header><a href=""/"">🔧 {E(Store.ShopName)}</a><span style=""font-size:13px""><a href=""/kiosk"">شاشة الزبون</a> · <a href=""/logout"">خروج</a></span></header><main>{body}</main></body></html>";

    static string LoginForm(string err) => $@"<div class=""card""><h3>صفحة الفني</h3>{(err != "" ? $"<p class=late>{E(err)}</p>" : "")}
<form method=post action=/login><input name=pin type=password inputmode=numeric placeholder=""رمز الدخول (PIN)"" autofocus><button class=p style=""width:100%"">دخول</button></form></div>";

    static string ListPage(string q, string tech)
    {
        var f = Txt.Fold(q);
        var list = Store.Orders.Where(o => Calc.IsOpen(o) && (tech == "" || Txt.Fold(o.Technician) == Txt.Fold(tech)) && (f == "" || Txt.Matches(Calc.Haystack(o), f)))
            .OrderByDescending(Calc.IsLate).ThenBy(o => o.DateEstimated == "" ? "9999" : o.DateEstimated, StringComparer.Ordinal).Take(80).ToList();
        var techOpts = string.Concat(Techs.All.Select(t => $"<option{(t.Name == tech ? " selected" : "")}>{E(t.Name)}</option>"));
        var sb = new StringBuilder($@"<form class=card method=get><div class=grid><input name=q value=""{E(q)}"" placeholder=""بحث: مرجع، زبون، جهاز""><select name=tech onchange=""this.form.submit()""><option value="""">كل الفنيين</option>{techOpts}</select></div></form>");
        if (list.Count == 0) sb.Append("<div class=card>لا توجد أجهزة مفتوحة</div>");
        foreach (var o in list)
            sb.Append($@"<a class=card href=""/o/{Uri.EscapeDataString(o.Id)}""><div class=row><b>{E(o.Device)}</b><span class=tag>{E(o.RefNo)}</span></div>
<div class=row><span class=k>{E(o.IssueType)} — {E(o.CustomerName)}</span><span class=st>{E(o.Status)}</span></div>
{(Calc.IsLate(o) ? $"<div class=late>متأخر {Calc.LateDays(o)} يوم</div>" : o.DateEstimated != "" ? $"<div class=k>الموعد {E(Txt.FmtShortDate(o.DateEstimated))}</div>" : "")}{(WorkTimer.IsRunning(o) ? "<div style=\"color:#1D8657\">⏱ يعمل الآن</div>" : "")}</a>");
        return Page("الأجهزة", sb.ToString());
    }

    static string OrderPage(string id, string msg)
    {
        var o = Calc.Find(id);
        if (o == null) return Page("غير موجود", "<div class=card>الطلب غير موجود</div>");
        var sb = new StringBuilder();
        if (msg != "") sb.Append($"<div class=msg>{E(msg)}</div>");
        sb.Append($@"<div class=card><div class=row><h3>{E(o.Device)}</h3><span class=tag>{E(o.RefNo)}</span></div>
<div class=k>{E(o.CustomerName)} — استُلم {E(Txt.FmtShortDate(o.DateReceived))}{(o.DateEstimated != "" ? " — الموعد " + E(Txt.FmtShortDate(o.DateEstimated)) : "")}</div>
<p><b>{E(o.IssueType)}:</b> {E(o.Issue)}</p>{(o.Passcode != "" ? $"<p class=k>رمز القفل: <b dir=ltr>{E(o.Passcode)}</b></p>" : "")}
{(o.X.Items.Count > 0 ? "<p class=k>البنود: " + E(string.Join("، ", o.X.Items.Where(i => i.Approved).Select(i => i.Desc))) + "</p>" : "")}
{(o.Parts.Count > 0 ? "<p class=k>القطع: " + E(string.Join("، ", o.Parts.Select(p => p.Name))) + "</p>" : "")}
{(o.Notes != "" ? $"<p class=k>ملاحظات: {E(o.Notes)}</p>" : "")}<p>الحالة: <span class=st>{E(o.Status)}</span></p></div>");
        var q = Uri.EscapeDataString(o.Id);
        sb.Append($"<div class=card><h3>تغيير الحالة</h3><form method=post action=\"/o/{q}/status\">");
        foreach (var s in K.OpenStatuses.Where(s => s != o.Status))
            sb.Append($"<button name=s value=\"{E(s)}\" class=\"{(s == K.Ready ? "g" : "")}\">{E(s)}</button> ");
        sb.Append("</form></div>");
        bool run = WorkTimer.IsRunning(o);
        sb.Append($@"<div class=card><div class=row><h3>وقت العمل</h3><b>{E(WorkTimer.Text(WorkTimer.Total(o)))}</b></div>
<form method=post action=""/o/{q}/timer""><input type=hidden name=tech value=""{E(o.Technician)}""><button class=""{(run ? "r" : "g")}"" style=""width:100%"">{(run ? "⏹ إيقاف" : "▶ بدء العمل")}</button></form></div>");
        sb.Append($@"<div class=card><h3>صورة الجهاز</h3><input type=file accept=""image/*"" capture=environment id=ph><div id=pst class=k></div>
<script>document.getElementById('ph').onchange=function(e){{var f=e.target.files[0];if(!f)return;var st=document.getElementById('pst');st.textContent='جارٍ الرفع...';
var rd=new FileReader();rd.onload=function(){{var im=new Image();im.onload=function(){{var m=1280,w=im.width,h=im.height;if(w>m||h>m){{if(w>h){{h=h*m/w;w=m}}else{{w=w*m/h;h=m}}}}
var c=document.createElement('canvas');c.width=w;c.height=h;c.getContext('2d').drawImage(im,0,0,w,h);
fetch('/o/{q}/photo',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{data:c.toDataURL('image/jpeg',0.75)}})}}).then(function(r){{location.href=r.url}});}};im.src=rd.result}};rd.readAsDataURL(f)}}</script></div>");
        sb.Append($"<div class=card><h3>فحص الجودة</h3><form method=post action=\"/o/{q}/qc\">");
        var items = QC.Items;
        for (int i = 0; i < items.Length; i++)
        {
            var cur = o.X.QC.GetValueOrDefault(items[i], "");
            sb.Append($@"<div class=row style=""align-items:center""><span>{E(items[i])}</span><select name=q{i} style=""width:auto""><option value=""""{(cur == "" ? " selected" : "")}>—</option>
<option value=ok{(cur == "ok" ? " selected" : "")}>يعمل</option><option value=bad{(cur == "bad" ? " selected" : "")}>لا يعمل</option></select></div>");
        }
        sb.Append($"<input name=by placeholder=\"فحصه\" value=\"{E(o.X.QcBy ?? o.Technician)}\"><button class=p style=\"width:100%\">حفظ الفحص</button></form></div>");
        sb.Append("<div class=card><h3>ملاحظات الموظفين</h3>");
        foreach (var n in o.X.Chat.TakeLast(10)) sb.Append($"<p><b>{E(n.Author)}</b> <span class=k>{E(Txt.FmtShortDate(Txt.Cut10(n.At)))}</span><br>{E(n.Text)}</p>");
        sb.Append($@"<form method=post action=""/o/{q}/note""><input name=author placeholder=""اسمك"" value=""{E(o.Technician)}""><textarea name=text rows=2 placeholder=""ملاحظة""></textarea><button class=p style=""width:100%"">إضافة</button></form></div>");
        sb.Append("<a class=btn href=\"/\">← كل الأجهزة</a>");
        return Page(o.Device, sb.ToString());
    }
}
