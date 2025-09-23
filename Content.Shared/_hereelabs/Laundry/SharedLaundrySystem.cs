using Content.Shared.Audio;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Shared._hereelabs.Laundry;

public abstract class SharedLaundrySystem : EntitySystem
{

    [Dependency] private readonly SharedJitteringSystem _jittering = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] protected readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly int _modeCount = Enum.GetValues<LaundryMachineMode>().Length;
    private readonly int _washerCycleCount = Enum.GetValues<WasherCycleSetting>().Length;
    private readonly int _dryerCycleCount = Enum.GetValues<DryerCycleSetting>().Length;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LaundryMachineComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<LaundryMachineComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<LaundryMachineComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<LaundryMachineComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<LaundryMachineComponent, EntRemovedFromContainerMessage>(OnMachineRemoveEntity);
    }
    private void OnExamined(Entity<LaundryMachineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var comp = ent.Comp;

        if (comp.CanWash)
            args.PushMarkup(Loc.GetString("laundry-machine-examined-wash-cycle", ("cycle", comp.WasherCycle)), 10);

        if (comp.CanDry)
        {
            args.PushMarkup(Loc.GetString("laundry-machine-examined-dry-cycle", ("cycle", comp.DryerCycle)), 9);
            if (comp.LaundryState != LaundryMachineState.Drying)
                args.PushMarkup(Loc.GetString("laundry-machine-examined-timer", ("time", comp.TimeSettingMinutes)), 8);
            else
                args.PushMarkup(Loc.GetString("laundry-machine-examined-time-remaining", ("time", (int)Math.Ceiling(comp.TimeRemaining.TotalMinutes))), 7);
        }

        args.PushMarkup(Loc.GetString("laundry-machine-examined-mode", ("mode", comp.Mode)), 6);
        args.PushMarkup(Loc.GetString("laundry-machine-examined-state", ("state", comp.LaundryState)), 11);
    }

    private void OnGetVerbs(EntityUid uid, LaundryMachineComponent comp, GetVerbsEvent<Verb> args)
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
                Priority = 10,
            });
            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    SwitchDryCycle(uid, comp, nextDryCycle, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-dry-cycle", ("cycle", nextDryCycle)),
                Priority = 9,
            });
            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    SwitchMode(uid, comp, nextMode, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-mode", ("mode", nextMode)),
                Priority = 8,
            });

            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    StartMachine(uid, comp, args.User);
                },
                Text = Loc.GetString("laundry-machine-verb-start"),
                Priority = 7,
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
                    Priority = 10,
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
                    Priority = 10,
                });
            }

            args.Verbs.Add(new Verb()
            {
                Act = () =>
                {
                    StopMachine(uid, comp, args.User);
                },
                Text = Loc.GetString("laundry-machine-verb-stop"),
                Priority = 9,
            });
        }
    }

    private void OnShutdown(EntityUid uid, LaundryMachineComponent comp, ComponentShutdown args)
    {
        StopJitterMachine(uid);
        _ambientSound.SetAmbience(uid, false);
    }

    private void OnUnanchorAttempt(EntityUid uid, LaundryMachineComponent comp, UnanchorAttemptEvent args)
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
            return;

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
                StateJitterMachine(uid, LaundryMachineWashState.Washing);
                _appearance.SetData(uid, LaundryMachineVisuals.Light, true);
                break;
            case LaundryMachineState.Washing:
                ChangeMachineWashState(ent, LaundryMachineWashState.WashFill);
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
                StateJitterMachine(uid, newWashState);
                break;
            case LaundryMachineWashState.WashDraining:
            case LaundryMachineWashState.RinseDraining:
                _audio.PlayPvs(comp.FillSound, uid);
                comp.TimeRemaining = TimeSpan.FromSeconds(10);
                break;
            case LaundryMachineWashState.FastSpin:
                _audio.PlayPvs(comp.StartSpinSound, uid);
                _ambientSound.SetSound(uid, comp.FastSpinSound);
                _ambientSound.SetAmbience(uid, true);
                comp.TimeRemaining = TimeSpan.FromMinutes(2);
                StateJitterMachine(uid, newWashState);
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

    private void StateJitterMachine(EntityUid uid, LaundryMachineWashState washState)
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
                DoJitterMachine(uid, 4f, 16f);
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

    protected bool PauseMachine(EntityUid uid, LaundryMachineComponent comp, EntityUid? user = null, bool doPopup = false)
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

        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.Paused = false;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupPredicted(Loc.GetString("laundry-machine-resumed"), uid, user);

        switch (comp.LaundryState)
        {
            case LaundryMachineState.Drying:
                StateJitterMachine(uid, LaundryMachineWashState.Washing);
                _ambientSound.SetAmbience(uid, true);
                break;
            case LaundryMachineState.Washing:
                StateJitterMachine(uid, comp.WashState);

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
}
