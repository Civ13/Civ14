namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(CaptureAreaSystem))]
public sealed partial class CaptureAreaRuleComponent : Component
{
    /// <summary>
    /// The type of captures possible
    /// - King of the Hill: All factions try to capture a specific area.
    /// - Asymmetric: One faction attacks, another defends. Only the attacker can capture.
    /// There is a timer for the defender.
    /// - Symmetric: Factions have to capture each other's bases.
    /// </summary>
    [DataField("mode")]
    public string Mode { get; set; } = "King of the Hill";
    /// <summary>
    /// The timer before the defender wins, in minutes. Only applies if Mode is Asymmetric.
    /// </summary>
    [DataField("timer")]
    public float Timer { get; set; } = 40f;
    /// <summary>
    /// The faction that is defending on this map.
    /// </summary>
    [DataField("defenderFactionName")]
    public string DefenderFactionName { get; set; } = "Defender";

    /// <summary>
    /// How much time has elapsed in an Asymmetric mode game.
    /// </summary>
    [DataField("asymmetricGameTimeElapsed"), ViewVariables(VVAccess.ReadWrite)]
    public float AsymmetricGameTimeElapsed { get; set; } = 0f;
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
    /// Has 1 minute left been announced?
    /// </summary>
    [DataField("captureTimerAnnouncement1")]
    public bool CaptureTimerAnnouncement1 { get; set; } = false;
    /// <summary>
    /// Has 2 minute left been announced?
    /// </summary>
    [DataField("captureTimerAnnouncement2")]
    public bool CaptureTimerAnnouncement2 { get; set; } = false;
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
    /// The previous controller (for announcements when controller changes)
    /// </summary>
    [DataField("previousController")]
    public string PreviousController { get; set; } = "";
    /// <summary>
    /// Which factions can occupy this area?
    /// </summary>
    [DataField("capturableFactions")]
    public List<string> CapturableFactions { get; set; } = [];

    /// <summary>
    /// How long the area needs to be contested or lost before the capture timer resets
    /// </summary>
    [DataField("contestedResetTime")]
    public float ContestedResetTime { get; set; } = 10f;

    /// <summary>
    /// Current timer tracking how long the area has been contested or lost
    /// </summary>
    [DataField("contestedTimer")]
    public float ContestedTimer { get; set; } = 0f;

    /// <summary>
    /// The last controller before the area became contested or lost
    /// </summary>
    [DataField("lastController")]
    public string LastController { get; set; } = "";
}
