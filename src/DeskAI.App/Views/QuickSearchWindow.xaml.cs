using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.App.Views.Buddies;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace DeskAI.App.Views;

/// <summary>
/// The quick search bar's window (ADR 0047): shown by Ctrl + Alt + Space or "Find a file" near
/// the clock, near the top of the screen with the pointer, and hidden by Esc, a click elsewhere,
/// or opening a file.
/// </summary>
/// <remarks>
/// Everything it decides lives in <see cref="QuickSearchViewModel"/>; this class only places the
/// window, draws the buddy, and turns keys into the view model's calls. It is not in the taskbar
/// or Alt + Tab and stays on top only while shown.
/// </remarks>
public sealed partial class QuickSearchWindow : Window
{
    /// <summary>How far down the screen the bar sits, as a share of the work area's height.</summary>
    private const double TopFraction = 0.12;

    /// <summary>The bar's width in view pixels, from the spec (about 640, scaled for the display).</summary>
    private const double BarWidth = 640;

    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;

    public QuickSearchWindow(QuickSearchViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        RoundTheCorners();
        Activated += OnActivated;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.NameRows.CollectionChanged += (_, _) => FitSoon();
        ViewModel.InsideRows.CollectionChanged += (_, _) => FitSoon();
        ShowBuddy();
    }

    public QuickSearchViewModel ViewModel { get; }

    /// <summary>The shortcut or the icon: on the screen with the pointer, top centre, focused.</summary>
    public async void ShowNearPointer()
    {
        await ViewModel.ShowAsync();
        var area = TrayInterop.GetCursorPos(out var pointer)
            ? DisplayArea.GetFromPoint(new PointInt32(pointer.X, pointer.Y), DisplayAreaFallback.Nearest).WorkArea
            : DisplayArea.Primary.WorkArea;
        var width = (int)Math.Ceiling(BarWidth * Scale());
        AppWindow.Move(new PointInt32(area.X + ((area.Width - width) / 2), area.Y + (int)(area.Height * TopFraction)));
        FitToContent();
        AppWindow.Show(activateWindow: true);
        Activate();
        TrayInterop.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Box.Focus(FocusState.Programmatic);
        FitSoon();
    }

    /// <summary>Esc, a click elsewhere, opening a file, or the shortcut again: stops any look and forgets the words.</summary>
    public void HideBar()
    {
        ViewModel.Hide();
        AppWindow.Hide();
    }

    /// <summary>Clicking anywhere else hides the bar, as Esc does.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
        {
            HideBar();
        }
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                HideBar();
                break;
            case VirtualKey.Down:
                args.Handled = true;
                ViewModel.MoveSelection(+1);
                break;
            case VirtualKey.Up:
                args.Handled = true;
                ViewModel.MoveSelection(-1);
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                await ActivateAsync(ViewModel.Selected, showInFolder: control);
                break;
            default:
                break;
        }
    }

    private async void OnRowButtonClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: QuickSearchRowViewModel row })
        {
            await ActivateAsync(row, showInFolder: false);
        }
    }

    private async Task ActivateAsync(QuickSearchRowViewModel? row, bool showInFolder)
    {
        if (await ViewModel.ActivateAsync(row, showInFolder))
        {
            HideBar();
        }
    }

    private async void OnExampleClicked(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string example })
        {
            await ViewModel.ChooseExampleAsync(example);
            Box.Focus(FocusState.Programmatic);
            Box.SelectionStart = Box.Text.Length;
        }
    }

    private void OnSeeMoreClicked(object sender, RoutedEventArgs args) => ViewModel.SeeMoreInDeskAi();

    private void OnOpenDeskAiClicked(object sender, RoutedEventArgs args) => ViewModel.OpenDeskAi();

    /// <summary>Clicking the buddy: a happy moment. It does nothing else.</summary>
    private void OnBuddyTapped(object sender, TappedRoutedEventArgs args) => _ = ViewModel.PetBuddyAsync();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(QuickSearchViewModel.Buddy):
                ShowBuddy();
                break;
            case nameof(QuickSearchViewModel.Mood) when BuddyHost.Content is BuddyControl buddy:
                buddy.Mood = ViewModel.Mood;
                break;
            default:
                FitSoon();
                break;
        }
    }

    private void ShowBuddy()
    {
        var buddy = BuddyFactory.Create(ViewModel.Buddy);
        buddy.Mood = ViewModel.Mood;
        BuddyHost.Content = buddy;
    }

    /// <summary>After the bindings have caught up, so the measured height includes the new lines.</summary>
    private void FitSoon() => DispatcherQueue.TryEnqueue(FitToContent);

    /// <summary>Makes the window exactly as tall as what it shows.</summary>
    private void FitToContent()
    {
        NameHeader.Visibility = ViewModel.NameRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Root.Measure(new Size(BarWidth, double.PositiveInfinity));
        var scale = Scale();
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Ceiling(BarWidth * scale),
            (int)Math.Ceiling(Root.DesiredSize.Height * scale)));
    }

    /// <summary>The display's scale: from the drawn content once it exists, from Windows before that.</summary>
    private double Scale() =>
        Content?.XamlRoot?.RasterizationScale
            ?? (GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) is > 0 and var dpi ? dpi / 96.0 : 1.0);

    /// <summary>Asks Windows 11 for rounded corners on this borderless window. Older Windows ignores it.</summary>
    private void RoundTheCorners()
    {
        var preference = DwmRound;
        _ = DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), DwmWindowCornerPreference, ref preference, sizeof(int));
    }

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
