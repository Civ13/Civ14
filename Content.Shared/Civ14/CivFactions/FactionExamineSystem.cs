using Content.Shared.Examine;

namespace Content.Shared.Civ14.CivFactions;

public sealed class FactionExamineSystem : EntitySystem
{
    /// <summary>
    /// Subscribes to examination events for entities with a faction component to provide custom examine text based on faction membership.
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CivFactionComponent, ExaminedEvent>(OnFactionExamine);
    }

    /// <summary>
    /// Adds a faction membership message to the examine event, indicating whether the examined entity shares a faction with the examiner or not.
    /// </summary>
    /// <param name="uid">The unique identifier of the examined entity.</param>
    /// <param name="component">The faction component of the examined entity.</param>
    /// <param name="args">The examination event arguments.</param>
    private void OnFactionExamine(EntityUid uid, CivFactionComponent component, ExaminedEvent args)
    {

        if (TryComp<CivFactionComponent>(args.Examiner, out var examinerFaction))
        {
            if (component.FactionName == "")
            {
                return;
            }
            if (component.FactionName == examinerFaction.FactionName)
            {
                var str = $"He is a member of your faction, [color=#007f00]{component.FactionName}[/color].";
                args.PushMarkup(str);
            }
            else
            {
                var str = $"He is a member of [color=#7f0000]{component.FactionName}[/color].";
                args.PushMarkup(str);
            }
        }
        else
        {
            var str = $"He is not a member of any factions.";
            args.PushMarkup(str);
        }
    }

}
