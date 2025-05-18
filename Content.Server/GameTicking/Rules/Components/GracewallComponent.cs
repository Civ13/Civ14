using Content.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(GracewallRuleSystem))]
public sealed partial class GracewallRuleComponent : Component
{
    /// <summary>
    /// How long the grace wall lasts since the round started
    /// </summary>
    [DataField("gracewallDuration")]
    public TimeSpan GracewallDuration { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>
    /// Is the grace wall currently active?
    /// </summary>
    [DataField("gracewallActive")]
    public bool GracewallActive { get; set; } = true;
    /// <summary>
    /// How much time is remaining until the grace wall drops.
    /// </summary>
    public float Timer;


}

[RegisterComponent]
public sealed partial class GracewallAreaComponent : Component
{
    /// <summary>
    /// How long the grace wall lasts since the round started
    /// </summary>
    [DataField("gracewallRadius")]
    public float GracewallRadius { get; set; } = 1.5f;
    /// <summary>
    /// Is the grace wall currently active?
    /// </summary>
    [DataField("gracewallActive")]
    public bool GracewallActive { get; set; } = true;
    /// <summary>
    /// Which factions are blocked by the wall? If 'All', it applies to all
    /// </summary>
    [DataField("blockingFactions")]
    public List<string> BlockingFactions { get; set; } = ["All"];
    /// <summary>
    /// Does the grace wall "go down" or is it permanent?
    /// Permanent ones are useful to stop spawnkilling
    /// </summary>
    [DataField("permanent")]
    public bool Permanent { get; set; } = false;
}
