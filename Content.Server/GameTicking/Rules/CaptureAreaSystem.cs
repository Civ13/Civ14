using Content.Server.Cloning;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Medical.SuitSensors;
using Content.Shared.Mind;
using Robust.Shared.Random;

namespace Content.Server.GameTicking.Rules;

public sealed class CaptureAreaSystem : GameRuleSystem<CaptureAreaComponent>
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

    }

}
