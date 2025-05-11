using Content.Shared.Examine;

namespace Content.Shared._Stalker.Weight;

public sealed class SharedWeightExamineInfoSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<STWeightComponent, ExaminedEvent>(OnWeightExamine);
    }

    private void OnWeightExamine(EntityUid uid, STWeightComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var r = HexFromId(255);
        var g = HexFromId(255 - 255 / 30 * ((int)component.Total - 50));

        if (component.Total < 50f)
        {
            r = HexFromId(255 / 50 * (int)component.Total);
            g = HexFromId(255);
        }

        var colorString = $"#{r}{g}00";
        var str = $"Weighs [color={colorString}]{component.Total:0.00}[/color] kg";

        args.PushMarkup(str);
    }

    private string HexFromId(int id)
    {
        switch (id)
        {
            case < 0:
                return "00";

            case < 16:
                return "0" + id.ToString("X");

            case > 255:
                id = 255;
                return id.ToString("X");

            default:
                return id.ToString("X");
        }
    }
}
