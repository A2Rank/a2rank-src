using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

public class EntitiesTests
{
    // Numbers seen in recordings: players (identity messages, PvP victims) and monsters (PvE deaths, the training dummy).
    [Theory]
    [InlineData(46u, true)]
    [InlineData(3218u, true)]      // my character in one zone
    [InlineData(6212u, true)]      // the same character after a zone change
    [InlineData(16375u, true)]     // highest player number seen
    [InlineData(16563u, false)]    // lowest monster number seen
    [InlineData(27094u, false)]    // the monster of the long fight
    [InlineData(38750u, false)]    // the training dummy
    public void Players_and_monsters_are_told_apart_by_entity_number(uint entity, bool player) =>
        Assert.Equal(player, Entities.IsPlayer(entity));
}
