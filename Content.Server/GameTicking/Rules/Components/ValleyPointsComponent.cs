using Content.Shared.Roles;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(ValleyPointsRuleSystem))]
public sealed partial class ValleyPointsComponent : Component
{
    /// <summary>
    /// The current points for Blugoslavia.
    /// </summary>
    [DataField]
    public int BlugoslaviaPoints = 0;

    /// <summary>
    /// The current points for the Insurgents.
    /// </summary>
    [DataField]
    public int InsurgentPoints = 0;

    /// <summary>
    /// Points needed to win the match.
    /// </summary>
    [DataField]
    public int PointsToWin = 1000;

    /// <summary>
    /// Match duration in minutes.
    /// </summary>
    [DataField]
    public float MatchDurationMinutes = 50f;

    /// <summary>
    /// Points awarded for killing a Blugoslavian soldier.
    /// </summary>
    [DataField]
    public int KillPoints = 30;

    /// <summary>
    /// Points awarded for delivering a supply box.
    /// </summary>
    [DataField]
    public int SupplyBoxDeliveryPoints = 50;

    /// <summary>
    /// Points awarded for stealing a supply box.
    /// </summary>
    [DataField]
    public int StolenSupplyBoxPoints = 75;

    /// <summary>
    /// Points awarded for escorting a convoy.
    /// </summary>
    [DataField]
    public int ConvoyEscortPoints = 100;

    /// <summary>
    /// Points awarded for holding a checkpoint.
    /// </summary>
    [DataField]
    public int CheckpointHoldPoints = 5;

    /// <summary>
    /// Bonus points for holding all checkpoints.
    /// </summary>
    [DataField]
    public int AllCheckpointsBonusPoints = 100;

    /// <summary>
    /// Time in seconds to hold a checkpoint before earning points.
    /// </summary>
    [DataField]
    public float CheckpointHoldTime = 60f; // 1 minute

    /// <summary>
    /// Time in seconds to secure a supply box at a checkpoint.
    /// </summary>
    [DataField]
    public float SupplyBoxSecureTime = 30f;

    /// <summary>
    /// Interval in seconds for all-checkpoints bonus.
    /// </summary>
    [DataField]
    public float CheckpointBonusInterval = 300f; // 5 minutes

    /// <summary>
    /// Initial count of civilian NPCs.
    /// </summary>
    [DataField]
    public int InitialCivilianCount = 0;

    /// <summary>
    /// Total civilian NPCs.
    /// </summary>
    [DataField]
    public int TotalCivilianNPCs = 0;
    /// <summary>
    /// Total civilian NPCs currently alive.
    /// </summary>
    [DataField]
    public int AliveCivilianNPCs = 0;
    /// <summary>
    /// Required civilian survival rate for UN objectives (0.0 to 1.0).
    /// </summary>
    [DataField]
    public float RequiredCivilianSurvivalRate = 0.8f; // 80%

    /// <summary>
    /// Whether the UN hospital zone is controlled by UN forces.
    /// </summary>
    [DataField]
    public bool UNHospitalZoneControlled = true;

    /// <summary>
    /// Whether the UN has maintained neutrality.
    /// </summary>
    [DataField]
    public bool UNNeutralityMaintained = true;

    /// <summary>
    /// When the game started.
    /// </summary>
    [DataField]
    public TimeSpan GameStartTime;

    /// <summary>
    /// Last time the all-checkpoints bonus was awarded.
    /// </summary>
    [DataField]
    public TimeSpan LastCheckpointBonusTime;

    /// <summary>
    /// Whether the game has ended.
    /// </summary>
    [DataField]
    public bool GameEnded = false;

    /// <summary>
    /// Checkpoints currently held by Blugoslavia.
    /// </summary>
    [DataField]
    public List<EntityUid> BlugoslaviaHeldCheckpoints = new();

    /// <summary>
    /// When each checkpoint started being held.
    /// </summary>
    [DataField]
    public Dictionary<EntityUid, TimeSpan> CheckpointHoldStartTimes = new();

    /// <summary>
    /// Supply boxes currently being secured.
    /// </summary>
    [DataField]
    public Dictionary<EntityUid, TimeSpan> SecuringSupplyBoxes = new();

    /// <summary>
    /// Last time scores were announced.
    /// </summary>
    [DataField]
    public TimeSpan LastScoreAnnouncementTime;
}
