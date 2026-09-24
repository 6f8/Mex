using System.Globalization;

namespace Raseed;

static class Program
{
    [STAThread]
    static void Main()
    {
        // ثقافة ثابتة: تواريخ ميلادية وأرقام إنجليزية مهما كانت إعدادات ويندوز
        var ci = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;

        // نسخة واحدة فقط: تشغيل نسختين معًا يسبب تعارض الكتابة على قاعدة البيانات ومنفذ ربط الهاتف
        using var mutex = new Mutex(true, @"Local\Raseed.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("البرنامج يعمل مسبقًا على هذا الجهاز.", "رصيد", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }

        ApplicationConfiguration.Initialize();
        FontKit.Init();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

        try { Db.Init(); }
        catch (Exception ex)
        {
            Log(ex);
            Dialogs.Error("تعذر فتح قاعدة البيانات:\n" + ex.Message, "رصيد");
            return;
        }

        // حلقة الدخول: «تسجيل الخروج» يعيد إلى شاشة الدخول بدل إغلاق البرنامج
        while (true)
        {
            using (var login = new LoginForm())
                if (login.ShowDialog() != DialogResult.OK) return;

            if (Session.UsingDefaultPassword)
                using (var setup = new SetupDialog()) setup.ShowDialog();

            var main = new MainForm();
            Application.Run(main);
            if (!main.LoggedOut) return;
            Session.Logout();
        }
    }

    static void ReportError(Exception ex)
    {
        Log(ex);
        var msg = ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 }
            ? "قاعدة البيانات مشغولة حاليًا. انتظر لحظة ثم أعد المحاولة."
            : ex.Message;
        try { Dialogs.Error(msg + "\n\nتم تسجيل التفاصيل في ملف errors.log داخل مجلد البيانات.", "خطأ غير متوقع"); }
        catch { MessageBox.Show(msg, "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>سجل الأخطاء: يساعد على معرفة سبب أي مشكلة لاحقًا</summary>
    static void Log(Exception ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Db.DataDir);
            File.AppendAllText(Path.Combine(Db.DataDir, "errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
