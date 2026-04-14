using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GlobalCpsTester;

internal static class UiTheme
{
    public static readonly Color Background = ColorTranslator.FromHtml("#181818");
    public static readonly Color Text = ColorTranslator.FromHtml("#FFFFFF");
    public static readonly Color Subtext = ColorTranslator.FromHtml("#A3A3A2");
    public static readonly Color Edge = ColorTranslator.FromHtml("#2A2B2D");
    public static readonly Color Accent = ColorTranslator.FromHtml("#005FB8");
    public static readonly Color Button = ColorTranslator.FromHtml("#222223");
    public static readonly Color ButtonHover = ColorTranslator.FromHtml("#2E3032");

    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaBorderColor = 34;
    private const int DwmaCaptionColor = 35;
    private const int DwmaTextColor = 36;
    private const int DwmWindowCornerPreferenceRound = 2;

    public static void TryApplyWindowChrome(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int darkModeEnabled = 1;
        DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkMode, ref darkModeEnabled, sizeof(int));

        int borderColor = ColorTranslator.ToWin32(Edge);
        DwmSetWindowAttribute(handle, DwmaBorderColor, ref borderColor, sizeof(int));

        int captionColor = ColorTranslator.ToWin32(Background);
        DwmSetWindowAttribute(handle, DwmaCaptionColor, ref captionColor, sizeof(int));

        int textColor = ColorTranslator.ToWin32(Text);
        DwmSetWindowAttribute(handle, DwmaTextColor, ref textColor, sizeof(int));

        int roundedCorners = DwmWindowCornerPreferenceRound;
        DwmSetWindowAttribute(handle, DwmaWindowCornerPreference, ref roundedCorners, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        GraphicsPath path = new();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);

        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }
}

internal sealed class ThemedCheckBox : CheckBox
{
    private const int BoxSize = 20;
    private const int TextGap = 8;
    private const int BoxRadius = 6;

    public ThemedCheckBox()
    {
        AutoSize = true;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        Margin = new Padding(0);
        MinimumSize = new Size(84, 32);

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int width = BoxSize + TextGap + textSize.Width + 6;
        int height = Math.Max(BoxSize, textSize.Height) + 10;
        return new Size(width, height);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle box = new(
            x: 0,
            y: Math.Max(0, (Height - BoxSize) / 2),
            width: BoxSize,
            height: BoxSize);

        using SolidBrush fillBrush = new(Checked ? UiTheme.Accent : UiTheme.Background);
        using Pen borderPen = new(UiTheme.Edge, 1f);
        using GraphicsPath boxPath = UiTheme.CreateRoundedRectanglePath(box, BoxRadius);
        e.Graphics.FillPath(fillBrush, boxPath);
        e.Graphics.DrawPath(borderPen, boxPath);

        if (Checked)
        {
            using Pen checkPen = new(UiTheme.Text, 2.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            PointF[] checkMark =
            {
                new(box.Left + 4.5f, box.Top + 10.5f),
                new(box.Left + 8.25f, box.Bottom - 4.75f),
                new(box.Right - 4.5f, box.Top + 5f)
            };

            e.Graphics.DrawLines(checkPen, checkMark);
        }

        Rectangle textBounds = new(
            box.Right + TextGap,
            0,
            Math.Max(0, Width - box.Right - TextGap),
            Height);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? UiTheme.Text : UiTheme.Subtext,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class ThemedButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    public ThemedButton()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = UiTheme.Button;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        ForeColor = UiTheme.Text;
        Margin = new Padding(0);
        MinimumSize = new Size(84, 32);
        Padding = new Padding(12, 4, 12, 4);
        UseVisualStyleBackColor = false;

        FlatAppearance.BorderSize = 0;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int width = textSize.Width + Padding.Horizontal + 2;
        int height = textSize.Height + Padding.Vertical + 2;
        return new Size(
            Math.Max(width, MinimumSize.Width),
            Math.Max(height, MinimumSize.Height));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _isPressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        using GraphicsPath path = UiTheme.CreateRoundedRectanglePath(ClientRectangle, 9);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Background);

        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        Color fillColor = _isPressed
            ? UiTheme.Accent
            : _isHovered
                ? UiTheme.ButtonHover
                : UiTheme.Button;

        using SolidBrush fillBrush = new(fillColor);
        using Pen borderPen = new(UiTheme.Edge, 1f);
        using GraphicsPath path = UiTheme.CreateRoundedRectanglePath(bounds, 9);
        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            bounds,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
