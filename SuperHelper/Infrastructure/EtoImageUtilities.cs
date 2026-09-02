namespace SuperHelper;

internal static class EtoImageUtilities
{
    public static Eto.Drawing.Image FromBitmap(System.Drawing.Bitmap bitmap) =>
        bitmap == null ? null : Rhino.UI.EtoExtensions.ToEto(bitmap);
}
