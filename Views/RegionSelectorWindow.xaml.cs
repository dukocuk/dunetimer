using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DuneTimer.Models;

namespace DuneTimer.Views;

public class RegionSelectorWindow : Window
{
    private Point _startPoint;
    private Rectangle? _selectionRect;
    private readonly Canvas _canvas;

    public ScanRegion? SelectedRegion { get; private set; }

    public RegionSelectorWindow()
    {
        // Fullscreen transparent overlay for region selection
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        Topmost = true;
        WindowState = WindowState.Maximized;
        Cursor = Cursors.Cross;
        ShowInTaskbar = false;
        Title = "Select OCR Region";

        // Instructions text
        var instructions = new TextBlock
        {
            Text = "🎯 Drag a rectangle over the crafting station area\n" +
                   "This is where the timer/countdown text appears\n\n" +
                   "Press ESC to cancel",
            Foreground = Brushes.White,
            FontSize = 18,
            FontFamily = new FontFamily("Segoe UI"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 60, 0, 0)
        };

        _canvas = new Canvas();

        var grid = new Grid();
        grid.Children.Add(_canvas);
        grid.Children.Add(instructions);
        Content = grid;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _selectionRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xE9, 0x45, 0x60)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 233, 69, 96)),
            StrokeDashArray = new DoubleCollection([5, 3])
        };

        _canvas.Children.Add(_selectionRect);
        Canvas.SetLeft(_selectionRect, _startPoint.X);
        Canvas.SetTop(_selectionRect, _startPoint.Y);

        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_selectionRect is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        var x = Math.Min(pos.X, _startPoint.X);
        var y = Math.Min(pos.Y, _startPoint.Y);
        var w = Math.Abs(pos.X - _startPoint.X);
        var h = Math.Abs(pos.Y - _startPoint.Y);

        Canvas.SetLeft(_selectionRect, x);
        Canvas.SetTop(_selectionRect, y);
        _selectionRect.Width = w;
        _selectionRect.Height = h;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();

        if (_selectionRect is null) return;

        var pos = e.GetPosition(this);
        double logicalX = Math.Min(pos.X, _startPoint.X);
        double logicalY = Math.Min(pos.Y, _startPoint.Y);
        double logicalW = Math.Abs(pos.X - _startPoint.X);
        double logicalH = Math.Abs(pos.Y - _startPoint.Y);

        // Convert WPF logical pixels to absolute Physical screen pixels for CopyFromScreen
        var topLeft = PointToScreen(new Point(logicalX, logicalY));
        var bottomRight = PointToScreen(new Point(logicalX + logicalW, logicalY + logicalH));

        int x = (int)topLeft.X;
        int y = (int)topLeft.Y;
        int w = (int)Math.Abs(bottomRight.X - topLeft.X);
        int h = (int)Math.Abs(bottomRight.Y - topLeft.Y);

        if (w > 10 && h > 10)
        {
            SelectedRegion = new ScanRegion(x, y, w, h);
            DialogResult = true;
        }
        Close();
    }
}
