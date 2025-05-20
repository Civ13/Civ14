using System;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Civ14.CivResearch;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CivResearchComponent : Component
{
    /// <summary>
    /// Defines if research is currently active and progressing
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("researchEnabled"), AutoNetworkedField]
    public bool ResearchEnabled { get; set; } = true;
    /// <summary>
    /// The current research level. From 0 to 800.
    /// </summary>

    [DataField("researchLevel"), AutoNetworkedField]
    public float ResearchLevel { get; set; } = 0f;
    /// <summary>
    /// For autoresearch, how much research increases per tick.
    /// This defaults to 100 levels per day.
    /// </summary>

    [DataField("researchSpeed"), AutoNetworkedField]
    public float ResearchSpeed { get; set; } = 0.000057f;
    /// <summary>
    /// The maximum research level.
    /// Should probably stay below 900 as 9 is used as the research level for disabled and futuristic stuff.
    /// </summary>

    [DataField("maxResearch"), AutoNetworkedField]
    public float MaxResearch { get; set; } = 800;
    /// <summary>
    /// If the map is tdm or not. Crafting is restricted in TDM maps.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("isTDM"), AutoNetworkedField]
    public bool IsTDM { get; set; } = false;
    /// <summary>
    /// Calculates the current age based on the ResearchLevel.
    /// Age is determined by flooring the division of ResearchLevel by 100.
    /// </summary>
    /// <returns>The current age as an integer.</returns>
    public int GetCurrentAge()
    {
        return (int)Math.Floor(ResearchLevel / 100f);
    }

}

