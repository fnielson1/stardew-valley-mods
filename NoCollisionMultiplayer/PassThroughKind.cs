namespace NoCollisionMultiplayer;

/// <summary>A kind of character the player can be allowed to walk through.</summary>
internal enum PassThroughKind
{
    /// <summary>The farm's cat or dog.</summary>
    Pet,

    /// <summary>A horse.</summary>
    Horse,

    /// <summary>A Junimo, including the ones which harvest for a Junimo Hut.</summary>
    Junimo,

    /// <summary>A villager.</summary>
    Villager,

    /// <summary>A coop or barn animal.</summary>
    FarmAnimal,

    /// <summary>A monster, which also can't hurt you while this is on.</summary>
    Monster
}
