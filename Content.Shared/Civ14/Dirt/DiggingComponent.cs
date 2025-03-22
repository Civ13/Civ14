using Robust.Shared.Prototypes;

namespace Content.Shared.Farming;

[RegisterComponent]
public sealed partial class DiggingComponent : Component
{
    /// <summary>
    /// Time in seconds to complete the digging action
    /// </summary>
    [DataField]
    public float DigTime = 6.0f;
}