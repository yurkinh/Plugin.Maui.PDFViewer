using FluentAssertions;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.ViewerModel;
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
