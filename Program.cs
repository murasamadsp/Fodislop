using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

class Program {
    static void Main() {
        Console.WriteLine("Start");
        using var img = new Image<Rgba32>(10, 10);
        Console.WriteLine("Created image");
        var methods = typeof(Image<Rgba32>).GetMethods();
        Console.WriteLine($"Methods: {methods.Length}");
        foreach (var m in methods) {
            Console.WriteLine($"{m.ReturnType.Name} {m.Name}");
        }
    }
}
