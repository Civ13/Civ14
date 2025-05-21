using Content.Shared.FixedPoint;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Gamerule that tracks stats for TDM gamemodes
/// </summary>
[RegisterComponent, Access(typeof(TeamDeathMatchRuleSystem))]
public sealed partial class TeamDeathMatchRuleComponent : Component
{

    [DataField("team1")]
    public string Team1 = "";

    [DataField("team1Points")]
    public int Team1Points = 0;

    [DataField("team1Deaths")]
    public int Team1Deaths = 0;

    [DataField("team1Kills")]
    public int Team1Kills = 0;

    [DataField("team2")]
    public string Team2 = "";

    [DataField("team2Points")]
    public int Team2Points = 0;

    [DataField("team2Deaths")]
    public int Team2Deaths = 0;

    [DataField("team2Kills")]
    public int Team2Kills = 0;

    [DataField("kdRatio")]
    public Dictionary<string, PlayerKDStats> KDRatio = new();
}

// Add this class to track player stats
[DataDefinition, Serializable]
public sealed partial class PlayerKDStats
{
    [DataField("kills")]
    public int Kills = 0;

    [DataField("deaths")]
    public int Deaths = 0;

    [DataField("team")]
    public string Team = "";

    [DataField("name")]
    public string Name = "";

    public float KDRatio => Deaths == 0 ? Kills : (float)Kills / Deaths;
}
