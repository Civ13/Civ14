using System;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared.Civ14.CivResearch;

[RegisterComponent]
[NetworkedComponent]
public sealed partial class CivResearchComponent : Component
{
    /// <summary>
    /// Defines if research is currently active and progressing
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("researchEnabled")]
    public bool ResearchEnabled { get; set; } = true;
    /// <summary>
    /// The current research level. From 0 to 800.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("researchLevel")]
    public float ResearchLevel { get; set; } = 0f;
    /// <summary>
    /// For autoresearch, how much research increases per tick.
    /// This defaults to 100 levels per day.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("researchSpeed")]
    public float ResearchSpeed { get; set; } = 0.000057f;
}

