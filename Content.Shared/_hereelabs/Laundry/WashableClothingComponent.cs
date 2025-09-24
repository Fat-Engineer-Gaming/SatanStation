using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

namespace Content.Shared._hereelabs.Laundry;

/// <summary>
///     Washable clothes!!!
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WashableClothingComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Solution = "soaked";

    [DataField("capacity"), AutoNetworkedField]
    public float SolutionCapacity = 20f;

    [DataField, AutoNetworkedField]
    public float WashFactor = 1f;
}

[Serializable, NetSerializable]
public enum ClothingWetness
{
    Dry,
    Damp,
    Moist,
    Wet,
    VeryWet,
    Drenched
}
