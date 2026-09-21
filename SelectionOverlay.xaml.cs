using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace ClickyBot;

public enum SelectionCaptureMode
{
    PointOrRegion
}

public sealed record ScreenSelection(int X, int Y, int Width, int Height);

public partial class SelectionOverlay : Window
{
    private readonly double _screenLeft;
    private readonly double _screenTop;
    private WpfPoint _start;
    private NativeMethods.POINT _startScreenPixels;
    private bool _selecting;

    public ScreenSelection? Selection { get; private set; }
    public IntPtr UnderlyingWindowHandle { get; private set; }

    public SelectionOverlay()
    {
        InitializeComponent();
        _screenLeft = SystemParameters.VirtualScreenLeft;
        _screenTop = SystemParameters.VirtualScreenTop;
        Left = _screenLeft;
        Top = _screenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        NativeMethods.GetCursorPos(out _startScreenPixels);
        _selecting = true;
        CaptureMouse();
        SelectionRectangle.Visibility = Visibility.Visible;
        SelectionSizeText.Visibility = Visibility.Visible;
        UpdateSelectionVisual(_start, _start);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        UpdateSelectionVisual(_start, e.GetPosition(this));
        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        var end = e.GetPosition(this);
        _selecting = false;
        ReleaseMouseCapture();
        // WPF mouse positions are device-independent pixels, while BitBlt,
        // GetPixel, and SendInput use physical desktop pixels. Read the
        // native cursor position so selection remains correct at 125%, 150%,
        // and mixed-DPI display settings.
        NativeMethods.GetCursorPos(out var endScreenPixels);
        var leftPixels = Math.Min(_startScreenPixels.X, endScreenPixels.X);
        var topPixels = Math.Min(_startScreenPixels.Y, endScreenPixels.Y);
        var widthPixels = Math.Max(1, Math.Abs(endScreenPixels.X - _startScreenPixels.X));
        var heightPixels = Math.Max(1, Math.Abs(endScreenPixels.Y - _startScreenPixels.Y));
        Selection = new ScreenSelection(leftPixels, topPixels, widthPixels, heightPixels);
        DialogResult = true;
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void UpdateSelectionVisual(WpfPoint first, WpfPoint second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var width = Math.Max(1, Math.Abs(second.X - first.X));
        var height = Math.Max(1, Math.Abs(second.Y - first.Y));
        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        Canvas.SetLeft(SelectionSizeText, left + 6);
        Canvas.SetTop(SelectionSizeText, Math.Max(70, top - 28));
        SelectionSizeText.Text = $"{(int)Math.Round(width)} × {(int)Math.Round(height)}";
    }

}
