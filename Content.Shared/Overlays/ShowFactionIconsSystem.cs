using Content.Shared.StatusIcon.Components; // For JobIconPrototype if you use it directly
using Robust.Shared.Prototypes;
using System.Linq; // For LINQ queries if needed for checking existing squad members

namespace Content.Shared.Overlays;

public abstract class SharedFactionIconsSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    // Example: Define your squad configurations. This could be more dynamic.
    // You'd likely have prototypes for squads themselves eventually.
    protected static readonly Dictionary<string, SquadConfig> Squads = new()
    {
        { "Alpha", new SquadConfig("JobIconSquad1", "JobIconSquad1", 20) },
        { "Bravo", new SquadConfig("JobIconSquad2", "JobIconSquad2", 20) },
        { "Charlie", new SquadConfig("JobIconSquad3", "JobIconSquad3", 20) },
        // Add more squads here: "SquadName", "MemberIconId", "SergeantIconId", MaxSize
    };
    protected record SquadConfig(string MemberIconId, string SergeantIconId, int MaxSize);

    /// <summary>
    /// Call this on the SERVER to attempt to assign an entity to a squad.
    /// </summary>
    public virtual bool TryAssignToSquad(EntityUid uid, string squadName, bool assignAsSergeant, ShowFactionIconsComponent? component = null)
    {
        if (!Resolve(uid, ref component, logMissing: false))
            return false; // Only run on server and if component exists

        if (!Squads.TryGetValue(squadName, out var config))
            return false; // Squad doesn't exist

        // --- Server-Side Logic to check rules based on new explicit fields ---
        int currentOccupantsInSquad = 0;
        bool sergeantRoleFilledInSquad = false;
        EntityUid? existingSergeantUid = null;
        ShowFactionIconsComponent? oldSergeantComp = null; // For demotion

        var query = AllEntityQuery<ShowFactionIconsComponent>();
        while (query.MoveNext(out var otherUid, out var otherComp))
        {
            if (otherComp.AssignSquad && otherComp.AssignedSquadNameKey == squadName)
            {
                currentOccupantsInSquad++;
                if (otherComp.IsSergeantInSquad)
                {
                    sergeantRoleFilledInSquad = true;
                    existingSergeantUid = otherUid;
                }
            }
        }

        // Current state of the entity 'uid' being assigned
        bool entityIsAlreadyInThisSquad = component.AssignSquad && component.AssignedSquadNameKey == squadName;
        bool entityIsAlreadySergeantOfThisSquad = entityIsAlreadyInThisSquad && component.IsSergeantInSquad;

        if (assignAsSergeant)
        {
            // Trying to make 'uid' the sergeant.
            // If another sergeant exists and it's not 'uid', 'uid' will replace them (demotion handled later).
            // Check if adding 'uid' (if not already in squad) would exceed MaxSize.
            if (!entityIsAlreadyInThisSquad && currentOccupantsInSquad >= config.MaxSize)
            {
                Log.Debug($"Cannot assign {ToPrettyString(uid)} as Sergeant to squad {squadName}. Squad is full ({currentOccupantsInSquad}/{config.MaxSize}) and entity is not already in squad.");
                return false; // Squad is full, cannot add a new person as sergeant.
            }
        }
        else // Assigning as member
        {
            // Trying to make 'uid' a member.
            if (entityIsAlreadySergeantOfThisSquad)
            {
                // 'uid' is demoting itself. Count doesn't change. Fine.
            }
            else if (entityIsAlreadyInThisSquad && !component.IsSergeantInSquad) // Already a member
            {
                // 'uid' is re-affirming member status. Count doesn't change. Fine.
            }
            else // 'uid' is not in this squad (or in another squad, or not assigned).
            {
                if (currentOccupantsInSquad >= config.MaxSize)
                {
                    Log.Debug($"Cannot assign {ToPrettyString(uid)} as Member to squad {squadName}. Squad is full ({currentOccupantsInSquad}/{config.MaxSize}).");
                    return false; // Squad is full, cannot add a new member.
                }
            }
        }

        // --- Update component ---
        component.AssignSquad = true;
        component.AssignedSquadNameKey = squadName;
        component.IsSergeantInSquad = assignAsSergeant;
        component.SquadIcon = assignAsSergeant ? config.SergeantIconId : config.MemberIconId;
        Dirty(uid, component); // Mark component as dirty to ensure state is synced

        // If 'uid' became sergeant, and there was a *different* existing sergeant in this squad, demote the old one.
        if (assignAsSergeant && sergeantRoleFilledInSquad && existingSergeantUid.HasValue && existingSergeantUid.Value != uid)
        {
            if (Resolve(existingSergeantUid.Value, ref oldSergeantComp, logMissing: false))
            {
                // Ensure this old sergeant was indeed for *this* squad before demoting.
                if (oldSergeantComp.AssignSquad && oldSergeantComp.AssignedSquadNameKey == squadName && oldSergeantComp.IsSergeantInSquad)
                {
                    oldSergeantComp.IsSergeantInSquad = false;
                    oldSergeantComp.SquadIcon = config.MemberIconId; // Demote to member icon
                    // oldSergeantComp.AssignedSquadNameKey remains squadName
                    Dirty(existingSergeantUid.Value, oldSergeantComp);
                    Log.Info($"Demoted {ToPrettyString(existingSergeantUid.Value)} from sergeant in squad {squadName} as {ToPrettyString(uid)} took the role.");
                }
            }
        }


        Log.Info($"Assigned {ToPrettyString(uid)} to squad {squadName} as {(assignAsSergeant ? "Sergeant" : "Member")}. Icon: {component.SquadIcon}");
        return true;
    }

    public virtual void RemoveFromSquad(EntityUid uid, ShowFactionIconsComponent? component = null)
    {
        if (!Resolve(uid, ref component, logMissing: false))
            return;

        component.AssignSquad = false;
        component.SquadIcon = null;
        component.AssignedSquadNameKey = null;
        component.IsSergeantInSquad = false;
        Dirty(uid, component);
        Log.Info($"Removed {ToPrettyString(uid)} from squad.");
    }
}
