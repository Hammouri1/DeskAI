using System.ComponentModel;
using DeskAI.App.ViewModels;
using DeskAI.App.Views.Buddies;
using DeskAI.Core.QuickSearch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskAI.App.Controls;

/// <summary>
/// The one-line tip about Ctrl + Alt + Space on Home and Search (ADR 0047): a small Sparky, the
/// tip, and a close button. Closing it on either page closes it on both.
/// </summary>
/// <remarks>
/// One line on purpose, so it never pushes a page's first step out of sight. It shows only while
/// <see cref="QuickSearchTipViewModel.IsShown"/> is true.
/// </remarks>
public sealed partial class QuickSearchTipBar : UserControl
{
    public static readonly DependencyProperty TipProperty = DependencyProperty.Register(
        nameof(Tip), typeof(QuickSearchTipViewModel), typeof(QuickSearchTipBar), new PropertyMetadata(null, OnTipChanged));

    public QuickSearchTipBar()
    {
        var buddy = BuddyFactory.Create(SearchBuddy.Sparky);
        buddy.Width = 28;
        buddy.Height = 28;
        buddy.HoldsStill = true;

        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(close, "Close the tip");
        close.Click += async (_, _) =>
        {
            if (Tip is not null)
            {
                await Tip.DismissAsync();
            }
        };

        var text = new TextBlock
        {
            Text = QuickSearchTipViewModel.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["BodySecondaryStyle"],
        };

        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(buddy);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Grid.SetColumn(close, 2);
        row.Children.Add(close);

        Content = new Border
        {
            Padding = new Thickness(12, 4, 4, 4),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["DeskLineBrush"],
            Background = (Brush)Application.Current.Resources["DeskSurfaceBrush"],
            Child = row,
        };
        Visibility = Visibility.Collapsed;
    }

    public QuickSearchTipViewModel? Tip
    {
        get => (QuickSearchTipViewModel?)GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    private static void OnTipChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var bar = (QuickSearchTipBar)sender;
        if (args.OldValue is QuickSearchTipViewModel old)
        {
            old.PropertyChanged -= bar.OnTipPropertyChanged;
        }

        if (args.NewValue is QuickSearchTipViewModel tip)
        {
            tip.PropertyChanged += bar.OnTipPropertyChanged;
        }

        bar.ShowOrHide();
    }

    private void OnTipPropertyChanged(object? sender, PropertyChangedEventArgs args) => ShowOrHide();

    private void ShowOrHide() => Visibility = Tip is { IsShown: true } ? Visibility.Visible : Visibility.Collapsed;
}
