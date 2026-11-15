using Xunit;

namespace NoCollisionMultiplayer.Tests;

public class ModConfigTests
{
    [Fact]
    public void Allows_FollowsEachSetting()
    {
        ModConfig config = new()
        {
            PassThroughPets = false,
            PassThroughHorses = true,
            PassThroughJunimos = false,
            PassThroughVillagers = true,
            PassThroughFarmAnimals = false,
            PassThroughMonsters = true
        };

        Assert.False(config.Allows(PassThroughKind.Pet));
        Assert.True(config.Allows(PassThroughKind.Horse));
        Assert.False(config.Allows(PassThroughKind.Junimo));
        Assert.True(config.Allows(PassThroughKind.Villager));
        Assert.False(config.Allows(PassThroughKind.FarmAnimal));
        Assert.True(config.Allows(PassThroughKind.Monster));
    }

    [Fact]
    public void Defaults_AllowJunimosButNotVillagersOrMonsters()
    {
        ModConfig config = new();

        Assert.True(config.Allows(PassThroughKind.Junimo));
        Assert.False(config.Allows(PassThroughKind.Villager));
        Assert.False(config.Allows(PassThroughKind.Monster));
        Assert.False(config.PassThroughEverything);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        ModConfig config = new()
        {
            PassThroughJunimos = false,
            PassThroughVillagers = true,
            PassThroughMonsters = true,
            PassThroughEverything = true
        };

        config.Reset();

        Assert.True(config.PassThroughJunimos);
        Assert.False(config.PassThroughVillagers);
        Assert.False(config.PassThroughMonsters);
        Assert.False(config.PassThroughEverything);
    }
}
