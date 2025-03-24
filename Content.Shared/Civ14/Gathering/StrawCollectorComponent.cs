using Robust.Shared.Prototypes;

namespace Content.Shared.Gathering;

[RegisterComponent]
public sealed partial class StrawCollectorComponent : Component
{
    /// <summary>
    /// Time in seconds to collect straw
    /// </summary>
    [DataField]
    public float CollectTime = 5.0f;

    /// <summary>
    /// Minimum amount harversted
    /// </summary>
    [DataField]
    public int MinAmount = 0;

    /// <summary>
    /// Maximum amount harversted
    /// </summary>
    [DataField]
    public int MaxAmount = 3;
}