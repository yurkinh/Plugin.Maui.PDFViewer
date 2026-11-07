using FluentAssertions;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.ReferenceDocuments.PageProperties;
using Melville.Pdf.ViewerModel;
using Microsoft.Maui;
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
        sut.DisplayMode.Should().Be(DisplayMode.SinglePage);
        sut.ShowToolbar.Should().BeTrue();
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

        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(6)));

        sut.PageNumber.Should().Be(4);
        sut.ZoomMode.Should().Be(ZoomMode.FitWidth);
    }

    [Fact]
    public async Task SwitchingToContinuousKeepsThePage()
    {
        var sut = NewView();
        await OpenAsync(sut, PdfSource.FromBytes(await PdfAsync(6)));
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
        sut.ShowToolbar = false;
        LayOut(sut, 612, 792); // fit page: scale 1, so view points are page points
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

        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
}
