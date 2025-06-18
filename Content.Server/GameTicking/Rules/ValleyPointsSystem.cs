using System.Linq;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Interaction;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Timing;
using Content.Server.KillTracking;
using Content.Shared.NPC.Systems;
using Content.Server.RoundEnd;


namespace Content.Server.GameTicking.Rules;

/// <summary>
/// Handles the Valley gamemode points system for Blugoslavia vs Insurgents
/// </summary>
public sealed class ValleyPointsRuleSystem : GameRuleSystem<ValleyPointsComponent>
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly RoundEndSystem _roundEndSystem = default!;
    [Dependency] private readonly NpcFactionSystem _factionSystem = default!; // Added dependency
    private ISawmill _sawmill = default!;
    private TimeSpan _lastSupplyBoxCheck = TimeSpan.Zero;
    private const float SupplyBoxCheckInterval = 30f; // Check every 30 seconds

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("valley-points");

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<CaptureAreaComponent, ComponentStartup>(OnCaptureAreaStartup);
        SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
    }

    protected override void Started(EntityUid uid, ValleyPointsComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        component.GameStartTime = _timing.CurTime;
        component.LastCheckpointBonusTime = _timing.CurTime;
        component.LastScoreAnnouncementTime = _timing.CurTime;
        _lastSupplyBoxCheck = _timing.CurTime;

        // Initialize checkpoints immediately
        InitializeCheckpoints(component);

        // Delay civilian count initialization to allow spawning
        Timer.Spawn(TimeSpan.FromSeconds(5), () => InitializeCivilianCount(component));

        _sawmill.Info("Valley gamemode started - 50 minute timer begins now");
        AnnounceToAll("Valley conflict has begun! Blugoslavia and Insurgents fight for control.");
    }

    private void OnCaptureAreaStartup(EntityUid uid, CaptureAreaComponent component, ComponentStartup args)
    {
        if (HasComp<ValleyCheckpointComponent>(uid))
        {
            component.CapturableFactions.Clear();
            component.CapturableFactions.Add("Blugoslavia");
            component.CapturableFactions.Add("Insurgents");
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && args.OldMobState == MobState.Alive)
        {
            if (TryComp<NpcFactionMemberComponent>(args.Target, out var factionMember) &&
                factionMember.Factions.Any(f => f == "Blugoslavia"))
            {
                var ruleQuery = EntityQueryEnumerator<ValleyPointsComponent>();
                if (ruleQuery.MoveNext(out var ruleEntity, out _))
                {
                    AwardInsurgentKill(ruleEntity);
                }
            }
        }
    }

    private void InitializeCivilianCount(ValleyPointsComponent component)
    {
        // Count civilian NPCs on the map
        var civilianCount = 0;
        var query = EntityQueryEnumerator<NpcFactionMemberComponent>();
        while (query.MoveNext(out _, out var faction))
        {
            if (faction.Factions.Any(f => f == "Civilian"))
                civilianCount++;
        }

        component.InitialCivilianCount = civilianCount;
        component.TotalCivilianNPCs = civilianCount;
        _sawmill.Info($"Initialized with {civilianCount} civilian NPCs");
    }

    private void InitializeCheckpoints(ValleyPointsComponent valley)
    {
        var checkpointQuery = EntityQueryEnumerator<ValleyCheckpointComponent, CaptureAreaComponent>();
        while (checkpointQuery.MoveNext(out var uid, out var checkpoint, out var area))
        {
            area.CapturableFactions.Clear();
            area.CapturableFactions.Add("Blugoslavia");
            area.CapturableFactions.Add("Insurgents");

            _sawmill.Info($"Initialized Valley checkpoint: {area.Name}");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ValleyPointsComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var valley, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(uid, gameRule))
                continue;

            if (valley.GameEnded)
                continue;

            UpdateCheckpointHolding(valley);
            UpdateSupplyBoxSecuring(valley);
            CheckCaptureAreaControl(valley);
            CheckSupplyBoxDeliveries(valley);
            CheckScoreAnnouncement(valley);
            CheckWinConditions(uid, valley);
            CheckTimeLimit(uid, valley);
        }
    }

    private void CheckSupplyBoxDeliveries(ValleyPointsComponent valley)
    {
        var currentTime = _timing.CurTime;

        // Only check every 30 seconds
        if ((currentTime - _lastSupplyBoxCheck).TotalSeconds < SupplyBoxCheckInterval)
            return;

        _lastSupplyBoxCheck = currentTime;

        // Check all capture areas for nearby supply boxes
        var areaQuery = EntityQueryEnumerator<CaptureAreaComponent>();
        while (areaQuery.MoveNext(out var areaUid, out var area))
        {
            var areaPos = _transform.GetMapCoordinates(areaUid);
            var nearbyEntities = _lookup.GetEntitiesInRange(areaPos, 3f);

            foreach (var entity in nearbyEntities)
            {
                if (TryComp<ValleySupplyBoxComponent>(entity, out var supplyBox) && !supplyBox.Delivered)
                {
                    // Check if this is a checkpoint controlled by Blugoslavia
                    if (HasComp<ValleyCheckpointComponent>(areaUid) && area.Controller == "Blugoslavia")
                    {
                        ProcessBlugoslavianSupplyDelivery(valley, entity, areaUid, area);
                    }
                    // Check if this is the Insurgent base
                    else if (IsInsurgentBase(areaUid, area))
                    {
                        ProcessInsurgentSupplyTheft(valley, entity, areaUid, area);
                    }
                }
            }
        }
    }

    private bool IsInsurgentBase(EntityUid areaUid, CaptureAreaComponent area)
    {
        // Check if this area is marked as insurgent base
        return area.Name.ToLower().Contains("insurgent");

    }

    private void ProcessBlugoslavianSupplyDelivery(ValleyPointsComponent valley, EntityUid supplyBox, EntityUid checkpoint, CaptureAreaComponent area)
    {
        if (!TryComp<ValleySupplyBoxComponent>(supplyBox, out var boxComp))
            return;

        boxComp.Delivered = true;
        boxComp.SecuringAtCheckpoint = checkpoint;

        // Start securing timer
        valley.SecuringSupplyBoxes[supplyBox] = _timing.CurTime;
        if (TryComp<ValleyCheckpointComponent>(checkpoint, out var checkpointComp))
        {
            checkpointComp.SecuringBoxes.Add(supplyBox);
        }

        _sawmill.Info($"Supply box delivery started at {area.Name}, securing for {valley.SupplyBoxSecureTime} seconds");

    }

    private void ProcessInsurgentSupplyTheft(ValleyPointsComponent valley, EntityUid supplyBox, EntityUid baseArea, CaptureAreaComponent area)
    {
        if (!TryComp<ValleySupplyBoxComponent>(supplyBox, out var boxComp))
            return;

        boxComp.Delivered = true;

        // Award points immediately for insurgent theft
        valley.InsurgentPoints += valley.StolenSupplyBoxPoints;
        _sawmill.Info($"Insurgents awarded {valley.StolenSupplyBoxPoints} points for stolen supply box at {area.Name}. Total: {valley.InsurgentPoints}");
        AnnounceToAll($"Insurgents: +{valley.StolenSupplyBoxPoints} points (Supply Theft at {area.Name}) - Total: {valley.InsurgentPoints}");

        // Remove the supply box
        QueueDel(supplyBox);
    }

    private void CheckCaptureAreaControl(ValleyPointsComponent valley)
    {
        var checkpointQuery = EntityQueryEnumerator<ValleyCheckpointComponent, CaptureAreaComponent>();
        while (checkpointQuery.MoveNext(out var uid, out var checkpoint, out var area))
        {
            var blugoslaviaControlled = area.Controller == "Blugoslavia";

            if (checkpoint.BlugoslaviaControlled != blugoslaviaControlled)
            {
                checkpoint.BlugoslaviaControlled = blugoslaviaControlled;
                SetCheckpointControl(valley, uid, blugoslaviaControlled);
            }
        }
    }

    public void SetCheckpointControl(ValleyPointsComponent valley, EntityUid checkpoint, bool blugoslaviaControlled)
    {
        if (blugoslaviaControlled)
        {
            if (!valley.BlugoslaviaHeldCheckpoints.Contains(checkpoint))
            {
                valley.BlugoslaviaHeldCheckpoints.Add(checkpoint);
                valley.CheckpointHoldStartTimes[checkpoint] = _timing.CurTime;
                _sawmill.Info($"Blugoslavia gained control of checkpoint {checkpoint}");
            }
        }
        else
        {
            if (valley.BlugoslaviaHeldCheckpoints.Contains(checkpoint))
            {
                valley.BlugoslaviaHeldCheckpoints.Remove(checkpoint);
                valley.CheckpointHoldStartTimes.Remove(checkpoint);
                _sawmill.Info($"Blugoslavia lost control of checkpoint {checkpoint}");
            }
        }
    }

    private void UpdateCheckpointHolding(ValleyPointsComponent valley)
    {
        var currentTime = _timing.CurTime;
        var checkpointsToAward = new List<EntityUid>();

        // Check individual checkpoint holding - award points every minute (60 seconds)
        foreach (var kvp in valley.CheckpointHoldStartTimes.ToList())
        {
            var checkpoint = kvp.Key;
            var startTime = kvp.Value;

            if ((currentTime - startTime).TotalSeconds >= 60f) // 1 minute instead of 5
            {
                checkpointsToAward.Add(checkpoint);
                valley.CheckpointHoldStartTimes[checkpoint] = currentTime;
            }
        }

        if (checkpointsToAward.Count > 0)
        {
            var pointsAwarded = checkpointsToAward.Count * 5; // 5 points per minute instead of 25 per 5 minutes
            valley.BlugoslaviaPoints += pointsAwarded;
            _sawmill.Info($"Blugoslavia awarded {pointsAwarded} points for holding {checkpointsToAward.Count} checkpoints");
            AnnounceToAll($"Blugoslavia: +{pointsAwarded} points (Checkpoint Control) - Total: {valley.BlugoslaviaPoints}");
        }

        // Check for all checkpoints bonus - still every 5 minutes but reduced frequency
        if (valley.BlugoslaviaHeldCheckpoints.Count >= 4 && // Assuming 4 checkpoints total
            (currentTime - valley.LastCheckpointBonusTime).TotalSeconds >= valley.CheckpointBonusInterval)
        {
            valley.BlugoslaviaPoints += valley.AllCheckpointsBonusPoints;
            valley.LastCheckpointBonusTime = currentTime;
            _sawmill.Info($"Blugoslavia awarded {valley.AllCheckpointsBonusPoints} bonus points for controlling all checkpoints");
            AnnounceToAll($"Blugoslavia: +{valley.AllCheckpointsBonusPoints} points (All Checkpoints Bonus) - Total: {valley.BlugoslaviaPoints}");
        }
    }

    private void UpdateSupplyBoxSecuring(ValleyPointsComponent valley)
    {
        var currentTime = _timing.CurTime;
        var securedBoxes = new List<EntityUid>();

        // Check all checkpoints for securing boxes
        var checkpointQuery = EntityQueryEnumerator<ValleyCheckpointComponent>();
        while (checkpointQuery.MoveNext(out var checkpointUid, out var checkpoint))
        {
            var boxesToRemove = new List<EntityUid>();

            foreach (var boxUid in checkpoint.SecuringBoxes)
            {
                if (!TryComp<ValleySupplyBoxComponent>(boxUid, out var boxComp))
                {
                    boxesToRemove.Add(boxUid);
                    continue;
                }

                // Check if box is still near the checkpoint
                var boxPos = _transform.GetMapCoordinates(boxUid);
                var checkpointPos = _transform.GetMapCoordinates(checkpointUid);

                if ((boxPos.Position - checkpointPos.Position).Length() > 3f)
                {
                    // Box moved away, cancel securing
                    boxesToRemove.Add(boxUid);
                    boxComp.Delivered = false;
                    boxComp.SecuringAtCheckpoint = null;
                    _sawmill.Info("Supply box moved away from checkpoint, delivery cancelled");
                    continue;
                }

                // Check if securing time has elapsed
                if (valley.SecuringSupplyBoxes.TryGetValue(boxUid, out var startTime) &&
                    (currentTime - startTime).TotalSeconds >= valley.SupplyBoxSecureTime)
                {
                    securedBoxes.Add(boxUid);
                    boxesToRemove.Add(boxUid);
                }
            }

            // Remove boxes that are no longer securing
            foreach (var box in boxesToRemove)
            {
                checkpoint.SecuringBoxes.Remove(box);
            }
        }

        // Award points for secured boxes
        foreach (var box in securedBoxes)
        {
            valley.SecuringSupplyBoxes.Remove(box);
            valley.BlugoslaviaPoints += valley.SupplyBoxDeliveryPoints;
            _sawmill.Info($"Blugoslavia awarded {valley.SupplyBoxDeliveryPoints} points for secured supply box delivery");
            AnnounceToAll($"Blugoslavia: +{valley.SupplyBoxDeliveryPoints} points (Supply Delivery) - Total: {valley.BlugoslaviaPoints}");

            // Delete the secured box
            QueueDel(box);
        }
    }

    /// <summary>
    /// Award points to insurgents for killing a Blugoslavian soldier.
    /// </summary>
    public void AwardInsurgentKill(EntityUid ruleEntity)
    {
        if (!TryComp<ValleyPointsComponent>(ruleEntity, out var valley))
            return;

        valley.InsurgentPoints += valley.KillPoints;
        _sawmill.Info($"Insurgents awarded {valley.KillPoints} points for kill. Total: {valley.InsurgentPoints}");
        AnnounceToAll($"Insurgents: +{valley.KillPoints} points (Kill) - Total: {valley.InsurgentPoints}");
    }

    /// <summary>
    /// Award points to Blugoslavia for successfully escorting a convoy.
    /// </summary>
    public void AwardConvoyEscort(EntityUid ruleEntity)
    {
        if (!TryComp<ValleyPointsComponent>(ruleEntity, out var valley))
            return;

        valley.BlugoslaviaPoints += valley.ConvoyEscortPoints;
        _sawmill.Info($"Blugoslavia awarded {valley.ConvoyEscortPoints} points for convoy escort. Total: {valley.BlugoslaviaPoints}");
        AnnounceToAll($"Blugoslavia: +{valley.ConvoyEscortPoints} points (Convoy Escort) - Total: {valley.BlugoslaviaPoints}");
    }

    private void CheckWinConditions(EntityUid uid, ValleyPointsComponent valley)
    {
        if (valley.BlugoslaviaPoints >= valley.PointsToWin)
        {
            EndGame(uid, valley, "Blugoslavia");
        }
        else if (valley.InsurgentPoints >= valley.PointsToWin)
        {
            EndGame(uid, valley, "Insurgents");
        }
    }

    private void CheckTimeLimit(EntityUid uid, ValleyPointsComponent valley)
    {
        var elapsed = (_timing.CurTime - valley.GameStartTime).TotalMinutes;
        if (elapsed >= valley.MatchDurationMinutes)
        {
            // Determine winner by points
            if (valley.BlugoslaviaPoints > valley.InsurgentPoints)
            {
                EndGame(uid, valley, "Blugoslavia");
            }
            else if (valley.InsurgentPoints > valley.BlugoslaviaPoints)
            {
                EndGame(uid, valley, "Insurgents");
            }
            else
            {
                EndGame(uid, valley, "Draw");
            }
        }
    }

    private void EndGame(EntityUid uid, ValleyPointsComponent valley, string winner)
    {
        valley.GameEnded = true;

        var finalMessage = winner switch
        {
            "Blugoslavia" => $"VICTORY: Blugoslavia wins with {valley.BlugoslaviaPoints} points!",
            "Insurgents" => $"VICTORY: Insurgents win with {valley.InsurgentPoints} points!",
            "Draw" => $"DRAW: Match ended in a tie! Blugoslavia: {valley.BlugoslaviaPoints}, Insurgents: {valley.InsurgentPoints}",
            _ => "Match ended."
        };

        _sawmill.Info($"Valley gamemode ended: {finalMessage}");
        AnnounceToAll(finalMessage);

        _roundEndSystem.EndRound();
    }
    private string CheckUNObjectives(ValleyPointsComponent valley)
    {
        var civilianSurvivalRate = valley.TotalCivilianNPCs > 0
            ? (float)valley.AliveCivilianNPCs / valley.TotalCivilianNPCs
            : 1.0f;

        var query = EntityQueryEnumerator<CaptureAreaComponent>();
        while (query.MoveNext(out var uid, out var area))
        {
            if (area.Name == "UN Hospital")
            {
                if (area.Occupied == true)
                {
                    valley.UNHospitalZoneControlled = false;
                }
                else
                {
                    valley.UNHospitalZoneControlled = true;
                }
            }
        }

        var unSuccess = civilianSurvivalRate >= valley.RequiredCivilianSurvivalRate &&
                       valley.UNHospitalZoneControlled &&
                       valley.UNNeutralityMaintained;

        var unMessage = unSuccess
            ? $"UN OBJECTIVES COMPLETED: {civilianSurvivalRate:P0} civilian survival rate maintained, hospital zone secured, neutrality preserved."
            : $"UN OBJECTIVES FAILED: {civilianSurvivalRate:P0} civilian survival rate, hospital zone: {(valley.UNHospitalZoneControlled ? "Secured" : "Lost")}, neutrality: {(valley.UNNeutralityMaintained ? "Maintained" : "Violated")}";

        _sawmill.Info(unMessage);
        return unMessage;
    }

    private void AnnounceToAll(string message)
    {
        _chatManager.DispatchServerAnnouncement(message);
    }

    protected override void AppendRoundEndText(EntityUid uid, ValleyPointsComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {

        if (component.BlugoslaviaPoints > component.InsurgentPoints)
        {
            args.AddLine($"[color=lime]Blugoslavia[/color] has won!");
        }
        else if (component.BlugoslaviaPoints < component.InsurgentPoints)
        {
            args.AddLine($"[color=lime]Insurgents[/color] have won!");

        }
        else
        {
            args.AddLine("The round ended in a [color=yellow]draw[/color]!");
        }
        args.AddLine("");
        args.AddLine($"Blugoslavia: {component.BlugoslaviaPoints} points");
        args.AddLine($"Insurgents: {component.InsurgentPoints} points");
        args.AddLine("");
        args.AddLine($"UN Objectives:");
        args.AddLine(CheckUNObjectives(component));
    }
    private void OnKillReported(ref KillReportedEvent ev)
    {
        var query = EntityQueryEnumerator<ValleyPointsComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var valley, out var rule))
        {
            if (!GameTicker.IsGameRuleActive(uid, rule))
                continue;

            // Check if UN member was involved (either as killer or victim) TODO: Check killer
            bool unInvolved = false;

            // Check if victim is UN
            if (TryComp<NpcFactionMemberComponent>(ev.Entity, out var victimFaction))
            {
                if (_factionSystem.IsMember(ev.Entity, "UnitedNations"))
                {
                    unInvolved = true;
                }

                // If UN was involved, set neutrality to false
                if (unInvolved && valley.UNNeutralityMaintained)
                {
                    valley.UNNeutralityMaintained = false;
                    _sawmill.Info("UN neutrality violated - UN member involved in combat");
                    AnnounceToAll("UN neutrality has been violated!");
                }

                // Award points to Insurgents for killing Blugoslavian soldiers
                if (TryComp<NpcFactionMemberComponent>(ev.Entity, out var deadFaction) &&
                    deadFaction.Factions.Any(f => f == "Blugoslavia"))
                {
                    valley.InsurgentPoints += valley.KillPoints;
                    _sawmill.Info($"Insurgents awarded {valley.KillPoints} points for Blugoslavian kill. Total: {valley.InsurgentPoints}");
                    AnnounceToAll($"Insurgents: +{valley.KillPoints} points (Kill) - Total: {valley.InsurgentPoints}");
                }
            }
        }
    }

    private void CheckScoreAnnouncement(ValleyPointsComponent valley)
    {
        var currentTime = _timing.CurTime;

        // Announce scores every 5 minutes (300 seconds)
        if ((currentTime - valley.LastScoreAnnouncementTime).TotalSeconds >= 300f)
        {
            valley.LastScoreAnnouncementTime = currentTime;

            var elapsedMinutes = (currentTime - valley.GameStartTime).TotalMinutes;
            var remainingMinutes = valley.MatchDurationMinutes - elapsedMinutes;

            var scoreMessage = $"SCORE UPDATE ({remainingMinutes:F0} minutes remaining): " +
                              $"Blugoslavia: {valley.BlugoslaviaPoints} points | " +
                              $"Insurgents: {valley.InsurgentPoints} points";

            _sawmill.Info($"Score announcement: {scoreMessage}");
            AnnounceToAll(scoreMessage);
        }
    }
}
