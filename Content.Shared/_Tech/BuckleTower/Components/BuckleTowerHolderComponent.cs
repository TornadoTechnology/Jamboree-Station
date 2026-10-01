using Robust.Shared.GameStates;

namespace Content.Shared._Tech.BuckleTower.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BuckleTowerHolderComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Rider;

    [ViewVariables, AutoNetworkedField]
    public int MaxRiders = 3;
}
