using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Mandarine.App.Services;

/// <summary>
/// The picture Explorer shows for a file (a preview for images and PDF, else the file's icon).
/// Asked to Windows, so no PDF engine runs inside the tray app.
/// </summary>
internal static class Thumbnails
{
    private static Guid _imageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>A frozen bitmap, or null when Windows has nothing to show. Safe to call off the UI thread.</summary>
    public static BitmapSource? Get(string path, int size)
    {
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref _imageFactory, out var factory) != 0 || factory is null)
        {
            return null;
        }

        IntPtr bitmap = IntPtr.Zero;
        try
        {
            if (factory.GetImage(new Size32 { Width = size, Height = size }, 0, out bitmap) != 0 || bitmap == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32
    {
        public int Width, Height;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size32 size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
