using BadgeForge.Core.Printing.Batch;
using BadgeForge.Core.Printing.Drivers;
using BadgeForge.Core.Printing.Exceptions;
using BadgeForge.Core.Printing.Models;
using BadgeForge.Core.Rendering;
using BadgeForge.Core.Rendering.Batch;
using BadgeForge.Core.Templates.Enums;
using BadgeForge.Core.Templates.Layers;
using BadgeForge.Core.Templates.Models;
using BadgeForge.Core.Templates.Storage;
using SkiaSharp;
using Xunit;

namespace BadgeForge.Tests;

public class DoubleSidedPrintingTests
{
    private static TemplateDefinition TwoSidedTemplate() => new()
    {
        Name = "Two-sided",
        IsDoubleSided = true,
        Layers = new List<TemplateLayer>
        {
            new TextLayer { Name = "Front text", Text = "FRONT", FontSize = 20, X = 5, Y = 5, Width = 60, Height = 15, Side = CardSide.Front },
            new TextLayer { Name = "Back text", Text = "BACK", FontSize = 20, X = 5, Y = 30, Width = 60, Height = 15, Side = CardSide.Back }
        }
    };

    private static int CountInk(SKBitmap bitmap, int fromY, int toY)
    {
        int ink = 0;
        for (int y = fromY; y < toY; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    [Fact]
    public void Render_DrawsOnlyTheRequestedSidesLayers()
    {
        var renderer = new CardRenderer();
        var template = TwoSidedTemplate();
        var fields = new Dictionary<string, string>();

        using var front = renderer.RenderAtSize(template, fields, 1011, 638);
        using var back = renderer.RenderAtSize(template, fields, 1011, 638, side: CardSide.Back);

        int upper = 638 / 2;
        Assert.True(CountInk(front, 0, upper) > 0, "front text is on the front");
        Assert.Equal(0, CountInk(front, upper, 638));
        Assert.True(CountInk(back, upper, 638) > 0, "back text is on the back");
        Assert.Equal(0, CountInk(back, 0, upper));
    }

    [Fact]
    public void Template_SavedBeforeDuplexSupport_LoadsAsSingleSidedFront()
    {
        var storage = new JsonTemplateStorage();
        string json = storage.Serialize(TwoSidedTemplate());

        // An old file has neither the template flag nor a per-layer side
        string legacy = json.Replace("\"IsDoubleSided\": true,", string.Empty)
            .Replace("\"Side\": 1", "\"Side\": 0");
        var loaded = storage.Deserialize(legacy);

        Assert.False(loaded.IsDoubleSided);
        Assert.All(loaded.Layers, l => Assert.Equal(CardSide.Front, l.Side));

        var roundTripped = storage.Deserialize(json);
        Assert.True(roundTripped.IsDoubleSided);
        Assert.Contains(roundTripped.Layers, l => l.Side == CardSide.Back);
    }

    [Fact]
    public void BackLayers_OfASingleSidedTemplate_AreNotCheckedForPhotos()
    {
        // Turning double-sided off keeps the back layers, but they aren't printed, so they mustn't raise warnings
        var template = new TemplateDefinition
        {
            IsDoubleSided = false,
            Layers = new List<TemplateLayer>
            {
                new PhotoLayer { Name = "Front photo", SourceToken = "{Photo}" },
                new PhotoLayer { Name = "Back photo", SourceToken = "{Photo}", Side = CardSide.Back }
            }
        };

        var single = TemplateBinder.Bind(template, new Dictionary<string, string>());
        var both = TemplateBinder.Bind(template with { IsDoubleSided = true }, new Dictionary<string, string>());

        Assert.Single(single.PhotoSources);
        Assert.Equal(2, both.PhotoSources.Count);
    }

    [Fact]
    public async Task MockDriver_RefusesBackSide_WhenNoFlipperIsReported()
    {
        using var driver = new MockCardPrinterDriver();
        var job = new CardPrintJob
        {
            FrontPixelBuffer = new byte[] { 1 },
            BackPixelBuffer = new byte[] { 2 }
        };

        await Assert.ThrowsAsync<PrinterException>(() => driver.SubmitCardAsync(job));
    }

    [Fact]
    public async Task BatchCard_RendersBackOnDemand_IntoTheDriverJob()
    {
        var card = new BatchCardItem
        {
            RenderFrontAsync = _ => Task.FromResult(new byte[] { 1 }),
            RenderBackAsync = _ => Task.FromResult(new byte[] { 2, 2 })
        };

        var job = await card.ToPrintJobAsync();

        Assert.True(job.IsDuplex);
        Assert.Equal(new byte[] { 2, 2 }, job.BackPixelBuffer);
    }

    [Fact]
    public async Task BatchCard_WithoutBackRenderer_StaysSingleSided()
    {
        var card = new BatchCardItem { RenderFrontAsync = _ => Task.FromResult(new byte[] { 1 }) };

        var job = await card.ToPrintJobAsync();

        Assert.False(job.IsDuplex);
    }
}
