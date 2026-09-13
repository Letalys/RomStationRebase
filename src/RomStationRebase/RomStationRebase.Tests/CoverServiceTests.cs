using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RomStationRebase.Services;
using Xunit;

namespace RomStationRebase.Tests;

/// <summary>Copie et réduction des jaquettes sur un PNG 600×800 généré en mémoire.</summary>
public class CoverServiceTests : IDisposable
{
    private const int SourceWidth  = 600;
    private const int SourceHeight = 800;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "RsrTests", Guid.NewGuid().ToString("N"));
    private readonly string _source;

    public CoverServiceTests()
    {
        Directory.CreateDirectory(_root);
        _source = Path.Combine(_root, "cover.png");
        RunSta(() => WritePng(_source, SourceWidth, SourceHeight));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* meilleur effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Exécute l'action sur un thread STA : les objets WPF gelés n'en ont en principe pas besoin,
    /// mais cela met les tests à l'abri d'une exigence d'apartment du décodeur/encodeur.
    /// </summary>
    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>Écrit un PNG opaque de la taille demandée avec un dégradé, pour que la réduction ait de la matière.</summary>
    private static void WritePng(string path, int width, int height)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * stride + x * 4;
            pixels[i]     = (byte)(x * 255 / width);   // B
            pixels[i + 1] = (byte)(y * 255 / height);  // G
            pixels[i + 2] = 128;                        // R
            pixels[i + 3] = 255;                        // A
        }

        // Pas de palette en Bgra32 ; le "!" évite un avertissement nullable selon l'annotation de l'assembly WPF.
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null!, pixels, stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    /// <summary>Relit les dimensions en pixels d'un PNG sur disque.</summary>
    private static (int Width, int Height) ReadSize(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private string Dest(params string[] segments) => Path.Combine(_root, Path.Combine(segments));

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void No_limit_copies_the_bytes_verbatim()
    {
        string dest = Dest("raw.png");
        long written = 0;

        RunSta(() => written = CoverService.CopyCover(_source, dest, 0, 0));

        byte[] expected = File.ReadAllBytes(_source);
        Assert.Equal(expected, File.ReadAllBytes(dest));
        Assert.Equal((long)expected.Length, written);
    }

    [Fact]
    public void Max_width_shrinks_the_image_keeping_its_ratio()
    {
        string dest = Dest("small.png");
        long written = 0;

        RunSta(() => written = CoverService.CopyCover(_source, dest, 300, 0));

        Assert.Equal((300, 400), RunStaValue(() => ReadSize(dest)));
        Assert.Equal(new FileInfo(dest).Length, written);
        Assert.False(File.Exists(dest + ".rsr-tmp"));
    }

    [Fact]
    public void Max_height_shrinks_the_image_keeping_its_ratio()
    {
        string dest = Dest("small-h.png");

        RunSta(() => CoverService.CopyCover(_source, dest, 0, 200));

        Assert.Equal((150, 200), RunStaValue(() => ReadSize(dest)));
    }

    [Fact]
    public void Image_within_the_limit_is_never_enlarged()
    {
        string dest = Dest("same.png");

        RunSta(() => CoverService.CopyCover(_source, dest, 1200, 1600));

        Assert.Equal((SourceWidth, SourceHeight), RunStaValue(() => ReadSize(dest)));
        Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(dest));
    }

    [Fact]
    public void Missing_parent_folder_is_created()
    {
        string dest = Dest("psx", "images", "Chrono Cross-image.png");

        RunSta(() => CoverService.CopyCover(_source, dest, 300, 0));

        Assert.True(File.Exists(dest));
        Assert.Equal((300, 400), RunStaValue(() => ReadSize(dest)));
    }

    [Fact]
    public void Unreadable_source_throws()
    {
        string bogus = Dest("bogus.png");
        File.WriteAllText(bogus, "ceci n'est pas un PNG");

        Assert.ThrowsAny<Exception>(() => RunSta(() => CoverService.CopyCover(bogus, Dest("out.png"), 300, 0)));
    }

    /// <summary>Variante de <see cref="RunSta"/> retournant une valeur.</summary>
    private static T RunStaValue<T>(Func<T> func)
    {
        T result = default!;
        RunSta(() => result = func());
        return result;
    }
}
