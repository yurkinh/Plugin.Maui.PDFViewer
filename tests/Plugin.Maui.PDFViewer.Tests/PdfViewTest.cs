using FluentAssertions;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.ReferenceDocuments.PageProperties;
using Melville.Pdf.ViewerModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Dispatching;
using Xunit;

namespace Plugin.Maui.PDFViewer.Tests;

/// <summary>
/// PdfView's public behaviour on plain .NET: no handler, no window, no platform bitmaps (pages are replayed), and a
/// dispatcher that runs work inline.
/// </summary>
public class PdfViewTest : IAsyncLifetime
{
    static PdfViewTest() => DispatcherProvider.SetCurrent(new InlineDispatcherProvider());

    // Every view a test opens is closed afterwards: a document that is never disposed leaks, and Melville's debug
    // builds throw from the finalizer of its input source (that would crash the test host).
    private readonly List<PdfView> views = [];

    private PdfView NewView(PdfViewState? state = null)
    {
        var view = new PdfView { ViewState = state };
        views.Add(view);
        return view;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var view in views) view.Source = null;
        await Task.Delay(100); // closing finishes asynchronously
    }

    private static async Task<byte[]> PdfAsync(int pages)
    {
        var creator = new PdfDocumentCreator();
        for (int i = 0; i < pages; i++) creator.Pages.CreatePage();
        using var bytes = new MemoryStream();
        await creator.CreateDocument().WriteToAsync(bytes);
        return bytes.ToArray();
    }

    private static async Task OpenAsync(PdfView sut, PdfSource source)
    {
        var loaded = new TaskCompletionSource();
        sut.DocumentLoaded += (_, _) => loaded.TrySetResult();
        sut.LoadFailed += (_, e) => loaded.TrySetException(e);
        sut.Source = source;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void WithoutADocumentNothingCanBeDone()
    {
        var sut = NewView();

        sut.PageCount.Should().Be(0);
        sut.ViewState.Should().BeNull();
        sut.DisplayMode.Should().Be(DisplayMode.Continuous);
        sut.ZoomMode.Should().Be(ZoomMode.FitWidth);
        sut.ShowToolbar.Should().BeFalse();
        new[] { sut.FirstPageCommand, sut.PreviousPageCommand, sut.NextPageCommand, sut.LastPageCommand,
                sut.ZoomInCommand, sut.ZoomOutCommand, sut.FitPageCommand, sut.FitWidthCommand }
            .Should().OnlyContain(c => !c.CanExecute(null));
    }

    [Fact]
    public async Task OpeningADocumentShowsItsFirstPageAndEnablesNavigation()
    {
        var sut = NewView();

        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(5)));

        sut.PageCount.Should().Be(5);
        sut.PageNumber.Should().Be(1);
        sut.IsLoading.Should().BeFalse();
        sut.PreviousPageCommand.CanExecute(null).Should().BeFalse();
        sut.NextPageCommand.CanExecute(null).Should().BeTrue();

        sut.NextPageCommand.Execute(null);
        sut.PageNumber.Should().Be(2);
        sut.ViewState!.Value.Page.Should().Be(2);

        sut.LastPageCommand.Execute(null);
        sut.PageNumber.Should().Be(5);
        sut.NextPageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task AViewStateSetBeforeTheSourceOpensTheDocumentThere()
    {
        var sut = NewView(new PdfViewState(4, ZoomMode.FitWidth, 1));
        sut.DisplayMode = DisplayMode.SinglePage;

        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(6)));

        sut.PageNumber.Should().Be(4);
        sut.ZoomMode.Should().Be(ZoomMode.FitWidth);
    }

    [Fact]
    public async Task AViewStateSetBeforeTheSourceOpensTheDocumentThereWhenScrolling()
    {
        var sut = NewView(new PdfViewState(4, ZoomMode.FitWidth, 1));
        LayOut(sut, 400, 600);

        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(6)));

        sut.DisplayMode.Should().Be(DisplayMode.Continuous);
        sut.PageNumber.Should().Be(4);
    }

    [Fact]
    public async Task SearchingMovesToTheMatchWhenScrolling()
    {
        var sut = NewView();
        LayOut(sut, 400, 300); // shorter than a page at fit width: the matches start out of view
        using var bytes = new MemoryStream();
        await new Links().WritePdfAsync(bytes);
        await OpenAsync(sut, PdfSource.FromBytes(bytes.ToArray()));

        (await sut.SearchAsync("back to PAGE 1")).Should().Be(3);

        sut.PageNumber.Should().Be(2, "the first match after page 1 is shown");
        sut.NextSearchResultCommand.Execute(null);
        sut.PageNumber.Should().Be(3);
    }

    [Fact]
    public async Task SwitchingToContinuousKeepsThePage()
    {
        var sut = NewView();
        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(6)));
        sut.DisplayMode = DisplayMode.SinglePage;
        sut.PageNumber = 3;

        sut.DisplayMode = DisplayMode.Continuous;

        sut.PageNumber.Should().Be(3);
        sut.NextPageCommand.Execute(null);
        sut.PageNumber.Should().Be(4);
    }

    [Fact]
    public async Task ABrokenDocumentReportsTheFailure()
    {
        var sut = NewView();
        Exception? failure = null;
        sut.LoadFailed += (_, e) => failure = e;

        var opening = OpenAsync(sut, PdfSource.FromBytes("not a pdf"u8.ToArray()));

        await FluentActions.Awaiting(() => opening).Should().ThrowAsync<Exception>();
        failure.Should().NotBeNull();
        sut.ErrorMessage.Should().StartWith("This document could not be opened");
        sut.PageCount.Should().Be(0);
    }

    [Fact]
    public async Task ClearingTheSourceClosesTheDocument()
    {
        var sut = NewView();
        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(3)));

        sut.Source = null;
        await Task.Delay(50); // closing finishes asynchronously

        sut.PageCount.Should().Be(0);
        sut.ViewState.Should().BeNull();
        sut.NextPageCommand.CanExecute(null).Should().BeFalse();
    }

    // Lay the view out without a window, so the page has a viewport. Handlers normally run the cross-platform
    // layout of each container; with none, do it here, down the tree.
    private static void LayOut(PdfView view, double width, double height)
    {
        IView root = view;
        root.Measure(width, height);
        root.Arrange(new Rect(0, 0, width, height));
        ArrangeChildren(root);

        static void ArrangeChildren(IView container)
        {
            if (container is ICrossPlatformLayout layout)
            {
                layout.CrossPlatformMeasure(container.Frame.Width, container.Frame.Height);
                layout.CrossPlatformArrange(new Rect(Point.Zero, container.Frame.Size));
            }
            if (container is IVisualTreeElement element)
                foreach (var child in element.GetVisualChildren().OfType<IView>()) ArrangeChildren(child);
        }
    }

    private async Task<PdfView> OpenLinksAsync()
    {
        var sut = NewView();
        // One page at fit page, scale 1: view points are page points.
        sut.DisplayMode = DisplayMode.SinglePage;
        sut.ZoomMode = ZoomMode.FitPage;
        LayOut(sut, 612, 792);
        using var bytes = new MemoryStream();
        await new Links().WritePdfAsync(bytes);
        await OpenAsync(sut, PdfSource.FromBytes(bytes.ToArray()));
        return sut;
    }

    [Fact]
    public async Task TappingALinkGoesToItsPage()
    {
        var sut = await OpenLinksAsync();
        PdfLinkTappedEventArgs? tapped = null;
        sut.LinkTapped += (_, e) => tapped = e;

        (await sut.FollowLinkAtAsync(100, 90)).Should().BeTrue(); // the first link: page 3

        tapped!.PageNumber.Should().Be(3);
        tapped.Uri.Should().BeNull();
        sut.PageNumber.Should().Be(3);
    }

    [Fact]
    public async Task AHandledLinkIsNotFollowed()
    {
        var sut = await OpenLinksAsync();
        sut.LinkTapped += (_, e) => e.Handled = true;

        (await sut.FollowLinkAtAsync(100, 90)).Should().BeTrue();

        sut.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task AWebLinkIsReportedWithItsAddress()
    {
        var sut = await OpenLinksAsync();
        PdfLinkTappedEventArgs? tapped = null;
        sut.LinkTapped += (_, e) => tapped = e;

        (await sut.FollowLinkAtAsync(100, 90 + 4 * 48)).Should().BeTrue();

        tapped!.Uri.Should().Be(Links.WebAddress);
        tapped.PageNumber.Should().BeNull();
        sut.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task TappingOutsideALinkDoesNothing()
    {
        var sut = await OpenLinksAsync();
        var raised = false;
        sut.LinkTapped += (_, _) => raised = true;

        (await sut.FollowLinkAtAsync(10, 10)).Should().BeFalse();

        raised.Should().BeFalse();
    }

    [Fact]
    public async Task APagesTextCanBeRead()
    {
        var sut = await OpenLinksAsync();

        (await sut.GetPageTextAsync(3)).Should().StartWith("Page 3\nBack to page 1");
        (await sut.GetPageTextAsync(9)).Should().BeEmpty();
        (await NewView().GetPageTextAsync(1)).Should().BeEmpty("no document is open");
    }

    [Fact]
    public async Task SearchingFindsMatchesOnEveryPageAndStepsThroughThem()
    {
        var sut = await OpenLinksAsync();

        var count = await sut.SearchAsync("back to PAGE 1");

        count.Should().Be(3, "pages 2, 3 and 4 link back to page 1");
        sut.SearchResultCount.Should().Be(3);
        sut.CurrentSearchResult.Should().Be(1);
        sut.IsSearching.Should().BeFalse();
        sut.PageNumber.Should().Be(2, "the first match after page 1 is shown");

        sut.NextSearchResultCommand.Execute(null);
        sut.CurrentSearchResult.Should().Be(2);
        sut.PageNumber.Should().Be(3);
        sut.PreviousSearchResultCommand.Execute(null);
        sut.PreviousSearchResultCommand.Execute(null);
        sut.CurrentSearchResult.Should().Be(3, "before the first match comes the last");
    }

    [Fact]
    public async Task ClosingTheFindBarClearsTheSearch()
    {
        var sut = await OpenLinksAsync();
        sut.IsSearchVisible = true;
        await sut.SearchAsync("page");
        sut.SearchResultCount.Should().BeGreaterThan(0);

        sut.IsSearchVisible = false;

        sut.SearchResultCount.Should().Be(0);
        sut.CurrentSearchResult.Should().Be(0);
        sut.NextSearchResultCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task PressingAWordSelectsItForCopying()
    {
        var sut = await OpenLinksAsync();
        sut.CopySelectionCommand.CanExecute(null).Should().BeFalse();

        (await sut.SelectWordAtAsync(45, 45)).Should().BeTrue(); // "Links", the title

        sut.SelectedText.Should().Be("Links");
        sut.CopySelectionCommand.CanExecute(null).Should().BeTrue();
        sut.ClearSelection();
        sut.SelectedText.Should().BeNull();
        sut.CopySelectionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SelectAllTakesTheCurrentPage()
    {
        var sut = await OpenLinksAsync();

        sut.SelectAllCommand.Execute(null);
        for (int i = 0; i < 50 && sut.SelectedText is null; i++) await Task.Delay(20);

        sut.SelectedText.Should().Be(await sut.GetPageTextAsync(1));
    }

    [Fact]
    public async Task ScreenReadersHearThePageText()
    {
        var sut = await OpenLinksAsync();
        var page = sut.GetVisualTreeDescendants().OfType<GraphicsView>().Single();

        string? description = null;
        for (int i = 0; i < 50; i++)
        {
            description = SemanticProperties.GetDescription(page);
            if (description?.Contains("Links") == true) break;
            await Task.Delay(20);
        }

        description.Should().StartWith("Page 1 of 4. Links Page 3, at the line marked 400");
    }

    [Fact]
    public async Task WithoutADocumentThereIsNothingToFind()
    {
        var sut = NewView();

        (await sut.SearchAsync("page")).Should().Be(0);
        sut.NextSearchResultCommand.CanExecute(null).Should().BeFalse();
        sut.PreviousSearchResultCommand.CanExecute(null).Should().BeFalse();
    }

    private sealed class InlineDispatcherProvider : IDispatcherProvider
    {
        private readonly InlineDispatcher dispatcher = new();
        public IDispatcher GetForCurrentThread() => dispatcher;
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            _ = Task.Delay(delay).ContinueWith(_ => action(), TaskScheduler.Default);
            return true;
        }

        // Animations (the scroll indicator's fade, a fling) start timers; tests drive the view directly, so the timer
        // never ticks.
        public IDispatcherTimer CreateTimer() => new IdleTimer();
    }

    private sealed class IdleTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }

    [Fact]
    public async Task PageChangedReportsTheOpenedDocumentAndEveryMove()
    {
        var sut = NewView();
        sut.DisplayMode = DisplayMode.SinglePage;
        var events = new List<PageChangedEventArgs>();
        var commanded = new List<PageChangedEventArgs>();
        sut.PageChanged += (_, e) => events.Add(e);
        sut.PageChangedCommand = new Command<PageChangedEventArgs>(commanded.Add);

        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(6)));
        sut.NextPageCommand.Execute(null);
        sut.PageNumber = 5;

        events.Should().Equal(new PageChangedEventArgs(1, 6), new PageChangedEventArgs(2, 6), new PageChangedEventArgs(5, 6));
        commanded.Should().Equal(events);
    }

    [Fact]
    public async Task ZoomingInStopsAtMaxZoomTimesFitWidth()
    {
        var sut = NewView();
        LayOut(sut, 400, 600);
        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(3)));
        var fitWidth = sut.ViewState!.Value.Zoom;
        sut.MaxZoom = 2;

        for (var i = 0; i < 10; i++) sut.ZoomInCommand.Execute(null);

        sut.ViewState!.Value.Zoom.Should().BeApproximately(2 * fitWidth, 1e-9);
        sut.ZoomInCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void MaxZoomBelowFitWidthIsIgnored()
    {
        var sut = NewView();
        sut.MaxZoom.Should().Be(4);
        sut.MinZoom.Should().Be(1);

        sut.MaxZoom = 0.5;

        sut.MaxZoom.Should().Be(4, "MAUI ignores values that fail validation");
    }

    [Fact]
    public async Task AStringPathOpensThatFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdfview-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, await PdfAsync(2));
        try
        {
            var sut = NewView();
            await OpenAsync(sut, path); // implicit conversion, as a binding to a string property does
            sut.PageCount.Should().Be(2);

            var converted = (PdfSource)new PdfSourceConverter().ConvertFrom(null, null, new Uri(path).AbsoluteUri)!;
            await using var stream = await converted.OpenAsync(CancellationToken.None);
            stream.Length.Should().Be(new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AMissingEmbeddedResourceFailsToOpen()
    {
        var source = PdfSource.FromEmbeddedResource(typeof(PdfViewTest).Assembly, "missing.pdf");

        var open = () => source.OpenAsync(CancellationToken.None);

        await open.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void OnlyWebAndFileUrisAreSources()
    {
        var ftp = () => PdfSource.FromUri(new Uri("ftp://example.com/a.pdf"));
        ftp.Should().Throw<ArgumentException>();
        PdfSource.FromUri(new Uri("https://example.com/a.pdf")).Should().NotBeNull(); // nothing is downloaded until opened
    }

    [Fact]
    public async Task ACroppedPageStillFollowsItsLinks()
    {
        var sut = NewView();
        sut.DisplayMode = DisplayMode.SinglePage;
        sut.ZoomMode = ZoomMode.FitPage;
        sut.PageAppearance = new PageAppearance { Crop = new Thickness(50, 0, 0, 0) };
        LayOut(sut, 562, 792); // the cropped page at scale 1
        using var bytes = new MemoryStream();
        await new Links().WritePdfAsync(bytes);
        await OpenAsync(sut, PdfSource.FromBytes(bytes.ToArray()));

        (await sut.FollowLinkAtAsync(50, 90)).Should().BeTrue(); // page point (100, 90): the first link, to page 3

        sut.PageNumber.Should().Be(3);
    }

    [Fact]
    public void PagesHaveAShadowAndAMarginAndNoCropByDefault()
    {
        var appearance = new PageAppearance();

        appearance.ShadowEnabled.Should().BeTrue();
        appearance.Margin.Should().Be(new Thickness(8, 4));
        appearance.Crop.Should().Be(Thickness.Zero);
        NewView().PageAppearance.Should().BeNull();
    }
}
