using JetBrains.Annotations;
using Robust.Shared.GameStates;

namespace Content.Shared.MapVars;

/// <summary>
/// Handles game vars
/// </summary>
[RegisterComponent, NetworkedComponent]
public class GameVarsComponent : Component
{
    [DataField("currentAge")]
    public int CurrentAge = 0;

    [UsedImplicitly, ViewVariables(VVAccess.ReadWrite)]
    public int CurrentAgeVV
    {
        get => CurrentAge;
        set
        {
            if (value.Equals(CurrentAge)) return;
            CurrentAge = value;
            IoCManager.Resolve<IEntityManager>().Dirty(this);
        }
    }
}
