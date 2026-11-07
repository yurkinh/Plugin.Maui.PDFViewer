using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Plugin.Maui.PDFViewer;

public partial class PdfView
{
    // One wheel notch is 120 units (WHEEL_DELTA): scroll 48 view units, or zoom by 10%.
    private const double WheelNotch = 120;
    private const double ScrollPerNotch = 48;
    private const double ZoomPerNotch = 1.1;

    private UIElement? wheelTarget;

    // Mouse wheels and precision touchpads: MAUI's PanGestureRecognizer reports only drags on Windows.
    // Wheel scrolls vertically, Shift + wheel or a horizontal wheel sideways, Ctrl + wheel zooms around the pointer.
    partial void AttachPlatformGestures(GraphicsView view)
    {
        if (wheelTarget is not null) wheelTarget.PointerWheelChanged -= OnPointerWheelChanged;
        wheelTarget = view.Handler?.PlatformView as UIElement;
        if (wheelTarget is not null) wheelTarget.PointerWheelChanged += OnPointerWheelChanged;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element) return;
        var point = e.GetCurrentPoint(element);
        var notches = point.Properties.MouseWheelDelta / WheelNotch;
        if (notches == 0) return;
        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(VirtualKeyModifiers.Control))
            ZoomBy(Math.Pow(ZoomPerNotch, notches), point.Position.X, point.Position.Y);
        else if (point.Properties.IsHorizontalMouseWheel)
            ScrollBy(-notches * ScrollPerNotch, 0);          // tilting right shows what is to the right
        else if (modifiers.HasFlag(VirtualKeyModifiers.Shift))
            ScrollBy(notches * ScrollPerNotch, 0);
        else
            ScrollBy(0, notches * ScrollPerNotch);           // turning the wheel down moves the page up
        e.Handled = true;
    }
}
