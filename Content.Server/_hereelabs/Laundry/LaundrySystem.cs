using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._hereelabs.Laundry;

namespace Content.Server._hereelabs.Laundry;

public sealed class LaundrySystem : SharedLaundrySystem
{
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    private const float UPDATE_TIME = 1f;
    private readonly TimeSpan _zeroTimeSpan = TimeSpan.FromSeconds(0);
    private readonly TimeSpan _oneTimeSpan = TimeSpan.FromSeconds(1);

    private float _timer;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LaundryMachineComponent, StorageBeforeOpenEvent>(OnMachineOpened);
        SubscribeLocalEvent<LaundryMachineComponent, DestructionEventArgs>(OnDestruction);

        _sawmill = _logManager.GetSawmill("laundry.server");
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timer += frameTime;
        if (_timer < UPDATE_TIME)
            return;

        var query = EntityQueryEnumerator<LaundryMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
            UpdateComponent((uid, machine), UPDATE_TIME);

        _timer -= UPDATE_TIME;
    }
    private void UpdateComponent(Entity<LaundryMachineComponent> ent, float deltaTime)
    {
        var comp = ent.Comp;

        if (comp.Paused)
            return;

        switch (comp.LaundryState)
        {
            case LaundryMachineState.Off:
                break;
            case LaundryMachineState.Washing:
                UpdateComponentWash(ent, deltaTime);
                break;
            case LaundryMachineState.Delay:
                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                    ChangeMachineState(ent, LaundryMachineState.Drying);
                break;
            case LaundryMachineState.Drying:
                UpdateComponentDry(ent, deltaTime);
                break;
        }
    }
    private void UpdateComponentWash(Entity<LaundryMachineComponent> ent, float deltaTime)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;

        switch (comp.WashState)
        {
            case LaundryMachineWashState.Inactive:
                break;
            case LaundryMachineWashState.Delay:
                if (comp.WashDelayNextState is null)
                    break;

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                    ChangeMachineWashState(ent, (LaundryMachineWashState)comp.WashDelayNextState);
                break;
            case LaundryMachineWashState.WashFill:
                // split solution `tank` into `drum`, and put detergent into `drum`
                FillDrumWater(uid, 2.5 * deltaTime);
                FillDrumDetergent(uid, 10 * deltaTime);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.Washing);
                }
                break;
            case LaundryMachineWashState.Washing:
                // do washing behavior
                MachineSpin(uid, comp, deltaTime);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    ChangeMachineWashState(ent, LaundryMachineWashState.WashDraining);
                }
                break;
            case LaundryMachineWashState.WashDraining:
                // drain solution `drum` onto a puddle on the ground
                DrainDrum(uid, 30 * deltaTime, false);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.RinseFill);
                }
                break;
            case LaundryMachineWashState.RinseFill:
                // split solution `tank` into `drum`
                FillDrumWater(uid, 2.5 * deltaTime);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.Rinsing);
                }
                break;
            case LaundryMachineWashState.Rinsing:
                // do washing behavior
                MachineSpin(uid, comp, deltaTime);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    ChangeMachineWashState(ent, LaundryMachineWashState.RinseDraining);
                }
                break;
            case LaundryMachineWashState.RinseDraining:
                // drain solution `drum` onto a puddle on the ground
                DrainDrum(uid, 30 * deltaTime, false);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.FastSpin);
                }
                break;
            case LaundryMachineWashState.FastSpin:
                // do fast-spin behavior
                MachineSpin(uid, comp, deltaTime);
                DrainDrum(uid, 2 * deltaTime, false);

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    switch (comp.Mode)
                    {
                        case LaundryMachineMode.Wash:
                            StopMachine(uid, comp);
                            break;
                        case LaundryMachineMode.WashAndDry:
                            ChangeMachineState(ent, LaundryMachineState.Delay);
                            break;
                    }
                }
                break;
        }
    }
    private void UpdateComponentDry(Entity<LaundryMachineComponent> ent, float deltaTime)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;

        /// do drying behavior
        MachineSpin(uid, comp, deltaTime);
        MachineHeat(uid, comp, deltaTime);

        comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
        if (comp.TimeRemaining <= _zeroTimeSpan)
            StopMachine(uid, comp);
    }

    private void FillDrumWater(EntityUid uid, double amount)
    {
        if (_solutions.EnsureSolutionEntity(uid, "drum", out var drumSoln) && _solutions.EnsureSolutionEntity(uid, "tank", out var tankSoln))
        {
            Solution takenSolution = _solutions.SplitSolution(tankSoln.Value, amount);
            var transferred = _solutions.AddSolution(drumSoln.Value, takenSolution);

            if (transferred < amount)
                _solutions.AddSolution(tankSoln.Value, takenSolution);
        }
    }
    private void FillDrumDetergent(EntityUid uid, double amount)
    {
        if (_solutions.EnsureSolutionEntity(uid, "drum", out var drumSoln))
        {
            var detergentContainer = _itemSlots.GetItemOrNull(uid, "detergentSlot");
            if (detergentContainer is null ||
                !_solutions.TryGetFitsInDispenser(detergentContainer.Value, out var detergentSoln, out var detergentSolution))
            {
                return;
            }

            Solution takenSolution = _solutions.SplitSolution(detergentSoln.Value, amount);
            var transferred = _solutions.AddSolution(drumSoln.Value, takenSolution);

            if (transferred < amount)
                _solutions.AddSolution(detergentSoln.Value, takenSolution);
        }
    }
    private void DrainDrum(EntityUid uid, double amount, bool sound)
    {
        if (_solutions.EnsureSolutionEntity(uid, "drum", out var soln))
        {
            if (amount < 0)
                amount = (double)soln.Value.Comp.Solution.Volume;

            Solution removed = _solutions.SplitSolution(soln.Value, amount);
            _puddle.TrySpillAt(uid, removed, out var puddle, sound);
        }
    }
    private void MachineSpin(EntityUid uid, LaundryMachineComponent comp, float deltaTime)
    {
        if (!_solutions.EnsureSolutionEntity(uid, "drum", out var soln))
            return;
        if (!TryComp<EntityStorageComponent>(uid, out var entStorage))
            return;

        var contained = entStorage.Contents.ContainedEntities;
        Solution solution = soln.Value.Comp.Solution;

        foreach (var containedUid in contained)
        {
            /// splash solution onto everything inside
            Solution removed = _solutions.SplitSolution(soln.Value, solution.Volume * 0.01f * deltaTime);

            _reactive.DoEntityReaction(containedUid, removed, ReactionMethod.Touch);

            _solutions.AddSolution(soln.Value, removed);

            /// damage things inside
            var damageFactor = 1f;
            if (comp.WashState == LaundryMachineWashState.FastSpin)
                damageFactor = 4f;

            _sawmill.Debug($"{containedUid} {damageFactor}");

            _damageable.TryChangeDamage(containedUid, comp.Damage * damageFactor * deltaTime, interruptsDoAfters:false);
        }
    }
    private void MachineHeat(EntityUid uid, LaundryMachineComponent comp, float deltaTime)
    {
        if (!_solutions.EnsureSolutionEntity(uid, "drum", out var soln))
            return;
        if (!TryComp<EntityStorageComponent>(uid, out var entStorage))
            return;

        Solution solution = soln.Value.Comp.Solution;

        /// i am very likely doing this horribly wrong but i do not care right now, i just want to get this working in some way at the moment
        if (solution.Temperature < Atmospherics.T0C + 71f)
        {
            _solutions.AddThermalEnergy(soln.Value, 3750f);
            if (solution.Temperature > Atmospherics.T0C + 71f)
                _solutions.SetTemperature(soln.Value, Atmospherics.T0C + 71f);
        }
        if (entStorage.Air.Temperature < Atmospherics.T0C + 71f)
        {
            _atmosphere.AddHeat(entStorage.Air, 3750f * deltaTime);
            if (entStorage.Air.Temperature > Atmospherics.T0C + 71f)
                entStorage.Air.Temperature = Atmospherics.T0C + 71f;
        }
    }

    private void OnMachineOpened(EntityUid uid, LaundryMachineComponent comp, StorageBeforeOpenEvent args)
    {
        /// dump drum solution onto floor
        DrainDrum(uid, -1, true);

        if (comp.LaundryState == LaundryMachineState.Off)
            return;

        /// pause
        PauseMachine(uid, comp, doPopup:false);
    }
    private void OnDestruction(EntityUid uid, LaundryMachineComponent comp, DestructionEventArgs args)
    {
        DrainDrum(uid, -1, true);
        if (_solutions.EnsureSolutionEntity(uid, "tank", out var tankSoln))
            _puddle.TrySpillAt(uid, tankSoln.Value.Comp.Solution, out var puddle);
    }
}
