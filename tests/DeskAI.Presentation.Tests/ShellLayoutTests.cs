using System.Text.RegularExpressions;
using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Reads the window and theme files the way <see cref="HelpPlacementTests"/> reads the pages, so
/// the design rules that live in XAML cannot drift: the menu keeps the names the owner chose,
/// the groups exist, and the soft tile tints never borrow the accent.
/// </summary>
public sealed partial class ShellLayoutTests
{
    private static readonly string[] TintTokens =
        ["DeskTintBlueBrush", "DeskTintVioletBrush", "DeskTintAmberBrush", "DeskTintRoseBrush"];

    [Fact]
    public void The_menu_shows_the_six_pages_by_the_names_the_top_bar_uses_in_the_same_order()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        var items = MenuItemPattern().Matches(xaml)
            .Select(match => (Title: match.Groups[1].Value, Route: match.Groups[2].Value))
            .ToArray();

        Assert.Equal(ShellViewModel.Pages.Select(page => (page.Title, page.Route)), items);
    }

    [Fact]
    public void The_menu_has_three_group_labels_in_plain_words()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));
        var headers = GroupHeaderPattern().Matches(xaml).Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(["Your files", "DeskAI for you", "Settings"], headers);
    }

    [Fact]
    public void The_top_bar_has_the_search_box_and_the_AI_pill_and_the_pane_has_the_dark_switch()
    {
        var xaml = File.ReadAllText(AppFile("MainWindow.xaml"));

        Assert.Contains("PlaceholderText=\"Find a file…\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AiState}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PageTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOn=\"{Binding IsDark, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ScopeMessage}\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>A first-time user should know the valid scope before the picker invites a choice.</summary>
    [Fact]
    public void Folder_pages_name_the_four_valid_places_before_a_person_chooses()
    {
        const string guidance = "Choose Desktop, Downloads, Documents, Pictures, or a folder inside one of them.";
        var search = File.ReadAllText(AppFile(Path.Combine("Views", "SearchPage.xaml")));
        var organize = File.ReadAllText(AppFile(Path.Combine("Views", "OrganizePage.xaml")));

        Assert.Contains($"Text=\"{guidance}\"", search, StringComparison.Ordinal);
        Assert.Contains("Content=\"Choose inside your folders\"", organize, StringComparison.Ordinal);
        Assert.Contains($"ToolTipService.ToolTip=\"{guidance}\"", organize, StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_DeskAI_logo_is_used_by_the_window_and_executable()
    {
        var logo = AppFile(Path.Combine("Assets", "DeskAI.Logo.png"));
        var icon = AppFile(Path.Combine("Assets", "DeskAI.ico"));
        Assert.True(File.Exists(logo));
        Assert.True(File.Exists(icon));
        Assert.True(new FileInfo(logo).Length > 0);
        Assert.True(new FileInfo(icon).Length > 0);

        var window = File.ReadAllText(AppFile("MainWindow.xaml"));
        Assert.Contains("Source=\"ms-appx:///Assets/DeskAI.Logo.png\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"DeskAI logo\"", window, StringComparison.Ordinal);

        var project = File.ReadAllText(AppFile("DeskAI.App.csproj"));
        Assert.Contains("<ApplicationIcon>Assets\\DeskAI.ico</ApplicationIcon>", project,
            StringComparison.Ordinal);
        Assert.Contains("<Content Include=\"Assets\\DeskAI.ico\" CopyToOutputDirectory=\"PreserveNewest\" />", project,
            StringComparison.Ordinal);

        var windowSource = File.ReadAllText(AppFile("MainWindow.xaml.cs"));
        Assert.Contains("AppWindow.SetIcon(iconPath);", windowSource, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(windowSource, @"\bApplyWindowIcon\(\);"));
    }

    /// <summary>Owner screenshot 2026-09-21: Home was visibly shifted right on a wide window.</summary>
    [Fact]
    public void Home_centers_its_bounded_content_in_the_visible_page_viewport()
    {
        var xaml = File.ReadAllText(AppFile(Path.Combine("Views", "DashboardPage.xaml")));

        Assert.Contains("<ScrollViewer HorizontalContentAlignment=\"Stretch\">", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1240\" HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"1240\" HorizontalAlignment=\"Stretch\"", xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_worker_copy_targets_follow_its_win_x64_build_output()
    {
        var repository = RepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(repository, "src", "DeskAI.PdfWorker", "DeskAI.PdfWorker.csproj"));
        Assert.Contains("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", worker, StringComparison.Ordinal);

        var consumers = new[]
        {
            AppFile("DeskAI.App.csproj"),
            Path.Combine(repository, "tests", "DeskAI.Infrastructure.Tests", "DeskAI.Infrastructure.Tests.csproj"),
            Path.Combine(repository, "tests", "DeskAI.Presentation.Tests", "DeskAI.Presentation.Tests.csproj"),
        };

        foreach (var consumer in consumers)
        {
            var project = File.ReadAllText(consumer);
            Assert.Contains("net10.0\\win-x64\\*.*", project, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_tile_tint_exists_in_dark_light_and_high_contrast_and_is_never_the_accent()
    {
        var theme = File.ReadAllText(AppFile(Path.Combine("Themes", "DeskAITheme.xaml")));
        foreach (var token in TintTokens)
        {
            var definitions = Regex.Matches(theme, $@"x:Key=""{token}"" Color=""([^""]+)""")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            // One per theme dictionary: dark, light, high contrast.
            Assert.Equal(3, definitions.Length);
            Assert.Equal("Transparent", definitions[2]);
            Assert.DoesNotContain("4DD8A8", definitions[0], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0E8C64", definitions[1], StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void No_page_paints_a_tile_with_the_accent()
    {
        foreach (var file in PageFiles())
        {
            var xaml = File.ReadAllText(file);
            foreach (Match tile in TilePattern().Matches(xaml))
            {
                var background = TileBackgroundPattern().Match(tile.Value).Groups[1].Value;
                Assert.True(
                    TintTokens.Contains(background),
                    $"{Path.GetFileName(file)} paints a tile with '{background}', which is not one of the four tints.");
            }
        }
    }

    /// <summary>
    /// Owner's screenshot 2026-09-16: a pinned tile drew "No folders connected" in the 30-point
    /// number style and clipped it. The big style may only ever bind a number, and it hides
    /// when there is none; the words go on a caption line that wraps.
    /// </summary>
    [Fact]
    public void A_pinned_tile_shows_only_a_number_in_the_big_style_and_its_words_on_a_wrapping_caption()
    {
        var xaml = File.ReadAllText(AppFile(Path.Combine("Views", "WorkspacePage.xaml")));
        var tile = Regex.Match(xaml, @"x:DataType=""viewmodels:PinnedSearchTileViewModel"">(.*?)</DataTemplate>", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(tile);

        var big = Regex.Match(tile, @"<TextBlock[^>]*Style=""\{StaticResource MetricStyle\}""[^>]*/>").Value;
        Assert.Contains("Text=\"{x:Bind Number}\"", big, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind HasNumber}\"", big, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind Count}", tile, StringComparison.Ordinal);

        var words = Regex.Match(tile, @"<TextBlock[^>]*Text=""\{x:Bind Words\}""[^>]*/>").Value;
        Assert.Contains("TextWrapping=\"Wrap\"", words, StringComparison.Ordinal);
    }

    [Fact]
    public void Busy_pages_group_choices_into_plain_task_tabs()
    {
        var workspace = File.ReadAllText(AppFile(Path.Combine("Views", "WorkspacePage.xaml")));
        Assert.Contains("AutomationProperties.Name=\"Workspace sections\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Header=\"Looks\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Header=\"Shortcuts\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Header=\"Folder sets\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Header=\"Desktop\"", workspace, StringComparison.Ordinal);

        var settings = File.ReadAllText(AppFile(Path.Combine("Views", "SettingsPage.xaml")));
        Assert.Contains("AutomationProperties.Name=\"Settings sections\"", settings, StringComparison.Ordinal);
        Assert.Contains("Header=\"AI setup\"", settings, StringComparison.Ordinal);
        Assert.Contains("Header=\"What you share\"", settings, StringComparison.Ordinal);
        Assert.Contains("Header=\"Your saved data\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_page_starts_with_the_same_calm_intro_surface()
    {
        foreach (var file in PageFiles())
        {
            var xaml = File.ReadAllText(file);
            Assert.Contains("Style=\"{StaticResource PageIntroStyle}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_visual_preview_is_opt_in_and_replaces_every_computer_facing_service()
    {
        var project = File.ReadAllText(AppFile("DeskAI.App.csproj"));
        Assert.Contains("Condition=\"'$(DeskAiUiPreview)' == 'true'\"", project, StringComparison.Ordinal);
        Assert.Contains("DESKAI_UI_PREVIEW", project, StringComparison.Ordinal);

        var preview = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "UiPreview.cs"));
        Assert.Contains("Directory.CreateTempSubdirectory", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.GetFolderPath", preview, StringComparison.Ordinal);
        foreach (var service in new[]
        {
            "IKnownFolders", "IFolderPickerService", "IPicturePickerService", "IBackupFilePickerService",
            "ICredentialVault", "IAiHttpTransport", "IWallpaperSetter", "IFindingNotifier",
        })
        {
            Assert.Contains($"Replace<{service}>", preview, StringComparison.Ordinal);
        }

        Assert.Contains("RemoveAll<IHostedService>", preview, StringComparison.Ordinal);
        Assert.Contains("NoBackgroundPresence", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Published_startup_explicitly_shows_and_foregrounds_the_main_window()
    {
        var source = File.ReadAllText(AppFile("App.xaml.cs"));
        var startup = Regex.Match(source,
            @"var window = _host\.Services\.GetRequiredService<MainWindow>\(\);(.*?)await ConnectTheBackgroundPresenceAsync",
            RegexOptions.Singleline).Groups[1].Value;

        Assert.NotEmpty(startup);
        Assert.Contains("window.Reveal();", startup, StringComparison.Ordinal);
    }

    /// <summary>Owner report 2026-09-22: startup recovery also remained invisible in the release.</summary>
    [Fact]
    public void Startup_failure_also_explicitly_shows_and_foregrounds_its_window()
    {
        var source = File.ReadAllText(AppFile("App.xaml.cs"));
        var recovery = Regex.Match(source,
            @"catch \(Exception exception\)(.*?)private async Task ConnectTheBackgroundPresenceAsync",
            RegexOptions.Singleline).Groups[1].Value;

        Assert.NotEmpty(recovery);
        Assert.Contains("failureWindow.Reveal();", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("_window.Activate();", recovery, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner report 2026-09-22: GitHub's ZIP ran without a window because publish omitted the
    /// app PRI and XBF files. Windows App SDK issue #6720 documents the unpackaged publish bug.
    /// </summary>
    [Fact]
    public void Unpackaged_publish_copies_the_compiled_WinUI_interface()
    {
        var project = File.ReadAllText(AppFile("DeskAI.App.csproj"));
        Assert.Contains("Name=\"CopyUnpackagedWinUiResources\"", project, StringComparison.Ordinal);
        Assert.Contains("Include=\"$(TargetDir)**\\*.xbf\"", project, StringComparison.Ordinal);
        Assert.Contains("Include=\"$(ProjectPriFullPath)\"", project, StringComparison.Ordinal);
        Assert.Contains("refusing to publish an app with no interface", project, StringComparison.Ordinal);

        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));
        Assert.Contains("Verify the published interface", workflow, StringComparison.Ordinal);
        Assert.Contains("publish/DeskAI/DeskAI.App.pri", workflow, StringComparison.Ordinal);
        Assert.Contains("publish/DeskAI/MainWindow.xbf", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Ask_DeskAI_is_visibly_marked_as_a_beta_feature()
    {
        var xaml = File.ReadAllText(AppFile(Path.Combine("Views", "DashboardPage.xaml")));
        var cardHeading = Regex.Match(xaml,
            @"Text=""Ask about your folders in your own words""(.*?)<controls:HelpButton Topic=""home.ask""",
            RegexOptions.Singleline).Groups[1].Value;

        Assert.NotEmpty(cardHeading);
        Assert.Contains("Text=\"BETA\"", cardHeading, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Beta feature\"", cardHeading, StringComparison.Ordinal);
    }

    private static string AppFile(string relative) =>
        Path.Combine(RepositoryRoot(), "src", "DeskAI.App", relative);

    private static IEnumerable<string> PageFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "Views"), "*.xaml");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DeskAI.sln was not found above the test output.");
    }

    [GeneratedRegex(@"<NavigationViewItem Content=""([^""]+)"" Tag=""([^""]+)""")]
    private static partial Regex MenuItemPattern();

    [GeneratedRegex(@"<NavigationViewItemHeader Content=""([^""]+)""")]
    private static partial Regex GroupHeaderPattern();

    [GeneratedRegex(@"<Border[^>]*Style=""\{StaticResource TileStyle\}""[^>]*>")]
    private static partial Regex TilePattern();

    [GeneratedRegex(@"Background=""\{ThemeResource ([A-Za-z]+)\}""")]
    private static partial Regex TileBackgroundPattern();
}
