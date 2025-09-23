using Content.Shared.Verbs;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared._hereelabs.Laundry;
using Robust.Shared.Audio.Systems;

namespace Content.Server._hereelabs.Laundry;

public sealed class LaundrySystem : SharedLaundrySystem
{
    private const float UPDATE_TIMER = 1f;
    private readonly TimeSpan _zeroTimeSpan = TimeSpan.FromSeconds(0);
    private readonly TimeSpan _oneTimeSpan = TimeSpan.FromSeconds(1);

    private float _timer;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timer += frameTime;
        if (_timer < UPDATE_TIMER)
            return;

        var query = EntityQueryEnumerator<LaundryMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
            UpdateComponent((uid, machine), UPDATE_TIMER);

        _timer -= UPDATE_TIMER;
    }
    private void UpdateComponent(Entity<LaundryMachineComponent> ent, float deltaTime)
    {
        var comp = ent.Comp;

        switch (comp.State)
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

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.Washing);
                }
                break;
            case LaundryMachineWashState.Washing:
                // do washing behavior

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.WashDraining);
                }
                break;
            case LaundryMachineWashState.WashDraining:
                // drain solution `drum` onto a puddle on the ground

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.RinseFill);
                }
                break;
            case LaundryMachineWashState.RinseFill:
                // split solution `tank` into `drum`

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.Rinsing);
                }
                break;
            case LaundryMachineWashState.Rinsing:
                // do washing behavior

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    comp.TimeRemaining = _oneTimeSpan;
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.RinseDraining);
                }
                break;
            case LaundryMachineWashState.RinseDraining:
                // drain solution `drum` onto a puddle on the ground

                comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
                if (comp.TimeRemaining <= _zeroTimeSpan)
                {
                    ChangeMachineWashState(ent, LaundryMachineWashState.Delay, LaundryMachineWashState.FastSpin);
                }
                break;
            case LaundryMachineWashState.FastSpin:
                // do fast-spin behavior

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

        comp.TimeRemaining -= TimeSpan.FromSeconds(deltaTime);
        if (comp.TimeRemaining <= _zeroTimeSpan)
            StopMachine(uid, comp);
    }
}
