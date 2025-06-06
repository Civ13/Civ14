namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Marks an entity as a Valley checkpoint for supply box deliveries and control points.
/// </summary>
[RegisterComponent, Access(typeof(ValleyPointsRuleSystem))]
public sealed partial class ValleyCheckpointComponent : Component
{
    /// <summary>
    /// Whether this checkpoint is controlled by Blugoslavia.
    /// </summary>
    [DataField]
    public bool BlugoslaviaControlled = false;

    /// <summary>
    /// Supply boxes currently being secured at this checkpoint.
    /// </summary>
    [DataField]
    public List<EntityUid> SecuringBoxes = new();
}
