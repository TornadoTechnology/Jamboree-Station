using System.Numerics;
using Content.Shared._Tech.BuckleTower.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;

namespace Content.Shared._Tech.BuckleTower;

public sealed class BuckleTowerSystem : EntitySystem
{
    private const float LayerHeight = 0.45f;

    [Dependency] private readonly SharedTransformSystem _transform = null!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = null!;
    [Dependency] private readonly SharedPopupSystem _popup = null!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BuckleTowerHolderComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);

        SubscribeLocalEvent<BuckleTowerHolderComponent, ComponentShutdown>(OnHolderShutdown);
        SubscribeLocalEvent<BuckleTowerRiderComponent, UpdateCanMoveEvent>(OnRiderUpdateCanMove);
    }

    private void OnGetVerbs(Entity<BuckleTowerHolderComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        var holderUid = ent.Owner;

        if (ent.Comp.Rider != null)
        {
            if (user == ent.Comp.Rider.Value)
            {
                args.Verbs.Add(new InteractionVerb
                {
                    Text = "Слезть",
                    Act = () => TryDismount(user)
                });
            }
            return;
        }

        if (!HasComp<BuckleTowerRiderComponent>(user))
            return;

        var root = GetRoot(holderUid);
        var riderRoot = GetRoot(user);

        if (root == riderRoot)
            return;

        if (!TryComp<BuckleTowerHolderComponent>(root, out var rootHolder))
            return;

        if (GetTowerHeight(root) + GetTowerHeight(riderRoot) > rootHolder.MaxRiders)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Text = "Забраться",
            Act = () => TryMount(user, holderUid)
        });
    }

    private void OnHolderShutdown(Entity<BuckleTowerHolderComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Rider != null)
            Dismount(ent.Comp.Rider.Value, ent.Owner);
    }

    private void OnRiderUpdateCanMove(EntityUid uid, BuckleTowerRiderComponent comp, UpdateCanMoveEvent args)
    {
        if (comp.RootHolderUid == null)
            return;

        args.Cancel();
    }

    public bool TryMount(EntityUid riderUid, EntityUid holderUid)
    {
        if (!TryComp<BuckleTowerHolderComponent>(holderUid, out var holder) || holder.Rider != null)
            return false;

        if (!HasComp<BuckleTowerRiderComponent>(riderUid))
            return false;

        var root = GetRoot(holderUid);
        var riderRoot = GetRoot(riderUid);

        if (root == riderRoot)
            return false;

        if (TryComp<BuckleTowerHolderComponent>(root, out var rootHolder) &&
            GetTowerHeight(root) + GetTowerHeight(riderRoot) > rootHolder.MaxRiders)
        {
            _popup.PopupClient("Башня слишком высокая!", riderUid);
            return false;
        }

        // --- Физически: точно в центр родителя, без сдвига ---
        var xform = Transform(riderUid);
        _transform.SetParent(riderUid, holderUid);
        _transform.SetCoordinates(riderUid, xform,
            new EntityCoordinates(holderUid, Vector2.Zero),  // <-- центр
            rotation: Angle.Zero);

        // --- Визуальный компонент: сдвиг будем делать на клиенте ---
        var visuals = EnsureComp<BuckleTowerVisualsComponent>(riderUid);

        holder.Rider = riderUid;
        Dirty(holderUid, holder);

        var riderComp = EnsureComp<BuckleTowerRiderComponent>(riderUid);
        riderComp.RootHolderUid = root;
        riderComp.Layer = TryComp<BuckleTowerRiderComponent>(holderUid, out var holderRider)
            ? holderRider.Layer + 1
            : 1;
        Dirty(riderUid, riderComp);

        // --- Сдвиг спрайта теперь через визуальный компонент ---
        visuals.OffsetY = riderComp.Layer * LayerHeight;
        Dirty(riderUid, visuals);

        if (riderRoot == riderUid)
            UpdateChainRoot(riderUid, root);

        _actionBlocker.UpdateCanMove(riderUid);
        return true;
    }

    public bool TryDismount(EntityUid riderUid)
    {
        var parent = _transform.GetParentUid(riderUid);

        if (!HasComp<BuckleTowerHolderComponent>(parent))
            return false;

        if (TryComp<BuckleTowerHolderComponent>(riderUid, out var riderAsHolder) && riderAsHolder.Rider != null)
        {
            _popup.PopupClient("На вас кто-то сидит!", riderUid);
            return false;
        }

        Dismount(riderUid, parent);
        return true;
    }

    private void Dismount(EntityUid riderUid, EntityUid holderUid)
    {
        if (!TryComp<BuckleTowerRiderComponent>(riderUid, out var rider))
            return;

        var worldPos = _transform.GetWorldPosition(riderUid);

        _transform.AttachToGridOrMap(riderUid);
        _transform.SetWorldPosition(riderUid, worldPos);

        // Удаление компонента -> клиент сбросит sprite.Offset в OnComponentRemove
        RemComp<BuckleTowerVisualsComponent>(riderUid);

        if (TryComp<BuckleTowerHolderComponent>(holderUid, out var holder) && holder.Rider == riderUid)
        {
            holder.Rider = null;
            Dirty(holderUid, holder);
        }

        rider.RootHolderUid = null;
        rider.Layer = 0;
        Dirty(riderUid, rider);

        _actionBlocker.UpdateCanMove(riderUid);
    }

    private void UpdateChainRoot(EntityUid start, EntityUid newRoot)
    {
        var current = start;
        var visited = new HashSet<EntityUid>();

        while (visited.Add(current))
        {
            if (TryComp<BuckleTowerRiderComponent>(current, out var rider))
            {
                rider.RootHolderUid = newRoot;
                Dirty(current, rider);
            }

            if (TryComp<BuckleTowerHolderComponent>(current, out var holder) && holder.Rider != null)
                current = holder.Rider.Value;
            else
                break;
        }
    }

    private EntityUid GetRoot(EntityUid uid)
    {
        var current = uid;
        var visited = new HashSet<EntityUid>();

        while (visited.Add(current))
        {
            var parent = _transform.GetParentUid(current);

            if (!parent.IsValid() || !TryComp<BuckleTowerHolderComponent>(parent, out var parentHolder))
                break;

            if (parentHolder.Rider != current)
                break;

            current = parent;
        }

        return current;
    }

    private int GetTowerHeight(EntityUid root)
    {
        var height = 1;
        var current = root;
        var visited = new HashSet<EntityUid> { root };

        while (TryComp<BuckleTowerHolderComponent>(current, out var holder) &&
               holder.Rider != null &&
               visited.Add(holder.Rider.Value))
        {
            current = holder.Rider.Value;
            height++;
        }

        return height;
    }
}
