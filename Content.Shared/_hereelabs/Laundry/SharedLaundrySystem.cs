using Content.Shared.Verbs;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._hereelabs.Laundry;

public abstract class SharedLaundrySystem : EntitySystem
{

    [Dependency] private readonly SharedJitteringSystem _jittering = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    private readonly int _modeCount = Enum.GetValues<LaundryMachineMode>().Length;
    private readonly int _washerCycleCount = Enum.GetValues<WasherCycleSetting>().Length;
    private readonly int _dryerCycleCount = Enum.GetValues<DryerCycleSetting>().Length;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LaundryMachineComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<LaundryMachineComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);

        _sawmill = _logManager.GetSawmill("laundry");
    }
    private void OnExamined(Entity<LaundryMachineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var comp = ent.Comp;

        if (comp.CanWash)
            args.PushMarkup(Loc.GetString("laundry-machine-examined-wash-cycle", ("cycle", comp.WasherCycle)));

        if (comp.CanDry)
        {
            args.PushMarkup(Loc.GetString("laundry-machine-examined-dry-cycle", ("cycle", comp.DryerCycle)));
            if (comp.State != LaundryMachineState.Drying)
                args.PushMarkup(Loc.GetString("laundry-machine-examined-timer", ("time", comp.TimeSettingMinutes)));
            else
                args.PushMarkup(Loc.GetString("laundry-machine-examined-time-remaining", ("time", (int)Math.Ceiling(comp.TimeRemaining.TotalMinutes))));
        }

        args.PushMarkup(Loc.GetString("laundry-machine-examined-state", ("state", comp.State)));
    }

    private void OnGetAltVerbs(EntityUid uid, LaundryMachineComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (comp.State == LaundryMachineState.Off)
        {
            var nextWashCycleIndex = ((int)comp.WasherCycle + 1) % _washerCycleCount;
            var nextWashCycle = (WasherCycleSetting)nextWashCycleIndex;
            var nextDryCycleIndex = ((int)comp.DryerCycle + 1) % _dryerCycleCount;
            var nextDryCycle = (DryerCycleSetting)nextDryCycleIndex;
            var nextModeIndex = ((int)comp.Mode + 1) % _modeCount;
            var nextMode = (LaundryMachineMode)nextModeIndex;

            args.Verbs.Add(new AlternativeVerb()
            {
                Act = () =>
                {
                    SwitchWashCycle(uid, comp, nextWashCycle, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-wash-cycle", ("cycle", nextWashCycle)),
            });
            args.Verbs.Add(new AlternativeVerb()
            {
                Act = () =>
                {
                    SwitchDryCycle(uid, comp, nextDryCycle, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-dry-cycle", ("dry", nextDryCycle)),
            });
            args.Verbs.Add(new AlternativeVerb()
            {
                Act = () =>
                {
                    SwitchMode(uid, comp, nextMode, args.User);
                },
                Text = Loc.GetString("laundry-machine-switch-mode", ("mode", nextMode)),
            });

            args.Verbs.Add(new AlternativeVerb()
            {
                Act = () =>
                {
                    StartMachine(uid, comp, args.User);
                },
                Text = Loc.GetString("laundry-machine-verb-start"),
            });
        }
        else
        {
            args.Verbs.Add(new AlternativeVerb()
            {
                Act = () =>
                {
                    StopMachine(uid, comp, args.User);
                },
                Text = Loc.GetString("laundry-machine-verb-stop"),
            });
        }
    }

    protected void ChangeMachineState(Entity<LaundryMachineComponent> ent, LaundryMachineState newState)
    {
        var comp = ent.Comp;
        if (comp.State == newState)
            return;

        comp.State = newState;

        var uid = ent.Owner;

        switch (newState)
        {
            case LaundryMachineState.Off:
                break;
            case LaundryMachineState.Delay:
                comp.TimeRemaining = TimeSpan.FromSeconds(10);
                break;
            case LaundryMachineState.Drying:
                ChangeMachineWashState(ent, LaundryMachineWashState.Inactive);
                break;
            case LaundryMachineState.Washing:
                ChangeMachineWashState(ent, LaundryMachineWashState.WashFill);
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
                _ambientSound.SetAmbience(uid, false);
                break;
            case LaundryMachineWashState.Washing:
            case LaundryMachineWashState.Rinsing:
            case LaundryMachineWashState.FastSpin:
                _ambientSound.SetAmbience(uid, false);
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
                _jittering.DoJitter(uid, comp.TimeRemaining, true, 10f, 8f);
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
                _jittering.DoJitter(uid, comp.TimeRemaining, true, 10f, 16f);
                break;
        }
    }

    private void SwitchWashCycle(EntityUid uid, LaundryMachineComponent comp, WasherCycleSetting cycle, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.WasherCycle = cycle;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupClient(Loc.GetString("laundry-machine-switched-wash-cycle", ("cycle", cycle)), ent, user);
        Dirty(ent);
    }

    private void SwitchDryCycle(EntityUid uid, LaundryMachineComponent comp, DryerCycleSetting cycle, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.DryerCycle = cycle;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupClient(Loc.GetString("laundry-machine-switched-dry-cycle", ("cycle", cycle)), ent, user);
        Dirty(ent);
    }

    private void SwitchMode(EntityUid uid, LaundryMachineComponent comp, LaundryMachineMode mode, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        comp.Mode = mode;
        _audio.PlayPredicted(comp.SwitchSound, ent, user);
        _popup.PopupClient(Loc.GetString("laundry-machine-switched-mode", ("mode", mode)), ent, user);
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

        Dirty(ent);
    }

    protected void StopMachine(EntityUid uid, LaundryMachineComponent comp, EntityUid? user = null)
    {
        Entity<LaundryMachineComponent> ent = (uid, comp);

        ChangeMachineState(ent, LaundryMachineState.Off);
        comp.WashState = LaundryMachineWashState.Inactive;
        comp.WashDelayNextState = null;
        _audio.PlayPredicted(comp.StartSound, ent, user);

        Dirty(ent);
    }
}
