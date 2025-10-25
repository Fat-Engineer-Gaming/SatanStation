using Content.Server.Speech.EntitySystems;

namespace Content.Server.Speech.Components;

/// <summary>
/// Forgetful Accent gives you silly stuff that makes you sound like you forget a lot (Dementia).
/// </summary>
[RegisterComponent]
[Access(typeof(ForgetfulAccentSystem))]
public sealed partial class ForgetfulAccentComponent : Component
{ }
