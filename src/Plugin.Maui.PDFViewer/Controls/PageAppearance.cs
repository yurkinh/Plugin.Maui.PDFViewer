namespace Plugin.Maui.PDFViewer;

/// <summary>
/// How <see cref="PdfView"/> presents each page: its shadow, the space around it and what is cropped off it.
/// Changes apply at once.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;pdf:PdfView Source="manual.pdf"&gt;
///     &lt;pdf:PdfView.PageAppearance&gt;
///         &lt;pdf:PageAppearance Margin="16,8" ShadowEnabled="False" Crop="0,36" /&gt;
///     &lt;/pdf:PdfView.PageAppearance&gt;
/// &lt;/pdf:PdfView&gt;
/// </code>
/// </example>
public sealed class PageAppearance : BindableObject
{
    /// <summary>The default <see cref="Margin"/>: 8 units between pages and at the sides, 4 at the ends.</summary>
    public static readonly Thickness DefaultMargin = new(8, 4);

    /// <summary>Whether pages cast a soft shadow. The default is true.</summary>
    public static readonly BindableProperty ShadowEnabledProperty = BindableProperty.Create(
        nameof(ShadowEnabled), typeof(bool), typeof(PageAppearance), true);

    /// <summary>
    /// Space around every page while scrolling (continuous mode), in device-independent units: two pages are one's
    /// bottom and the next one's top margin apart (right and left when scrolling horizontally). The default, "8,4",
    /// puts 8 units between pages and at the sides and 4 at the ends.
    /// </summary>
    public static readonly BindableProperty MarginProperty = BindableProperty.Create(
        nameof(Margin), typeof(Thickness), typeof(PageAppearance), DefaultMargin);



    /// <summary>
    /// What to cut off every side of every page, in PDF points (1/72 inch), in both display modes: for example
    /// "0,36" hides half an inch at the top and bottom. Links, text selection and search still work on what is shown.
    /// </summary>
    public static readonly BindableProperty CropProperty = BindableProperty.Create(
        nameof(Crop), typeof(Thickness), typeof(PageAppearance), Thickness.Zero);

    /// <inheritdoc cref="ShadowEnabledProperty"/>
    public bool ShadowEnabled { get => (bool)GetValue(ShadowEnabledProperty); set => SetValue(ShadowEnabledProperty, value); }

    /// <inheritdoc cref="MarginProperty"/>
    public Thickness Margin { get => (Thickness)GetValue(MarginProperty); set => SetValue(MarginProperty, value); }

    /// <inheritdoc cref="CropProperty"/>
    public Thickness Crop { get => (Thickness)GetValue(CropProperty); set => SetValue(CropProperty, value); }
}
