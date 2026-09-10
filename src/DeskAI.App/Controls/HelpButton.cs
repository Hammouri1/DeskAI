using DeskAI.App.Help;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DeskAI.App.Controls;

/// <summary>
/// The small "?" beside a feature. Pressing it shows that feature's explanation.
/// </summary>
/// <remarks>
/// The text comes from <see cref="HelpCatalog"/> by ID, never from the page, so every
/// explanation is held to the same tested rules. The button changes nothing; it only reads.
/// </remarks>
public sealed partial class HelpButton : Button
{
    public static readonly DependencyProperty TopicProperty = DependencyProperty.Register(
        nameof(Topic),
        typeof(string),
        typeof(HelpButton),
        new PropertyMetadata(null, (sender, _) => ((HelpButton)sender).DescribeForScreenReaders()));

    public HelpButton()
    {
        Style = (Style)Application.Current.Resources["HelpButtonStyle"];

        // Segoe Fluent Icons "Help": a question mark.
        Content = new FontIcon { Glyph = "\uE897", FontSize = 11 };
        ToolTipService.SetToolTip(this, "What is this?");
        Click += OnClick;
    }

    /// <summary>The ID of a topic in <see cref="HelpCatalog"/>.</summary>
    public string? Topic
    {
        get => (string?)GetValue(TopicProperty);
        set => SetValue(TopicProperty, value);
    }

    private HelpTopic Resolve() => HelpCatalog.Find(Topic) ?? HelpTopic.Missing;

    private void DescribeForScreenReaders() =>
        AutomationProperties.SetName(this, $"Help: {Resolve().Title}");

    private void OnClick(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            Content = new ContentControl
            {
                Content = Resolve(),
                ContentTemplate = (DataTemplate)Application.Current.Resources["HelpTopicTemplate"],
                IsTabStop = false,
            },
        };
        flyout.ShowAt(this);
    }
}
