using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._hereelabs.Laundry;

/// <summary>
///     Washing machine. Ha
///     For now washing and drying are merged into one component, sorry not sorry
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LaundryMachineComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool CanWash = true;

    [DataField, AutoNetworkedField]
    public bool CanDry = true;

    [DataField, AutoNetworkedField]
    public WasherCycleSetting WasherCycle = WasherCycleSetting.Normal;

    [DataField, AutoNetworkedField]
    public DryerCycleSetting DryerCycle = DryerCycleSetting.Normal;

    [DataField, AutoNetworkedField]
    public LaundryMachineMode Mode = LaundryMachineMode.WashAndDry;

    [DataField, AutoNetworkedField]
    public int TimeSettingMinutes = 15;

    [AutoNetworkedField]
    public TimeSpan TimeRemaining;

    [AutoNetworkedField]
    public LaundryMachineState State = LaundryMachineState.Off;

    [AutoNetworkedField]
    public LaundryMachineWashState WashState = LaundryMachineWashState.Inactive;

    [AutoNetworkedField]
    public LaundryMachineWashState WashDelayNextState = LaundryMachineWashState.Inactive;
}

[Serializable, NetSerializable]
public enum WasherCycleSetting
{
    Normal,
    Delicate,
    RinseAndSpin,
}

[Serializable, NetSerializable]
public enum DryerCycleSetting
{
    Normal,
    Delicate,
    Timed,
}

[Serializable, NetSerializable]
public enum LaundryMachineMode
{
    Wash,
    Dry,
    WashAndDry,
}

[Serializable, NetSerializable]
public enum LaundryMachineState
{
    Off,
    Washing,
    Delay,
    Drying,
}

[Serializable, NetSerializable]
public enum LaundryMachineWashState
{
    Inactive,
    Delay,
    WaterFill,
    Washing,
    WashDraining,
    Rinsing,
    RinseDraining,
    FastSpin
}
