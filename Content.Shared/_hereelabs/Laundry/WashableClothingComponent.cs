using Content.Shared.Atmos;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

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

    [DataField, AutoNetworkedField]
    public float WetnessScale = 1f;

    [DataField, AutoNetworkedField]
    public FixedPoint2 DripVolume = 10f;

    [DataField, AutoNetworkedField]
    public float DryTemperature = Atmospherics.T0C + 60f;
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
