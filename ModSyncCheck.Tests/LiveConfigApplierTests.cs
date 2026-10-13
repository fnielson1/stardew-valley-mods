using System;
using System.Collections.Generic;
using Xunit;

namespace ModSyncCheck.Tests;

public class LiveConfigApplierTests
{
    [Fact]
    public void TryApply_PrivateField_CopiesValuesIntoTheExistingObject()
    {
        FieldMod mod = new();
        SampleConfig original = mod.GetConfig();

        bool applied = LiveConfigApplier.TryApply(mod, _ => new SampleConfig { Speed = 5, Name = "host", Keys = new List<string> { "A" }, ReadOnlyCount = 9 }, out string? error);

        Assert.True(applied, error);
        Assert.Same(original, mod.GetConfig()); // other parts of the mod may hold this object
        Assert.Equal(5, original.Speed);
        Assert.Equal("host", original.Name);
        Assert.Equal(new[] { "A" }, original.Keys);
        Assert.Equal(7, original.PublicField);
    }

    [Fact]
    public void CanApply_MatchesWhetherTheSettingsCanBeFound()
    {
        Assert.True(LiveConfigApplier.CanApply(new FieldMod(), out _));
        Assert.False(LiveConfigApplier.CanApply(new NoConfigMod(), out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryApply_Twice_SwitchesToTheHostsSettingsAndBack()
    {
        FieldMod mod = new();

        LiveConfigApplier.TryApply(mod, _ => new SampleConfig { Speed = 5, Name = "host" }, out _);
        Assert.Equal("host", mod.GetConfig().Name);

        LiveConfigApplier.TryApply(mod, _ => new SampleConfig(), out _);
        Assert.Equal(1, mod.GetConfig().Speed);
        Assert.Equal("mine", mod.GetConfig().Name);
    }

    [Fact]
    public void TryApply_StaticProperty_IsFound()
    {
        StaticPropertyMod.Config = new SampleConfig();

        bool applied = LiveConfigApplier.TryApply(new StaticPropertyMod(), _ => new SampleConfig { Speed = 8 }, out _);

        Assert.True(applied);
        Assert.Equal(8, StaticPropertyMod.Config.Speed);
    }

    [Fact]
    public void TryApply_ReadsTheSettingsAsTheModsOwnType()
    {
        Type? requested = null;

        LiveConfigApplier.TryApply(new FieldMod(), type =>
        {
            requested = type;
            return new SampleConfig();
        }, out _);

        Assert.Equal(typeof(SampleConfig), requested);
    }

    [Fact]
    public void TryApply_SeveralSettingsObjects_UsesTheOneNamedConfig()
    {
        TwoConfigsMod mod = new();

        bool applied = LiveConfigApplier.TryApply(mod, _ => new SampleConfig { Speed = 4 }, out _);

        Assert.True(applied);
        Assert.Equal(4, mod.Config.Speed);
        Assert.Equal(1, mod.DefaultConfig.Speed);
    }

    [Fact]
    public void TryApply_SeveralSettingsObjects_NoneNamedConfig_Fails()
    {
        bool applied = LiveConfigApplier.TryApply(new AmbiguousMod(), _ => new SampleConfig { Speed = 4 }, out string? error);

        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryApply_NoSettingsObject_Fails()
    {
        bool applied = LiveConfigApplier.TryApply(new NoConfigMod(), _ => throw new InvalidOperationException("shouldn't read the file"), out string? error);

        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryApply_SettingsNotLoadedYet_Fails()
    {
        bool applied = LiveConfigApplier.TryApply(new NullConfigMod(), _ => new SampleConfig(), out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_FileCantBeRead_FailsAndLeavesTheSettingsAlone()
    {
        FieldMod mod = new();

        bool applied = LiveConfigApplier.TryApply(mod, _ => null, out string? error);

        Assert.False(applied);
        Assert.NotNull(error);
        Assert.Equal(1, mod.GetConfig().Speed);
    }

    [Fact]
    public void GetSnapshot_ChangesWhenASettingChanges_AndMatchesAgainOnceItsSetBack()
    {
        FieldMod mod = new();
        LiveConfigApplier.TryApply(mod, _ => new SampleConfig { Speed = 5, Keys = new List<string> { "A" } }, out _);
        string? applied = LiveConfigApplier.GetSnapshot(mod);
        Assert.NotNull(applied);
        Assert.Equal(applied, LiveConfigApplier.GetSnapshot(mod));

        // a property, a field, and a value inside a list
        mod.GetConfig().Speed = 6;
        Assert.NotEqual(applied, LiveConfigApplier.GetSnapshot(mod));
        mod.GetConfig().Speed = 5;
        mod.GetConfig().PublicField = 8;
        Assert.NotEqual(applied, LiveConfigApplier.GetSnapshot(mod));
        mod.GetConfig().PublicField = 7;
        mod.GetConfig().Keys.Add("B");
        Assert.NotEqual(applied, LiveConfigApplier.GetSnapshot(mod));

        LiveConfigApplier.TryApply(mod, _ => new SampleConfig { Speed = 5, Keys = new List<string> { "A" } }, out _);
        Assert.Equal(applied, LiveConfigApplier.GetSnapshot(mod));
    }

    [Fact]
    public void GetSnapshot_NoSettingsObject_IsNull()
    {
        Assert.Null(LiveConfigApplier.GetSnapshot(new NoConfigMod()));
    }

    [Fact]
    public void GetSnapshot_SettingsWhichCantBeRead_IsNull()
    {
        Assert.Null(LiveConfigApplier.GetSnapshot(new BrokenConfigMod()));
    }

    private class BrokenConfig
    {
        public int Speed => throw new InvalidOperationException("broken");
    }

    private class BrokenConfigMod
    {
        public BrokenConfig Config = new();
    }

    private class SampleConfig
    {
        public int Speed { get; set; } = 1;
        public string Name { get; set; } = "mine";
        public List<string> Keys { get; set; } = new();
        public int ReadOnlyCount { get; init; }
        public int PublicField = 7;
    }

    private class FieldMod
    {
        private readonly SampleConfig Config = new();
        private readonly string ConfigPath = "config.json";
        public SampleConfig GetConfig() => this.Config;
        public string GetPath() => this.ConfigPath;
    }

    private class StaticPropertyMod
    {
        public static SampleConfig Config { get; set; } = new();
    }

    private class TwoConfigsMod
    {
        public SampleConfig Config = new();
        public SampleConfig DefaultConfig = new();
    }

    private class AmbiguousMod
    {
        public SampleConfig MainConfig = new();
        public SampleConfig OtherConfig = new();
    }

    private class NoConfigMod
    {
        public int Speed = 3;
        public List<string> ConfigNames = new();
    }

    private class NullConfigMod
    {
        public SampleConfig? Config = null;
    }
}
