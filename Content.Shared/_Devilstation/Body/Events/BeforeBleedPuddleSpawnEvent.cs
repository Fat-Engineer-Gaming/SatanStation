
using Content.Shared.Chemistry.Components;
using Content.Shared.Inventory;

namespace Content.Shared._Devilstation.Body.Events;

/// <summary>
/// Raised on an entity before they spawn a puddle on the floor from bleeding.
/// </summary>
[ByRefEvent]
public record struct BeforeBleedPuddleSpawnEvent(Solution tempSolution, Entity<SolutionComponent>? tempSoln) : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    public Solution BleedSolution = tempSolution;

    public Entity<SolutionComponent>? BleedSolutionEntity = tempSoln;

    public bool Cancelled = false;
}
