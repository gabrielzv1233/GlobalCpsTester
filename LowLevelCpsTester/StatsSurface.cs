using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LowLevelCpsTester;

internal sealed class StatsSurface : Control
{
    public const int OuterPadding = 5;
    public const int ControlWidth = 90;
    public const int ControlHeight = 32;
    public const int GridColumns = 3;
    public const int GridRows = 2;
    public const int InstantBottomMargin = 0;
    public const int PeakBottomMargin = 0;
    public const int TotalBottomMargin = 0;
    public const int SinceLastBottomMargin = 5;

    private const string SubtitleSample = "Captures left, right, middle, and X1/2";
    private const string InstantSample = "Instant CPS: 0.00";
    private const string PeakSample = "Peak CPS: 0.00";
    private const string TotalSample = "Total clicks: 0";
    private const string SinceLastSample = "Ms since last click: 0.0";

    private readonly Font _subtitleFont = new("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _instantFont = new("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _infoFont = new("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

    private readonly int _subtitleHeight;
    private readonly int _instantHeight;
    private readonly int _infoHeight;
    private readonly int _statsWidth;
    private readonly int _controlsTop;

    private ClickSnapshot _snapshot;
    private string _subtitle = SubtitleSample;

    public StatsSurface()
    {
        BackColor = UiTheme.Background;
        Cursor = Cursors.Default;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        TabStop = false;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);

        _subtitleHeight = MeasureTextHeight(_subtitleFont, SubtitleSample);
        _instantHeight = MeasureTextHeight(_instantFont, InstantSample);
        _infoHeight = MeasureTextHeight(_infoFont, PeakSample);
        _statsWidth = Math.Max(
            Math.Max(MeasureTextWidth(_instantFont, InstantSample), MeasureTextWidth(_infoFont, PeakSample)),
            Math.Max(MeasureTextWidth(_infoFont, TotalSample), MeasureTextWidth(_infoFont, SinceLastSample)));
        _controlsTop = OuterPadding
            + _subtitleHeight
            + _instantHeight + InstantBottomMargin
            + _infoHeight + PeakBottomMargin
            + _infoHeight + TotalBottomMargin
            + _infoHeight + SinceLastBottomMargin;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ClickSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            if (_snapshot.Equals(value))
            {
                return;
            }

            _snapshot = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle
    {
        get => _subtitle;
        set
        {
            string nextValue = value ?? string.Empty;
            if (string.Equals(_subtitle, nextValue, StringComparison.Ordinal))
            {
                return;
            }

            _subtitle = nextValue;
            Invalidate();
        }
    }

    [Browsable(false)]
    public int StatsWidth => _statsWidth;

    [Browsable(false)]
    public int GridWidth => ControlWidth * GridColumns;

    [Browsable(false)]
    public int ControlsTop => _controlsTop;

    public Size GetIdealClientSize()
    {
        int width = Math.Max(_statsWidth, GridWidth) + (OuterPadding * 2);
        int height = _controlsTop + (ControlHeight * GridRows) + OuterPadding;
        return new Size(width, height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _subtitleFont.Dispose();
            _instantFont.Dispose();
            _infoFont.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        int x = OuterPadding;
        int y = OuterPadding;
        int width = Math.Max(0, Width - (OuterPadding * 2));

        DrawTextLine(e.Graphics, _subtitle, _subtitleFont, UiTheme.Subtext, new Rectangle(x, y, width, _subtitleHeight));
        y += _subtitleHeight;

        DrawTextLine(
            e.Graphics,
            $"Instant CPS: {FormatTruncated(_snapshot.InstantCps, 2)}",
            _instantFont,
            UiTheme.Text,
            new Rectangle(x, y, width, _instantHeight));
        y += _instantHeight + InstantBottomMargin;

        DrawTextLine(
            e.Graphics,
            $"Peak CPS: {FormatTruncated(_snapshot.PeakCps, 2)}",
            _infoFont,
            UiTheme.Text,
            new Rectangle(x, y, width, _infoHeight));
        y += _infoHeight + PeakBottomMargin;

        DrawTextLine(
            e.Graphics,
            $"Total clicks: {_snapshot.TotalClicks}",
            _infoFont,
            UiTheme.Text,
            new Rectangle(x, y, width, _infoHeight));
        y += _infoHeight + TotalBottomMargin;

        DrawTextLine(
            e.Graphics,
            double.IsNaN(_snapshot.SinceLastClickMs)
                ? "Ms since last click: --"
                : $"Ms since last click: {FormatTruncated(_snapshot.SinceLastClickMs, 1)}",
            _infoFont,
            UiTheme.Text,
            new Rectangle(x, y, width, _infoHeight));
    }

    protected override void WndProc(ref Message m)
    {
        if (IsIgnoredMouseMessage(m.Msg))
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    private static void DrawTextLine(Graphics graphics, string text, Font font, Color color, Rectangle bounds)
    {
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            bounds,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private static int MeasureTextWidth(Font font, string sampleText)
    {
        Size measured = TextRenderer.MeasureText(sampleText, font, Size.Empty, TextFormatFlags.NoPadding);
        return measured.Width + 4;
    }

    private static int MeasureTextHeight(Font font, string sampleText)
    {
        return TextRenderer.MeasureText(sampleText, font, Size.Empty, TextFormatFlags.NoPadding).Height + 2;
    }

    private static string FormatTruncated(double value, int decimals)
    {
        double factor = Math.Pow(10, decimals);
        double truncatedValue = Math.Truncate(value * factor) / factor;
        return truncatedValue.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static bool IsIgnoredMouseMessage(int message)
    {
        return message is
            0x0200 or
            0x0201 or
            0x0202 or
            0x0203 or
            0x0204 or
            0x0205 or
            0x0206 or
            0x0207 or
            0x0208 or
            0x0209 or
            0x020A or
            0x020B or
            0x020C or
            0x020D or
            0x02A1 or
            0x02A3;
    }
}
