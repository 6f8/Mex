using System.Globalization;
using Raseed;

namespace Workshop;

static class Program
{
    const string RestartArg = "--restarted";

    /// <summary>إعادة تشغيل البرنامج (بعد تغيير الألوان)</summary>
    public static void Restart()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, RestartArg) { UseShellExecute = false }); }
        catch { }
        Environment.Exit(0);
    }

    [STAThread]
    static void Main()
    {
        // ثقافة ثابتة: أرقام إنجليزية وتواريخ ميلادية مهما كانت إعدادات ويندوز
        var ci = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;

        // بيانات الورشة في مجلد مستقل عن برنامج رصيد
        AppPaths.DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RaseedWorkshop");
        // وضع التدريب: بيانات تجريبية في مجلد منفصل تماماً
        Training.RealDir = AppPaths.DataDir;
        if (File.Exists(Training.FlagFile)) { Training.Active = true; AppPaths.DataDir = Training.Dir; }

        using var mutex = new Mutex(true, @"Local\RaseedWorkshop.SingleInstance", out bool first);
        if (!first && Environment.GetCommandLineArgs().Contains(RestartArg))
        {
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            MessageBox.Show("برنامج الورشة يعمل مسبقاً على هذا الجهاز.", "ورشة الصيانة", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }

        ApplicationConfiguration.Initialize();
        FontKit.Init();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

        try { Store.Init(); }
        catch (Exception ex)
        {
            Log(ex);
            Dialogs.Error("تعذر فتح قاعدة البيانات:\n" + ex.Message, "ورشة الصيانة");
            return;
        }
        try { Theme.Apply(Store.Get("ui_theme", "corporate")); } catch { Theme.Apply("corporate"); }
        // تكبير الواجهة (حجم الخط ووضع اللمس)
        if (int.TryParse(Store.Get("ui_scale", "100"), out var zoom) && zoom != 100) Dpi.SetUserScale(zoom / 100f);
        if (Training.Active && !Store.Flag("demo_seeded"))
            try { Demo.Seed(); Store.NotifyChanged(); } catch (Exception ex) { Log(ex); }

        Application.Run(new MainForm());
    }

    static void ReportError(Exception ex)
    {
        Log(ex);
        var msg = ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 }
            ? "قاعدة البيانات مشغولة حالياً. انتظر لحظة ثم أعد المحاولة."
            : ex.Message;
        try { Dialogs.Error(msg + "\n\nتم تسجيل التفاصيل في ملف errors.log داخل مجلد البيانات.", "خطأ غير متوقع"); }
        catch { MessageBox.Show(msg, "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>سجل الأخطاء في مجلد البيانات</summary>
    public static void Log(Exception ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Store.DataDir);
            File.AppendAllText(Path.Combine(Store.DataDir, "errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
