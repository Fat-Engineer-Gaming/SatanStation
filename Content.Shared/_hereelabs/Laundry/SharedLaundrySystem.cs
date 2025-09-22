using Content.Shared.Verbs;
using Content.Shared.Examine;
using Content.Shared.Popups;

namespace Content.Shared._hereelabs.Laundry;

public sealed partial class SharedLaundrySystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LaundryMachineComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<LaundryMachineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        LaundryMachineComponent comp = ent.Comp;

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
}
