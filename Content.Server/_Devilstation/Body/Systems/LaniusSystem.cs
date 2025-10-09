using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.EntityEffects;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Server._Devilstation.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.EffectConditions;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.Mobs.Systems;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Devilstation.Body.Systems;

[UsedImplicitly]
public sealed class LaniusSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly AtmosphereSystem _atmosSys = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private static readonly ProtoId<MetabolismGroupPrototype> GasId = new("Gas");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LaniusComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<LaniusComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.AdjustedUpdateInterval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LaniusComponent>();
        while (query.MoveNext(out var uid, out var respirator))
        {
            if (_gameTiming.CurTime < respirator.NextUpdate)
                continue;

            respirator.NextUpdate += respirator.AdjustedUpdateInterval;

            Siphon((uid, respirator));
            break;
        }
    }

    public void Siphon(Entity<LaniusComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        // Inhale gas
        var ev = new SiphonLocationEvent
        {
            Respirator = entity.Comp,
        };
        RaiseLocalEvent(entity, ref ev);

        ev.Gas ??= _atmosSys.GetContainingMixture(entity.Owner, excite: true);

        if (ev.Gas is null)
            return;

        var gas = ev.Gas.RemoveVolume(entity.Comp.BreathVolume);

        var inhaleEv = new SiphonedGasEvent(gas);
        RaiseLocalEvent(entity, ref inhaleEv);

        if (inhaleEv.Handled && inhaleEv.Succeeded)
            return;
    }

    /// <summary>
    /// Event raised when an entity first tries to inhale that returns a GasMixture from a given location.
    /// </summary>
    /// <param name="Gas">The gas that gets returned, null if there is none.</param>
    /// <param name="Respirator">The Respirator component of the entity attempting to inhale</param>
    [ByRefEvent]
    public record struct SiphonLocationEvent(GasMixture? Gas, LaniusComponent Respirator);

    /// <summary>
    /// Event raised when an entity successfully inhales a gas, attempts to find a place to put the gas.
    /// </summary>
    /// <param name="Gas">The gas we're inhaling.</param>
    /// <param name="Handled">Whether a system has responded appropriately.</param>
    /// <param name="Succeeded">Whether we successfully managed to inhale the gas</param>
    [ByRefEvent]
    public record struct SiphonedGasEvent(GasMixture Gas, bool Handled = false, bool Succeeded = false);
}