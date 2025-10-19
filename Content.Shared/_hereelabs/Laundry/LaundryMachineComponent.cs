using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

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
    public int TimeSettingMinutes = 10;

    [DataField, AutoNetworkedField]
    public float TemperatureCelcius = 80f;

    [DataField("openChanceWhenUnlocked"), AutoNetworkedField]
    public float UnlockedOpenChance = 0.01f;

    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan TimeRemaining = TimeSpan.Zero;

    [AutoNetworkedField]
    public LaundryMachineState LaundryState = LaundryMachineState.Off;

    [AutoNetworkedField]
    public LaundryMachineWashState WashState = LaundryMachineWashState.Inactive;

    [AutoNetworkedField]
    public LaundryMachineWashState? WashDelayNextState = null;

    [AutoNetworkedField]
    public bool Paused = false;

    [DataField, AutoNetworkedField]
    public SoundSpecifier SwitchSound = new SoundPathSpecifier(
        "/Audio/Weapons/click.ogg",
        AudioParams.Default.WithVolume(-6)
    );

    [DataField, AutoNetworkedField]
    public SoundSpecifier StartSound = new SoundPathSpecifier(
        "/Audio/Weapons/click.ogg",
        AudioParams.Default.WithVolume(-6).WithPitchScale(0.9f)
    );

    [DataField, AutoNetworkedField]
    public SoundSpecifier FillSound = new SoundPathSpecifier("/Audio/_hereelabs/Machines/laundry_fillwater.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier(
        "/Audio/_hereelabs/Machines/laundry_spin.ogg",
        AudioParams.Default.WithLoop(true)
    );

    [DataField, AutoNetworkedField]
    public SoundSpecifier StartSpinSound = new SoundPathSpecifier("/Audio/_hereelabs/Machines/laundry_spinstart.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier FastSpinSound = new SoundPathSpecifier(
        "/Audio/_hereelabs/Machines/laundry_spinfast.ogg",
        AudioParams.Default.WithLoop(true)
    );

    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = default!;
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
    WashFill,
    Washing,
    WashDraining,
    RinseFill,
    Rinsing,
    RinseDraining,
    FastSpin
}
