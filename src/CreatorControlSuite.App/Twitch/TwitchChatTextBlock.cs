using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace CreatorControlSuite.App.Twitch;

public sealed class TwitchChatTextBlock : TextBlock
{
    public static readonly DependencyProperty ChatItemProperty = DependencyProperty.Register(
        nameof(ChatItem), typeof(TwitchChatDisplayItem), typeof(TwitchChatTextBlock),
        new PropertyMetadata(null, OnChatItemChanged));

    public TwitchChatDisplayItem? ChatItem
    {
        get => (TwitchChatDisplayItem?)GetValue(ChatItemProperty);
        set => SetValue(ChatItemProperty, value);
    }

    private static void OnChatItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var block = (TwitchChatTextBlock)sender;
        block.Inlines.Clear();
        if (args.NewValue is not TwitchChatDisplayItem item) return;

        block.Inlines.Add(new Run(item.Prefix));
        foreach (TwitchChatDisplayPart part in item.Parts)
        {
            if (!part.IsEmote || !Uri.TryCreate(part.ImageUrl, UriKind.Absolute, out Uri? uri))
            {
                block.Inlines.Add(new Run(part.Text));
                continue;
            }

            var image = new Image
            {
                Height = 24,
                MaxWidth = 72,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(2, 0, 2, -5),
                ToolTip = part.Text,
                Source = new BitmapImage(uri)
            };
            block.Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
        }
    }
}
