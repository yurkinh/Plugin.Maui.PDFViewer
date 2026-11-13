using System.Windows.Input;
using System.ComponentModel;
using System.Globalization;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;
using Melville.Pdf.ViewerModel;
using ScrollOrientation = Microsoft.Maui.ScrollOrientation;
using ModelZoomMode = Melville.Pdf.ViewerModel.ZoomMode;
using Microsoft.Extensions.DependencyInjection;
using ViewModel = Melville.Pdf.ViewerModel.PdfViewerViewModel<Melville.Pdf.MauiGraphics.Drawing.PdfDisplayList>;

namespace Plugin.Maui.PDFViewer;

/// <summary>
/// Displays a PDF document one page at a time or as a scrolling strip, with page navigation, zoom (buttons, pinch,
/// double tap), panning, links and an optional layers list. Set <see cref="Source"/> to open a document.
/// </summary>
public partial class PdfView : ContentView
{
    // ---- Public API -------------------------------------------------------------------------------------------

    /// <summary>The document to show. Setting it closes the current document.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(PdfSource), typeof(PdfView), propertyChanged: (b, _, _) => ((PdfView)b).OnSourceChanged());

    /// <summary>Supplies passwords for encrypted documents. Without one, encrypted documents fail to open.</summary>
    public static readonly BindableProperty PasswordSourceProperty = BindableProperty.Create(
        nameof(PasswordSource), typeof(IPasswordSource), typeof(PdfView));

    /// <summary>The displayed page, first page 1. Two-way by default.</summary>
    public static readonly BindableProperty PageNumberProperty = BindableProperty.Create(
        nameof(PageNumber), typeof(int), typeof(PdfView), 1, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((PdfView)b).OnPageNumberChanged((int)n));

    private static readonly BindablePropertyKey PageCountKey = BindableProperty.CreateReadOnly(
        nameof(PageCount), typeof(int), typeof(PdfView), 0);

    /// <summary>Number of pages in the open document, 0 when none is open.</summary>
    public static readonly BindableProperty PageCountProperty = PageCountKey.BindableProperty;

    /// <summary>How the page is sized: fit width (the default), fit page, or the zoom the user chose.</summary>
    public static readonly BindableProperty ZoomModeProperty = BindableProperty.Create(
        nameof(ZoomMode), typeof(ZoomMode), typeof(PdfView), ZoomMode.FitWidth, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => { if (((PdfView)b).viewModel is { } vm) vm.ZoomMode = ((ZoomMode)n).ToModel(); });

    private static readonly BindablePropertyKey IsLoadingKey = BindableProperty.CreateReadOnly(
        nameof(IsLoading), typeof(bool), typeof(PdfView), false);

    /// <summary>True while a document is being opened.</summary>
    public static readonly BindableProperty IsLoadingProperty = IsLoadingKey.BindableProperty;

    private static readonly BindablePropertyKey ErrorMessageKey = BindableProperty.CreateReadOnly(
        nameof(ErrorMessage), typeof(string), typeof(PdfView), null,
        propertyChanged: (b, _, n) => ((PdfView)b).ShowError((string?)n));

    /// <summary>Why the document or the current page could not be shown, or null.</summary>
    public static readonly BindableProperty ErrorMessageProperty = ErrorMessageKey.BindableProperty;

    /// <summary>All pages in one scrolling strip (the default), or one page at a time.</summary>
    public static readonly BindableProperty DisplayModeProperty = BindableProperty.Create(
        nameof(DisplayMode), typeof(DisplayMode), typeof(PdfView), DisplayMode.Continuous, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((PdfView)b).OnDisplayModeChanged((DisplayMode)n));

    /// <summary>
    /// The direction pages follow each other in continuous mode, as for ScrollView: Vertical (the default) or
    /// Horizontal. Both and Neither are treated as Vertical.
    /// </summary>
    public static readonly BindableProperty ScrollOrientationProperty = BindableProperty.Create(
        nameof(ScrollOrientation), typeof(ScrollOrientation), typeof(PdfView), ScrollOrientation.Vertical,
        propertyChanged: (b, _, n) => { if (((PdfView)b).viewModel is { } vm) vm.ScrollOrientation = ToStrip((ScrollOrientation)n); });

    private static Melville.Pdf.ViewerModel.ScrollOrientation ToStrip(ScrollOrientation orientation) =>
        orientation == ScrollOrientation.Horizontal
            ? Melville.Pdf.ViewerModel.ScrollOrientation.Horizontal
            : Melville.Pdf.ViewerModel.ScrollOrientation.Vertical;

    /// <summary>Whether the built-in navigation and zoom toolbar is shown. Off by default: hosts usually bring their own
    /// controls and use the public commands.</summary>
    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar), typeof(bool), typeof(PdfView), false);

    /// <summary>
    /// The largest zoom as a multiple of fit width: 4 (the default) zooms in to four times the fit-width size. Values
    /// below 1 are ignored.
    /// </summary>
    public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
        nameof(MaxZoom), typeof(double), typeof(PdfView), 4.0,
        validateValue: (_, v) => (double)v >= 1,
        propertyChanged: (b, _, n) => { if (((PdfView)b).viewModel is { } vm) vm.MaxZoom = (double)n; });

    /// <summary>
    /// The smallest zoom as a multiple of fit width: 1 (the default) stops zooming out at fit width. Fit page stays
    /// reachable when it is smaller (a landscape view). Negative values are ignored.
    /// </summary>
    public static readonly BindableProperty MinZoomProperty = BindableProperty.Create(
        nameof(MinZoom), typeof(double), typeof(PdfView), 1.0,
        validateValue: (_, v) => (double)v >= 0,
        propertyChanged: (b, _, n) => { if (((PdfView)b).viewModel is { } vm) vm.MinZoom = (double)n; });

    /// <summary>The page shadow, margin and crop. Null (the default) is the same as a new <see cref="PageAppearance"/>.</summary>
    public static readonly BindableProperty PageAppearanceProperty = BindableProperty.Create(
        nameof(PageAppearance), typeof(PageAppearance), typeof(PdfView),
        propertyChanged: (b, o, n) => ((PdfView)b).OnPageAppearanceChanged((PageAppearance?)o, (PageAppearance?)n));

    /// <summary>Executed with a <see cref="PageChangedEventArgs"/> whenever <see cref="PageChanged"/> is raised.</summary>
    public static readonly BindableProperty PageChangedCommandProperty = BindableProperty.Create(
        nameof(PageChangedCommand), typeof(ICommand), typeof(PdfView));

    /// <summary>
    /// The page, zoom and scroll position, two-way. The viewer updates it as the user moves around, so a host can
    /// save it (for example when the window is stopped). Setting it moves the viewer there; set it before
    /// <see cref="Source"/> to open the next document at a saved position. Null when no document is open.
    /// </summary>
    public static readonly BindableProperty ViewStateProperty = BindableProperty.Create(
        nameof(ViewState), typeof(PdfViewState?), typeof(PdfView), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((PdfView)b).OnViewStateSet((PdfViewState?)n));

    /// <inheritdoc cref="SourceProperty"/>
    public PdfSource? Source { get => (PdfSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <inheritdoc cref="PasswordSourceProperty"/>
    public IPasswordSource? PasswordSource
    {
        get => (IPasswordSource?)GetValue(PasswordSourceProperty);
        set => SetValue(PasswordSourceProperty, value);
    }

    /// <inheritdoc cref="PageNumberProperty"/>
    public int PageNumber { get => (int)GetValue(PageNumberProperty); set => SetValue(PageNumberProperty, value); }

    /// <inheritdoc cref="PageCountProperty"/>
    public int PageCount { get => (int)GetValue(PageCountProperty); private set => SetValue(PageCountKey, value); }

    /// <inheritdoc cref="ZoomModeProperty"/>
    public ZoomMode ZoomMode { get => (ZoomMode)GetValue(ZoomModeProperty); set => SetValue(ZoomModeProperty, value); }

    /// <inheritdoc cref="IsLoadingProperty"/>
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); private set => SetValue(IsLoadingKey, value); }

    /// <inheritdoc cref="ErrorMessageProperty"/>
    public string? ErrorMessage { get => (string?)GetValue(ErrorMessageProperty); private set => SetValue(ErrorMessageKey, value); }

    /// <inheritdoc cref="DisplayModeProperty"/>
    public DisplayMode DisplayMode { get => (DisplayMode)GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }

    /// <inheritdoc cref="ScrollOrientationProperty"/>
    public ScrollOrientation ScrollOrientation
    {
        get => (ScrollOrientation)GetValue(ScrollOrientationProperty);
        set => SetValue(ScrollOrientationProperty, value);
    }

    /// <inheritdoc cref="MaxZoomProperty"/>
    public double MaxZoom { get => (double)GetValue(MaxZoomProperty); set => SetValue(MaxZoomProperty, value); }

    /// <inheritdoc cref="MinZoomProperty"/>
    public double MinZoom { get => (double)GetValue(MinZoomProperty); set => SetValue(MinZoomProperty, value); }

    /// <inheritdoc cref="PageAppearanceProperty"/>
    public PageAppearance? PageAppearance
    {
        get => (PageAppearance?)GetValue(PageAppearanceProperty);
        set => SetValue(PageAppearanceProperty, value);
    }

    private void OnPageAppearanceChanged(PageAppearance? old, PageAppearance? appearance)
    {
        if (old is not null) old.PropertyChanged -= OnPageAppearancePropertyChanged;
        if (appearance is not null) appearance.PropertyChanged += OnPageAppearancePropertyChanged;
        ApplyPageAppearance();
    }

    private void OnPageAppearancePropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyPageAppearance();

    private void ApplyPageAppearance()
    {
        var appearance = PageAppearance;
        drawable.ShadowEnabled = appearance?.ShadowEnabled ?? true;
        if (viewModel is { } vm) ApplyPageAppearance(vm);
        pageView?.Invalidate(); // null while the constructor runs
    }

    private void ApplyPageAppearance(ViewModel vm)
    {
        var m = PageAppearance?.Margin ?? PageAppearance.DefaultMargin;
        var c = PageAppearance?.Crop ?? Thickness.Zero;
        vm.PageMargin = new PageInsets(m.Left, m.Top, m.Right, m.Bottom);
        vm.PageCrop = new PageInsets(c.Left, c.Top, c.Right, c.Bottom);
    }

    /// <inheritdoc cref="PageChangedCommandProperty"/>
    public ICommand? PageChangedCommand
    {
        get => (ICommand?)GetValue(PageChangedCommandProperty);
        set => SetValue(PageChangedCommandProperty, value);
    }

    /// <summary>
    /// Raised when the page shown or the page count changes while a document is open: after opening, scrolling,
    /// navigating or searching. Also executes <see cref="PageChangedCommand"/>.
    /// </summary>
    public event EventHandler<PageChangedEventArgs>? PageChanged;

    /// <inheritdoc cref="ShowToolbarProperty"/>
    public bool ShowToolbar { get => (bool)GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }

    /// <inheritdoc cref="ViewStateProperty"/>
    public PdfViewState? ViewState { get => (PdfViewState?)GetValue(ViewStateProperty); set => SetValue(ViewStateProperty, value); }

    /// <summary>Go to the first page. Like the other commands, for menus and keyboard shortcuts.</summary>
    public ICommand FirstPageCommand { get; }

    /// <summary>Go to the previous page.</summary>
    public ICommand PreviousPageCommand { get; }

    /// <summary>Go to the next page.</summary>
    public ICommand NextPageCommand { get; }

    /// <summary>Go to the last page.</summary>
    public ICommand LastPageCommand { get; }

    /// <summary>Zoom in one step around the centre.</summary>
    public ICommand ZoomInCommand { get; }

    /// <summary>Zoom out one step around the centre.</summary>
    public ICommand ZoomOutCommand { get; }

    /// <summary>Show the whole page.</summary>
    public ICommand FitPageCommand { get; }

    /// <summary>Fit the page to the width.</summary>
    public ICommand FitWidthCommand { get; }

    /// <summary>
    /// The text of a page, as it is drawn: words separated by spaces and lines by line breaks ("\n"). Empty when no
    /// document is open, the page does not exist, or the page has no text (a scanned page without recognised text).
    /// </summary>
    /// <param name="pageNumber">The page; the first page is 1.</param>
    public async Task<string> GetPageTextAsync(int pageNumber) =>
        viewModel is { } vm ? (await vm.GetTextAsync(pageNumber)).Text : "";

    /// <summary>Whether the find bar is shown (the toolbar's Find button toggles it). Two-way. Hiding it clears the
    /// search.</summary>
    public static readonly BindableProperty IsSearchVisibleProperty = BindableProperty.Create(
        nameof(IsSearchVisible), typeof(bool), typeof(PdfView), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((PdfView)b).OnSearchVisibleChanged((bool)n));

    private static readonly BindablePropertyKey SearchResultCountKey = BindableProperty.CreateReadOnly(
        nameof(SearchResultCount), typeof(int), typeof(PdfView), 0);

    /// <summary>How many matches the current search has found so far; 0 when nothing is searched.</summary>
    public static readonly BindableProperty SearchResultCountProperty = SearchResultCountKey.BindableProperty;

    private static readonly BindablePropertyKey CurrentSearchResultKey = BindableProperty.CreateReadOnly(
        nameof(CurrentSearchResult), typeof(int), typeof(PdfView), 0);

    /// <summary>The match being shown, counting from 1 in document order; 0 when there is none.</summary>
    public static readonly BindableProperty CurrentSearchResultProperty = CurrentSearchResultKey.BindableProperty;

    private static readonly BindablePropertyKey IsSearchingKey = BindableProperty.CreateReadOnly(
        nameof(IsSearching), typeof(bool), typeof(PdfView), false);

    /// <summary>True while a search is still reading pages.</summary>
    public static readonly BindableProperty IsSearchingProperty = IsSearchingKey.BindableProperty;

    /// <inheritdoc cref="IsSearchVisibleProperty"/>
    public bool IsSearchVisible { get => (bool)GetValue(IsSearchVisibleProperty); set => SetValue(IsSearchVisibleProperty, value); }

    /// <inheritdoc cref="SearchResultCountProperty"/>
    public int SearchResultCount { get => (int)GetValue(SearchResultCountProperty); private set => SetValue(SearchResultCountKey, value); }

    /// <inheritdoc cref="CurrentSearchResultProperty"/>
    public int CurrentSearchResult { get => (int)GetValue(CurrentSearchResultProperty); private set => SetValue(CurrentSearchResultKey, value); }

    /// <inheritdoc cref="IsSearchingProperty"/>
    public bool IsSearching { get => (bool)GetValue(IsSearchingProperty); private set => SetValue(IsSearchingKey, value); }

    /// <summary>Show the next search match, wrapping to the first.</summary>
    public ICommand NextSearchResultCommand { get; }

    /// <summary>Show the previous search match, wrapping to the last.</summary>
    public ICommand PreviousSearchResultCommand { get; }

    /// <summary>
    /// Find text in the document and highlight every match, replacing the previous search. Case is ignored unless
    /// <paramref name="matchCase"/> is set, and white space in <paramref name="text"/> matches any space or line
    /// break, so phrases are found across lines. The first match on or after the current page is shown as soon as it
    /// is found; matches on later pages keep appearing until every page is read.
    /// </summary>
    /// <returns>The number of matches, once every page has been searched (or the search was replaced).</returns>
    public async Task<int> SearchAsync(string text, bool matchCase = false)
    {
        if (viewModel is not { } vm) return 0;
        var count = await Task.Run(() => vm.SearchAsync(text, matchCase));
        if (ReferenceEquals(vm, viewModel) && vm.SearchQuery == TextSearch.Normalize(text) && !vm.IsSearching)
            Announce(count switch { 0 => "No matches", 1 => "1 match", _ => $"{count} matches" });
        return count;
    }

    /// <summary>Remove the search highlights; a search still reading pages stops.</summary>
    public void ClearSearch() => viewModel?.ClearSearch();

    private static readonly BindablePropertyKey SelectedTextKey = BindableProperty.CreateReadOnly(
        nameof(SelectedText), typeof(string), typeof(PdfView), null);

    /// <summary>
    /// The selected text, with spaces between words and line breaks between lines, or null. The user selects by
    /// pressing and holding on a word, then dragging without lifting the finger; a tap clears the selection.
    /// </summary>
    public static readonly BindableProperty SelectedTextProperty = SelectedTextKey.BindableProperty;

    /// <inheritdoc cref="SelectedTextProperty"/>
    public string? SelectedText { get => (string?)GetValue(SelectedTextProperty); private set => SetValue(SelectedTextKey, value); }

    /// <summary>Copy the selected text to the clipboard.</summary>
    public ICommand CopySelectionCommand { get; }

    /// <summary>Select all the text of the current page.</summary>
    public ICommand SelectAllCommand { get; }

    /// <summary>Select nothing.</summary>
    public void ClearSelection() => viewModel?.ClearSelection();

    /// <summary>Raised on the UI thread when a document has opened and its first page is shown.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised on the UI thread when a document could not be opened.</summary>
    public event EventHandler<Exception>? LoadFailed;

    /// <summary>
    /// Raised on the UI thread when a link in the document is tapped, before the viewer follows it. Set
    /// <see cref="PdfLinkTappedEventArgs.Handled"/> to follow it yourself, or not at all.
    /// </summary>
    public event EventHandler<PdfLinkTappedEventArgs>? LinkTapped;

    // ---- Implementation ---------------------------------------------------------------------------------------

    private const uint BitmapDelayMs = 150;

    private readonly GraphicsView pageView;
    private readonly PdfPageDrawable drawable = new();
    private readonly Grid body;
    private readonly Label error;
    private ViewModel? viewModel;
    private CancellationTokenSource? loading;
    private int bitmapVersion;
    private Window? window;
    private readonly List<ForwardingCommand> commands = [];
    private bool updatingViewState;     // the viewer, not the host, is setting ViewState
    private PdfViewState? requestedViewState; // set by the host; applied when the next document opens

    /// <summary>Create a viewer with no document.</summary>
    public PdfView()
    {
        FirstPageCommand = Forward(vm => vm.PageSelector.ToStartCommand);
        PreviousPageCommand = Forward(vm => vm.PageSelector.DecrementCommand);
        NextPageCommand = Forward(vm => vm.PageSelector.IncrementCommand);
        LastPageCommand = Forward(vm => vm.PageSelector.ToEndCommand);
        ZoomInCommand = Forward(vm => vm.ZoomInCommand);
        ZoomOutCommand = Forward(vm => vm.ZoomOutCommand);
        FitPageCommand = Forward(vm => vm.FitPageCommand);
        FitWidthCommand = Forward(vm => vm.FitWidthCommand);
        NextSearchResultCommand = Forward(vm => vm.NextMatchCommand);
        PreviousSearchResultCommand = Forward(vm => vm.PreviousMatchCommand);
        SelectAllCommand = Forward(vm => vm.SelectAllCommand);
        CopySelectionCommand = new Command(async () => await CopySelectionAsync(), () => SelectedText is not null);

        pageView = new GraphicsView { Drawable = drawable, AutomationId = "PdfView.Page" };
        SemanticProperties.SetDescription(pageView, "No document");
        SemanticProperties.SetHint(pageView, "Pinch or use the zoom buttons to zoom, drag to scroll, tap a link to follow it");
        pageView.SizeChanged += (_, _) => viewModel?.SetViewport(pageView.Width, pageView.Height);
        AddGestures(pageView);
        pageView.HandlerChanged += (_, _) => AttachPlatformGestures(pageView);

        var spinner = new ActivityIndicator { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        spinner.SetBinding(ActivityIndicator.IsRunningProperty, static (PdfView v) => v.IsLoading, source: this);
        spinner.SetBinding(IsVisibleProperty, static (PdfView v) => v.IsLoading, source: this);

        error = new Label
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center, Margin = 24,
        };
        // Dark red is unreadable on the dark-theme background.
        error.SetAppThemeColor(Label.TextColorProperty, Colors.DarkRed, Color.FromArgb("#FF8A80"));

        var content = new Grid { Children = { pageView, spinner, error, CreateSelectionBar(), CreateLayersPanel() } };
        var toolbar = CreateToolbar();
        toolbar.SetBinding(IsVisibleProperty, static (PdfView v) => v.ShowToolbar, source: this);
        content.Add(moreMenu);
        body = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        body.Add(toolbar, 0, 0);
        body.Add(CreateSearchBar(), 0, 1);
        body.Add(content, 0, 2);
        Content = body;

        UpdatePageBackground();
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => UpdatePageBackground();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // Screen reader announcements; plain .NET (unit tests, tools) has no screen reader, and Essentials throws there.
    private static void Announce(string text)
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        SemanticScreenReader.Default.Announce(text);
#endif
    }

    private void ShowError(string? message)
    {
        error.Text = message;
        error.IsVisible = message is not null;
        if (message is null) return;
        Announce(message);
        if (error.Handler is not null) error.SetSemanticFocus(); // needs the platform view
    }

    /// <summary>
    /// Behind the pages: the view's <see cref="VisualElement.BackgroundColor"/> when set, otherwise white (dark grey in
    /// the dark theme). Pages stand out by their shadow.
    /// </summary>
    private void UpdatePageBackground()
    {
        drawable.Background = BackgroundColor
            ?? ((Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Dark ? Color.FromArgb("#1E1E1E") : Colors.White);
        pageView?.Invalidate(); // null while the constructor runs
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == BackgroundColorProperty.PropertyName) UpdatePageBackground();
        else if (propertyName == PageNumberProperty.PropertyName || propertyName == PageCountProperty.PropertyName)
            RaisePageChanged();
    }

    private (int Page, int Count) lastPageChanged;

    private void RaisePageChanged()
    {
        if (PageCount <= 0)
        {
            lastPageChanged = default; // closed: the next document reports its first page again
            return;
        }
        if ((PageNumber, PageCount) == lastPageChanged) return;
        lastPageChanged = (PageNumber, PageCount);
        var args = new PageChangedEventArgs(PageNumber, PageCount);
        PageChanged?.Invoke(this, args);
        if (PageChangedCommand is { } command && command.CanExecute(args)) command.Execute(args);
    }

    private async void OnSourceChanged()
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref loading, cancellation)?.Cancel();
        await CloseDocumentAsync();
        if (Source is not { } source) return;

        IsLoading = true;
        ErrorMessage = null;
        Announce("Opening document");
        try
        {
            var fonts = Handler?.MauiContext?.Services.GetService<IDefaultFontMapper>() ?? SelfContainedDefaultFonts.Instance;
            var passwords = PasswordSource;
            var opened = await Task.Run(async () =>
            {
                byte[] bytes;
                await using (var stream = await source.OpenAsync(cancellation.Token))
                {
                    using var copy = new MemoryStream();
                    await stream.CopyToAsync(copy, cancellation.Token);
                    bytes = copy.ToArray();
                }
                var document = await new PdfReader(passwords, fonts).ReadFromAsync(bytes);
                try
                {
                    return await ViewModel.CreateAsync(document, new MauiGraphicsPageRenderer()); // owns it from here
                }
                catch
                {
                    document.Dispose(); // otherwise leaked (Melville's debug builds throw from the finalizer)
                    throw;
                }
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested)
            {
                await opened.DisposeAsync();
                return;
            }
            ShowDocument(opened);
            IsLoading = false; // before the event, so handlers see the loaded state
            Announce(
                opened.PageCount == 1 ? "Document opened, 1 page" : $"Document opened, {opened.PageCount} pages");
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            requestedViewState = null; // it was meant for this document
            if (ReferenceEquals(loading, cancellation)) IsLoading = false;
            ErrorMessage = $"This document could not be opened: {e.Message}";
            LoadFailed?.Invoke(this, e);
        }
        finally
        {
            if (ReferenceEquals(loading, cancellation)) IsLoading = false;
        }
    }

    private void ShowDocument(ViewModel opened)
    {
        viewModel = opened;
        body.BindingContext = opened;
        // A custom zoom belongs to the document it was chosen for; a new document starts fitted, unless the host
        // asked for a saved position.
        if (ZoomMode == ZoomMode.Custom) ZoomMode = ZoomMode.FitWidth;
        opened.ZoomMode = ZoomMode.ToModel();
        opened.MinZoom = MinZoom;
        opened.MaxZoom = MaxZoom;
        ApplyPageAppearance(opened);
        opened.PropertyChanged += OnViewModelChanged;
        opened.PageSelector.PropertyChanged += OnPageSelectorChanged;
        PageCount = opened.PageCount;
        opened.SetViewport(pageView.Width, pageView.Height);
        opened.ScrollOrientation = ToStrip(ScrollOrientation);
        opened.DisplayMode = DisplayMode.ToModel();
        if (requestedViewState is { } requested)
        {
            requestedViewState = null;
            opened.Restore(requested.ToModel());
            ZoomMode = opened.ZoomMode.ToPublic();
        }
        PageNumber = opened.PageSelector.Page;
        PublishViewState();
        foreach (var command in commands) command.Retarget();
        drawable.Matches = opened.MatchesOn;
        UpdatePage();
        // The find bar stays open across documents: search the new one for the same text.
        if (IsSearchVisible && !string.IsNullOrWhiteSpace(searchBar.Text)) _ = SearchAsync(searchBar.Text);
    }

    private async Task CloseDocumentAsync()
    {
        if (viewModel is not { } old) return;
        viewModel = null;
        old.PropertyChanged -= OnViewModelChanged;
        old.PageSelector.PropertyChanged -= OnPageSelectorChanged;
        body.BindingContext = null;
        PageCount = 0;
        foreach (var command in commands) command.Retarget();
        updatingViewState = true;
        ViewState = null;
        updatingViewState = false;
        ClearBitmaps();
        drawable.Placements = [];
        drawable.Matches = null;
        drawable.CurrentMatch = null;
        UpdateSelection(null);
        describedPage = 0;
        UpdateSearchState(null);
        pageView.Invalidate();
        await old.DisposeAsync();
    }

    // View-model changes can arrive on a worker thread (rendering); the drawable is only touched on the UI thread.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.VisiblePages):
                Dispatcher.Dispatch(UpdatePage);
                break;
            case nameof(ViewModel.RenderError):
                Dispatcher.Dispatch(() => ErrorMessage = viewModel?.RenderError);
                break;
            case nameof(ViewModel.ZoomMode):
                Dispatcher.Dispatch(() => { if (viewModel is { } vm) ZoomMode = vm.ZoomMode.ToPublic(); });
                break;
            case nameof(ViewModel.ViewState):
                Dispatcher.Dispatch(PublishViewState);
                break;
            case nameof(ViewModel.Selection):
                Dispatcher.Dispatch(() => { if (ReferenceEquals(sender, viewModel)) UpdateSelection(viewModel!.Selection); });
                break;
            case nameof(ViewModel.SearchMatches) or nameof(ViewModel.CurrentMatchIndex) or nameof(ViewModel.IsSearching)
                or nameof(ViewModel.SearchedPages):
                Dispatcher.Dispatch(() => { if (ReferenceEquals(sender, viewModel)) UpdateSearchState(viewModel); });
                break;
        }
    }

    private void OnPageSelectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageSelectorViewModel.Page)) return;
        Dispatcher.Dispatch(() =>
        {
            if (viewModel is not { } vm) return;
            PageNumber = vm.PageSelector.Page;
            Announce($"Page {vm.PageSelector.Page} of {vm.PageCount}");
            DescribePage(vm);
        });
    }

    private void OnDisplayModeChanged(DisplayMode mode)
    {
        if (viewModel is { } vm) vm.DisplayMode = mode.ToModel();
        UpdateDisplayModeButton(mode);
    }

    private void UpdateDisplayModeButton(DisplayMode mode)
    {
        if (displayModeButton is not { } button) return;
        // The button names the mode it switches to.
        button.Text = mode == DisplayMode.Continuous ? "One page at a time" : "Scroll all pages";
        SemanticProperties.SetDescription(button, mode == DisplayMode.Continuous ? "Show one page at a time" : "Scroll through all pages");
    }

    private Button? displayModeButton;

    private void PublishViewState()
    {
        if (viewModel is not { } vm) return;
        updatingViewState = true;
        try { ViewState = vm.ViewState.ToPublic(); }
        finally { updatingViewState = false; }
    }

    private void OnViewStateSet(PdfViewState? state)
    {
        if (updatingViewState || state is not { } requested) return;
        if (viewModel is { } vm && !IsLoading)
        {
            vm.Restore(requested.ToModel());
            ZoomMode = vm.ZoomMode.ToPublic();
        }
        else
        {
            requestedViewState = requested; // applied by ShowDocument
        }
    }

    private ForwardingCommand Forward(Func<ViewModel, ICommand> target)
    {
        var command = new ForwardingCommand(() => viewModel is { } vm ? target(vm) : null, Dispatcher);
        commands.Add(command);
        return command;
    }

    /// <summary>
    /// A stable command (for the toolbar and the host) that runs the open document's command, if any. It raises
    /// CanExecuteChanged on the UI thread: the view model raises it from whatever thread changed a property, often the
    /// render thread, and a Button that updates IsEnabled there throws on Android ("Only the original thread that
    /// created a view hierarchy can touch its views").
    /// </summary>
    private sealed class ForwardingCommand(Func<ICommand?> target, IDispatcher dispatcher) : ICommand
    {
        private ICommand? current;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => current?.CanExecute(parameter) ?? false;

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter)) current!.Execute(parameter);
        }

        public void Retarget()
        {
            if (current is not null) current.CanExecuteChanged -= OnTargetChanged;
            current = target();
            if (current is not null) current.CanExecuteChanged += OnTargetChanged;
            Raise();
        }

        private void OnTargetChanged(object? sender, EventArgs e)
        {
            if (dispatcher.IsDispatchRequired) dispatcher.Dispatch(Raise);
            else Raise();
        }

        private void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPageNumberChanged(int page)
    {
        if (viewModel is { } vm && vm.PageSelector.Page != page) vm.PageSelector.Page = page;
    }

    private void UpdatePage()
    {
        if (viewModel is not { } vm) return;
        var placements = vm.VisiblePages;
        // A page whose rendering changed (another page, a re-render after a layer toggle) loses its bitmaps; a page
        // that appears without one is blank until it has one, so bitmaps then start at once.
        var newContent = false;
        foreach (var placement in placements)
        {
            var hasBitmap = drawable.Bitmaps.TryGetValue(placement.Page, out var bitmaps) &&
                            ReferenceEquals((bitmaps.Backdrop ?? bitmaps.Detail)?.Page, placement.Rendered?.Content);
            if (!hasBitmap && placement.Rendered is not null) newContent = true;
        }
        drawable.Placements = placements;
        DescribePage(vm);
        pageView.Invalidate();
        ScheduleBitmap(immediate: newContent);
    }

    // ---- Bitmap cache -----------------------------------------------------------------------------------------

    private async void ScheduleBitmap(bool immediate = false)
    {
        var version = ++bitmapVersion;
        if (!immediate) await Task.Delay(TimeSpan.FromMilliseconds(BitmapDelayMs));
        else await Task.Yield(); // let the layout settle (viewport size) before planning
        if (version != bitmapVersion || window is null) return;

        var placements = drawable.Placements.Where(p => p.Rendered is not null).ToList();
        // Forget bitmaps of pages that left the view (they are rebuilt if the page comes back).
        foreach (var gone in drawable.Bitmaps.Keys.Where(k => placements.All(p => p.Page != k)).ToList())
        {
            drawable.Bitmaps[gone].Dispose();
            drawable.Bitmaps.Remove(gone);
        }
        var density = DeviceDisplay.Current.MainDisplayInfo.Density is > 0 and var d ? d : 1;
        // The pixel budget is shared by the pages in view (several pages at fit width in continuous mode).
        var budget = Math.Max(PageBitmap.MaxPixels / Math.Max(1, placements.Count), PageBitmap.MaxPixels / 4);
        // Nearest the centre first: that is where the user looks.
        var centre = pageView.Height / 2;
        foreach (var placement in placements.OrderBy(p => Math.Abs(p.Layout.OffsetY + p.Layout.ContentHeight / 2 - centre)))
        {
            if (version != bitmapVersion) return;
            await UpdatePageBitmapsAsync(placement, density, budget, version);
        }
    }

    private async Task UpdatePageBitmapsAsync(PagePlacement<PdfDisplayList> placement, double density, long budget, int version)
    {
        var page = placement.Rendered!.Content;
        var layout = placement.Layout.Uncropped(page.PageWidth, page.PageHeight); // bitmaps hold whole pages
        var plan = BitmapPlan.Compute(page.PageWidth, page.PageHeight, layout, pageView.Width, pageView.Height, density, budget);
        if (plan.BackdropScale <= 0) return;
        var visible = BitmapPlan.Visible(page.PageWidth, page.PageHeight, layout, pageView.Width, pageView.Height);
        if (!drawable.Bitmaps.TryGetValue(placement.Page, out var bitmaps)) drawable.Bitmaps[placement.Page] = bitmaps = new PageBitmaps();
        var keepBackdrop = bitmaps.Backdrop is { } backdrop && ReferenceEquals(backdrop.Page, page) &&
                           backdrop.Scale == plan.BackdropScale;
        var keepDetail = plan.Detail is null
            ? bitmaps.Detail is null
            : bitmaps.Detail is { } detail && ReferenceEquals(detail.Page, page) && detail.Scale == plan.Scale &&
              detail.Region.Contains(visible);
        if (keepBackdrop && keepDetail) return;

        PageBitmap? newBackdrop = null, newDetail = null;
        try
        {
            (newBackdrop, newDetail) = await Task.Run(() => (
                keepBackdrop ? null : PageBitmap.Create(page, plan.BackdropScale, density),
                keepDetail || plan.Detail is not { } region ? null : PageBitmap.Create(page, plan.Scale, density, region)));
        }
        catch (Exception)
        {
            // Out of memory or a platform failure: keep what is cached, and draw from the display list without one.
            drawable.ReplayWithoutBitmap = true;
        }
        // Superseded, or the page left the view or was re-rendered while drawing: throw the result away.
        if (version != bitmapVersion || !drawable.Bitmaps.TryGetValue(placement.Page, out var current) ||
            !ReferenceEquals(current, bitmaps) ||
            drawable.Placements.FirstOrDefault(p => p.Page == placement.Page)?.Rendered?.Content is not { } shown ||
            !ReferenceEquals(shown, page))
        {
            newBackdrop?.Dispose();
            newDetail?.Dispose();
            return;
        }
        if (!keepBackdrop) Replace(bitmaps.Backdrop, newBackdrop, b => bitmaps.Backdrop = b);
        if (!keepDetail) Replace(bitmaps.Detail, newDetail, b => bitmaps.Detail = b);
        pageView.Invalidate();
    }

    // Safe to dispose at once: drawing also happens on this (UI) thread.
    private static void Replace(PageBitmap? old, PageBitmap? replacement, Action<PageBitmap?> set)
    {
        set(replacement);
        if (!ReferenceEquals(old, replacement)) old?.Dispose();
    }

    private void ClearBitmaps()
    {
        foreach (var bitmaps in drawable.Bitmaps.Values) bitmaps.Dispose();
        drawable.Bitmaps.Clear();
        drawable.ResetReplay();
    }

    // Free the bitmaps while the app is in the background; they are rebuilt when the window is resumed.
    private void OnLoaded(object? sender, EventArgs e)
    {
        window = Window;
        if (window is null) return;
        window.Stopped += OnWindowStopped;
        window.Resumed += OnWindowResumed;
        window.Destroying += OnWindowDestroying;
        ScheduleBitmap(immediate: true);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (window is not null)
        {
            window.Stopped -= OnWindowStopped;
            window.Resumed -= OnWindowResumed;
            window.Destroying -= OnWindowDestroying;
        }
        window = null;
        ClearBitmaps();
    }

    private void OnWindowStopped(object? sender, EventArgs e) => ClearBitmaps();

    private void OnWindowResumed(object? sender, EventArgs e) => ScheduleBitmap(immediate: true);

    // The window is going away (closed on desktop, or the Android activity finishing): release the document now
    // rather than when the control is collected. Hosts save ViewState before this, in their own Destroying handler.
    private async void OnWindowDestroying(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref loading, null)?.Cancel();
        await CloseDocumentAsync();
    }

    // ---- Gestures ---------------------------------------------------------------------------------------------

    private double panX, panY;

    private void AddGestures(GraphicsView view)
    {
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            // Scale is the change since the previous update; ScaleOrigin is relative to the view's size.
            if (e.Status == GestureStatus.Started) CancelPress();
            if (e.Status == GestureStatus.Running)
                viewModel?.ZoomAt(e.Scale, e.ScaleOrigin.X * view.Width, e.ScaleOrigin.Y * view.Height);
        };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panX = panY = 0;
                    if (!selectingByDrag) CancelPress(); // moving before the hold ends is a pan
                    break;
                case GestureStatus.Running when selectingByDrag:
                    // The finger that pressed and held now drags the end of the selection.
                    _ = viewModel?.ExtendSelectionToAsync(pressPoint.X + e.TotalX, pressPoint.Y + e.TotalY);
                    break;
                case GestureStatus.Running:
                    viewModel?.PanBy(e.TotalX - panX, e.TotalY - panY);
                    (panX, panY) = (e.TotalX, e.TotalY);
                    break;
                case GestureStatus.Completed or GestureStatus.Canceled:
                    selectingByDrag = false;
                    break;
            }
        };

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, e) =>
        {
            if (viewModel is not { } vm) return;
            if (vm.ZoomMode != ModelZoomMode.FitWidth)
                vm.ZoomMode = ModelZoomMode.FitWidth;
            else if (e.GetPosition(view) is { } point)
                vm.ZoomAt(2, point.X, point.Y);
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, e) =>
        {
            if (suppressTap)
            {
                suppressTap = false; // the end of a press and hold
                return;
            }
            if (e.GetPosition(view) is { } point && await FollowLinkAtAsync(point.X, point.Y)) return;
            viewModel?.ClearSelection();
        };

        // Press and hold selects; MAUI has no long-press recognizer, so a pointer press starts a timer that a
        // release, a pan or a pinch cancels.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, e) =>
        {
            if (e.GetPosition(view) is { } point) StartPress(point);
        };
        pointer.PointerReleased += (_, _) =>
        {
            CancelPress();
            selectingByDrag = false;
        };

        view.GestureRecognizers.Add(pinch);
        view.GestureRecognizers.Add(pan);
        view.GestureRecognizers.Add(doubleTap);
        view.GestureRecognizers.Add(tap);
        view.GestureRecognizers.Add(pointer);
    }

    // ---- Links ------------------------------------------------------------------------------------------------

    // Addresses opened without asking the app: web pages and mail. Anything else (file:, javascript:, custom app
    // schemes) is only reported through LinkTapped, because a document should not launch arbitrary things.
    private static readonly string[] OpenedSchemes = ["http", "https", "mailto"];

    /// <summary>
    /// Follow the link under a point of the page view, if there is one: raise <see cref="LinkTapped"/>, then
    /// show its page or open its address unless the app handled it. Returns whether there was a link.
    /// </summary>
    internal async Task<bool> FollowLinkAtAsync(double x, double y)
    {
        if (viewModel is not { } vm) return false;
        PdfLinkTarget? target;
        try
        {
            target = await vm.LinkAtAsync(x, y);
        }
        catch (Exception)
        {
            return false; // a document that cannot be read here has no usable links here
        }
        if (target is null || !ReferenceEquals(vm, viewModel)) return false;

        var args = target switch
        {
            PdfPageLinkTarget page => new PdfLinkTappedEventArgs(page.OneBasedPageNumber, null),
            PdfUriLinkTarget uri => new PdfLinkTappedEventArgs(null, uri.Uri),
            _ => null,
        };
        if (args is null) return false;
        LinkTapped?.Invoke(this, args);
        if (args.Handled) return true;

        if (target is PdfPageLinkTarget destination)
        {
            vm.GoTo(destination);
            Announce($"Page {destination.OneBasedPageNumber} of {vm.PageCount}");
        }
        else if (args.Uri is { } address && Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) &&
                 OpenedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            await OpenAsync(uri);
        }
        return true;
    }

    private static async Task OpenAsync(Uri uri)
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // No browser or mail app; there is nothing more the viewer can do.
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>Platform-specific input such as trackpad and mouse-wheel scrolling.</summary>
    partial void AttachPlatformGestures(GraphicsView view);

    /// <summary>Scroll the page by a wheel or trackpad delta, in view units (used by platform input).</summary>
    internal void ScrollBy(double dx, double dy) => viewModel?.PanBy(dx, dy);

    /// <summary>Zoom by a factor around a point of the page view (used by platform input such as Ctrl + wheel).</summary>
    internal void ZoomBy(double factor, double x, double y) => viewModel?.ZoomAt(factor, x, y);

    // ---- Toolbar and layers -----------------------------------------------------------------------------------

    private View CreateToolbar()
    {
        var previous = ToolbarButton("‹", "Previous page");
        previous.Command = PreviousPageCommand;
        var next = ToolbarButton("›", "Next page");
        next.Command = NextPageCommand;

        var page = new Entry
        {
            // Minimum, not fixed, width so large accessibility text sizes still fit.
            Keyboard = Keyboard.Numeric, MinimumWidthRequest = 48, MinimumHeightRequest = 40, FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center,
            AutomationId = "PdfView.PageNumber",
        };
        // A hint rather than a description or placeholder: Description breaks TalkBack's edit action on Android,
        // and Placeholder would compete with Hint for the same Android attribute.
        SemanticProperties.SetHint(page, "Page number. Type a page to go to it");
        // One way, committed on Enter or when focus leaves: a two-way binding would jump to page 1 while "12" is
        // typed, and its converter needs Binding.DoNothing, which is not trim-safe.
        page.SetBinding(Entry.TextProperty, static (ViewModel vm) => vm.PageSelector.Page, BindingMode.OneWay,
            converter: IntTextConverter.Instance);
        page.Completed += (_, _) => CommitPageEntry(page);
        page.Unfocused += (_, _) => CommitPageEntry(page);
        var count = new Label { VerticalOptions = LayoutOptions.Center, FontSize = 15, Margin = new Thickness(0, 0, 4, 0) };
        count.SetBinding(Label.TextProperty, static (ViewModel vm) => vm.PageCount, stringFormat: "of {0}");
        count.SetAppThemeColor(Label.TextColorProperty, ToolbarText.Light, ToolbarText.Dark);

        var zoomOut = ToolbarButton("−", "Zoom out");
        zoomOut.Command = ZoomOutCommand;
        var zoomIn = ToolbarButton("+", "Zoom in");
        zoomIn.Command = ZoomInCommand;
        var more = ToolbarButton("⋯", "More actions");
        more.Clicked += (_, _) => SetMoreMenuVisible(!moreMenu.IsVisible);

        CreateMoreMenu();

        var navigation = new HorizontalStackLayout { Spacing = 0, Children = { previous, page, count, next } };
        var zoom = new HorizontalStackLayout { Spacing = 0, Children = { zoomOut, zoomIn, more } };
        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            Padding = new Thickness(4, 2),
        };
        row.Add(navigation, 0);
        row.Add(zoom, 2);
        var bar = new Border { Content = row, StrokeThickness = 0, Padding = 0 };
        bar.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F6F6F6"), Color.FromArgb("#262626"));
        var divider = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        divider.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#E0E0E0"), Color.FromArgb("#3A3A3A"));
        return new Grid { Children = { bar, divider } };
    }

    private static readonly (Color Light, Color Dark) ToolbarText = (Color.FromArgb("#202020"), Color.FromArgb("#EDEDED"));

    // A flat button with its own explicit style, so the app's implicit Button style (filled, coloured, large) does not
    // apply. AutomationId can be set only once: pass `id` for buttons whose description changes later.
    private static Button ToolbarButton(string text, string description, string? id = null)
    {
        var button = new Button
        {
            Style = ToolbarButtonStyle.Value, Text = text,
            AutomationId = "PdfView." + (id ?? description.Replace(" ", "")),
        };
        button.SetAppThemeColor(Button.TextColorProperty, ToolbarText.Light, ToolbarText.Dark);
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static readonly Lazy<Style> ToolbarButtonStyle = new(() =>
    {
        var style = new Style(typeof(Button))
        {
            Setters =
            {
                new Setter { Property = Button.BackgroundColorProperty, Value = Colors.Transparent },
                new Setter { Property = Button.BorderWidthProperty, Value = 0.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 8 },
                new Setter { Property = Button.FontSizeProperty, Value = 20.0 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(8, 0) },
                // 44 x 44: the smallest comfortable touch target (Apple HIG; Android asks for 48dp).
                new Setter { Property = MinimumWidthRequestProperty, Value = 44.0 },
                new Setter { Property = MinimumHeightRequestProperty, Value = 44.0 },
                new Setter { Property = Button.ShadowProperty, Value = null },
            },
        };
        var disabled = new VisualState { Name = VisualStateManager.CommonStates.Disabled };
        disabled.Setters.Add(new Setter { Property = OpacityProperty, Value = 0.35 });
        var normal = new VisualState { Name = VisualStateManager.CommonStates.Normal };
        normal.Setters.Add(new Setter { Property = OpacityProperty, Value = 1.0 });
        var pressed = new VisualState { Name = "Pressed" }; // Button's pressed state, in the common states group
        pressed.Setters.Add(new Setter { Property = OpacityProperty, Value = 0.6 });
        var states = new VisualStateGroup { Name = nameof(VisualStateManager.CommonStates) };
        states.States.Add(normal);
        states.States.Add(disabled);
        states.States.Add(pressed);
        style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = new VisualStateGroupList { states } });
        return style;
    });

    private Border moreMenu = null!;

    // The actions that do not fit the toolbar row, in a menu under its right end.
    private void CreateMoreMenu()
    {
        Button Item(string text, string description, Action act, string? id = null)
        {
            var button = ToolbarButton(text, description, id);
            button.FontSize = 15;
            button.HorizontalOptions = LayoutOptions.Fill;
            button.Padding = new Thickness(16, 0);
            button.Clicked += (_, _) =>
            {
                SetMoreMenuVisible(false);
                act();
            };
            return button;
        }

        Button Command(string text, string description, ICommand command)
        {
            var button = Item(text, description, () => { });
            button.Command = command;
            return button;
        }

        displayModeButton = Item("", "", () =>
        {
            DisplayMode = DisplayMode == DisplayMode.Continuous ? DisplayMode.SinglePage : DisplayMode.Continuous;
            Announce(DisplayMode == DisplayMode.Continuous ? "Continuous scroll" : "One page at a time");
        }, id: "Displaymode");
        UpdateDisplayModeButton(DisplayMode);

        var find = Item("Find", "Find in document", () => IsSearchVisible = !IsSearchVisible);
        var layers = Item("Layers", "Show layers", () => { });
        layers.SetBinding(IsVisibleProperty, static (ViewModel vm) => vm.Layers, converter: NotEmptyConverter.Instance);
        layers.Clicked += (_, _) =>
        {
            layersPanel.IsVisible = !layersPanel.IsVisible;
            SemanticProperties.SetDescription(layers, layersPanel.IsVisible ? "Hide layers" : "Show layers");
            Announce(layersPanel.IsVisible ? "Layers shown" : "Layers hidden");
        };

        moreMenu = new Border
        {
            IsVisible = false, StrokeThickness = 0, Padding = new Thickness(0, 4), Margin = new Thickness(0, 4, 8, 0),
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, MinimumWidthRequest = 200,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Opacity = 0.3f, Radius = 12, Offset = new Point(0, 4) },
            Content = new VerticalStackLayout
            {
                Children =
                {
                    Command("First page", "First page", FirstPageCommand),
                    Command("Last page", "Last page", LastPageCommand),
                    Command("Fit page", "Fit page", FitPageCommand),
                    Command("Fit width", "Fit width", FitWidthCommand),
                    displayModeButton, find, layers,
                },
            },
        };
        moreMenu.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D2D"));
    }

    private void SetMoreMenuVisible(bool visible)
    {
        moreMenu.IsVisible = visible;
        Announce(visible ? "More actions shown" : "More actions hidden");
    }

    // ---- Selection ----------------------------------------------------------------------------------------------

    private const int LongPressMs = 500;
    private Point pressPoint;
    private int pressVersion;
    private bool selectingByDrag;
    private bool suppressTap;
    private Border selectionBar = null!;

    private async void StartPress(Point point)
    {
        pressPoint = point;
        var version = ++pressVersion;
        suppressTap = false;
        await Task.Delay(LongPressMs);
        if (version != pressVersion || viewModel is not { } vm) return;
        await SelectWordAtAsync(point.X, point.Y);
        if (version != pressVersion || vm.Selection is null) return;
        selectingByDrag = true;
        suppressTap = true;
        Vibrate();
    }

    private void CancelPress() => pressVersion++;

    /// <summary>Select the word under a point of the page view, as a press and hold does.</summary>
    internal async Task<bool> SelectWordAtAsync(double x, double y)
    {
        if (viewModel is not { } vm) return false;
        try
        {
            return await vm.SelectWordAtAsync(x, y);
        }
        catch (Exception)
        {
            return false; // a page whose text cannot be read has nothing to select
        }
    }

    private static void Vibrate()
    {
#if ANDROID || IOS
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
        catch (Exception) { } // no haptics, or no permission
#endif
    }

    private async Task CopySelectionAsync()
    {
        if (SelectedText is not { } text) return;
#if ANDROID || IOS || MACCATALYST || WINDOWS
        await Clipboard.Default.SetTextAsync(text);
#else
        await Task.CompletedTask;
#endif
        Announce("Copied");
    }

    private View CreateSelectionBar()
    {
        static Button BarButton(string text, string description, string id)
        {
            var button = new Button
            {
                Text = text, Padding = new Thickness(10, 4), MinimumWidthRequest = 44, MinimumHeightRequest = 44,
                AutomationId = "PdfView." + id,
            };
            SemanticProperties.SetDescription(button, description);
            return button;
        }

        var copy = BarButton("Copy", "Copy the selected text", "Copy");
        copy.Command = CopySelectionCommand;
        var page = BarButton("Select page", "Select all the text on this page", "SelectPage");
        page.Command = SelectAllCommand;
        var close = BarButton("✕", "Clear the selection", "ClearSelection");
        close.Clicked += (_, _) => ClearSelection();

        selectionBar = new Border
        {
            IsVisible = false, Padding = 4, Margin = 8, StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Shadow = new Shadow { Opacity = 0.3f, Radius = 8, Offset = new Point(0, 2) },
            Content = new HorizontalStackLayout { Spacing = 4, Children = { copy, page, close } },
        };
        selectionBar.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D2D"));
        return selectionBar;
    }

    private void UpdateSelection(TextSelection? selection)
    {
        var hadSelection = SelectedText is not null;
        SelectedText = selection?.Text;
        drawable.Selection = selection;
        selectionBar.IsVisible = selection is not null;
        ((Command)CopySelectionCommand).ChangeCanExecute();
        pageView.Invalidate();
        if (selection is not null && !hadSelection) Announce("Text selected");
    }

    // ---- Page text for screen readers ---------------------------------------------------------------------------

    private int describedPage;

    // The page view's description is the page number followed by the page's text, so a screen reader that focuses
    // the page reads what is on it. The text is read once per page shown.
    private async void DescribePage(ViewModel vm)
    {
        var page = vm.PageSelector.Page;
        if (page == describedPage) return;
        describedPage = page;
        var heading = $"Page {page} of {vm.PageCount}";
        SemanticProperties.SetDescription(pageView, heading);
        string text;
        try
        {
            text = (await vm.GetTextAsync(page)).Text;
        }
        catch (Exception)
        {
            return;
        }
        if (!ReferenceEquals(vm, viewModel) || describedPage != page) return;
        SemanticProperties.SetDescription(pageView,
            text.Length == 0 ? $"{heading}. No text on this page." : $"{heading}. {text.Replace('\n', ' ')}");
    }

    // ---- Search ------------------------------------------------------------------------------------------------

    private const int SearchDelayMs = 350;
    private SearchBar searchBar = null!;
    private Label searchStatus = null!;
    private View searchRow = null!;
    private int searchVersion;
    private int announcedMatch = -1;

    private View CreateSearchBar()
    {
        searchBar = new SearchBar { Placeholder = "Find in document", AutomationId = "PdfView.SearchText" };
        searchBar.TextChanged += (_, _) => ScheduleSearch();
        // Enter searches at once; pressed again for the same text, it moves to the next match.
        searchBar.SearchButtonPressed += (_, _) =>
        {
            if (viewModel is { } vm && vm.SearchQuery.Length > 0 && vm.SearchQuery == TextSearch.Normalize(searchBar.Text ?? ""))
            {
                NextSearchResultCommand.Execute(null);
                return;
            }
            searchVersion++;
            _ = SearchAsync(searchBar.Text ?? "");
        };

        searchStatus = new Label { VerticalOptions = LayoutOptions.Center, Margin = new Thickness(4, 0), AutomationId = "PdfView.SearchStatus" };

        static Button SearchButton(string text, string description, string id, ICommand? command = null)
        {
            var button = new Button
            {
                Text = text, Padding = new Thickness(10, 4), MinimumWidthRequest = 44, MinimumHeightRequest = 44,
                AutomationId = "PdfView." + id, Command = command,
            };
            SemanticProperties.SetDescription(button, description);
            return button;
        }

        var previous = SearchButton("<", "Previous match", "PreviousMatch", PreviousSearchResultCommand);
        var next = SearchButton(">", "Next match", "NextMatch", NextSearchResultCommand);
        var close = SearchButton("Done", "Close find", "CloseFind");
        close.Clicked += (_, _) => IsSearchVisible = false;

        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
            Padding = new Thickness(4, 0, 4, 4),
            IsVisible = false,
        };
        row.Add(searchBar, 0);
        row.Add(searchStatus, 1);
        row.Add(previous, 2);
        row.Add(next, 3);
        row.Add(close, 4);
        searchRow = row;
        return row;
    }

    private void OnSearchVisibleChanged(bool visible)
    {
        searchRow.IsVisible = visible;
        if (visible)
        {
            if (searchBar.Handler is not null) searchBar.Focus();
            if (!string.IsNullOrWhiteSpace(searchBar.Text)) _ = SearchAsync(searchBar.Text);
            return;
        }
        searchVersion++;
        searchBar.Unfocus();
        ClearSearch();
    }

    // Search as the user types, once typing pauses.
    private async void ScheduleSearch()
    {
        var version = ++searchVersion;
        await Task.Delay(SearchDelayMs);
        if (version != searchVersion || !IsSearchVisible) return;
        var text = searchBar.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) ClearSearch();
        else await SearchAsync(text);
    }

    private static int Percent(ViewModel vm) => vm.PageCount == 0 ? 0 : vm.SearchedPages * 100 / vm.PageCount;

    private void UpdateSearchState(ViewModel? vm)
    {
        var count = vm?.SearchMatches.Count ?? 0;
        var index = vm?.CurrentMatchIndex ?? -1;
        var searching = vm?.IsSearching ?? false;
        SearchResultCount = count;
        CurrentSearchResult = index + 1;
        IsSearching = searching;
        searchStatus.Text = vm is null || vm.SearchQuery.Length == 0 ? ""
            : count == 0 ? searching ? $"Searching… {Percent(vm)}%" : "No matches"
            : $"{index + 1} of {count}{(searching ? "…" : "")}";
        drawable.CurrentMatch = vm?.CurrentMatch;
        pageView.Invalidate();
        // Stepping through matches is announced; the first match of a search is announced with the count instead.
        if (vm?.CurrentMatch is { } match && !searching && index != announcedMatch)
        {
            if (announcedMatch >= 0) Announce($"Match {index + 1} of {count}, page {match.Page}");
            announcedMatch = index;
        }
        if (count == 0) announcedMatch = -1;
    }

    // Go to the typed page (clamped by the page selector); anything else puts the current page back.
    private void CommitPageEntry(Entry entry)
    {
        if (viewModel is not { } vm) return;
        if (int.TryParse(entry.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var typed)) vm.PageSelector.Page = typed;
        entry.Text = vm.PageSelector.Page.ToString(CultureInfo.CurrentCulture);
    }

    private Border layersPanel = null!;

    private View CreateLayersPanel()
    {
        var list = new CollectionView
        {
            // Not MeasureFirstItem: that gives every row the first row's width and truncates longer layer names.
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() =>
            {
                var check = new CheckBox { VerticalOptions = LayoutOptions.Center };
                check.SetBinding(CheckBox.IsCheckedProperty, static (LayerRow row) => row.Visible, BindingMode.TwoWay);
                check.SetBinding(IsVisibleProperty, static (LayerRow row) => row.ShowCheck);
                // The check box carries the layer name for screen readers; the label beside it is visual only, and
                // tapping it toggles the layer too (a bigger target than the box).
                check.SetBinding(SemanticProperties.DescriptionProperty, static (LayerRow row) => row.Name);
                var name = new Label { VerticalOptions = LayoutOptions.Center, MinimumHeightRequest = 44, VerticalTextAlignment = TextAlignment.Center };
                name.SetBinding(Label.TextProperty, static (LayerRow row) => row.Name);
                AutomationProperties.SetIsInAccessibleTree(name, false);
                var toggle = new TapGestureRecognizer();
                toggle.Tapped += (_, _) => { if (name.BindingContext is LayerRow { ShowCheck: true } row) row.Visible = !row.Visible; };
                name.GestureRecognizers.Add(toggle);
                var row = new HorizontalStackLayout { Spacing = 4, Children = { check, name } };
                row.SetBinding(PaddingProperty, static (LayerRow r) => r.Depth, converter: DepthToIndentConverter.Instance);
                return row;
            }),
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, static (ViewModel vm) => vm.Layers);

        layersPanel = new Border
        {
            Content = list, IsVisible = false, WidthRequest = 240, Padding = 8,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Fill,
        };
        layersPanel.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D2D"));
        return layersPanel;
    }

    private sealed class NotEmptyConverter : IValueConverter
    {
        public static readonly NotEmptyConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is System.Collections.ICollection { Count: > 0 };
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class DepthToIndentConverter : IValueConverter
    {
        public static readonly DepthToIndentConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            new Thickness((value is int depth ? depth : 0) * 16, 0, 0, 0);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class IntTextConverter : IValueConverter
    {
        public static readonly IntTextConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int i ? i.ToString(CultureInfo.CurrentCulture) : "";
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
