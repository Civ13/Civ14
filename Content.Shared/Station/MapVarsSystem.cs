using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using JetBrains.Annotations;
using Robust.Shared.GameStates;

namespace Content.Shared.MapVars;
public abstract class SharedGameVarsSystem: EntitySystem
{
    /// <summary>
    ///     Stores map/server data like ages, etc.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class GameVarsComponentState : ComponentState
    {
        /// <summary>
        ///     The in-game name of this entity.
        /// </summary>
        public int CurrentAge = 0;
    }
}
