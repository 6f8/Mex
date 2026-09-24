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

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (s, e) =>
            MessageBox.Show(e.Exception.Message, "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error);

        try { Db.Init(); }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر فتح قاعدة البيانات:\n" + ex.Message, "رصيد", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (var login = new LoginForm())
            if (login.ShowDialog() != DialogResult.OK) return;

        Application.Run(new MainForm());
    }
}
