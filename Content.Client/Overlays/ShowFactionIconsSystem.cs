using Content.Shared.Overlays;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed class ShowFactionIconsSystem : EquipmentHudSystem<ShowFactionIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowFactionIconsComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);

    }

    /// <summary>
    /// Adds faction and job icons to the status icon list for an entity if their prototypes are found.
    /// </summary>
    /// <param name="uid">The entity requesting status icons.</param>
    /// <param name="component">The component specifying faction and job icon identifiers.</param>
    /// <param name="ev">The event containing the status icon list to update.</param>
    private void OnGetStatusIconsEvent(EntityUid uid, ShowFactionIconsComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        // Display regular faction icon
        if (_prototype.TryIndex<FactionIconPrototype>(component.FactionIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);

        // Display squad-specific icon if assigned by the server
        if (component.AssignSquad && component.SquadIcon != null)
        {
            if (_prototype.TryIndex<JobIconPrototype>(component.SquadIcon, out var squadIconPrototype))
                ev.StatusIcons.Add(squadIconPrototype);
        }
        // Otherwise, display the general job icon if no squad icon is present or if not part of a squad
        if (component.JobIcon != null && component.JobIcon != "JobIconSoldier" && component.JobIcon != "JobIconRifleman" && component.JobIcon != "JobIconMg")
        {
            if (_prototype.TryIndex<JobIconPrototype>(component.JobIcon, out var jobIconPrototype))
                ev.StatusIcons.Add(jobIconPrototype);
        }
    }
}
