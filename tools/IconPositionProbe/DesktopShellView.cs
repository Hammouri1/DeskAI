using System.Runtime.InteropServices;

namespace DeskAI.IconProbe;

/// <summary>
/// The Desktop's shell view: read and set icon positions and the Auto arrange flags.
/// Path: ShellWindows.FindWindowSW(SWC_DESKTOP) → top-level browser → active view → IFolderView2
/// (Raymond Chen, "Manipulating the positions of desktop icons", 2013-11-18).
/// </summary>
internal sealed class DesktopShellView : IDisposable
{
    internal const uint AutoArrange = 0x1;
    internal const uint SnapToGrid = 0x4;
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvsiPositionItem = 0x80;
    private const uint SigdnParentRelativeParsing = 0x80018001;
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = typeof(IShellBrowser).GUID;
    private static readonly Guid IidShellItem = typeof(IShellItem).GUID;

    private readonly IShellView _view;
    private readonly IFolderView2 _folder;

    private DesktopShellView(IShellView view)
    {
        _view = view;
        _folder = (IFolderView2)view;
        _folder.GetSpacing(out var spacing);
        Spacing = spacing;
    }

    internal Point Spacing { get; }

    /// <returns>The view, or null while Explorer has not made one yet.</returns>
    internal static DesktopShellView? TryOpen()
    {
        try
        {
            var windows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!)!;
            object location = CsidlDesktop;
            object root = null!;
            var dispatch = windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            if (dispatch is not IServiceProvider provider)
            {
                return null;
            }

            var sid = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            provider.QueryService(ref sid, ref iid, out var browserObject);
            ((IShellBrowser)browserObject).QueryActiveShellView(out var view);
            return new DesktopShellView(view);
        }
        catch (COMException)
        {
            return null;
        }
    }

    internal IReadOnlyDictionary<string, Point> ReadPositions()
    {
        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        _folder.ItemCount(0x2 /* SVGIO_ALLVIEW */, out var count);
        for (var i = 0; i < count; i++)
        {
            _folder.Item(i, out var pidl);
            try
            {
                _folder.GetItemPosition(pidl, out var point);
                positions[NameOf(i)] = point;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        return positions;
    }

    internal void Place(IReadOnlyDictionary<string, Point> targets)
    {
        _folder.ItemCount(0x2, out var count);
        var pidls = new List<IntPtr>();
        var points = new List<Point>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (targets.TryGetValue(NameOf(i), out var target))
                {
                    _folder.Item(i, out var pidl);
                    pidls.Add(pidl);
                    points.Add(target);
                }
            }

            _folder.SelectAndPositionItems((uint)pidls.Count, [.. pidls], [.. points], SvsiPositionItem);
        }
        finally
        {
            pidls.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    internal uint ReadFlags()
    {
        _folder.GetCurrentFolderFlags(out var flags);
        return flags;
    }

    internal void WriteFlags(uint mask, uint flags) => _folder.SetCurrentFolderFlags(mask, flags);

    internal void Refresh() => _view.Refresh();

    /// <summary>
    /// Asks Explorer to store the current layout. Without it, a refresh or restart reloads the
    /// last stored layout and placed icons fall back to the grid (probe run 1, ADR 0043).
    /// </summary>
    internal void Save() => _view.SaveViewState();

    public void Dispose()
    {
        Marshal.ReleaseComObject(_view);
    }

    private string NameOf(int index)
    {
        var iid = IidShellItem;
        _folder.GetItem(index, ref iid, out var item);
        item.GetDisplayName(SigdnParentRelativeParsing, out var name);
        try
        {
            return Marshal.PtrToStringUni(name) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object location, ref object locationRoot, int windowClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        void QueryActiveShellView(out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();
        void Item(int index, out IntPtr pidl);
        void ItemCount(uint flags, out int count);
        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();
        void GetItemPosition(IntPtr pidl, out Point point);
        void GetSpacing(out Point spacing);
        void GetDefaultSpacing();
        void GetAutoArrange();
        void SelectItem();
        void SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,
            [MarshalAs(UnmanagedType.LPArray)] Point[] points, uint flags);

        // IFolderView2
        void SetGroupBy();
        void GetGroupBy();
        void SetViewProperty();
        void GetViewProperty();
        void SetTileViewProperties();
        void SetExtendedTileViewProperties();
        void SetText();
        void SetCurrentFolderFlags(uint mask, uint flags);
        void GetCurrentFolderFlags(out uint flags);
        void GetSortColumnCount();
        void SetSortColumns();
        void GetSortColumns();
        void GetItem(int index, ref Guid riid, out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName(uint sigdn, out IntPtr name);
    }
}
