using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fodinae.UIAssets;

internal static class Program
{
    private const string OutputDir = "Assets/Textures/UI";

    public static int Main(string[] args)
    {
        Directory.CreateDirectory(OutputDir);

        GenerateExactPlanet(1024);
        GenerateBrandLogo(128);
        GenerateCleanSpaceBg(1920, 1080);
        GenerateSidebarIcons();

        return 0;
    }

    private static void GenerateExactPlanet(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        var pixels = img.GetPixelSpan();
        Random random = new Random(999);

        double cx = size * 0.5;
        double cy = size * 0.5;
        double radius = size * 0.40;
        double atmoOuter = size * 0.48;

        double sx = cx - radius * 0.36;
        double sy = cy - radius * 0.40;

        (byte r, byte g, byte b) cHighlight = (235, 142, 86);
        (byte r, byte g, byte b) cMid = (195, 96, 48);
        (byte r, byte g, byte b) cDeep = (96, 32, 14);
        (byte r, byte g, byte b) cNight = (3, 6, 10);
        (byte r, byte g, byte b) cAtmo = (112, 229, 221);

        float[,] pGrid = new float[64, 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                pGrid[y, x] = (float)random.NextDouble();
            }
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist > radius)
                {
                    if (dist <= atmoOuter)
                    {
                        double t = (dist - radius) / (atmoOuter - radius);
                        byte alpha = (byte)Math.Clamp((int)Math.Round(255.0 * Math.Exp(-t * 4.5) * 0.9), 0, 255);
                        pixels[y * size + x] = new Rgba32(cAtmo.r, cAtmo.g, cAtmo.b, alpha);
                    }
                    continue;
                }

                double distSun = Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy)) / (radius * 1.6);
                distSun = Math.Min(1.0, distSun);

                double nx = dx / radius;
                double ny = dy / radius;
                double nz = Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - ny * ny));
                double uCoord = Math.Atan2(nx, nz) / Math.PI * 0.5 + 0.5;
                double vCoord = Math.Asin(Math.Max(-1.0, Math.Min(1.0, ny))) / Math.PI + 0.5;

                double geo = Noise2D(pGrid, uCoord, vCoord) * 0.18;

                double t = Math.Clamp(distSun + geo - 0.09, 0.0, 1.0);
                int r, g, b;
                if (t < 0.46)
                {
                    double k = t / 0.46;
                    r = (int)Math.Round(cHighlight.r + (cMid.r - cHighlight.r) * k);
                    g = (int)Math.Round(cHighlight.g + (cMid.g - cHighlight.g) * k);
                    b = (int)Math.Round(cHighlight.b + (cMid.b - cHighlight.b) * k);
                }
                else if (t < 0.72)
                {
                    double k = (t - 0.46) / 0.26;
                    r = (int)Math.Round(cMid.r + (cDeep.r - cMid.r) * k);
                    g = (int)Math.Round(cMid.g + (cDeep.g - cMid.g) * k);
                    b = (int)Math.Round(cMid.b + (cDeep.b - cMid.b) * k);
                }
                else
                {
                    double k = (t - 0.72) / 0.28;
                    r = (int)Math.Round(cDeep.r + (cNight.r - cDeep.r) * k);
                    g = (int)Math.Round(cDeep.g + (cNight.g - cDeep.g) * k);
                    b = (int)Math.Round(cDeep.b + (cNight.b - cDeep.b) * k);
                }

                pixels[y * size + x] = new Rgba32((byte)r, (byte)g, (byte)b, 255);
            }
        }

        img.Save(Path.Combine(OutputDir, "planet_exact.png"));
        Console.WriteLine("Generated planet_exact.png");
    }

    private static void GenerateBrandLogo(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        var pixels = img.GetPixelSpan();
        double cx = size / 2.0;
        double cy = size / 2.0;
        double r = size * 0.44;

        var points = new (double x, double y)[6];
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI * 60 * i / 180.0 - Math.PI / 6.0;
            points[i] = (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }

        DrawPolygon(pixels, size, points, (255, 255, 255, 255));

        double rInner = r * 0.62;
        var innerPoints = new (double x, double y)[6];
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI * 60 * i / 180.0 - Math.PI / 6.0;
            innerPoints[i] = (cx + rInner * Math.Cos(angle), cy + rInner * Math.Sin(angle));
        }

        FillPolygon(pixels, size, innerPoints, (0, 0, 0, 0));

        double rCore = r * 0.28;
        var corePoints = new (double x, double y)[4];
        for (int i = 0; i < 4; i++)
        {
            double angle = Math.PI * 90 * i / 180.0;
            corePoints[i] = (cx + rCore * Math.Cos(angle), cy + rCore * Math.Sin(angle));
        }

        FillPolygon(pixels, size, corePoints, (255, 255, 255, 255));

        img.Save(Path.Combine(OutputDir, "mm_logo.png"));
        Console.WriteLine("Generated mm_logo.png");
    }

    private static void GenerateCleanSpaceBg(int width, int height)
    {
        using var img = new Image<Rgba32>(width, height);
        var pixels = img.GetPixelSpan();
        Random random = new Random(4242);

        double ncx = width * 0.74;
        double ncy = height * 0.50;
        double nebulaRad = width * 0.45;

        var stars = new List<(int x, int y, byte b, int sz)>();
        for (int i = 0; i < 220; i++)
        {
            int sx = random.Next((int)(width * 0.15), width);
            int sy = random.Next(0, height);
            byte brightness = random.Next(140, 256);
            int sz = brightness >= 220 ? 2 : 1;
            stars.Add((sx, sy, brightness, sz));
        }

        var starMap = new Dictionary<(int, int), byte>();
        foreach (var (sx, sy, b, sz) in stars)
        {
            for (int oy = 0; oy < sz; oy++)
            {
                for (int ox = 0; ox < sz; ox++)
                {
                    int px = sx + ox;
                    int py = sy + oy;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        starMap[(px, py)] = b;
                    }
                }
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double tVert = (double)y / height;
                int baseR = (int)Math.Round(3 * (1 - tVert) + 1 * tVert);
                int baseG = (int)Math.Round(6 * (1 - tVert) + 2 * tVert);
                int baseB = (int)Math.Round(10 * (1 - tVert) + 4 * tVert);

                double dNebula = Math.Sqrt((x - ncx) * (x - ncx) + (y - ncy) * (y - ncy));
                if (dNebula < nebulaRad)
                {
                    double tNebula = 1.0 - dNebula / nebulaRad;
                    baseR = (int)Math.Round(baseR + 14 * tNebula * tNebula);
                    baseG = (int)Math.Round(baseG + 48 * tNebula * tNebula);
                    baseB = (int)Math.Round(baseB + 56 * tNebula * tNebula);
                }

                double tLeft = Math.Max(0.0, 1.0 - (x / (width * 0.55)));
                double leftShade = Math.Pow(tLeft, 1.5);
                baseR = (int)Math.Round(baseR * (1 - leftShade * 0.75) + 3 * leftShade * 0.75);
                baseG = (int)Math.Round(baseG * (1 - leftShade * 0.75) + 6 * leftShade * 0.75);
                baseB = (int)Math.Round(baseB * (1 - leftShade * 0.75) + 10 * leftShade * 0.75);

                if (starMap.TryGetValue((x, y), out byte sb))
                {
                    baseR = Math.Min(255, baseR + sb);
                    baseG = Math.Min(255, baseG + sb);
                    baseB = Math.Min(255, baseB + sb);
                }

                pixels[y * width + x] = new Rgba32(
                    (byte)Math.Clamp(baseR, 0, 255),
                    (byte)Math.Clamp(baseG, 0, 255),
                    (byte)Math.Clamp(baseB, 0, 255),
                    255);
            }
        }

        img.Save(Path.Combine(OutputDir, "mm_space_bg.png"));
        Console.WriteLine("Generated mm_space_bg.png");
    }

    private static void GenerateSidebarIcons()
    {
        GenerateIcon("mm_icon_chronicle.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            double pad = s * 0.22;
            double x0 = pad, y0 = pad * 0.8, x1 = s - pad, y1 = s - pad * 0.8;
            DrawPolygon(pixels, s, new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, (255, 255, 255, 255));
            float lw = (float)(s * 0.035);
            DrawLine(pixels, s, (float)(pad + s * 0.1), (float)(y0 + s * 0.18), (float)(x1 - s * 0.1), (float)(y0 + s * 0.18), (255, 255, 255, 255), lw);
        });

        GenerateIcon("mm_icon_settings.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            double cx = s / 2.0, cy = s / 2.0;
            double rOuter = s * 0.34;
            FillCircle(pixels, s, cx, cy, rOuter, (255, 255, 255, 255));
            FillCircle(pixels, s, cx, cy, s * 0.12, (0, 0, 0, 0));
        });

        GenerateIcon("mm_icon_repair.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            float lw = (float)(s * 0.09);
            DrawLine(pixels, s, (float)(s * 0.28), (float)(s * 0.72), (float)(s * 0.62), (float)(s * 0.38), (255, 255, 255, 255), lw);
            FillCircle(pixels, s, s * 0.68, s * 0.32, s * 0.16, (255, 255, 255, 255));
            FillCircle(pixels, s, s * 0.68, s * 0.32, s * 0.09, (0, 0, 0, 0));
            FillCircle(pixels, s, s * 0.26, s * 0.74, s * 0.08, (255, 255, 255, 255));
        });

        GenerateIcon("mm_icon_update.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            float lw = (float)(s * 0.045);
            DrawArc(pixels, s, s * 0.28, s * 0.24, s * 0.44, s * 0.44, 180, 180, (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.28), (float)(s * 0.46), (float)(s * 0.22), (float)(s * 0.66), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.72), (float)(s * 0.46), (float)(s * 0.78), (float)(s * 0.66), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.18), (float)(s * 0.66), (float)(s * 0.82), (float)(s * 0.66), (255, 255, 255, 255), lw);
            FillCircle(pixels, s, s / 2, s * 0.75, s * 0.06, (255, 255, 255, 255));
        });

        GenerateIcon("mm_icon_discord.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            double padX = s * 0.20, padY = s * 0.28;
            DrawPolygon(pixels, s, new[] { (padX, padY), (s - padX, padY), (s - padX, s - padY), (padX, s - padY) }, (255, 255, 255, 255));
            FillCircle(pixels, s, s / 2 - s * 0.12, s / 2, s * 0.05, (255, 255, 255, 255));
            FillCircle(pixels, s, s / 2 + s * 0.12, s / 2, s * 0.05, (255, 255, 255, 255));
        });

        GenerateIcon("mm_icon_telegram.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            var pts = new (double x, double y)[]
            {
                (s * 0.80, s * 0.22),
                (s * 0.22, s * 0.52),
                (s * 0.44, s * 0.60),
                (s * 0.52, s * 0.78),
                (s * 0.62, s * 0.62)
            };
            FillTriangle(pixels, s, pts[0], pts[1], pts[2], (255, 255, 255, 255));
            FillTriangle(pixels, s, pts[0], pts[2], pts[3], (255, 255, 255, 255));
            FillTriangle(pixels, s, pts[0], pts[3], pts[4], (255, 255, 255, 255));
        });

        GenerateIcon("mm_icon_vk.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            double pad = s * 0.22;
            DrawPolygon(pixels, s, new[] { (pad, pad), (s - pad, pad), (s - pad, s - pad), (pad, s - pad) }, (255, 255, 255, 255));
            float lw = (float)(s * 0.05);
            DrawLine(pixels, s, (float)(s * 0.38), (float)(s * 0.32), (float)(s * 0.38), (float)(s * 0.68), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.38), (float)(s * 0.50), (float)(s * 0.62), (float)(s * 0.32), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.44), (float)(s * 0.46), (float)(s * 0.62), (float)(s * 0.68), (255, 255, 255, 255), lw);
        });

        GenerateIcon("mm_icon_exit.png", 128, (img, s) =>
        {
            var pixels = img.GetPixelSpan();
            float lw = (float)(s * 0.045);
            DrawLine(pixels, s, (float)(s * 0.54), (float)(s * 0.22), (float)(s * 0.24), (float)(s * 0.22), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.24), (float)(s * 0.22), (float)(s * 0.24), (float)(s * 0.78), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.24), (float)(s * 0.78), (float)(s * 0.54), (float)(s * 0.78), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.40), (float)(s * 0.50), (float)(s * 0.76), (float)(s * 0.50), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.62), (float)(s * 0.36), (float)(s * 0.76), (float)(s * 0.50), (255, 255, 255, 255), lw);
            DrawLine(pixels, s, (float)(s * 0.62), (float)(s * 0.64), (float)(s * 0.76), (float)(s * 0.50), (255, 255, 255, 255), lw);
        });
    }

    private delegate void IconDraw(Image<Rgba32> image, int size);

    private static void GenerateIcon(string filename, int size, IconDraw drawFunc)
    {
        int hiSize = size * 4;
        using var img = new Image<Rgba32>(hiSize, hiSize);
        drawFunc(img, hiSize);
        img.Mutate(ctx => ctx.Resize(size, SixLabors.ImageSharp.Processing.Processors.Quantization.KnownResamplers.Lanczos3));
        img.Save(Path.Combine(OutputDir, filename));
        Console.WriteLine($"Generated {filename}");
    }

    private static float Noise2D(float[,] grid, double u, double v)
    {
        int w = grid.GetLength(1);
        int h = grid.GetLength(0);
        double x = u * w - 0.5;
        double y = v * h - 0.5;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;
        int x1 = (x0 + 1) % w;
        int y0c = Math.Clamp(y0, 0, h - 1);
        int y1c = Math.Clamp(y0 + 1, 0, h - 1);

        float top = grid[y0c, x0] * (float)(1 - fx) + grid[y0c, x1] * (float)fx;
        float bottom = grid[y1c, x0] * (float)(1 - fx) + grid[y1c, x1] * (float)fx;
        return top * (float)(1 - fy) + bottom * (float)fy;
    }

    private static void DrawPolygon(Span<Rgba32> pixels, int width, (double x, double y)[] points, (byte r, byte g, byte b, byte a) color)
    {
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            DrawLine(pixels, width, (int)Math.Round(a.x), (int)Math.Round(a.y), (int)Math.Round(b.x), (int)Math.Round(b.y), color, 1.0f);
        }
    }

    private static void FillPolygon(Span<Rgba32> pixels, int width, (double x, double y)[] points, (byte r, byte g, byte b, byte a) color)
    {
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var (x, y) in points)
        {
            int iy = (int)Math.Round(y);
            if (iy < minY) minY = iy;
            if (iy > maxY) maxY = iy;
        }

        int height = pixels.Length / width;
        for (int y = minY; y <= maxY; y++)
        {
            var intersections = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                {
                    double t = (y - a.y) / (b.y - a.y);
                    int x = (int)Math.Round(a.x + t * (b.x - a.x));
                    intersections.Add(x);
                }
            }
            intersections.Sort();
            for (int i = 0; i < intersections.Count - 1; i += 2)
            {
                for (int x = intersections[i]; x <= intersections[i + 1]; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        pixels[y * width + x] = new Rgba32(color.r, color.g, color.b, color.a);
                    }
                }
            }
        }
    }

    private static void FillTriangle(Span<Rgba32> pixels, int width, (double x, double y) a, (double x, double y) b, (double x, double y) c, (byte r, byte g, byte b, byte a) color)
    {
        FillPolygon(pixels, width, new[] { a, b, c }, color);
    }

    private static void DrawLine(Span<Rgba32> pixels, int width, int x0, int y0, int x1, int y1, (byte r, byte g, byte b, byte a) color, float thickness)
    {
        if (thickness <= 1.0f)
        {
            DrawLineThin(pixels, width, x0, y0, x1, y1, color);
            return;
        }

        int r = (int)Math.Ceiling(thickness / 2.0);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy <= r * r)
                {
                    DrawLineThin(pixels, width, x0 + dx, y0 + dy, x1 + dx, y1 + dy, color);
                }
            }
        }
    }

    private static void DrawLineThin(Span<Rgba32> pixels, int width, int x0, int y0, int x1, int y1, (byte r, byte g, byte b, byte a) color)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int height = pixels.Length / width;

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                pixels[y0 * width + x0] = new Rgba32(color.r, color.g, color.b, color.a);
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private static void FillCircle(Span<Rgba32> pixels, int width, double cx, double cy, double radius, (byte r, byte g, byte b, byte a) color)
    {
        int height = pixels.Length / width;
        int xMin = (int)Math.Floor(cx - radius);
        int xMax = (int)Math.Ceiling(cx + radius);
        int yMin = (int)Math.Floor(cy - radius);
        int yMax = (int)Math.Ceiling(cy + radius);

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d <= radius)
                    {
                        pixels[y * width + x] = new Rgba32(color.r, color.g, color.b, color.a);
                    }
                }
            }
        }
    }

    private static void DrawArc(Span<Rgba32> pixels, int width, double cx, double cy, double rx, double ry, int startAngle, int sweepAngle, (byte r, byte g, byte b, byte a) color, float thickness)
    {
        int steps = (int)Math.Max(Math.Abs(sweepAngle), 1);
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = (startAngle + sweepAngle) * Math.PI / 180.0;

        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double angle = startRad + t * (endRad - startRad);
            double x = cx + rx * Math.Cos(angle);
            double y = cy + ry * Math.Sin(angle);
            FillCircle(pixels, width, x, y, thickness / 2.0, color);
        }
    }
}
