using System.Linq;
using Content.Server.Administration.Commands;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Points;
using Content.Server.RoundEnd;
using Content.Server.Station.Systems;
using Content.Server.NPC.Systems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Points;
using Content.Shared.Storage;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Utility;
using Content.Shared.NPC.Systems;
using Robust.Shared.Player;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// Manages <see cref="TeamDeathMatchRuleComponent"/>
/// </summary>
public sealed class TeamDeathMatchRuleSystem : GameRuleSystem<TeamDeathMatchRuleComponent>
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly PointSystem _point = default!;
    [Dependency] private readonly RespawnRuleSystem _respawn = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly NpcFactionSystem _factionSystem = default!; // Added dependency
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        var query = EntityQueryEnumerator<TeamDeathMatchRuleComponent, RespawnTrackerComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var dm, out var tracker, out var rule))
        {
            if (!GameTicker.IsGameRuleActive(uid, rule))
                continue;

            var newMind = _mind.CreateMind(ev.Player.UserId, ev.Profile.Name);
            _mind.SetUserId(newMind, ev.Player.UserId);

            var mobMaybe = _stationSpawning.SpawnPlayerCharacterOnStation(ev.Station, null, ev.Profile);
            DebugTools.AssertNotNull(mobMaybe);
            var mob = mobMaybe!.Value;

            _mind.TransferTo(newMind, mob);
            EnsureComp<KillTrackerComponent>(mob);

            ev.Handled = true;
            break;
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        EnsureComp<KillTrackerComponent>(ev.Mob);
        var query = EntityQueryEnumerator<TeamDeathMatchRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var component, out var rule))
        {
            if (!GameTicker.IsGameRuleActive(uid, rule))
                continue;
            if (component.Team1 == "")
            {
                if (TryComp<NpcFactionMemberComponent>(ev.Mob, out var npc))
                {
                    if (npc.Factions.Count > 0 && npc.Factions.First() != component.Team2)
                    {
                        component.Team1 = npc.Factions.First();
                    }
                }
            }
            if (component.Team2 == "")
            {
                if (TryComp<NpcFactionMemberComponent>(ev.Mob, out var npc))
                {
                    if (npc.Factions.Count > 0 && npc.Factions.First() != component.Team1)
                    {
                        component.Team2 = npc.Factions.First();
                    }
                }
            }
        }
    }

    private void OnKillReported(ref KillReportedEvent ev)
    {
        var query = EntityQueryEnumerator<TeamDeathMatchRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var dm, out var rule))
        {
            if (!GameTicker.IsGameRuleActive(uid, rule))
                continue;

            // Check if the killed entity is part of either team using FactionSystem
            if (HasComp<NpcFactionMemberComponent>(ev.Entity))
            {
                string killedTeam = "";

                if (_factionSystem.IsMember(ev.Entity, dm.Team1))
                {
                    dm.Team1Deaths += 1;
                    dm.Team2Kills += 1;
                    killedTeam = dm.Team1;
                }
                else if (_factionSystem.IsMember(ev.Entity, dm.Team2))
                {
                    dm.Team2Deaths += 1;
                    dm.Team1Kills += 1;
                    killedTeam = dm.Team2;
                }

                // Track individual player stats
                if (ev.Primary is KillPlayerSource playerSource)
                {
                    var playerIdStr = playerSource.PlayerId.ToString();

                    if (!dm.KDRatio.ContainsKey(playerIdStr))
                    {
                        // Find player name from sessions
                        string playerName = "Unknown";
                        foreach (var session in _player.Sessions)
                        {
                            if (session.UserId == playerSource.PlayerId)
                            {
                                playerName = session.Name;
                                break;
                            }
                        }

                        string playerTeam = killedTeam == dm.Team1 ? dm.Team2 : dm.Team1;

                        dm.KDRatio[playerIdStr] = new PlayerKDStats
                        {
                            Name = playerName,
                            Team = playerTeam
                        };
                    }

                    dm.KDRatio[playerIdStr].Kills++;
                }

                // Track deaths for the killed player
                if (_entities.TryGetComponent(ev.Entity, out ActorComponent? actorComponent))
                {

                    var playerIdStrKilled = actorComponent.PlayerSession.UserId.ToString();

                    if (!dm.KDRatio.ContainsKey(playerIdStrKilled))
                    {
                        dm.KDRatio[playerIdStrKilled] = new PlayerKDStats
                        {
                            Name = actorComponent.PlayerSession.Name,
                            Team = killedTeam
                        };
                    }

                    dm.KDRatio[playerIdStrKilled].Deaths++;
                }
            }
        }
    }

    protected override void AppendRoundEndText(EntityUid uid, TeamDeathMatchRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        // If we are using points, use them to display winner
        if (component.Team1Points > 0 && component.Team2Points > 0)
        {
            if (component.Team1Points > component.Team2Points)
            {
                args.AddLine($"[color=lime]{component.Team1}[/color] has won!");
            }
            else if (component.Team1Points < component.Team2Points)
            {
                args.AddLine($"[color=lime]{component.Team2}[/color] has won!");

            }
            else
            {
                args.AddLine("The round ended in a [color=yellow]draw[/color]!");
            }
        }
        args.AddLine("");
        args.AddLine($"[color=cyan]{component.Team1}[/color]: {component.Team1Kills} Kills, {component.Team1Deaths} Deaths");
        args.AddLine("");
        args.AddLine($"[color=cyan]{component.Team2}[/color]: {component.Team2Kills} Kills, {component.Team2Deaths} Deaths");

        // Display K/D ratio per player, sorted by K/D ratio
        args.AddLine("");
        args.AddLine("[color=yellow]Player Statistics:[/color]");

        // Sort players by K/D ratio (highest first)
        var sortedPlayers = component.KDRatio
            .OrderByDescending(p => p.Value.KDRatio)
            .ThenByDescending(p => p.Value.Kills)
            .ToList();

        // Display team 1 players
        args.AddLine($"[color=cyan]{component.Team1}[/color] Players:");
        foreach (var player in sortedPlayers.Where(p => p.Value.Team == component.Team1))
        {
            var kdRatio = player.Value.Deaths == 0 ? player.Value.Kills.ToString() : (player.Value.Kills / (float)player.Value.Deaths).ToString("F2");
            args.AddLine($"  [color=white]{player.Value.Name}[/color]: {player.Value.Kills} Kills, {player.Value.Deaths} Deaths, K/D: {kdRatio}");
        }

        // Display team 2 players
        args.AddLine("");
        args.AddLine($"[color=cyan]{component.Team2}[/color] Players:");
        foreach (var player in sortedPlayers.Where(p => p.Value.Team == component.Team2))
        {
            var kdRatio = player.Value.Deaths == 0 ? player.Value.Kills.ToString() : (player.Value.Kills / (float)player.Value.Deaths).ToString("F2");
            args.AddLine($"  [color=white]{player.Value.Name}[/color]: {player.Value.Kills} Kills, {player.Value.Deaths} Deaths, K/D: {kdRatio}");
        }
    }
}
