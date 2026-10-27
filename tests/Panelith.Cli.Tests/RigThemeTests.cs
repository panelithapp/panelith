using Panelith.Cli.Support;
using Panelith.Devices.Profiles;
using Panelith.Engine;
using Panelith.Hosting;
using Panelith.Monitoring.Binding;
using Panelith.Monitoring.Hub;
using Panelith.Monitoring.Providers;
using Panelith.Rendering;
using Panelith.Themes;

namespace Panelith.Cli.Tests;

public sealed class RigThemeTests
{
    [Fact]
    public void BundledRigThemeValidates()
    {
        var theme = new ThemeLibrary(new AppPaths(Path.GetTempPath())).Load("rig");

        var diagnostics = ThemeValidator.Validate(theme);

        Assert.Equal("rig", theme.Id);
        Assert.Equal(1280, theme.Canvas.Width);
        Assert.Equal(480, theme.Canvas.Height);
        Assert.DoesNotContain(diagnostics, d => d.Severity == ThemeDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task PreviewPngMatchesTheRequestedTrofeoSize()
    {
        var theme = new ThemeLibrary(new AppPaths(Path.GetTempPath())).Load("rig");
        await using var sensors = new SensorHub([new SyntheticSensorSource(seed: 7)]);
        await sensors.StartAsync(TestContext.Current.CancellationToken);
        await sensors.PollOnceAsync(TestContext.Current.CancellationToken);

        var codec = new SkiaImageCodec();
        var bindings = new SensorBindingContext(sensors);

        foreach (var (width, height) in new[] { (1280, 480), (1920, 480) })
        {
            var png = ThemePreview.RenderPng(theme, bindings, codec, width, height);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

            using var decoded = codec.Decode(png);
            Assert.Equal(width, decoded.Width);
            Assert.Equal(height, decoded.Height);
            Assert.Contains(decoded.ReadOnlySpan.ToArray(), b => b != 0);
        }
    }

    [Theory]
    [InlineData("aurora")]
    [InlineData("pulse")]
    [InlineData("neon")]
    [InlineData("minimal")]
    [InlineData("gpu-focus")]
    [InlineData("overview")]
    [InlineData("rig")]
    [InlineData("game")]
    [InlineData("esports")]
    [InlineData("studio")]
    [InlineData("desk")]
    [InlineData("clock")]
    [InlineData("night")]
    [InlineData("now-playing")]
    public async Task EveryBundledScreenValidatesAndRenders(string id)
    {
        var library = new ThemeLibrary(new AppPaths(Path.GetTempPath()));
        Assert.Contains(library.Catalog(), entry => entry.Id == id && !entry.IsUserTheme);

        var theme = library.Load(id);
        Assert.DoesNotContain(ThemeValidator.Validate(theme), d => d.Severity == ThemeDiagnosticSeverity.Error);

        await using var sensors = new SensorHub([new SyntheticSensorSource(seed: 3)]);
        await sensors.StartAsync(TestContext.Current.CancellationToken);
        await sensors.PollOnceAsync(TestContext.Current.CancellationToken);

        var codec = new SkiaImageCodec();
        using var decoded = codec.Decode(ThemePreview.RenderPng(theme, new SensorBindingContext(sensors), codec, 1920, 480));
        Assert.Equal(1920, decoded.Width);
        Assert.Contains(decoded.ReadOnlySpan.ToArray(), b => b != 0);
    }

    [Fact]
    public void UdevRulesCoverBothTrofeoUsbIds()
    {
        var rules = UdevRules.Build(PanelProfileRegistry.Load());

        Assert.Contains("0416", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5408", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5409", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5302", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hidraw", rules, StringComparison.Ordinal);
    }
}
