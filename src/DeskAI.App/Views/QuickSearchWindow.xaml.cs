using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.App.Views.Buddies;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace DeskAI.App.Views;

/// <summary>
/// The quick search bar's window (ADR 0047): shown by the chosen shortcut or "Find a file" near
/// the clock, near the top of the screen with the pointer, and hidden by Esc, a click elsewhere,
/// or opening a file.
/// </summary>
/// <remarks>
/// Everything it decides lives in <see cref="QuickSearchViewModel"/>; this class only places the
/// window, draws the buddy, and turns keys into the view model's calls. It is not in the taskbar
/// or Alt + Tab and stays on top only while shown. Its look (2026-09-25) is look C, Glowing edge;
/// the opening and the turning edge play only while "Let my buddy move" is on.
/// </remarks>
public sealed partial class QuickSearchWindow : Window
{
    /// <summary>How far down the screen the bar sits, as a share of the work area's height.</summary>
    private const double TopFraction = 0.12;

    /// <summary>The bar's width in view pixels, from the spec (about 640, scaled for the display).</summary>
    private const double BarWidth = 640;

    private const int DwmWindowCornerPreference = 33;
    private const int DwmDoNotRound = 1;
    private const int DwmBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint WmNcCalcSize = 0x0083;
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorEffectiveDpi = 0;

    /// <summary>The see-through room around the card for the glow, on each side, in view pixels.</summary>
    private const double GlowMargin = 24;

    private readonly BuddyMotion _motion;
    private readonly Storyboard _edgeTurn;

    /// <summary>Held so the window procedure Windows calls is never collected.</summary>
    private readonly SubclassProcedure _frameless = OnFrameMessage;

    /// <summary>The work area of the screen the bar last opened on.</summary>
    private RectInt32 _area = DisplayArea.Primary.WorkArea;

    /// <summary>The scale the bar was last drawn at, so only a change of screen scale refits it.</summary>
    private double _drawnScale;

    public QuickSearchWindow(QuickSearchViewModel viewModel, BuddyMotion motion)
    {
        ViewModel = viewModel;
        _motion = motion;
        InitializeComponent();
        _edgeTurn = (Storyboard)Root.Resources["EdgeTurn"];
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        ClearTheFrame();
        Root.PointerPressed += OnRootPressed;
        Root.Loaded += (_, _) =>
        {
            Root.XamlRoot.Changed -= OnXamlRootChanged;
            Root.XamlRoot.Changed += OnXamlRootChanged;
        };
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
        _area = TrayInterop.GetCursorPos(out var pointer)
            ? DisplayArea.GetFromPoint(new PointInt32(pointer.X, pointer.Y), DisplayAreaFallback.Nearest).WorkArea
            : DisplayArea.Primary.WorkArea;
        FitToContent();
        PlayOpening();
        AppWindow.Show(activateWindow: true);
        KeepFrameCleared();
        Activate();
        TrayInterop.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Box.Focus(FocusState.Programmatic);
        FitSoon();
    }

    /// <summary>Esc, a click elsewhere, opening a file, or the shortcut again: stops any look and forgets the words.</summary>
    public void HideBar()
    {
        ViewModel.Hide();
        _edgeTurn.Stop();
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

    /// <summary>The pointer over a row selects it, so the highlight follows the mouse as it does the arrows.</summary>
    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: QuickSearchRowViewModel row })
        {
            ViewModel.PointAt(row);
        }
    }

    /// <summary>A click anywhere on a row does what Enter does; a click on the row's own button is the button's.</summary>
    private async void OnRowTapped(object sender, TappedRoutedEventArgs args)
    {
        for (var part = args.OriginalSource as DependencyObject; part is not null && !ReferenceEquals(part, sender); part = VisualTreeHelper.GetParent(part))
        {
            if (part is ButtonBase)
            {
                return;
            }
        }

        if (sender is FrameworkElement { Tag: QuickSearchRowViewModel row })
        {
            args.Handled = true;
            await ActivateAsync(row, showInFolder: false);

            // When it could not open, the bar stays: typing goes on in the box, not on the row.
            if (AppWindow.IsVisible)
            {
                Box.Focus(FocusState.Programmatic);
            }
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
        var buddy = BuddyFactory.Create(ViewModel.Buddy, _motion);
        buddy.Mood = ViewModel.Mood;
        BuddyHost.Content = buddy;
    }

    /// <summary>After the bindings have caught up, so the measured height includes the new lines.</summary>
    private void FitSoon() => DispatcherQueue.TryEnqueue(FitToContent);

    /// <summary>Makes the window exactly as tall as what it shows, top centre on its screen.</summary>
    /// <remarks>The frame is cleared, so the window's outside is its inside and the whole size is the content's.</remarks>
    private void FitToContent()
    {
        NameHeader.Visibility = ViewModel.NameRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Root.Measure(new Size(BarWidth + (2 * GlowMargin), double.PositiveInfinity));
        var scale = Scale();
        var width = (int)Math.Ceiling((BarWidth + (2 * GlowMargin)) * scale);
        var height = (int)Math.Ceiling(Root.DesiredSize.Height * scale);
        AppWindow.MoveAndResize(new RectInt32(
            _area.X + ((_area.Width - width) / 2), _area.Y + (int)(_area.Height * TopFraction), width, height));
    }

    /// <summary>
    /// The scale of the screen the bar opens on, asked of Windows for that screen.
    /// </summary>
    /// <remarks>
    /// Not the scale the bar is drawn at: that one is still the last screen's until Windows has
    /// moved the bar over, so with a 100% and a 125% screen the first opening on the other one was
    /// cut off, or too wide, until typing refitted it (owner-found, 2026-09-26).
    /// </remarks>
    private double Scale()
    {
        var middle = new TrayInterop.Point { X = _area.X + (_area.Width / 2), Y = _area.Y + (_area.Height / 2) };
        var screen = MonitorFromPoint(middle, MonitorDefaultToNearest);
        return GetDpiForMonitor(screen, MonitorEffectiveDpi, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>When the bar is drawn at a new screen's scale, it is measured again at that scale.</summary>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale != _drawnScale)
        {
            _drawnScale = sender.RasterizationScale;
            FitSoon();
        }
    }

    /// <summary>
    /// Makes the window see-through around the card with no frame from Windows (the probe of
    /// 2026-09-25): the glass reaches the whole window, the inside is the whole window, no rounding.
    /// </summary>
    /// <remarks>
    /// The presenter keeps a 3 px dialog frame and Windows puts its style back on every show, so
    /// the frame is removed by answering WM_NCCALCSIZE ("the inside is the whole window") rather
    /// than by changing the style. Only drawing changes; the window gains no other ability.
    /// </remarks>
    private void ClearTheFrame()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = SetWindowSubclass(window, _frameless, 1, 0);
        var corners = DwmDoNotRound;
        _ = DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref corners, sizeof(int));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(window, ref margins);
        KeepFrameCleared();
    }

    /// <summary>After each show: Windows asks again for the frame's size, and the border colour is cleared again.</summary>
    private void KeepFrameCleared()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = SetWindowPos(window, 0, 0, 0, 0, 0, SwpFrameChangedInPlace);
        var none = unchecked((int)DwmColorNone);
        _ = DwmSetWindowAttribute(window, DwmBorderColor, ref none, sizeof(int));
    }

    /// <summary>The inside of the window is the whole window: no frame from Windows.</summary>
    private static nint OnFrameMessage(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data) =>
        message == WmNcCalcSize && wParam != 0 ? 0 : DefSubclassProc(window, message, wParam, lParam);

    /// <summary>A click on the see-through part around the card is a click elsewhere: the bar hides.</summary>
    private void OnRootPressed(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, Root))
        {
            HideBar();
        }
    }

    /// <summary>
    /// With "Let my buddy move" on: the card fades and drops in, the buddy pops up a moment later,
    /// the rows slide in one after another, and the edge starts turning. Off: shown at once, still.
    /// </summary>
    private void PlayOpening()
    {
        UseRowMotion();
        if (_motion.IsOn)
        {
            _edgeTurn.Begin();
            var drop = new Storyboard();
            drop.Children.Add(BuddyAnimations.To(Card, "Opacity", 0, 1, null, 0.32));
            drop.Children.Add(BuddyAnimations.To(CardShift, "Y", -14, 0, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }, 0.32));
            drop.Begin();
            BuddyAnimations.PopIn(Perch, TimeSpan.FromSeconds(0.12));
        }
        else
        {
            Card.Opacity = 1;
            CardShift.Y = 0;
            Perch.Opacity = 1;
            Perch.RenderTransform = null;
        }
    }

    /// <summary>Rows slide in one after another only while buddies may move.</summary>
    private void UseRowMotion()
    {
        foreach (var list in new[] { NameRowsList, InsideRowsList })
        {
            list.ItemContainerTransitions = _motion.IsOn
                ? [new EntranceThemeTransition { IsStaggeringEnabled = true, FromHorizontalOffset = 0, FromVerticalOffset = -6 }]
                : null;
        }
    }

    /// <summary>Each kind of file's tile colour, as in the mockup: document blue, PDF red, picture orange, video violet, anything else grey.</summary>
    public static Brush KindBrush(string kind)
    {
        var (from, to) = kind switch
        {
            "doc" => ("#4F8CFF", "#2A5BD7"),
            "pdf" => ("#FF6B6B", "#D63D3D"),
            "img" => ("#FFB347", "#F07B2E"),
            "vid" => ("#A77BFF", "#7042D9"),
            _ => ("#7D8A99", "#56616E"),
        };

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(from), Offset = 0 },
                new GradientStop { Color = WindowsAppearanceApplier.ToColor(to), Offset = 1 },
            },
        };
    }

    private const uint SwpFrameChangedInPlace = 0x0027;

    private delegate nint SubclassProcedure(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);

    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll", EntryPoint = "DwmExtendFrameIntoClientArea")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    private static extern nint MonitorFromPoint(TrayInterop.Point point, uint flags);

    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
    private static extern int GetDpiForMonitor(nint monitor, int kind, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
