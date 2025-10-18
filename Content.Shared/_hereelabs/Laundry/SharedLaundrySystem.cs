using Content.Shared.Audio;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._hereelabs.Laundry;

public abstract class SharedLaundrySystem : EntitySystem
{

    [Dependency] private readonly SharedJitteringSystem _jittering = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    /// [Dependency] private readonly SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    /// [Dependency] private readonly ThrowingSystem _throwing = default!;
    /// [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] protected readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] protected readonly SharedSolutionContainerSystem _solutions = default!;

    private readonly int _modeCount = Enum.GetValues<LaundryMachineMode>().Length;
    private readonly int _washerCycleCount = Enum.GetValues<WasherCycleSetting>().Length;
    private readonly int _dryerCycleCount = Enum.GetValues<DryerCycleSetting>().Length;

    public readonly FixedPoint2 DRIP_AMOUNT = 0.08;
    public const float CLEAN_STRENGTH_FACTOR = 0.25f;
    public const float WASH_STRENGTH_FACTOR = 0.75f;
    public const float MACHINE_WASH_PORTION = 0.25f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LaundryMachineComponent, ComponentShutdown>(OnMachineShutdown);
        SubscribeLocalEvent<LaundryMachineComponent, ExaminedEvent>(OnMachineExamined);
        SubscribeLocalEvent<LaundryMachineComponent, GetVerbsEvent<Verb>>(OnMachineGetVerbs);
        SubscribeLocalEvent<LaundryMachineComponent, UnanchorAttemptEvent>(OnMachineUnanchorAttempt);
        SubscribeLocalEvent<LaundryMachineComponent, EntRemovedFromContainerMessage>(OnMachineRemoveEntity);

        SubscribeLocalEvent<WashableClothingComponent, ExaminedEvent>(OnWashableExamined);
    }

    #region Machine

    private void OnMachineExamined(Entity<LaundryMachineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var comp = ent.Comp;

        if (comp.CanDry)
        {
            if (comp.LaundryState == LaundryMachineState.Drying)
                args.PushMarkup(Loc.GetString("laundry-machine-examined-time-remaining", ("time", (int)Math.Ceiling(comp.TimeRemaining.TotalMinutes))), 12);
        }

        if (comp.CanWash)
            args.PushMarkup(Loc.GetString("laundry-machine-examined-wash-cycle", ("cycle", comp.WasherCycle)), 10);

        if (comp.CanDry)
        {
            // args.PushMarkup(Loc.GetString("laundry-machine-examined-dry-cycle", ("cycle", comp.DryerCycle)), 9);
            if (comp.LaundryState != LaundryMachineState.Drying)
                args.PushMarkup(Loc.GetString("laundry-machine-examined-timer", ("time", comp.TimeSettingMinutes)), 8);
        }

        if (comp.CanWash && comp.CanDry)
            args.PushMarkup(Loc.GetString("laundry-machine-examined-mode", ("mode", comp.Mode)), 6);
        args.PushMarkup(Loc.GetString("laundry-machine-examined-state", ("state", comp.LaundryState)), 11);
    }

    private void OnMachineGetVerbs(EntityUid uid, LaundryMachineComponent comp, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        if (comp.LaundryState == LaundryMachineState.Off)
        {
            var nextWashCycleIndex = ((int)comp.WasherCycle + 1) % _washerCycleCount;
            var nextDryCycleIndex = ((int)comp.DryerCycle + 1) % _dryerCycleCount;
            var nextModeIndex = ((int)comp.Mode + 1) % _modeCount;
            var nextWashCycle = (WasherCycleSetting)nextWashCycleIndex;
            var nextDryCycle = (DryerCycleSetting)nextDryCycleIndex;
            var nextMode = (LaundryMachineMode)nextModeIndex;

            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    SwitchWashCycle(uid, comp, nextWashCycle, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-wash-cycle", ("cycle", nextWashCycle)),
                Priority = 15,
            });
            /* args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    SwitchDryCycle(uid, comp, nextDryCycle, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-dry-cycle", ("cycle", nextDryCycle)),
                Priority = 14,
            }); */
            if (comp.CanDry && comp.CanWash)
            {
                args.Verbs.Add(new Verb()
                {
                    Act = () =>
                    {
                        SwitchMode(uid, comp, nextMode, args.User);
                    },
                    Text = Loc.GetString("laundry-machine-switch-mode", ("mode", nextMode)),
                    Priority = 13,
                });
            }

            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    StartMachine(uid, comp, args.User);
                },
                Text = Loc.GetString("laundry-machine-verb-start"),
                Priority = 12,
            });
        }
        else
        {
            if (comp.Paused)
            {
                args.Verbs.Add(new Verb()
                {
                    Act = () =>
                    {
                        ResumeMachine(uid, comp, args.User);
                    },
                    Text = Loc.GetString("laundry-machine-verb-resume"),
                    Priority = 15,
                });
            }
            else
            {
                args.Verbs.Add(new Verb()
                {
                    Act = () =>
                    {
                        PauseMachine(uid, comp, args.User);
                    },
                    Text = Loc.GetString("laundry-machine-verb-pause"),
                    Priority = 14,
                });
            }

            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    StopMachine(uid, comp, args.User);
                },
                Text = Loc.GetString("laundry-machine-verb-stop"),
                Priority = 13,
            });
        }
    }

    private void OnMachineShutdown(EntityUid uid, LaundryMachineComponent comp, ComponentShutdown args)
    {
        StopJitterMachine(uid);
        _ambientSound.SetAmbience(uid, false);
    }

    private void OnMachineUnanchorAttempt(EntityUid uid, LaundryMachineComponent comp, UnanchorAttemptEvent args)
    {
        if (!comp.Paused)
        {
            switch (comp.LaundryState)
            {
                case LaundryMachineState.Washing:
                    switch (comp.WashState)
                    {
                        case LaundryMachineWashState.Washing:
                        case LaundryMachineWashState.Rinsing:
                        case LaundryMachineWashState.FastSpin:
                            args.Cancel();
                            break;
                    }
                    break;
                case LaundryMachineState.Drying:
                    args.Cancel();
                    break;
            }
        }
    }

    private void OnMachineRemoveEntity(EntityUid uid, LaundryMachineComponent comp, EntRemovedFromContainerMessage args)
    {
        /// cant do this with how everything works unfortunately
        /*
        if (!TryComp<ItemSlotsComponent>(uid, out var itemSlots))
            return;
        var detergentContainer = itemSlots.Slots.GetValueOrDefault("detergentSlot")?.ContainerSlot;
        if (detergentContainer is not null && args.Container == detergentContainer)
            return;

        if (comp.Paused)
            return;

        /// fling entity

        var angle = _random.NextAngle();

        var minSpeed = 0f;
        var maxSpeed = 0f;
        switch (comp.LaundryState)
        {
            case LaundryMachineState.Washing:
                switch (comp.WashState)
                {
                    case LaundryMachineWashState.Washing:
                    case LaundryMachineWashState.Rinsing:
                        minSpeed = 6f;
                        maxSpeed = 12f;
                        break;
                    case LaundryMachineWashState.FastSpin:
                        minSpeed = 8f;
                        maxSpeed = 16f;
                        break;
                }
                break;
            case LaundryMachineState.Drying:
                minSpeed = 6f;
                maxSpeed = 12f;
                break;
        }

        var direction = angle.ToVec();
        var speed = _random.NextFloat(minSpeed, maxSpeed);

        _throwing.TryThrow(args.Entity, direction, speed, args.Entity);
        */
    }

    protected void ChangeMachineState(Entity<LaundryMachineComponent> ent, LaundryMachineState newState)
    {
        var comp = ent.Comp;
        if (comp.LaundryState == newState)
            return;

        comp.LaundryState = newState;

        var uid = ent.Owner;

        if (comp.Paused)
        {
            if (newState == LaundryMachineState.Off)
                _appearance.SetData(uid, LaundryMachineVisuals.Light, false);

            return;
        }

        switch (newState)
        {
            case LaundryMachineState.Off:
                StopJitterMachine(uid);
                _appearance.SetData(uid, LaundryMachineVisuals.Light, false);
                break;
            case LaundryMachineState.Delay:
                comp.TimeRemaining = TimeSpan.FromSeconds(10);
                _appearance.SetData(uid, LaundryMachineVisuals.Light, true);
                break;
            case LaundryMachineState.Drying:
                ChangeMachineWashState(ent, LaundryMachineWashState.Inactive);

                _audio.PlayPvs(comp.StartSpinSound, uid);
                _ambientSound.SetSound(uid, comp.SpinSound);
                _ambientSound.SetAmbience(uid, true);
                comp.TimeRemaining = TimeSpan.FromMinutes(comp.TimeSettingMinutes);
                StateJitterMachine(uid, LaundryMachineWashState.Washing, comp.WasherCycle);
                _appearance.SetData(uid, LaundryMachineVisuals.Light, true);
                break;
            case LaundryMachineState.Washing:
                switch (comp.WasherCycle)
                {
                    default:
                        ChangeMachineWashState(ent, LaundryMachineWashState.WashFill);
                        break;
                    case WasherCycleSetting.RinseAndSpin:
                        ChangeMachineWashState(ent, LaundryMachineWashState.RinseFill);
                        break;
                }
                _appearance.SetData(uid, LaundryMachineVisuals.Light, true);
                break;
        }
    }

    protected void ChangeMachineWashState(Entity<LaundryMachineComponent> ent, LaundryMachineWashState newWashState, LaundryMachineWashState? delayNextState = null)
    {
        var comp = ent.Comp;
        if (comp.WashState == newWashState)
            return;

        var prevWashState = comp.WashState;

        comp.WashState = newWashState;
        comp.WashDelayNextState = comp.WashState == LaundryMachineWashState.Delay ?
            delayNextState :
            null;

        var uid = ent.Owner;

        switch (prevWashState)
        {
            case LaundryMachineWashState.WashFill:
            case LaundryMachineWashState.RinseFill:
                break;
            case LaundryMachineWashState.Washing:
            case LaundryMachineWashState.Rinsing:
            case LaundryMachineWashState.FastSpin:
                _ambientSound.SetAmbience(uid, false);
                StopJitterMachine(uid);
                break;
            case LaundryMachineWashState.WashDraining:
            case LaundryMachineWashState.RinseDraining:
                break;
        }
        switch (newWashState)
        {
            case LaundryMachineWashState.WashFill:
            case LaundryMachineWashState.RinseFill:
                _audio.PlayPvs(comp.FillSound, uid);
                comp.TimeRemaining = TimeSpan.FromSeconds(10);
                break;
            case LaundryMachineWashState.Washing:
            case LaundryMachineWashState.Rinsing:
                _audio.PlayPvs(comp.StartSpinSound, uid);
                _ambientSound.SetSound(uid, comp.SpinSound);
                _ambientSound.SetAmbience(uid, true);
                comp.TimeRemaining = TimeSpan.FromMinutes(2);
                StateJitterMachine(uid, newWashState, comp.WasherCycle);
                break;
            case LaundryMachineWashState.WashDraining:
            case LaundryMachineWashState.RinseDraining:
                _audio.PlayPvs(comp.FillSound, uid);
                comp.TimeRemaining = TimeSpan.FromSeconds(10);
                break;
            case LaundryMachineWashState.FastSpin:
                _audio.PlayPvs(comp.StartSpinSound, uid);
                if (comp.WasherCycle != WasherCycleSetting.Delicate)
                    _ambientSound.SetSound(uid, comp.FastSpinSound);
                else
                    _ambientSound.SetSound(uid, comp.SpinSound);
                _ambientSound.SetAmbience(uid, true);
                comp.TimeRemaining = TimeSpan.FromMinutes(2);
                StateJitterMachine(uid, newWashState, comp.WasherCycle);
                break;
        }
    }

    private void DoJitterMachine(EntityUid uid, float amplitude, float rate)
    {
        _jittering.AddJitter(uid, amplitude, rate);
    }

    private void StopJitterMachine(EntityUid uid)
    {
        RemComp<JitteringComponent>(uid);
    }

    private void StateJitterMachine(EntityUid uid, LaundryMachineWashState washState, WasherCycleSetting washerCycle)
    {
        switch (washState)
        {
            default:
                StopJitterMachine(uid);
                break;
            case LaundryMachineWashState.Washing:
            case LaundryMachineWashState.Rinsing:
                DoJitterMachine(uid, 4f, 8f);
                break;
            case LaundryMachineWashState.FastSpin:
                if (washerCycle != WasherCycleSetting.Delicate)
                    DoJitterMachine(uid, 4f, 16f);
                else
                    DoJitterMachine(uid, 4f, 8f);
                break;
        }
    }

    private void SwitchWashCycle(EntityUid uid, LaundryMachineComponent comp, WasherCycleSetting cycle, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.WasherCycle = cycle;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupClient(Loc.GetString("laundry-machine-switched-wash-cycle", ("cycle", cycle)), uid, user);
        Dirty(ent);
    }

    private void SwitchDryCycle(EntityUid uid, LaundryMachineComponent comp, DryerCycleSetting cycle, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.DryerCycle = cycle;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupClient(Loc.GetString("laundry-machine-switched-dry-cycle", ("cycle", cycle)), uid, user);
        Dirty(ent);
    }

    private void SwitchMode(EntityUid uid, LaundryMachineComponent comp, LaundryMachineMode mode, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.Mode = mode;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupClient(Loc.GetString("laundry-machine-switched-mode", ("mode", mode)), uid, user);
        Dirty(ent);
    }

    private void StartMachine(EntityUid uid, LaundryMachineComponent comp, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        if (comp.CanWash && comp.CanDry)
        {
            switch (comp.Mode)
            {
                case LaundryMachineMode.Wash:
                case LaundryMachineMode.WashAndDry:
                    ChangeMachineState(ent, LaundryMachineState.Washing);
                    break;
                case LaundryMachineMode.Dry:
                    ChangeMachineState(ent, LaundryMachineState.Drying);
                    break;
            }
        }
        else
        {
            if (comp.CanWash)
                ChangeMachineState(ent, LaundryMachineState.Washing);
            else
                ChangeMachineState(ent, LaundryMachineState.Drying);
        }

        _audio.PlayPredicted(comp.StartSound, ent, user);
        _popup.PopupPredicted(Loc.GetString("laundry-machine-started"), uid, user);

        Dirty(ent);
    }

    protected void StopMachine(EntityUid uid, LaundryMachineComponent comp, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        ChangeMachineState(ent, LaundryMachineState.Off);
        comp.WashState = LaundryMachineWashState.Inactive;
        comp.WashDelayNextState = null;
        comp.Paused = false;
        _ambientSound.SetAmbience(uid, false);
        _audio.PlayPredicted(comp.StartSound, ent, user);
        _popup.PopupPredicted(Loc.GetString("laundry-machine-stopped"), uid, user);

        Dirty(ent);
    }

    protected bool PauseMachine(EntityUid uid, LaundryMachineComponent comp, EntityUid? user = null, bool doPopup = true)
    {
        if (comp.Paused)
            return false;

        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.Paused = true;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        if (doPopup)
            _popup.PopupPredicted(Loc.GetString("laundry-machine-paused"), uid, user);

        StopJitterMachine(uid);
        _ambientSound.SetAmbience(uid, false);

        Dirty(ent);

        return true;
    }

    protected bool ResumeMachine(EntityUid uid, LaundryMachineComponent comp, EntityUid? user = null)
    {
        if (!comp.Paused)
            return false;
        if (!TryComp<EntityStorageComponent>(uid, out var entStorage))
            return false;

        if (entStorage.Open)
        {
            _popup.PopupPredicted(Loc.GetString("laundry-machine-resume-must-close", ("machine", Identity.Entity(uid, EntityManager))), uid, user);
            return false;
        }

        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.Paused = false;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupPredicted(Loc.GetString("laundry-machine-resumed"), uid, user);

        switch (comp.LaundryState)
        {
            case LaundryMachineState.Drying:
                StateJitterMachine(uid, LaundryMachineWashState.Washing, comp.WasherCycle);
                _ambientSound.SetAmbience(uid, true);
                break;
            case LaundryMachineState.Washing:
                StateJitterMachine(uid, comp.WashState, comp.WasherCycle);

                switch (comp.WashState)
                {
                    case LaundryMachineWashState.Washing:
                    case LaundryMachineWashState.Rinsing:
                    case LaundryMachineWashState.FastSpin:
                        _ambientSound.SetAmbience(uid, true);
                        break;
                }
                break;
        }

        Dirty(ent);

        return true;
    }

    #endregion

    #region Washable clothing

    private void OnWashableExamined(Entity<WashableClothingComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var comp = ent.Comp;
        if (!TryGetWashableSolution(ent, out var _, out var solution))
            return;

        args.PushMarkup(Loc.GetString($"washable-clothing-examined-status-{GetWashableWetness(ent).ToString().ToLower()}"), 10);
        if (solution.Volume >= comp.DripVolume)
            args.PushMarkup(Loc.GetString("washable-clothing-examined-dripping"), 9);


    }
    public bool TryGetWashableSolution(Entity<WashableClothingComponent> ent, [NotNullWhen(true)] out Entity<SolutionComponent>? soln, [NotNullWhen(true)] out Solution? solution)
    {
        if (!TryComp<SolutionContainerManagerComponent>(ent.Owner, out var solutionContainer))
        {
            soln = null;
            solution = null;
            return false;
        }

        if (!_solutions.TryGetSolution((ent.Owner, solutionContainer), ent.Comp.Solution, out var gottenSoln, out var gottenSolution))
        {
            soln = null;
            solution = null;
            return false;
        }

        soln = gottenSoln;
        solution = gottenSolution;
        return true;
    }
    public ClothingWetness GetWashableWetness(Entity<WashableClothingComponent> ent)
    {
        if (!TryGetWashableSolution(ent, out var _, out var solution))
            return ClothingWetness.Dry;

        var wetnessScale = ent.Comp.WetnessScale;

        if (solution.Volume >= 17.5 * wetnessScale)
            return ClothingWetness.Drenched;
        if (solution.Volume >= 15 * wetnessScale)
            return ClothingWetness.VeryWet;
        if (solution.Volume >= 7.5 * wetnessScale)
            return ClothingWetness.Wet;
        if (solution.Volume >= 5 * wetnessScale)
            return ClothingWetness.Moist;
        if (solution.Volume > 0)
            return ClothingWetness.Damp;

        return ClothingWetness.Dry;
    }

    #endregion
}
