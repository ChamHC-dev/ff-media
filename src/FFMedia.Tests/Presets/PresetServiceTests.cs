using System;
using System.IO;
using System.Linq;
using FFMedia.Core.Presets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FFMedia.Tests.Presets;

public class PresetServiceTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ffmedia-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_ThenList_ReturnsPreset()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);

        svc.Save(new Preset("1080p MP4", "{payload}"));

        var p = Assert.Single(svc.List());
        Assert.Equal("1080p MP4", p.Name);
        Assert.Equal("{payload}", p.Payload);
    }

    [Fact]
    public void Save_SameName_UpsertsInPlace()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);
        svc.Save(new Preset("Fast", "old"));

        svc.Save(new Preset("Fast", "new"));

        var p = Assert.Single(svc.List());
        Assert.Equal("new", p.Payload);
    }

    [Fact]
    public void Delete_RemovesByName()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);
        svc.Save(new Preset("Keep", "k"));
        svc.Save(new Preset("Drop", "d"));

        svc.Delete("Drop");

        Assert.Equal("Keep", Assert.Single(svc.List()).Name);
    }

    [Fact]
    public void Presets_PersistAcrossReload()
    {
        var dir = TempDir();
        new PresetService(dir, NullLogger<PresetService>.Instance).Save(new Preset("P", "x"));

        var reloaded = new PresetService(dir, NullLogger<PresetService>.Instance);

        Assert.Equal("P", Assert.Single(reloaded.List()).Name);
    }

    [Fact]
    public void Save_RaisesChanged()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        svc.Save(new Preset("P", "x"));

        Assert.Equal(1, raised);
    }
}
