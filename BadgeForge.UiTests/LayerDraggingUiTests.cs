using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using BadgeForge.App;
using BadgeForge.App.Controls;
using BadgeForge.App.ViewModels;
using Xunit;

namespace BadgeForge.UiTests;

/// <summary>
/// Dragging layers on the card: a click never nudges a layer, a real drag moves it and stays one undo step,
/// Shift holds it to one direction, and it can't be dragged off the card.
/// </summary>
public class LayerDraggingUiTests
{
    private sealed record Harness(MainWindow Window, MainWindowViewModel Vm, InteractiveBadgeCanvas Canvas);

    private static Harness Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.IsDesignPageActive = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var canvas = window.FindControl<InteractiveBadgeCanvas>("BadgeCanvas")!;
        canvas.Focus();
        Dispatcher.UIThread.RunJobs();
        return new Harness(window, vm, canvas);
    }

    private static Point Center(Harness h)
    {
        var box = h.Canvas.GetLayerBounds(h.Vm.SelectedLayer!);
        return h.Canvas.TranslatePoint(box.Center, h.Window)!.Value;
    }

    private static void Drag(Harness h, Point from, Point to, KeyModifiers modifiers = KeyModifiers.None)
    {
        var raw = (RawInputModifiers)(int)modifiers;
        h.Window.MouseDown(from, MouseButton.Left, raw);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseMove(to, raw);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseUp(to, MouseButton.Left, raw);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task ASmallWobbleDuringAClick_DoesNotMoveTheLayer() => HeadlessTestApp.RunOnUiThread(() =>
    {
        var h = Open();
        try
        {
            h.Vm.AddTextLayer(20, 20, "Hello");
            Dispatcher.UIThread.RunJobs();
            var layer = h.Vm.SelectedLayer!;
            var start = Center(h);

            Drag(h, start, new Point(start.X + 2, start.Y + 1), KeyModifiers.Control);

            Assert.Equal(layer.X, h.Vm.SelectedLayer!.X);
            Assert.Equal(layer.Y, h.Vm.SelectedLayer.Y);
        }
        finally
        {
            h.Window.Close();
        }
    });

    [Fact]
    public Task DraggingPastTheThreshold_MovesTheLayer_AsOneUndoStep() => HeadlessTestApp.RunOnUiThread(() =>
    {
        var h = Open();
        try
        {
            h.Vm.AddTextLayer(20, 20, "Hello");
            Dispatcher.UIThread.RunJobs();
            double x = h.Vm.SelectedLayer!.X;
            var start = Center(h);

            Drag(h, start, new Point(start.X + 40, start.Y), KeyModifiers.Control);

            Assert.True(h.Vm.SelectedLayer!.X > x + 1, $"Expected the layer to move right from {x}, but it is at {h.Vm.SelectedLayer.X}.");
            Assert.True(h.Vm.Undo());
            Assert.Equal(x, h.Vm.SelectedLayer!.X, 2);
        }
        finally
        {
            h.Window.Close();
        }
    });

    [Fact]
    public Task HoldingShift_KeepsTheDragToOneDirection() => HeadlessTestApp.RunOnUiThread(() =>
    {
        var h = Open();
        try
        {
            h.Vm.AddTextLayer(20, 20, "Hello");
            Dispatcher.UIThread.RunJobs();
            double y = h.Vm.SelectedLayer!.Y;
            var start = Center(h);

            Drag(h, start, new Point(start.X + 60, start.Y + 10), KeyModifiers.Shift | KeyModifiers.Control);

            Assert.True(h.Vm.SelectedLayer!.X > 20.5, "The layer should have moved sideways.");
            Assert.Equal(y, h.Vm.SelectedLayer.Y, 2);
        }
        finally
        {
            h.Window.Close();
        }
    });

    [Fact]
    public Task DraggingFarOffTheCard_StopsAtTheCardEdge() => HeadlessTestApp.RunOnUiThread(() =>
    {
        var h = Open();
        try
        {
            h.Vm.AddTextLayer(20, 20, "Hello");
            Dispatcher.UIThread.RunJobs();
            var layer = h.Vm.SelectedLayer!;
            var start = Center(h);

            Drag(h, start, new Point(start.X + 2000, start.Y), KeyModifiers.Control);

            double maxX = h.Vm.ActiveFormat.WidthMm - layer.Width;
            Assert.Equal(maxX, h.Vm.SelectedLayer!.X, 1);
        }
        finally
        {
            h.Window.Close();
        }
    });
}
