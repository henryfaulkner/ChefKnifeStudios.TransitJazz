using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

// US3 / SC-004 confirming check: the design mandates Worker.cs, RouteSnapper, and
// CrossingDetector stay untouched — no "if (city == nymta)" branch anywhere in the
// shared snap/lerp/crossing pipeline. All NYC-specific logic must live in
// Cities/NymtaCity.cs + Subway/*. This is a grep + assertion, not an edit (T019).
public class NoNymtaLeakageTests
{
    // This file lives at src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/
    // — two levels up from here is src/.
    static string SrcRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Theory]
    [InlineData("Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs")]
    [InlineData("ChefKnifeStudios.MartaJazz.Shared/Geospatial/RouteSnapper.cs")]
    [InlineData("Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Checkpoints/CrossingDetector.cs")]
    public void SharedPipelineFile_ContainsNoNymtaBranch(string relativePath)
    {
        var path = Path.Combine(SrcRoot(), relativePath);
        Assert.True(File.Exists(path), $"expected file to exist: {path}");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("nymta", content, StringComparison.OrdinalIgnoreCase);
    }
}
