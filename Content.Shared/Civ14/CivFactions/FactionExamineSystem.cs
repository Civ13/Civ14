using Content.Shared.Examine;

namespace Content.Shared.Civ14.CivFactions;

public sealed class FactionExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CivFactionComponent, ExaminedEvent>(OnFactionExamine);
    }

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
            var str = $"He is a member of [color=#7f0000]{component.FactionName}[/color].";
            args.PushMarkup(str);
        }
    }

}
