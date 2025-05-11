using Content.Shared.Examine;

namespace Content.Shared._Stalker.Weight;

public sealed class SharedWeightExamineInfoSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<STWeightComponent, ExaminedEvent>(OnWeightExamine);
    }

    /// <summary>
    /// Adds a colour-coded weight description to the examination event based on the entity's total weight.
    /// </summary>
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

    /// <summary>
    /// Converts an integer to a two-character hexadecimal string, clamping values below 0 to "00" and above 255 to "FF".
    /// </summary>
    /// <param name="id">The integer value to convert.</param>
    /// <returns>A two-character hexadecimal string representing the clamped value.</returns>
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
