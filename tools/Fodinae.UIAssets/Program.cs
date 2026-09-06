using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Drawing;

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

                double fresnel = Math.Pow(1.0 - nz, 2.6);
                double rimFactor = fresnel * 0.95;
                r = (int)Math.Round(r * (1 - rimFactor) + cAtmo.r * rimFactor);
                g = (int)Math.Round(g * (1 - rimFactor) + cAtmo.g * rimFactor);
                b = (int)Math.Round(b * (1 - rimFactor) + cAtmo.b * rimFactor);

                double edgeDist = radius - dist;
                if (edgeDist < 3.0)
                {
                    double edgeT = edgeDist / 3.0;
                    r = (int)Math.Round(cAtmo.r * (1 - edgeT) + r * edgeT);
                    g = (int)Math.Round(cAtmo.g * (1 - edgeT) + g * edgeT);
                    b = (int)Math.Round(cAtmo.b * (1 - edgeT) + b * edgeT);
                }

                pixels[y * size + x] = new Rgba32(
                    (byte)Math.Clamp(r, 0, 255),
                    (byte)Math.Clamp(g, 0, 255),
                    (byte)Math.Clamp(b, 0, 255),
                    255);
            }
        }

        img.Save(Path.Combine(OutputDir, "mm_planet.png"));
        Console.WriteLine("Generated mm_planet.png");
    }

    private static float Noise2D(float[,] grid, double u, double v)
    {
        double gu = (u * 6) % 64;
        double gv = (v * 6) % 64;
        int x0 = (int)gu;
        int y0 = (int)gv;
        int x1 = (x0 + 1) % 64;
        int y1 = (y0 + 1) % 64;
        double fx = gu - x0;
        double fy = gv - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);

        float top = grid[y0, x0] * (1 - (float)fx) + grid[y0, x1] * (float)fx;
        float bottom = grid[y1, x0] * (1 - (float)fx) + grid[y1, x1] * (float)fx;
        return top * (1 - (float)fy) + bottom * (float)fy;
    }

    private static void GenerateBrandLogo(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        var g = new SixLabors.ImageSharp.Drawing.Processing.PathBuilder();
        double cx = size / 2.0;
        double cy = size / 2.0;
        double r = size * 0.44;

        var points = new List<SixLabors.ImageSharp.Drawing.PointF>();
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI * 60 * i / 180.0 - Math.PI / 6.0;
            points.Add(new SixLabors.ImageSharp.Drawing.PointF((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle))));
        }

        g.AddPolygon(points.ToArray());
        img.Mutate(ctx => ctx.Draw(SixLabors.ImageSharp.Drawing.Color.White, 4.0f, g.Build()));

        double rInner = r * 0.62;
        var innerPoints = new List<SixLabors.ImageSharp.Drawing.PointF>();
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI * 60 * i / 180.0 - Math.PI / 6.0;
            innerPoints.Add(new SixLabors.ImageSharp.Drawing.PointF((float)(cx + rInner * Math.Cos(angle)), (float)(cy + rInner * Math.Sin(angle))));
        }

        var g2 = new SixLabors.ImageSharp.Drawing.Processing.PathBuilder();
        g2.AddPolygon(innerPoints.ToArray());
        img.Mutate(ctx => ctx.Draw(SixLabors.ImageSharp.Drawing.Color.Transparent, 2.0f, g2.Build()));

        double rCore = r * 0.28;
        var corePoints = new List<SixLabors.ImageSharp.Drawing.PointF>();
        for (int i = 0; i < 4; i++)
        {
            double angle = Math.PI * 90 * i / 180.0;
            corePoints.Add(new SixLabors.ImageSharp.Drawing.PointF((float)(cx + rCore * Math.Cos(angle)), (float)(cy + rCore * Math.Sin(angle))));
        }

        var g3 = new SixLabors.ImageSharp.Drawing.Processing.PathBuilder();
        g3.AddPolygon(corePoints.ToArray());
        img.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Drawing.Color.White, g3.Build()));

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
        GenerateIcon("mm_icon_chronicle.png", 128, (draw, s) =>
        {
            double pad = s * 0.22;
            double x0 = pad, y0 = pad * 0.8, x1 = s - pad, y1 = s - pad * 0.8;
            draw.DrawPolygon(SixLabors.ImageSharp.Drawing.Color.White, (float)(s * 0.04),
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)x0, (float)y0), new((float)x1, (float)y0), new((float)x1, (float)y1), new((float)x0, (float)y1)
                });
            float lw = (float)(s * 0.035);
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(pad + s * 0.1), (float)(y0 + s * 0.18)),
                    new((float)(x1 - s * 0.1), (float)(y0 + s * 0.18))
                });
        });

        GenerateIcon("mm_icon_settings.png", 128, (draw, s) =>
        {
            double cx = s / 2.0, cy = s / 2.0;
            double rOuter = s * 0.34;
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.White, (float)cx, (float)cy, (float)rOuter);
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.Transparent, (float)cx, (float)cy, (float)(s * 0.12));
        });

        GenerateIcon("mm_icon_repair.png", 128, (draw, s) =>
        {
            float lw = (float)(s * 0.09);
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.28), (float)(s * 0.72)),
                    new((float)(s * 0.62), (float)(s * 0.38))
                });
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.White, (float)(s * 0.68), (float)(s * 0.32), (float)(s * 0.16));
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.Transparent, (float)(s * 0.68), (float)(s * 0.32), (float)(s * 0.09));
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.White, (float)(s * 0.26), (float)(s * 0.74), (float)(s * 0.08));
        });

        GenerateIcon("mm_icon_update.png", 128, (draw, s) =>
        {
            float lw = (float)(s * 0.045);
            draw.DrawArc(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.RectangleF((float)(s * 0.28), (float)(s * 0.24), (float)(s * 0.44), (float)(s * 0.44)),
                180, 180);
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.28), (float)(s * 0.46)),
                    new((float)(s * 0.22), (float)(s * 0.66))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.72), (float)(s * 0.46)),
                    new((float)(s * 0.78), (float)(s * 0.66))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.18), (float)(s * 0.66)),
                    new((float)(s * 0.82), (float)(s * 0.66))
                });
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.White, (float)(s / 2), (float)(s * 0.75), (float)(s * 0.06));
        });

        GenerateIcon("mm_icon_discord.png", 128, (draw, s) =>
        {
            double padX = s * 0.20, padY = s * 0.28;
            draw.DrawPolygon(SixLabors.ImageSharp.Drawing.Color.White, (float)(s * 0.045),
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)padX, (float)padY), new((float)(s - padX), (float)padY),
                    new((float)(s - padX), (float)(s - padY)), new((float)padX, (float)(s - padY))
                });
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.White, (float)(s / 2 - s * 0.12), (float)(s / 2), (float)(s * 0.05));
            draw.FillCircle(SixLabors.ImageSharp.Drawing.Color.White, (float)(s / 2 + s * 0.12), (float)(s / 2), (float)(s * 0.05));
        });

        GenerateIcon("mm_icon_telegram.png", 128, (draw, s) =>
        {
            var pts = new[]
            {
                new SixLabors.ImageSharp.Drawing.PointF((float)(s * 0.80), (float)(s * 0.22)),
                new SixLabors.ImageSharp.Drawing.PointF((float)(s * 0.22), (float)(s * 0.52)),
                new SixLabors.ImageSharp.Drawing.PointF((float)(s * 0.44), (float)(s * 0.60)),
                new SixLabors.ImageSharp.Drawing.PointF((float)(s * 0.52), (float)(s * 0.78)),
                new SixLabors.ImageSharp.Drawing.PointF((float)(s * 0.62), (float)(s * 0.62)),
            };
            draw.FillPolygon(SixLabors.ImageSharp.Drawing.Color.White, new[] { pts[0], pts[1], pts[2] });
            draw.FillPolygon(SixLabors.ImageSharp.Drawing.Color.White, new[] { pts[0], pts[2], pts[3] });
            draw.FillPolygon(SixLabors.ImageSharp.Drawing.Color.White, new[] { pts[0], pts[3], pts[4] });
        });

        GenerateIcon("mm_icon_vk.png", 128, (draw, s) =>
        {
            double pad = s * 0.22;
            draw.DrawPolygon(SixLabors.ImageSharp.Drawing.Color.White, (float)(s * 0.045),
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)pad, (float)pad), new((float)(s - pad), (float)pad),
                    new((float)(s - pad), (float)(s - pad)), new((float)pad, (float)(s - pad))
                });
            float lw = (float)(s * 0.05);
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.38), (float)(s * 0.32)),
                    new((float)(s * 0.38), (float)(s * 0.68))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.38), (float)(s * 0.50)),
                    new((float)(s * 0.62), (float)(s * 0.32))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.44), (float)(s * 0.46)),
                    new((float)(s * 0.62), (float)(s * 0.68))
                });
        });

        GenerateIcon("mm_icon_exit.png", 128, (draw, s) =>
        {
            float lw = (float)(s * 0.045);
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.54), (float)(s * 0.22)),
                    new((float)(s * 0.24), (float)(s * 0.22))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.24), (float)(s * 0.22)),
                    new((float)(s * 0.24), (float)(s * 0.78))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.24), (float)(s * 0.78)),
                    new((float)(s * 0.54), (float)(s * 0.78))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.40), (float)(s * 0.50)),
                    new((float)(s * 0.76), (float)(s * 0.50))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.62), (float)(s * 0.36)),
                    new((float)(s * 0.76), (float)(s * 0.50))
                });
            draw.Lines(SixLabors.ImageSharp.Drawing.Color.White, lw,
                new SixLabors.ImageSharp.Drawing.PointF[] {
                    new((float)(s * 0.62), (float)(s * 0.64)),
                    new((float)(s * 0.76), (float)(s * 0.50))
                });
        });
    }

    private static void GenerateIcon(string filename, int size, Action<DrawingContext, double> drawFunc)
    {
        int hiSize = size * 4;
        using var img = new Image<Rgba32>(hiSize, hiSize);
        var g = new DrawingContext(hiSize, hiSize);
        drawFunc(g, hiSize);
        img.Mutate(ctx => ctx.DrawImage(g.BuildImage(), 1.0f));
        img.Mutate(ctx => ctx.Resize(size, KnownResamplers.Lanczos3));
        img.Save(Path.Combine(OutputDir, filename));
        Console.WriteLine($"Generated {filename}");
    }
}
