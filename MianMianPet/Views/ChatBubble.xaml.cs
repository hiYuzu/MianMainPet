using System.Windows.Controls;

namespace MianMianPet.Views;

public partial class ChatBubble : UserControl
{
    public ChatBubble()
    {
        InitializeComponent();
    }

    public void SetMaximumHeight(double height)
    {
        // Reserve room for the cloud lobes, outer margin and the two small trailing puffs.
        BubbleBorder.MaxHeight = Math.Max(80, height - 75);
        MessageScroller.MaxHeight = Math.Max(46, height - 110);
    }
}
