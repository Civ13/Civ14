namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(CaptureAreaSystem))]
public sealed partial class CaptureAreaRuleComponent : Component
{
}
[RegisterComponent]
public sealed partial class CaptureAreaComponent : Component
{
    /// <summary>
    /// The name for this capturable area
    /// </summary>
    [DataField("name")]
    public string Name { get; set; } = "Objective Area";
    /// <summary>
    /// How long does the area need to be held for, in seconds
    /// </summary>
    [DataField("captureDuration")]
    public float CaptureDuration { get; set; } = 300f;
    /// <summary>
    /// How far entities need to be to count towards capture
    /// </summary>
    [DataField("captureRadius")]
    public float CaptureRadius { get; set; } = 4f;
    /// <summary>
    /// What the capture timer is currently at
    /// </summary>

    [DataField("captureTimer")]
    public float CaptureTimer { get; set; } = 0f;
    /// <summary>
    /// Is the area currently occupied?
    /// </summary>
    [DataField("occupied")]
    public bool Occupied { get; set; } = false;
    /// <summary>
    /// Which faction is occupying the area?
    /// </summary>
    [DataField("controller")]
    public string Controller { get; set; } = "";
    /// <summary>
    /// Which factions can occupy this area?
    /// </summary>
    [DataField("capturableFactions")]
    public List<string> CapturableFactions { get; set; } = [];



}
