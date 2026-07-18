using System.Diagnostics;
using ComputeSharp;
using InkDispersion;

var width = 1280;
var height = 720;
var outputDirectory = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : Path.Combine(AppContext.BaseDirectory, "harness-output");
Directory.CreateDirectory(outputDirectory);

using var pipeline = InkDispersionPipeline.TryCreate();
if (pipeline is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

var source = CreateTestImage(width, height);
var destination = new int[source.Length];

if (args.Contains("--golden"))
{
    var goldenCases = new (string Name, InkDispersionPipeline.Parameters Parameters)[]
    {
        ("balanced-default", new(InkDispersionQuality.Balanced, 1f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 120f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7)),
        ("high-default", new(InkDispersionQuality.High, 1f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 120f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7)),
        ("seed-42", new(InkDispersionQuality.Balanced, 1f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 120f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 42)),
        ("partial-spread", new(InkDispersionQuality.Balanced, 0.35f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 120f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7)),
        ("smooth", new(InkDispersionQuality.Balanced, 1f, 0.6f, 0f, 0f, 0.3f, 0.4f, 120f, 0.8f, 0f, 0.12f, 0.1f, 0.09f, 7)),
        ("branching", new(InkDispersionQuality.Balanced, 1f, 1f, 1f, 0.8f, 0f, 0.2f, 120f, 1f, 1f, 0.12f, 0.1f, 0.09f, 7)),
        ("viscous", new(InkDispersionQuality.Balanced, 1f, 0.6f, 0.5f, 0.3f, 1f, 0.8f, 120f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7)),
        ("short-reach", new(InkDispersionQuality.Balanced, 1f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 24f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7)),
    };
    foreach (var (name, goldenParameters) in goldenCases)
    {
        var parameters = goldenParameters;
        pipeline.Process(source, destination, width, height, in parameters);
        var bytes = new byte[destination.Length * sizeof(int)];
        Buffer.BlockCopy(destination, 0, bytes, 0, bytes.Length);
        Console.WriteLine($"{name}: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
    }
    return 0;
}

foreach (var quality in new[] { InkDispersionQuality.Balanced, InkDispersionQuality.High, InkDispersionQuality.Ultra })
{
    var parameters = new InkDispersionPipeline.Parameters(quality, 1f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 160f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    pipeline.Process(source, destination, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    const int frames = 5;
    for (var frame = 0; frame < frames; frame++)
        pipeline.Process(source, destination, width, height, in parameters);
    stopwatch.Stop();
    Console.WriteLine($"{quality}: {stopwatch.Elapsed.TotalMilliseconds / frames:F2} ms/frame ({width}x{height}) litPixels={CountLit(destination)}");
}

{
    var device = GraphicsDevice.GetDefault();
    using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
    var pixels = new Bgra32[source.Length];
    for (var index = 0; index < source.Length; index++)
        pixels[index].PackedValue = unchecked((uint)source[index]);
    sourceTexture.CopyFrom(pixels);
    var parameters = new InkDispersionPipeline.Parameters(InkDispersionQuality.High, 1f, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 160f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7);

    pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
    var stopwatch = Stopwatch.StartNew();
    pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, parameters with { Seed = 8 });
    stopwatch.Stop();
    Console.WriteLine($"structure recompute: {stopwatch.Elapsed.TotalMilliseconds:F2} ms");

    if (pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect))
    {
        using var rectOutput = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(rectOutput, width, height, rect, in parameters);
        pipeline.WaitForCompletion();
        stopwatch.Restart();
        const int rectFrames = 20;
        for (var frame = 0; frame < rectFrames; frame++)
        {
            pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
            pipeline.TryGetVisibleBounds(width, height, in parameters, out rect);
            pipeline.RenderVisible(rectOutput, width, height, rect, in parameters);
        }
        pipeline.WaitForCompletion();
        stopwatch.Stop();
        Console.WriteLine($"cached frame with rect {rect.Width}x{rect.Height} at ({rect.X},{rect.Y}): {stopwatch.Elapsed.TotalMilliseconds / rectFrames:F2} ms/frame");
    }
}

foreach (var spread in new[] { 0.25f, 0.5f, 0.75f, 1f })
{
    var parameters = new InkDispersionPipeline.Parameters(InkDispersionQuality.High, spread, 0.6f, 0.5f, 0.3f, 0.3f, 0.4f, 160f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"spread={spread:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"spread{(int)(spread * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var fiber in new[] { 0f, 0.5f, 1f })
{
    var parameters = new InkDispersionPipeline.Parameters(InkDispersionQuality.High, 1f, 0.6f, fiber, 0.3f, 0.3f, 0.4f, 160f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"fiber={fiber:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"fiber{(int)(fiber * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var sizing in new[] { 0f, 0.5f, 1f })
{
    var parameters = new InkDispersionPipeline.Parameters(InkDispersionQuality.High, 1f, 0.6f, 0.5f, sizing, 0.3f, 0.4f, 160f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"sizing={sizing:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"sizing{(int)(sizing * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var water in new[] { 0f, 0.5f, 1f })
{
    var parameters = new InkDispersionPipeline.Parameters(InkDispersionQuality.High, 1f, water, 0.5f, 0.3f, 0.3f, 0.4f, 160f, 0.8f, 0.4f, 0.12f, 0.1f, 0.09f, 7);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"water={water:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"water{(int)(water * 100):D3}.bmp"), Composite(source, destination), width, height);
}

WriteBmp(Path.Combine(outputDirectory, "source.bmp"), source, width, height);
Console.WriteLine($"images written to {outputDirectory}");
return 0;

static int[] CreateTestImage(int width, int height)
{
    var pixels = new int[width * height];
    var left = width / 2 - 100;
    var top = height / 2 - 40;
    for (var y = top; y < top + 80; y++)
    {
        for (var x = left; x < left + 200; x++)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                continue;
            pixels[y * width + x] = unchecked((int)0xFF303030);
        }
    }
    return pixels;
}

static int CountLit(int[] pixels)
{
    var count = 0;
    foreach (var pixel in pixels)
    {
        if (((pixel >> 24) & 255) > 8)
            count++;
    }
    return count;
}

static int[] Composite(int[] source, int[] ink)
{
    var result = new int[source.Length];
    for (var index = 0; index < source.Length; index++)
    {
        var s = source[index];
        var i = ink[index];
        var sa = (s >> 24) & 255;
        var ia = (i >> 24) & 255;
        var a = Math.Min(ia + sa * (255 - ia) / 255, 255);
        var r = Over((s >> 16) & 255, (i >> 16) & 255, ia);
        var g = Over((s >> 8) & 255, (i >> 8) & 255, ia);
        var b = Over(s & 255, i & 255, ia);
        result[index] = 255 << 24 | OverWhite(r, a) << 16 | OverWhite(g, a) << 8 | OverWhite(b, a);
    }
    return result;

    static int Over(int s, int i, int ia) => Math.Min(i + s * (255 - ia) / 255, 255);

    static int OverWhite(int c, int a) => Math.Min(c + 255 - a, 255);
}

static void WriteBmp(string path, int[] pixels, int width, int height)
{
    var stride = width * 3;
    var padding = (4 - stride % 4) % 4;
    var dataSize = (stride + padding) * height;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(54 + dataSize);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((short)1);
    writer.Write((short)24);
    writer.Write(0);
    writer.Write(dataSize);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);
    var pad = new byte[padding];
    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            writer.Write((byte)(pixel & 255));
            writer.Write((byte)((pixel >> 8) & 255));
            writer.Write((byte)((pixel >> 16) & 255));
        }
        writer.Write(pad);
    }
}
