using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace MianMianPet.Infrastructure;

internal static class PopupWindowHelper
{
    private const uint PositionFlags = 0x0001 | 0x0002 | 0x0010; // No size, move or activation.

    internal static void RefreshPosition(Window owner, Popup popup)
    {
        if (!popup.IsOpen) return;
        // WPF does not update an open popup when only its owner moves and relative offsets stay equal.
        var offset = popup.HorizontalOffset;
        popup.HorizontalOffset = offset + 0.01;
        popup.HorizontalOffset = offset;
        SynchronizeZOrder(owner, popup);
    }

    internal static void SynchronizeZOrder(Window owner, Popup popup)
    {
        if (!popup.IsOpen || popup.Child is null ||
            PresentationSource.FromVisual(popup.Child) is not HwndSource source) return;

        var handle = source.Handle;
        SetWindowPos(handle, owner.Topmost ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, PositionFlags);
        if (!owner.Topmost)
        {
            // Keep inactive floating content with its owner, behind the user's foreground application.
            var after = owner.IsActive ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
            SetWindowPos(handle, after, 0, 0, 0, 0, PositionFlags);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
