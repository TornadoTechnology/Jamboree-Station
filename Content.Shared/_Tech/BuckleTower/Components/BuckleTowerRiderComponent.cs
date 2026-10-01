using Robust.Shared.GameStates;

namespace Content.Shared._Tech.BuckleTower.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class BuckleTowerRiderComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? RootHolderUid;

    [ViewVariables, AutoNetworkedField]
    public int Layer;
}
