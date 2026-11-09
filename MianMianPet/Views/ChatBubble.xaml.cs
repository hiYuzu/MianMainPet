using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MianMianPet.Views;

public partial class ChatBubble : UserControl
{
    public ChatBubble()
    {
        InitializeComponent();
        var pulse = (Storyboard)Resources["ThinkingPulse"];
        ThinkingDots.IsVisibleChanged += (_, _) =>
        {
            if (ThinkingDots.IsVisible) pulse.Begin(this, isControllable: true);
            else pulse.Remove(this);
        };
    }

    public void SetMaximumHeight(double height)
    {
        // The header, padding and shadow margins take up 82 DIPs.
        MessageScroller.MaxHeight = Math.Clamp(height - 82, 36, 250);
    }
}
