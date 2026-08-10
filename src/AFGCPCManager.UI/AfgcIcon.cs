using System.Reflection;

namespace AFGCPCManager.UI;

public static class AfgcIcon
{
    public static Icon CreateIcon()
    {
        using Stream stream = Open("AFGCPCManager.UI.Assets.afgc-icon.ico");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    public static Bitmap CreateBitmap()
    {
        using Stream stream = Open("AFGCPCManager.UI.Assets.afgc-icon.png");
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static Stream Open(string name) =>
        typeof(AfgcIcon).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded UI asset '{name}' is missing.");
}
