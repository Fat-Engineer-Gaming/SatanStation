-laundry-machine-wash-cycle-name =
    { $cycle ->
        [normal] Normal
        [delicate] Delicate
        [rinseandspin] Rinse and Spin
       *[other] Unknown
    }

-laundry-machine-dry-cycle-name =
    { $cycle ->
        [normal] Normal
        [delicate] Delicate
        [timed] Timed
       *[other] Unknown
    }

-laundry-machine-state-name =
    { $state ->
        [washing] washing
        [delay] waiting to dry
        [drying] drying
       *[other] off
    }

-laundry-machine-mode-name =
    { $mode ->
        [wash] wash
        [dry] dry
        [washanddry] wash and dry
       *[other] unknown
    }

laundry-machine-examined-wash-cycle = The washing cycle is set to [color=white]{ -laundry-machine-wash-cycle-name(cycle: $cycle) }[/color].
laundry-machine-examined-dry-cycle = The drying cycle is set to [color=white]{ -laundry-machine-dry-cycle-name(cycle: $cycle) }[/color].
laundry-machine-examined-timer = The timer is set to [color=white]{$time}[/color] minutes.
laundry-machine-examined-time-remaining = It has [color=white]{$time}[/color] minutes remaining.
laundry-machine-examined-state = It is currently [color=white]{ -laundry-machine-state-name(state: $state) }[/color].
laundry-machine-examined-mode = It is currently set to [color=white]{ -laundry-machine-mode-name(mode: $mode) }[/color].

laundry-machine-switch-wash-cycle = Switch washer cycle to { -laundry-machine-wash-cycle-name(cycle: $cycle) }
laundry-machine-switched-wash-cycle = Switched to { -laundry-machine-wash-cycle-name(cycle: $cycle) }.

laundry-machine-switch-dry-cycle = Switch dryer cycle to { -laundry-machine-dry-cycle-name(cycle: $cycle) }
laundry-machine-switched-dry-cycle = Switched to { -laundry-machine-dry-cycle-name(cycle: $cycle) }.

laundry-machine-switch-mode = Switch mode to { -laundry-machine-mode-name(mode: $mode) }
laundry-machine-switched-mode = Switched to { -laundry-machine-mode-name(mode: $mode) }.

laundry-machine-verb-start = Start
laundry-machine-verb-pause = Pause
laundry-machine-verb-resume = Resume
laundry-machine-verb-stop = Stop

laundry-machine-started = Started
laundry-machine-start-must-close = {CAPITALIZE(THE($machine))} must be closed to start
laundry-machine-paused = Paused
laundry-machine-resumed = Resumed
laundry-machine-resume-must-close = {CAPITALIZE(THE($machine))} must be closed to resume
laundry-machine-stopped = Stopped
