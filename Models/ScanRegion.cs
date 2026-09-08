namespace DuneTimer.Models;

public class ScanRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? AnchorKind { get; set; }

    public ScanRegion() { }
    public ScanRegion(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public bool IsValid => Width > 10 && Height > 10;

    public System.Drawing.Rectangle ToRectangle()
        => new(X, Y, Width, Height);

    // Intersection-area / smaller-box-area. Deliberately not IoU: a small box
    // fully contained inside a larger one should read as "fully covered" even
    // though it's a tiny fraction of the union.
    public double OverlapRatio(int x, int y, int width, int height)
    {
        int ix1 = Math.Max(X, x), iy1 = Math.Max(Y, y);
        int ix2 = Math.Min(X + Width, x + width), iy2 = Math.Min(Y + Height, y + height);
        int iw = Math.Max(0, ix2 - ix1), ih = Math.Max(0, iy2 - iy1);
        long intersection = (long)iw * ih;
        if (intersection == 0) return 0;
        long smaller = Math.Min((long)Width * Height, (long)width * height);
        return smaller == 0 ? 0 : (double)intersection / smaller;
    }
}
