using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace MianMianPet.Infrastructure;

public static class ScreenPlacementHelper
{
    public static Point GetDefaultPetPosition(Window window)
    {
        var screen = WinForms.Screen.PrimaryScreen;
        if (screen is null)
        {
            return new Point(100, 100);
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var work = screen.WorkingArea;
        return new Point(
            (work.Right / dpi.DpiScaleX) - window.Width - 28,
            (work.Bottom / dpi.DpiScaleY) - window.Height - 20);
    }

    public static Point ClampPetPosition(Window window, double left, double top)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var centerPx = new System.Drawing.Point(
            (int)((left + (window.Width / 2)) * dpi.DpiScaleX),
            (int)((top + (window.Height / 2)) * dpi.DpiScaleY));
        var work = WinForms.Screen.FromPoint(centerPx).WorkingArea;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workRight = work.Right / dpi.DpiScaleX;
        var workBottom = work.Bottom / dpi.DpiScaleY;

        return new Point(
            Math.Clamp(left, workLeft, Math.Max(workLeft, workRight - window.Width)),
            Math.Clamp(top, workTop, Math.Max(workTop, workBottom - window.Height)));
    }

    public static void PlacePopup(Window owner, Popup popup, double popupWidth, double popupHeight)
    {
        var dpi = VisualTreeHelper.GetDpi(owner);
        var ownerTopLeftPx = owner.PointToScreen(new Point(0, 0));
        var ownerCenterPx = new System.Drawing.Point(
            (int)(ownerTopLeftPx.X + (owner.ActualWidth * dpi.DpiScaleX / 2)),
            (int)(ownerTopLeftPx.Y + (owner.ActualHeight * dpi.DpiScaleY / 2)));
        var work = WinForms.Screen.FromPoint(ownerCenterPx).WorkingArea;

        var ownerLeft = ownerTopLeftPx.X / dpi.DpiScaleX;
        var ownerTop = ownerTopLeftPx.Y / dpi.DpiScaleY;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workRight = work.Right / dpi.DpiScaleX;
        var workBottom = work.Bottom / dpi.DpiScaleY;

        var canShowRight = ownerLeft + owner.ActualWidth + popupWidth - 24 <= workRight;
        var x = canShowRight ? owner.ActualWidth - 24 : -popupWidth + 24;
        var y = -popupHeight + 54;

        if (ownerTop + y < workTop)
        {
            y = owner.ActualHeight - 42;
        }

        var minimumX = workLeft - ownerLeft + 8;
        var maximumX = workRight - ownerLeft - popupWidth - 8;
        var minimumY = workTop - ownerTop + 8;
        var maximumY = workBottom - ownerTop - popupHeight - 8;
        x = maximumX < minimumX ? minimumX : Math.Clamp(x, minimumX, maximumX);
        y = maximumY < minimumY ? minimumY : Math.Clamp(y, minimumY, maximumY);

        popup.Placement = PlacementMode.Relative;
        popup.HorizontalOffset = x;
        popup.VerticalOffset = y;
    }

    public static void PlaceFloatingWindow(Window owner, Window floatingWindow)
    {
        var dpi = VisualTreeHelper.GetDpi(owner);
        var ownerTopLeftPx = owner.PointToScreen(new Point(0, 0));
        var ownerCenterPx = new System.Drawing.Point(
            (int)(ownerTopLeftPx.X + (owner.ActualWidth * dpi.DpiScaleX / 2)),
            (int)(ownerTopLeftPx.Y + (owner.ActualHeight * dpi.DpiScaleY / 2)));
        var work = WinForms.Screen.FromPoint(ownerCenterPx).WorkingArea;

        var ownerLeft = ownerTopLeftPx.X / dpi.DpiScaleX;
        var ownerTop = ownerTopLeftPx.Y / dpi.DpiScaleY;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workRight = work.Right / dpi.DpiScaleX;
        var workBottom = work.Bottom / dpi.DpiScaleY;
        var width = floatingWindow.ActualWidth > 0 ? floatingWindow.ActualWidth : floatingWindow.Width;
        var height = floatingWindow.ActualHeight > 0 ? floatingWindow.ActualHeight : 96;

        var canShowRight = ownerLeft + owner.ActualWidth + width - 24 <= workRight;
        var left = canShowRight ? ownerLeft + owner.ActualWidth - 24 : ownerLeft - width + 24;
        var top = ownerTop - height + 54;
        if (top < workTop)
        {
            top = ownerTop + owner.ActualHeight - 42;
        }

        floatingWindow.Left = Math.Clamp(left, workLeft + 8, Math.Max(workLeft + 8, workRight - width - 8));
        floatingWindow.Top = Math.Clamp(top, workTop + 8, Math.Max(workTop + 8, workBottom - height - 8));
    }

    public static Size GetWorkingAreaSize(Window owner)
    {
        var dpi = VisualTreeHelper.GetDpi(owner);
        var topLeftPx = owner.PointToScreen(new Point(0, 0));
        var center = new System.Drawing.Point(
            (int)(topLeftPx.X + owner.ActualWidth * dpi.DpiScaleX / 2),
            (int)(topLeftPx.Y + owner.ActualHeight * dpi.DpiScaleY / 2));
        var work = WinForms.Screen.FromPoint(center).WorkingArea;
        return new Size(work.Width / dpi.DpiScaleX, work.Height / dpi.DpiScaleY);
    }
}
