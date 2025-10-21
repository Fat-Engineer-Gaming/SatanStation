
using Content.Shared.Chemistry.Components;
using Content.Shared.Inventory;

namespace Content.Shared._hereelabs.Medical;

/// <summary>
/// Raised on an entity before they spawn a puddle on the floor from bleeding.
/// </summary>
[ByRefEvent]
public record struct BeforeVomitEvent(Solution vomitSolution) : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    public Solution Vomit = vomitSolution;

    public bool Cancelled = false;

    public bool SpawnPuddle = true;
}
