using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics;
using Content.Shared.Physics.Components;

namespace Content.Shared.Physics.Systems;

public sealed class FixturesReplaceSystem : EntitySystem
{
    [Dependency] private readonly FixtureSystem _fixtures = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        SubscribeLocalEvent<FixturesReplaceComponent, ComponentStartup>(OnChangeStartup);
        SubscribeLocalEvent<FixturesReplaceComponent, ComponentShutdown>(OnChangeShutdown);
    }

    private void OnChangeStartup(Entity<FixturesReplaceComponent> ent, ref ComponentStartup args)
    {
        if (!_physicsQuery.TryComp(ent, out var physics) || !_fixturesQuery.TryComp(ent, out var fixtures))
            return;

        foreach (var (id, fixture) in ent.Comp.Fixtures)
        {
			_fixtures.DestroyFixture(ent.Owner, id);
			
            _fixtures.TryCreateFixture(ent.Owner,
                fixture.Shape,
                id,
                fixture.Density,
                fixture.Hard,
                fixture.CollisionLayer,
                fixture.CollisionMask,
                fixture.Friction,
                fixture.Restitution,
                manager: fixtures,
                body: physics);
        }

        // TODO: Fixture creation should be handling this.
        _physics.WakeBody(ent.Owner, manager: fixtures, body: physics);
    }

    private void OnChangeShutdown(Entity<FixturesReplaceComponent> ent, ref ComponentShutdown args)
    {
        foreach (var id in ent.Comp.Fixtures.Keys)
        {
            _fixtures.DestroyFixture(ent.Owner, id);
        }
    }
}
