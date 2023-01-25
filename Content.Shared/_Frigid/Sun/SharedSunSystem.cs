using Robust.Shared.Serialization;

namespace Content.Shared.Sun;

public abstract class SharedSunSystem : EntitySystem
{
    [Serializable]
    [NetSerializable]
    public enum Time
    {
        Midnight,
        Day,
        Noon,
        Night
    }

    public Color DayColor = new (85,85,85);
    public Color NoonColor = new(100, 100, 100);
    public Color NightColor = new(45, 35, 45);
    public Color MidnightColor = new(0, 0, 0);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<TimeChangeEvent>(OnTimeChangeEvent);
    }

    protected virtual void OnTimeChangeEvent(TimeChangeEvent message, EntitySessionEventArgs eventArgs)
    {
        // Specific side code in target.
    }

    [Serializable]
    [NetSerializable]
    public sealed class TimeChangeEvent : EntityEventArgs
    {
        public TimeChangeEvent(Time timeOfDay)
        {
            TimeOfDay = timeOfDay;
        }

        public Time TimeOfDay { get; }
    }
}