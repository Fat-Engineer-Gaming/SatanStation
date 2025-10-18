using Content.Shared.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.Temperature;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared._hereelabs.Laundry;
using Robust.Shared.Prototypes;

namespace Content.Server._hereelabs.Laundry;

public sealed class LaundrySystem : SharedLaundrySystem
{
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private ISawmill _sawmill = default!;

    private const float UPDATE_TIME = 1f;
    private readonly TimeSpan _zeroTimeSpan = TimeSpan.FromSeconds(0);
    private readonly TimeSpan _oneTimeSpan = TimeSpan.FromSeconds(1);

    private float _timer;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LaundryMachineComponent, StorageBeforeOpenEvent>(OnMachineOpened);
        SubscribeLocalEvent<LaundryMachineComponent, DestructionEventArgs>(OnMachineDestruction);

        SubscribeLocalEvent<WashableClothingComponent, ComponentInit>(OnWashableInit);
        SubscribeLocalEvent<WashableClothingComponent, ReactionEntityEvent>(OnWashableSplashed);
        SubscribeLocalEvent<WashableClothingComponent, DestructionEventArgs>(OnWashableDestruction);
        SubscribeLocalEvent<WashableClothingComponent, OnTemperatureChangeEvent>(OnWashableTemperatureChange);

        _sawmill = _logManager.GetSawmill("laundry.server");
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timer += frameTime;
        if (_timer < UPDATE_TIME)
            return;

        var machineQuery = EntityQueryEnumerator<LaundryMachineComponent>();
        while (machineQuery.MoveNext(out var uid, out var machine))
            UpdateMachineComponent((uid, machine), UPDATE_TIME);

        var washableQuery = EntityQueryEnumerator<WashableClothingComponent>();
        while (washableQuery.MoveNext(out var uid, out var washable))
            UpdateWashableComponent((uid, washable), UPDATE_TIME);

        _timer -= UPDATE_TIME;
    }

    #region Machine

    private void UpdateMachineComponent(Entity<LaundryMachineComponent> ent, float deltaTime)
    {
        var comp = ent.Comp;

        if (comp.Paused)
            return;

        switch (comp.LaundryState)
        {
            case LaundryMachineState.Off:
                break;
            case LaundryMachineState.Washing:
                UpdateMachineComponentWash(ent, deltaTime);
                break;
            case LaundryMachineState.Delay:
                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                    ChangeMachineState(ent, LaundryMachineState.Drying);
                break;
            case LaundryMachineState.Drying:
                UpdateMachineComponentDry(ent, deltaTime);
                break;
        }
    }
    private void UpdateMachineComponentWash(Entity<LaundryMachineComponent> ent, float deltaTime)
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
                if (comp.WasherCycle != WasherCycleSetting.Delicate)
                {
                    // do fast-spin behavior
                    DrainDrum(uid, deltaTime, false);
                    MachineSpin(uid, comp, 2 * deltaTime, true);
                }
                else
                {
                    // delicate behavior
                    DrainDrum(uid, deltaTime, false);
                    MachineSpin(uid, comp, deltaTime);
                }

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    if (comp.CanWash && comp.CanDry)
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
                }
                break;
        }
    }
    private void UpdateMachineComponentDry(Entity<LaundryMachineComponent> ent, float deltaTime)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;

        /// do drying behavior
        MachineSpin(uid, comp, deltaTime, false);
        DrainDrum(uid, deltaTime, false);
        MachineHeat(uid, comp, deltaTime);

        comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
        if (comp.TimeRemaining <= _zeroTimeSpan)
            StopMachine(uid, comp);
    }

    private void FillDrumWater(EntityUid uid, double amount)
    {
        if (_solutions.EnsureSolutionEntity(uid, "drum", out var drumSoln) && _solutions.EnsureSolutionEntity(uid, "tank", out var tankSoln))
        {
            var takenSolution = _solutions.SplitSolution(tankSoln.Value, amount);
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
                !_solutions.TryGetFitsInDispenser(detergentContainer.Value, out var detergentSoln, out var _))
            {
                return;
            }

            var takenSolution = _solutions.SplitSolution(detergentSoln.Value, amount);
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

            var removed = _solutions.SplitSolution(soln.Value, amount);
            _puddle.TrySpillAt(uid, removed, out var _, sound);
        }
    }
    private void MachineSpin(EntityUid uid, LaundryMachineComponent comp, float deltaTime, bool dripWashables = true)
    {
        if (!_solutions.EnsureSolutionEntity(uid, "drum", out var soln))
            return;
        if (!TryComp<EntityStorageComponent>(uid, out var entStorage))
            return;

        var contained = entStorage.Contents.ContainedEntities;
        var solution = soln.Value.Comp.Solution;

        var containedLen = contained.Count;
        var portion = solution.Volume / containedLen * MACHINE_WASH_PORTION;

        foreach (var containedUid in contained)
        {
            /// splash solution onto everything inside
            var removed = _solutions.SplitSolution(soln.Value, solution.Volume * portion * deltaTime);
            _reactive.DoEntityReaction(containedUid, removed, ReactionMethod.Touch);
            if (dripWashables)
            {
                if (TryComp<WashableClothingComponent>(containedUid, out var washable))
                {
                    /// force washable to drip
                    WashableDrip((containedUid, washable), DRIP_AMOUNT * deltaTime);
                }
            }
            _solutions.AddSolution(soln.Value, removed);

            /// damage things inside
            var damageFactor = 1f;
            if (comp.WashState == LaundryMachineWashState.FastSpin && comp.WasherCycle != WasherCycleSetting.Delicate)
                damageFactor = 2f;

            _damageable.TryChangeDamage(containedUid, comp.Damage * damageFactor * deltaTime, interruptsDoAfters: false);
        }
    }
    private void MachineHeat(EntityUid uid, LaundryMachineComponent comp, float deltaTime)
    {
        if (!_solutions.EnsureSolutionEntity(uid, "drum", out var soln))
            return;
        if (!TryComp<EntityStorageComponent>(uid, out var entStorage))
            return;

        var solution = soln.Value.Comp.Solution;

        /// i am very likely doing this horribly wrong or smth but i do not care right now, i just want to get this working in some way at the moment
        var targetTemp = Atmospherics.T0C + comp.TemperatureCelcius;
        if (solution.Temperature < targetTemp)
        {
            _solutions.AddThermalEnergy(soln.Value, 18.75f);
            if (solution.Temperature > targetTemp)
                _solutions.SetTemperature(soln.Value, targetTemp);
        }
        if (entStorage.Air.Temperature < targetTemp)
        {
            _atmosphere.AddHeat(entStorage.Air, 18.75f * deltaTime);
            if (entStorage.Air.Temperature > targetTemp)
                entStorage.Air.Temperature = targetTemp;
        }
    }

    private void OnMachineOpened(EntityUid uid, LaundryMachineComponent comp, StorageBeforeOpenEvent args)
    {
        /// dump drum solution onto floor
        DrainDrum(uid, -1, true);

        if (comp.LaundryState == LaundryMachineState.Off)
            return;

        /// pause
        PauseMachine(uid, comp, doPopup: false);
    }
    private void OnMachineDestruction(EntityUid uid, LaundryMachineComponent comp, DestructionEventArgs args)
    {
        DrainDrum(uid, -1, true);
        if (_solutions.EnsureSolutionEntity(uid, "tank", out var tankSoln))
            _puddle.TrySpillAt(uid, tankSoln.Value.Comp.Solution, out var _);
    }

    #endregion

    #region Washable clothing

    private void UpdateWashableComponent(Entity<WashableClothingComponent> ent, float deltaTime)
    {
        if (!TryGetWashableSolution(ent, out var _, out var solution))
            return;

        /// drip
        if (solution.Volume > ent.Comp.DripVolume)
            WashableDrip(ent, DRIP_AMOUNT * deltaTime);

        /// temperature dry
        if (TryComp<TemperatureComponent>(ent.Owner, out var temperature))
            WashableDry(ent, deltaTime, temperature);
    }

    public FixedPoint2 WashableWash(Entity<WashableClothingComponent> ent, Solution washSolution)
    {
        if (!TryGetWashableSolution(ent, out var soln, out var solution))
            return 0;

        var incomingQuantity = FixedPoint2.Min(washSolution.Volume, solution.MaxVolume - solution.Volume);
        if (incomingQuantity <= 0)
            return 0;

        var incomingSolution = washSolution.SplitSolution(incomingQuantity);
        var solutionContents = solution.Contents.ToArray();

        /// cleaning reagents
        var cleaningStrength = 0f;
        var cleaningVolume = 0f;

        List<ReagentQuantity> cleaners = new();
        foreach (var reagentQuantity in solutionContents)
        {
            var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
            if (reagentProto.LaundryCleaningStrength > 0)
            {
                cleaningStrength += reagentProto.LaundryCleaningStrength;
                cleaningVolume += (float)reagentQuantity.Quantity;
                cleaners.Add(reagentQuantity);
            }
        }
        if (cleaningVolume > 0)
        {
            cleaningStrength /= cleaningVolume;

            List<ReagentQuantity> toClean = new();
            foreach (var reagentQuantity in solutionContents)
            {
                var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
                if (reagentProto.LaundryCleaningStrength <= 0 && !reagentProto.Absorbent)
                    toClean.Add(reagentQuantity);
            }

            FixedPoint2 totalRemoved = 0;
            foreach (var reagentQuantity in toClean)
            {
                var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
                var removed = solution.RemoveReagent(reagentQuantity.Reagent, cleaningStrength * CLEAN_STRENGTH_FACTOR / reagentProto.LaundryCleanResistance);
                totalRemoved += removed;
            }
            if (totalRemoved > 0)
            {
                foreach (var reagentQuantity in cleaners)
                {
                    solution.RemoveReagent(reagentQuantity.Reagent, totalRemoved * (reagentQuantity.Quantity / cleaningVolume) * CLEAN_STRENGTH_FACTOR);
                }
            }
        }

        /// absorbent reagents (like water) to wash away other non-absorbent or non-cleaning reagents
        solutionContents = solution.Contents.ToArray();

        Solution washedAwaySolution = new();
        FixedPoint2 washStrength = 0;

        foreach (var reagentQuantity in solutionContents)
        {
            var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
            if (reagentProto.Absorbent)
                washStrength += reagentQuantity.Quantity;
        }

        if (washStrength > 0)
        {
            foreach (var reagentQuantity in solutionContents)
            {
                var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
                if (!reagentProto.Absorbent)
                {
                    var removed = solution.RemoveReagent(reagentQuantity.Reagent, washStrength * WASH_STRENGTH_FACTOR);
                    washedAwaySolution.AddReagent(reagentQuantity.Reagent, removed);
                }
            }
        }

        WashableSpill(ent, washedAwaySolution);

        return _solutions.AddSolution(soln.Value, incomingSolution);
    }
    public void WashableDrip(Entity<WashableClothingComponent> ent, FixedPoint2 amount, bool sound = false)
    {
        if (!TryGetWashableSolution(ent, out var soln, out var solution))
            return;

        if (amount < 0)
            amount = solution.Volume;

        var dripped = _solutions.SplitSolution(soln.Value, amount);

        WashableSpill(ent, dripped, sound);
    }
    private void WashableSpill(Entity<WashableClothingComponent> ent, Solution solution, bool sound = false)
    {
        if (TryComp<InsideEntityStorageComponent>(ent.Owner, out var insideEntStorage))
        {
            var entStorageUid = insideEntStorage.Storage;
            if (TryComp<EntityStorageComponent>(entStorageUid, out var _) && TryComp<LaundryMachineComponent>(entStorageUid, out var _))
            {
                /// try dripping into laundry machine drum
                if (_solutions.EnsureSolutionEntity(entStorageUid, "drum", out var drumSoln))
                {
                    var transferred = _solutions.AddSolution(drumSoln.Value, solution);

                    /// spill leftovers as a puddle
                    if (transferred < solution.Volume)
                        _puddle.TrySpillAt(ent.Owner, solution, out var _, sound);
                    return;
                }
            }
        }

        /// spill dripped as puddle
        _puddle.TrySpillAt(ent.Owner, solution, out var _, sound);
    }
    public Solution? WashableDry(Entity<WashableClothingComponent> ent, float deltaTime, TemperatureComponent temperatureComponent)
    {
        if (!TryGetWashableSolution(ent, out var soln, out var solution) || !soln.HasValue)
            return null;

        Solution driedSolution = new();
        driedSolution.MaxVolume = solution.MaxVolume;

        foreach (var reagentQuantity in solution.Contents.ToArray())
        {
            var reagentProto = _prototypeManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);

            var evaporationSpeed = reagentProto.ImpEvaporates ?
                reagentProto.ImpEvaporationAmount :
                (float)reagentProto.EvaporationSpeed;
            if (evaporationSpeed <= 0f)
                continue;

            evaporationSpeed = Math.Clamp(evaporationSpeed, 0f, 1f);

            var dryStrength = Math.Min((temperatureComponent.CurrentTemperature - ent.Comp.DryTemperature) * evaporationSpeed / 150f, 3f);
            if (dryStrength <= 0f)
                continue;

            /// _sawmill.Debug($"reagent {reagentQuantity.Reagent.Prototype}: taking {dryStrength * deltaTime}");
            /// _sawmill.Debug($"before volume: {solution.Volume}");

            var removed = _solutions.RemoveReagent(soln.Value, reagentQuantity.Reagent, dryStrength * deltaTime);

            /// _sawmill.Debug($"reagent {reagentQuantity.Reagent.Prototype}: removed {removed}");
            /// _sawmill.Debug($"after  volume: {solution.Volume}");

            driedSolution.AddReagent(reagentQuantity.Reagent, removed);
        }

        return driedSolution;
    }
    private void OnWashableInit(Entity<WashableClothingComponent> ent, ref ComponentInit args)
    {
        if (!_solutions.EnsureSolution(ent.Owner, ent.Comp.Solution, out var _, ent.Comp.SolutionCapacity))
            return;

        EnsureComp<ReactiveComponent>(ent.Owner);
        EnsureComp<TemperatureComponent>(ent.Owner);
    }
    private void OnWashableSplashed(Entity<WashableClothingComponent> ent, ref ReactionEntityEvent args)
    {
        if (args.Method != ReactionMethod.Touch)
            return;
        if (args.Source is null)
            return;
        if (args.Source.Contents.Count <= 0)
            return;

        Solution splashSolution = args.Source.SplitSolution(args.Source.Volume / args.Source.Contents.Count);

        WashableWash(ent, splashSolution);
    }
    private void OnWashableDestruction(Entity<WashableClothingComponent> ent, ref DestructionEventArgs args)
    {
        WashableDrip(ent, -1, true);
    }
    private void OnWashableTemperatureChange(Entity<WashableClothingComponent> ent, ref OnTemperatureChangeEvent args)
    {
        if (!TryGetWashableSolution(ent, out var soln, out var _) || !soln.HasValue)
            return;

        _solutions.SetTemperature(soln.Value, args.CurrentTemperature);
    }

    #endregion
}
