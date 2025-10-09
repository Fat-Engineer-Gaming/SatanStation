using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chat.Prototypes;
using Content.Server._Devilstation.Body.Systems;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Devilstation.Body.Components
{
    [RegisterComponent, Access(typeof(LaniusSystem)), AutoGenerateComponentPause]
    public sealed partial class LaniusComponent : Component
    {
        /// <summary>
        ///     Volume of our breath in liters
        /// </summary>
        [DataField]
        public float BreathVolume = Atmospherics.BreathVolume;

        /// <summary>
        ///     The next time that this body will inhale or exhale.
        /// </summary>
        [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
        public TimeSpan NextUpdate;

        /// <summary>
        ///     The interval between updates. Each update is either inhale or exhale,
        ///     so a full cycle takes twice as long.
        /// </summary>
        [DataField]
        public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Multiplier applied to <see cref="UpdateInterval"/> for adjusting based on metabolic rate multiplier.
        /// </summary>
        [DataField]
        public float UpdateIntervalMultiplier = 1f;

        /// <summary>
        /// Adjusted update interval based off of the multiplier value.
        /// </summary>
        [ViewVariables]
        public TimeSpan AdjustedUpdateInterval => UpdateInterval * UpdateIntervalMultiplier;
    }
}
