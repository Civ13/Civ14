namespace Content.Client.Civ14.LookZoom;

[RegisterComponent]
public sealed partial class LookZoomComponent : Component
{
    public bool State = false;

    public TimeSpan DelayedTime;
}
