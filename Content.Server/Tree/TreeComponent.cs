namespace Content.Server.Tree;

[RegisterComponent]
public sealed class TreeComponent : Component
{
    [DataField("sound")]
    public string Sound = string.Empty;

    [DataField("breakTime")]
    public float BreakTime = 3.0f;
}
