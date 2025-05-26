using Content.Shared.Overlays;
using Robust.Shared.Player;
using Content.Shared.Civ14.CivTDMFactions;
using Robust.Shared.Random; // Added for IRobustRandom
using System.Linq;
using Content.Shared.NPC.Components;         // Added for LINQ

namespace Content.Server.Overlays
{
    /// <summary>
    /// Server-side system for managing faction and squad icon assignments.
    /// Inherits core logic from SharedFactionIconsSystem.
    /// </summary>
    public sealed class FactionIconsSystem : SharedFactionIconsSystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly EntityManager _entityManager = default!;

        public override void Initialize()
        {
            base.Initialize();
        }

        /// <summary>
        /// Attempts to assign a player entity to a squad within a specific CivFaction,
        /// balancing members and managing sergeant roles per CivFaction.
        /// This can be called by other server systems (e.g., role assignment, admin commands).
        /// </summary>
        /// <param name="playerUid">The entity UID of the player.</param>
        /// <param name="playerAssignedCivFactionId">The ID of the CivFaction the player belongs to (e.g., Faction1Id from CivTDMFactionsComponent).</param>
        /// <param name="wantsToBeSergeant">Whether the player desires a sergeant role.</param>
        /// <param name="sfiComponent">The player's ShowFactionIconsComponent.</param>
        /// <returns>True if successfully assigned, false otherwise.</returns>
        public bool AttemptAssignPlayerToSquad(EntityUid playerUid, string playerAssignedCivFactionId, bool wantsToBeSergeant, ShowFactionIconsComponent? sfiComponent = null)
        {
            if (!Resolve(playerUid, ref sfiComponent, logMissing: false))
            {
                Log.Warning($"Player {ToPrettyString(playerUid)} does not have ShowFactionIconsComponent. Cannot assign to squad.");
                return false;
            }
            if (TryComp<ShowFactionIconsComponent>(playerUid, out var factMem))
            {
                if (factMem.AssignSquad == false)
                {
                    return false;
                }
            }
            // Get the CivTDMFactionsComponent
            CivTDMFactionsComponent? civTDMComp = null;
            var civQuery = _entityManager.EntityQueryEnumerator<CivTDMFactionsComponent>();
            if (civQuery.MoveNext(out _, out civTDMComp))
            {
                // Ensure Faction IDs are set
                if (civTDMComp.Faction1Id == null || civTDMComp.Faction2Id == null)
                {
                    Log.Error("CivTDMFactionsComponent Faction1Id or Faction2Id is not set. Cannot assign squads.");
                    return false;
                }
            }
            else
            {
                Log.Error("CivTDMFactionsComponent not found. Cannot assign squads.");
                return false;
            }

            string? targetSquadNameKey = null; // e.g., "Alpha", "Bravo"
            bool assignAsSergeantInCall = wantsToBeSergeant;
            string targetCivFactionIdForAssignment = playerAssignedCivFactionId;

            if (wantsToBeSergeant)
            {
                var squadsInTargetCivFaction = GetCivFactionSquads(civTDMComp, targetCivFactionIdForAssignment);
                targetSquadNameKey = FindBestSquadForRole(squadsInTargetCivFaction, true);

                if (targetSquadNameKey == null) // No suitable squad for sergeant in target CivFaction
                {
                    assignAsSergeantInCall = false; // Try to assign as member in their original faction
                    targetCivFactionIdForAssignment = playerAssignedCivFactionId; // Revert to player's own faction
                }
            }

            if (!assignAsSergeantInCall) // Assigning as member (either initially or fallback)
            {
                targetCivFactionIdForAssignment = playerAssignedCivFactionId; // Ensure assignment is to player's own faction
                var squadsInPlayerCivFaction = GetCivFactionSquads(civTDMComp, playerAssignedCivFactionId);
                targetSquadNameKey = FindBestSquadForRole(squadsInPlayerCivFaction, false);
            }

            if (targetSquadNameKey == null)
            {
                Log.Info($"Could not find a suitable squad for {ToPrettyString(playerUid)} in CivFaction {targetCivFactionIdForAssignment} as {(assignAsSergeantInCall ? "Sergeant" : "Member")}.");
                return false;
            }

            // Store old state for count updates
            var oldSquadIcon = sfiComponent.SquadIcon;
            var oldBelongsToCivFaction = sfiComponent.BelongsToCivFactionId;

            bool success = base.TryAssignToSquad(playerUid, targetSquadNameKey, assignAsSergeantInCall, sfiComponent);

            if (success)
            {
                sfiComponent.BelongsToCivFactionId = targetCivFactionIdForAssignment; // Update player's CivFaction if it changed
                Dirty(playerUid, sfiComponent);
                RecalculateAllCivFactionSquadCounts(civTDMComp); // Recalculate counts after assignment
                Log.Info($"Successfully assigned {ToPrettyString(playerUid)} to squad {targetSquadNameKey} in CivFaction {targetCivFactionIdForAssignment} as {(assignAsSergeantInCall ? "Sergeant" : "Member")}. New icon: {sfiComponent.SquadIcon}");
            }
            else
            {
                Log.Info($"Failed to assign {ToPrettyString(playerUid)} to squad {targetSquadNameKey} via SharedFactionIconsSystem.");
                // Revert BelongsToCivFactionId if it was tentatively changed for sergeant assignment
                if (wantsToBeSergeant && targetCivFactionIdForAssignment != playerAssignedCivFactionId)
                {
                    sfiComponent.BelongsToCivFactionId = playerAssignedCivFactionId;
                }
            }
            return success;
        }

        private Dictionary<string, int> CountSergeantsPerCivFaction(CivTDMFactionsComponent civTDMComp)
        {
            var counts = new Dictionary<string, int>();
            if (civTDMComp.Faction1Id != null) counts[civTDMComp.Faction1Id] = 0;
            if (civTDMComp.Faction2Id != null) counts[civTDMComp.Faction2Id] = 0;

            var query = _entityManager.EntityQueryEnumerator<ShowFactionIconsComponent>();
            while (query.MoveNext(out _, out var sfiComp))
            {
                if (sfiComp.BelongsToCivFactionId != null &&
                    sfiComp.AssignSquad && // Must be assigned to a squad
                    sfiComp.IsSergeantInSquad && // Must be a sergeant
                    counts.ContainsKey(sfiComp.BelongsToCivFactionId))
                {
                    counts[sfiComp.BelongsToCivFactionId]++;
                }
            }
            return counts;
        }

        private Dictionary<string, SquadData>? GetCivFactionSquads(CivTDMFactionsComponent civTDMComp, string civFactionId)
        {
            if (civFactionId == civTDMComp.Faction1Id)
                return civTDMComp.Faction1Squads;
            if (civFactionId == civTDMComp.Faction2Id)
                return civTDMComp.Faction2Squads;
            return null;
        }

        private string? FindBestSquadForRole(Dictionary<string, SquadData>? squadsData, bool findForSergeant)
        {
            if (squadsData == null)
                return null;

            string? bestSquad = null;
            int minMembers = int.MaxValue;

            foreach (var (squadNameKey, squadData) in squadsData.OrderBy(x => _random.Next())) // Randomize tie-breaking
            {
                if (!Squads.TryGetValue(squadNameKey, out var squadConfig))
                    continue; // This squad type isn't defined in SharedFactionIconsSystem

                int currentTotal = squadData.SergeantCount + squadData.MemberCount;
                if (currentTotal >= squadConfig.MaxSize)
                    continue; // Squad is full

                if (findForSergeant)
                {
                    if (squadData.SergeantCount == 0) // Slot for sergeant is open
                    {
                        if (currentTotal < minMembers) // Prefer less populated squads for sergeants too
                        {
                            minMembers = currentTotal;
                            bestSquad = squadNameKey;
                        }
                    }
                }
                else // Finding for member
                {
                    if (currentTotal < minMembers)
                    {
                        minMembers = currentTotal;
                        bestSquad = squadNameKey;
                    }
                }
            }
            return bestSquad;
        }

        public void RecalculateAllCivFactionSquadCounts(CivTDMFactionsComponent civTDMComp)
        {
            // Reset counts
            foreach (var squadDataDict in new[] { civTDMComp.Faction1Squads, civTDMComp.Faction2Squads })
            {
                foreach (var squadData in squadDataDict.Values)
                {
                    squadData.MemberCount = 0;
                    squadData.SergeantCount = 0;
                }
            }

            var memberIconIds = Squads.ToDictionary(kvp => kvp.Value.MemberIconId, kvp => kvp.Key);
            var sergeantIconIds = Squads.ToDictionary(kvp => kvp.Value.SergeantIconId, kvp => kvp.Key);

            var query = _entityManager.EntityQueryEnumerator<ShowFactionIconsComponent>();
            while (query.MoveNext(out _, out var sfiComp))
            {
                if (sfiComp.SquadIcon == null || sfiComp.BelongsToCivFactionId == null || sfiComp.AssignSquad == false)
                    continue;

                var targetSquadsDict = GetCivFactionSquads(civTDMComp, sfiComp.BelongsToCivFactionId);
                if (targetSquadsDict == null)
                    continue;

                if (memberIconIds.TryGetValue(sfiComp.SquadIcon, out var squadNameKeySgt) && targetSquadsDict.TryGetValue(squadNameKeySgt, out var squadData))
                {
                    if (sfiComp.JobIcon == "JobIconISgt")
                    {
                        squadData.SergeantCount++;
                    }
                    else
                    {
                        squadData.MemberCount++;
                    }
                }

            }
            // Mark CivTDMFactionsComponent as dirty. Need to get the MapUid from the TransformComponent.
            if (_entityManager.TryGetComponent<TransformComponent>(civTDMComp.Owner, out var xform) && xform.MapUid.HasValue)
            {
                Dirty(xform.MapUid.Value, civTDMComp);
            }
        }
    }
}

