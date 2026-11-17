using BadgeForge.Core.Printing.Interfaces;
using BadgeForge.Core.Printing.Models;
using BadgeForge.Core.Rendering.Elements;
using BadgeForge.Core.Templates.Enums;
using BadgeForge.Core.Templates.Layers;
using BadgeForge.Core.Templates.Models;
using SkiaSharp;

namespace BadgeForge.Core.Rendering;

/// <summary>
/// Core badge rendering engine.
/// Queries the active printer driver at runtime for actual canvas resolution and printable bounds,
/// validates format compatibility to prevent silent stretching, and produces a 300 DPI SKBitmap
/// ready for physical spooling or UI preview.
/// </summary>
public class CardRenderer : ICardRenderer
{
    /// <summary>
    /// When true, a layer that can't be drawn faithfully (such as a barcode that can't be encoded or doesn't fit its
    /// frame) throws instead of being skipped or clipped. Use for printing; leave false for live previews.
    /// </summary>
    public bool StrictLayerRendering { get; init; }

    /// <summary>
    /// Largest difference allowed between a requested canvas's aspect ratio and the card's before rendering refuses
    /// to stretch the design onto it.
    /// </summary>
    private const double MaxAspectRatioDeviation = 0.01;

    /// <inheritdoc/>
    public async Task<SKBitmap> RenderCardAsync(
        TemplateDefinition template,
        IReadOnlyDictionary<string, string> fieldData,
        ICardPrinterDriver driver,
        CancellationToken ct = default,
        CardSide side = CardSide.Front)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fieldData);
        ArgumentNullException.ThrowIfNull(driver);

        ct.ThrowIfCancellationRequested();

        // 1. Probe printer capabilities at runtime — NEVER use hardcoded dimensions
        var capabilities = await driver.ProbeCapabilitiesAsync(driver.TargetPrinterName ?? string.Empty, ct);

        // 2. Validate driver capabilities against the expected template card format — FAIL LOUDLY on mismatch
        capabilities.EnsureCompatible(template.TargetFormat);

        // 3. Allocate canvas with exact driver-reported pixel dimensions, turned to the card's orientation
        var (canvasWidth, canvasHeight) = capabilities.GetCanvasSizeFor(template.TargetFormat);

        return Render(template, fieldData, canvasWidth, canvasHeight, StrictLayerRendering, ct, side);
    }

    /// <summary>
    /// Renders a card onto a canvas of an explicit pixel size, e.g. for exporting at a chosen DPI without a printer.
    /// Layer geometry is in millimeters, so any resolution works — but the canvas must have the card's proportions.
    /// </summary>
    /// <exception cref="InvalidOperationException">The canvas proportions don't match the card format.</exception>
    public SKBitmap RenderAtSize(
        TemplateDefinition template,
        IReadOnlyDictionary<string, string> fieldData,
        int canvasWidth,
        int canvasHeight,
        CancellationToken ct = default,
        CardSide side = CardSide.Front)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fieldData);

        var format = template.TargetFormat;
        if (canvasWidth > 0 && canvasHeight > 0)
        {
            double expected = format.WidthMm / format.HeightMm;
            double actual = (double)canvasWidth / canvasHeight;
            if (Math.Abs(actual - expected) / expected > MaxAspectRatioDeviation)
            {
                throw new InvalidOperationException(
                    $"A {canvasWidth}x{canvasHeight}px canvas doesn't have the proportions of a {format.WidthMm}x{format.HeightMm}mm card; " +
                    "rendering onto it would stretch the design.");
            }
        }

        return Render(template, fieldData, canvasWidth, canvasHeight, StrictLayerRendering, ct, side);
    }

    /// <inheritdoc/>
    public SKBitmap RenderCard(
        TemplateDefinition template,
        IReadOnlyDictionary<string, string> fieldData,
        ICardPrinterDriver driver)
    {
        return RenderCardAsync(template, fieldData, driver, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc/>
    public SKBitmap RenderPreview(TemplateDefinition template, IReadOnlyDictionary<string, string> fieldData)
        => RenderPreview(template, fieldData, 0);

    /// <summary>
    /// Live preview at a requested pixel width (never below the card's native size), so a zoomed-in designer
    /// view is drawn sharp instead of stretching the native bitmap. Geometry is in mm, so any size is valid.
    /// </summary>
    public SKBitmap RenderPreview(TemplateDefinition template, IReadOnlyDictionary<string, string> fieldData, int targetWidthPixels, CardSide side = CardSide.Front)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fieldData);

        var format = template.TargetFormat;
        int width = Math.Max(format.WidthPixels, targetWidthPixels);
        int height = (int)Math.Round(width * (double)format.HeightPixels / format.WidthPixels);
        return Render(template, fieldData, width, height, strict: false, CancellationToken.None, side);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FindPrintProblems(
        TemplateDefinition template,
        IReadOnlyDictionary<string, string> fieldData,
        PrinterCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fieldData);
        ArgumentNullException.ThrowIfNull(capabilities);

        var (canvasWidth, canvasHeight) = capabilities.GetCanvasSizeFor(template.TargetFormat);
        double scaleFactorX = canvasWidth / template.TargetFormat.WidthMm;
        double scaleFactorY = canvasHeight / template.TargetFormat.HeightMm;

        var problems = new List<string>();
        foreach (var barcode in template.Layers.OfType<BarcodeLayer>().Where(template.PrintsLayer))
        {
            var destRect = GetLayerRect(barcode, scaleFactorX, scaleFactorY);
            if (BarcodeRenderer.FindPrintProblem(barcode, destRect, fieldData, scaleFactorX, scaleFactorY) is { } problem)
            {
                problems.Add(problem);
            }
        }

        return problems;
    }

    private static SKBitmap Render(
        TemplateDefinition template,
        IReadOnlyDictionary<string, string> fieldData,
        int canvasWidth,
        int canvasHeight,
        bool strict,
        CancellationToken ct,
        CardSide side = CardSide.Front)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            throw new InvalidOperationException($"Can't render a card on a {canvasWidth}x{canvasHeight}px canvas.");
        }

        var bitmap = new SKBitmap(canvasWidth, canvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            // Keeps cached photos and logos alive while they are drawn
            using var imageLease = ImageSourceLoader.AcquireLease();
            using var canvas = new SKCanvas(bitmap);

            // 4. Clear card canvas with white background
            canvas.Clear(SKColors.White);

            // 5. Compute millimeter-to-pixel coordinate transformation factors
            double scaleFactorX = canvasWidth / template.TargetFormat.WidthMm;
            double scaleFactorY = canvasHeight / template.TargetFormat.HeightMm;

            // 6. Render layers sorted by visual stacking order (ZIndex ascending)
            var orderedLayers = template.Layers
                .Where(l => l.IsVisible && l.Side == side)
                .OrderBy(l => l.ZIndex)
                .ToList();

            foreach (var layer in orderedLayers)
            {
                ct.ThrowIfCancellationRequested();

                var destRect = GetLayerRect(layer, scaleFactorX, scaleFactorY);
                RenderLayer(canvas, layer, destRect, fieldData, scaleFactorX, scaleFactorY, strict);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Destination rectangle of a layer in canvas pixels.
    /// </summary>
    private static SKRect GetLayerRect(TemplateLayer layer, double scaleFactorX, double scaleFactorY) => new(
        (float)(layer.X * scaleFactorX),
        (float)(layer.Y * scaleFactorY),
        (float)((layer.X + layer.Width) * scaleFactorX),
        (float)((layer.Y + layer.Height) * scaleFactorY));

    private static void RenderLayer(
        SKCanvas canvas,
        TemplateLayer layer,
        SKRect destRect,
        IReadOnlyDictionary<string, string> fieldData,
        double scaleFactorX,
        double scaleFactorY,
        bool strict)
    {
        switch (layer)
        {
            case TextLayer textLayer:
                TextRenderer.Render(canvas, textLayer, destRect, fieldData, scaleFactorX, scaleFactorY);
                break;

            case PhotoLayer photoLayer:
                PhotoRenderer.Render(canvas, photoLayer, destRect, fieldData, scaleFactorX, scaleFactorY);
                break;

            case BarcodeLayer barcodeLayer:
                BarcodeRenderer.Render(canvas, barcodeLayer, destRect, fieldData, scaleFactorX, scaleFactorY, strict);
                break;

            case StaticImageLayer imageLayer:
                StaticImageRenderer.Render(canvas, imageLayer, destRect, scaleFactorX, scaleFactorY);
                break;

            default:
                // Extensible layers can be handled or subclassed
                break;
        }
    }
}
