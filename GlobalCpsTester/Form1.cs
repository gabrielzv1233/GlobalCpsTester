using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace GlobalCpsTester;

public sealed class Form1 : Form
{
    private readonly ClickMetrics _metrics = new();
    private readonly GlobalMouseHook _hook;
    private readonly StatsSurface _statsSurface;
    private readonly System.Threading.Timer _metricsTimer;
    private readonly System.Threading.Timer _uiTimer;

    private volatile bool _trackLeft = true;
    private volatile bool _trackRight = true;
    private volatile bool _trackMiddle = true;
    private volatile bool _trackX1 = true;
    private volatile bool _trackX2 = true;

    private int _metricsAdvanceQueued;
    private int _refreshQueued;
    private int _isClosing;

    public Form1()
    {
        _hook = new GlobalMouseHook(HandleGlobalClick);

        Text = "Global CPS Tester";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = false;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(0);
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterScreen;

        _statsSurface = new StatsSurface
        {
            Subtitle = "Captures left, right, middle, and X1/2"
        };

        Size idealClientSize = _statsSurface.GetIdealClientSize();
        ClientSize = idealClientSize;
        MinimumSize = Size;
        MaximumSize = Size;

        _statsSurface.Bounds = new Rectangle(Point.Empty, idealClientSize);
        Controls.Add(_statsSurface);

        ThemedCheckBox chkLeft = CreateFilterCheckBox("LMB", value => _trackLeft = value);
        ThemedCheckBox chkRight = CreateFilterCheckBox("RMB", value => _trackRight = value);
        ThemedCheckBox chkMiddle = CreateFilterCheckBox("MMB", value => _trackMiddle = value);
        ThemedCheckBox chkX1 = CreateFilterCheckBox("X1", value => _trackX1 = value);
        ThemedCheckBox chkX2 = CreateFilterCheckBox("X2", value => _trackX2 = value);
        ThemedButton btnReset = CreateResetButton();

        ConfigureControlBounds(chkLeft, 0, 0);
        ConfigureControlBounds(chkRight, 1, 0);
        ConfigureControlBounds(chkMiddle, 2, 0);
        ConfigureControlBounds(chkX1, 0, 1);
        ConfigureControlBounds(chkX2, 1, 1);
        ConfigureControlBounds(btnReset, 2, 1);

        _statsSurface.Controls.Add(chkLeft);
        _statsSurface.Controls.Add(chkRight);
        _statsSurface.Controls.Add(chkMiddle);
        _statsSurface.Controls.Add(chkX1);
        _statsSurface.Controls.Add(chkX2);
        _statsSurface.Controls.Add(btnReset);

        chkLeft.BringToFront();
        chkRight.BringToFront();
        chkMiddle.BringToFront();
        chkX1.BringToFront();
        chkX2.BringToFront();
        btnReset.BringToFront();

        _metricsTimer = new System.Threading.Timer(
            _ => AdvanceMetrics(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        _uiTimer = new System.Threading.Timer(
            _ => QueueUiRefresh(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        Shown += (_, _) => StartMonitoring();
        FormClosed += (_, _) => Cleanup();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.TryApplyWindowChrome(Handle);
    }

    private static ThemedCheckBox CreateFilterCheckBox(string text, Action<bool> onChange)
    {
        ThemedCheckBox checkBox = new()
        {
            Checked = true,
            Text = text
        };
        checkBox.CheckedChanged += (_, _) => onChange(checkBox.Checked);
        return checkBox;
    }

    private ThemedButton CreateResetButton()
    {
        ThemedButton button = new()
        {
            Margin = Padding.Empty,
            MinimumSize = new Size(StatsSurface.ControlWidth, StatsSurface.ControlHeight),
            TabStop = false,
            Text = "Reset"
        };
        button.Click += (_, _) =>
        {
            _metrics.Reset();
            RenderSnapshot(_metrics.GetCachedSnapshot());
        };

        return button;
    }

    private void ConfigureControlBounds(Control control, int column, int row)
    {
        int x = StatsSurface.OuterPadding + (column * StatsSurface.ControlWidth);
        int y = _statsSurface.ControlsTop + (row * StatsSurface.ControlHeight);

        control.Margin = Padding.Empty;
        control.Location = new Point(x, y);
        control.Size = new Size(StatsSurface.ControlWidth, StatsSurface.ControlHeight);
    }

    private void StartMonitoring()
    {
        if (!_hook.TryStart())
        {
            string error = _hook.LastError ?? "Unknown error.";
            MessageBox.Show(
                this,
                $"Failed to install the global mouse hook.\n\n{error}",
                "Hook startup failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _metrics.Advance(Environment.TickCount64);
        _metricsTimer.Change(0, 10);
        _uiTimer.Change(0, 50);
        RenderSnapshot(_metrics.GetCachedSnapshot());
    }

    private void Cleanup()
    {
        Interlocked.Exchange(ref _isClosing, 1);
        _metricsTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _uiTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _metricsTimer.Dispose();
        _uiTimer.Dispose();
        _hook.Dispose();
    }

    private void AdvanceMetrics()
    {
        if (Volatile.Read(ref _isClosing) == 1)
        {
            return;
        }

        if (Interlocked.Exchange(ref _metricsAdvanceQueued, 1) == 1)
        {
            return;
        }

        try
        {
            _metrics.Advance(Environment.TickCount64);
        }
        finally
        {
            Interlocked.Exchange(ref _metricsAdvanceQueued, 0);
        }
    }

    private void QueueUiRefresh()
    {
        if (!IsHandleCreated || IsDisposed || Disposing || Volatile.Read(ref _isClosing) == 1)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(ProcessQueuedRefresh));
        }
        catch (ObjectDisposedException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private void ProcessQueuedRefresh()
    {
        try
        {
            if (!IsDisposed && !Disposing)
            {
                UpdateUi();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private void HandleGlobalClick(MouseButtonKind button, long timestampMs)
    {
        if (ShouldTrack(button))
        {
            _metrics.RegisterClick(timestampMs);
        }
    }

    private bool ShouldTrack(MouseButtonKind button)
    {
        return button switch
        {
            MouseButtonKind.Left => _trackLeft,
            MouseButtonKind.Right => _trackRight,
            MouseButtonKind.Middle => _trackMiddle,
            MouseButtonKind.XButton1 => _trackX1,
            MouseButtonKind.XButton2 => _trackX2,
            _ => false
        };
    }

    private void UpdateUi()
    {
        RenderSnapshot(_metrics.GetCachedSnapshot());
    }

    private void RenderSnapshot(ClickSnapshot snapshot)
    {
        _statsSurface.Snapshot = snapshot;
    }

    private static string FormatTruncated(double value, int decimals)
    {
        double factor = Math.Pow(10, decimals);
        double truncatedValue = Math.Truncate(value * factor) / factor;
        return truncatedValue.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }
}
