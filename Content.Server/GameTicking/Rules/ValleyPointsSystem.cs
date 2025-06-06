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

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("valley-points");

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ValleySupplyBoxComponent, InteractUsingEvent>(OnSupplyBoxInteract);
        SubscribeLocalEvent<CaptureAreaComponent, ComponentStartup>(OnCaptureAreaStartup);

    }

    protected override void Started(EntityUid uid, ValleyPointsComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.GameStartTime = _timing.CurTime;
        component.LastCheckpointBonusTime = _timing.CurTime;

        InitializeCivilianCount(component);
        InitializeCheckpoints(component);

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

    private void OnSupplyBoxInteract(EntityUid uid, ValleySupplyBoxComponent component, InteractUsingEvent args)
    {
        var nearbyCheckpoints = _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), 2f);

        foreach (var entity in nearbyCheckpoints)
        {
            if (HasComp<ValleyCheckpointComponent>(entity) && HasComp<CaptureAreaComponent>(entity))
            {
                TryDeliverSupplyBox(uid, entity, args.User);
                break;
            }
        }
    }

    private void TryDeliverSupplyBox(EntityUid supplyBox, EntityUid checkpoint, EntityUid user)
    {
        if (!TryComp<ValleySupplyBoxComponent>(supplyBox, out var boxComp) ||
            !TryComp<ValleyCheckpointComponent>(checkpoint, out var checkpointComp) ||
            !TryComp<CaptureAreaComponent>(checkpoint, out var areaComp))
            return;

        if (!TryComp<NpcFactionMemberComponent>(user, out var factionMember) ||
            !factionMember.Factions.Any(f => f == "Blugoslavia"))
        {
            _popup.PopupEntity("Only Blugoslavian forces can deliver supply boxes to checkpoints!", user, user);
            return;
        }

        if (!checkpointComp.BlugoslaviaControlled)
        {
            _popup.PopupEntity("This checkpoint must be under Blugoslavian control to deliver supplies!", user, user);
            return;
        }

        if (boxComp.Delivered)
        {
            _popup.PopupEntity("This supply box has already been delivered!", user, user);
            return;
        }

        var ruleQuery = EntityQueryEnumerator<ValleyPointsComponent>();
        if (ruleQuery.MoveNext(out var ruleEntity, out _))
        {
            boxComp.Delivered = true;
            boxComp.SecuringAtCheckpoint = checkpoint;
            checkpointComp.SecuringBoxes.Add(supplyBox);

            AwardSupplyBoxDelivery(ruleEntity, checkpoint, supplyBox);
            _popup.PopupEntity($"Supply box delivery started at {areaComp.Name}! Securing for 30 seconds...", user, user);

            _hands.TryDrop(user, supplyBox);
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
            CheckWinConditions(uid, valley);
            CheckTimeLimit(uid, valley);
        }
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

        foreach (var kvp in valley.CheckpointHoldStartTimes.ToList())
        {
            var checkpoint = kvp.Key;
            var startTime = kvp.Value;

            if ((currentTime - startTime).TotalSeconds >= valley.CheckpointHoldTime)
            {
                checkpointsToAward.Add(checkpoint);
                valley.CheckpointHoldStartTimes[checkpoint] = currentTime;
            }
        }

        if (checkpointsToAward.Count > 0)
        {
            var pointsAwarded = checkpointsToAward.Count * valley.CheckpointHoldPoints;
            valley.BlugoslaviaPoints += pointsAwarded;
            _sawmill.Info($"Blugoslavia awarded {pointsAwarded} points for holding {checkpointsToAward.Count} checkpoints");
            AnnounceToAll($"Blugoslavia: +{pointsAwarded} points (Checkpoint Control) - Total: {valley.BlugoslaviaPoints}");
        }

        // Check for all checkpoints bonus
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
    /// Award points to Blugoslavia for delivering a supply box to a checkpoint.
    /// </summary>
    public void AwardSupplyBoxDelivery(EntityUid ruleEntity, EntityUid checkpoint, EntityUid supplyBox)
    {
        if (!TryComp<ValleyPointsComponent>(ruleEntity, out var valley))
            return;

        // Start securing timer
        valley.SecuringSupplyBoxes[supplyBox] = _timing.CurTime;
        _sawmill.Info($"Supply box delivery started at checkpoint, securing for {valley.SupplyBoxSecureTime} seconds");
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
    /// Award points to insurgents for stealing and delivering a supply box.
    /// </summary>
    public void AwardStolenSupplyBox(EntityUid ruleEntity)
    {
        if (!TryComp<ValleyPointsComponent>(ruleEntity, out var valley))
            return;

        valley.InsurgentPoints += valley.StolenSupplyBoxPoints;
        _sawmill.Info($"Insurgents awarded {valley.StolenSupplyBoxPoints} points for stolen supply box. Total: {valley.InsurgentPoints}");
        AnnounceToAll($"Insurgents: +{valley.StolenSupplyBoxPoints} points (Supply Theft) - Total: {valley.InsurgentPoints}");
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

        // Check UN objectives
        CheckUNObjectives(valley);
    }
    private void CheckUNObjectives(ValleyPointsComponent valley)
    {
        var civilianSurvivalRate = valley.TotalCivilianNPCs > 0
            ? (float)valley.AliveCivilianNPCs / valley.TotalCivilianNPCs
            : 1.0f;

        var unSuccess = civilianSurvivalRate >= valley.RequiredCivilianSurvivalRate &&
                       valley.UNHospitalZoneControlled &&
                       valley.UNNeutralityMaintained;

        var unMessage = unSuccess
            ? $"UN OBJECTIVES COMPLETED: {civilianSurvivalRate:P0} civilian survival rate maintained, hospital zone secured, neutrality preserved."
            : $"UN OBJECTIVES FAILED: {civilianSurvivalRate:P0} civilian survival rate, hospital zone: {(valley.UNHospitalZoneControlled ? "Secured" : "Lost")}, neutrality: {(valley.UNNeutralityMaintained ? "Maintained" : "Violated")}";

        _sawmill.Info(unMessage);
        AnnounceToAll(unMessage);
    }

    private void AnnounceToAll(string message)
    {
        _chatManager.DispatchServerAnnouncement(message);
    }
}
