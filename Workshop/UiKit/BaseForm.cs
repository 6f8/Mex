namespace Raseed;

/// <summary>نموذج أساسي: عربي من اليمين لليسار، خط وألوان موحدة</summary>
public class BaseForm : Form
{
    public BaseForm()
    {
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = Theme.F();
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
    }

    /// <summary>تم تكبير أبعاد الشاشة حسب دقة العرض (مرة واحدة فقط)</summary>
    internal bool DpiScaled { get; set; }

    protected override void OnLoad(EventArgs e)
    {
        // النوافذ المستقلة تُكبَّر هنا؛ الشاشات داخل التبويبات تُكبَّر عند فتحها في النافذة الرئيسية
        if (TopLevel && !DpiScaled)
        {
            Dpi.ScaleTree(this);
            FitToScreen();
        }
        base.OnLoad(e);
    }

    /// <summary>لا تتجاوز النافذة مساحة الشاشة (شاشات صغيرة أو تكبير 150%) وتبقى في الوسط</summary>
    void FitToScreen()
    {
        var area = Screen.FromPoint(Owner != null ? Owner.Location : Cursor.Position).WorkingArea;
        if (MinimumSize.Width > area.Width || MinimumSize.Height > area.Height)
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        if (WindowState == FormWindowState.Normal)
        {
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            if (StartPosition == FormStartPosition.CenterParent && Owner != null)
                Location = new Point(Owner.Left + (Owner.Width - Width) / 2, Owner.Top + (Owner.Height - Height) / 2);
            else if (StartPosition is FormStartPosition.CenterScreen or FormStartPosition.CenterParent)
                Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
            Location = new Point(Math.Max(area.X, Math.Min(Left, area.Right - Width)), Math.Max(area.Y, Math.Min(Top, area.Bottom - Height)));
        }
    }

    /// <summary>يُستدعى كلما عادت الشاشة لتكون التبويب النشط (لتحديث القوائم مثلًا)</summary>
    public virtual void OnPageActivated() { }
    /// <summary>قبل إغلاق التبويب: false لإلغاء الإغلاق (مثل فاتورة لم تُحفظ)</summary>
    public virtual bool ConfirmClose() => true;
}
