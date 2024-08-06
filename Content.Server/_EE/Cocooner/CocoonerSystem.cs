using Content.Shared._EE.Cocooner;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Stunnable;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Server.Buckle.Systems;
using Content.Server.Popups;
using Content.Server.DoAfter;
using Content.Shared.Body.Components;
using Content.Server._EE.Vampiric;
using Content.Server.Speech.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Containers;
using Robust.Shared.Utility;
using Robust.Server.Console;

namespace Content.Server._EE.Cocooner
{
    public sealed class CocoonerSystem : EntitySystem
    {
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly DoAfterSystem _doAfter = default!;
        [Dependency] private readonly BuckleSystem _buckleSystem = default!;
        [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
        [Dependency] private readonly BlindableSystem _blindableSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;

        [Dependency] private readonly IServerConsoleHost _host = default!;
        [Dependency] private readonly BloodSuckerSystem _bloodSuckerSystem = default!;
        [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

        private const string BodySlot = "body_slot";

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<CocoonerComponent, GetVerbsEvent<InnateVerb>>(AddCocoonVerb);

            SubscribeLocalEvent<CocoonComponent, EntInsertedIntoContainerMessage>(OnCocEntInserted);
            SubscribeLocalEvent<CocoonComponent, EntRemovedFromContainerMessage>(OnCocEntRemoved);
            SubscribeLocalEvent<CocoonComponent, DamageChangedEvent>(OnDamageChanged);
            SubscribeLocalEvent<CocoonComponent, GetVerbsEvent<AlternativeVerb>>(AddSuckVerb);
            SubscribeLocalEvent<CocoonerComponent, CocoonerCocoonDoAfterEvent>(OnCocoonDoAfter);
        }

        private void AddCocoonVerb(EntityUid uid, CocoonerComponent component, GetVerbsEvent<InnateVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            if (args.Target == uid)
                return;

            if (!TryComp<BloodstreamComponent>(args.Target, out var bloodstream))
                return;

            if (bloodstream.BloodReagent != component.WebBloodReagent)
                return;

            InnateVerb verb = new()
            {
                Act = () =>
                {
                    StartCocooning(uid, component, args.Target);
                },
                Text = Loc.GetString("cocoon"),
                Priority = 2
            };
            args.Verbs.Add(verb);
        }

        private void OnCocEntInserted(EntityUid uid, CocoonComponent component, EntInsertedIntoContainerMessage args)
        {
            _blindableSystem.UpdateIsBlind(args.Entity);
            EnsureComp<StunnedComponent>(args.Entity);

            if (TryComp<ReplacementAccentComponent>(args.Entity, out var currentAccent))
            {
                component.WasReplacementAccent = true;
                component.OldAccent = currentAccent.Accent;
                currentAccent.Accent = "mumble";
            } else
            {
                component.WasReplacementAccent = false;
                var replacement = EnsureComp<ReplacementAccentComponent>(args.Entity);
                replacement.Accent = "mumble";
            }
        }

        private void OnCocEntRemoved(EntityUid uid, CocoonComponent component, EntRemovedFromContainerMessage args)
        {
            if (component.WasReplacementAccent && TryComp<ReplacementAccentComponent>(args.Entity, out var replacement))
            {
                replacement.Accent = component.OldAccent;
            } else
            {
                RemComp<ReplacementAccentComponent>(args.Entity);
            }

            RemComp<StunnedComponent>(args.Entity);
            _blindableSystem.UpdateIsBlind(args.Entity);
        }

        private void OnDamageChanged(EntityUid uid, CocoonComponent component, DamageChangedEvent args)
        {
            if (!args.DamageIncreased)
                return;

            if (args.DamageDelta == null)
                return;

            var body = _itemSlots.GetItemOrNull(uid, BodySlot);

            if (body == null)
                return;

            var damage = args.DamageDelta * component.DamagePassthrough;
            _damageableSystem.TryChangeDamage(body, damage);
        }

        private void AddSuckVerb(EntityUid uid, CocoonComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            if (!TryComp<BloodSuckerComponent>(args.User, out var sucker))
                return;

            if (!sucker.WebRequired)
                return;

            var victim = _itemSlots.GetItemOrNull(uid, BodySlot);

            if (victim == null)
                return;

            if (!TryComp<BloodstreamComponent>(victim, out var stream))
                return;

            AlternativeVerb verb = new()
            {
                Act = () =>
                {
                    _bloodSuckerSystem.StartSuckDoAfter(args.User, victim.Value, sucker, stream, false); // start doafter
                },
                Text = Loc.GetString("action-name-suck-blood"),
                Icon = new SpriteSpecifier.Texture(new ("/Textures/Nyanotrasen/Icons/verbiconfangs.png")),
                Priority = 2
            };
            args.Verbs.Add(verb);
        }

        private void OnEntRemoved(EntityUid uid, WebComponent web, EntRemovedFromContainerMessage args)
        {
            if (!TryComp<StrapComponent>(uid, out var strap))
                return;

            if (HasComp<CocoonerComponent>(args.Entity))
                _buckleSystem.StrapSetEnabled(uid, false, strap);
        }

        private void StartCocooning(EntityUid uid, CocoonerComponent component, EntityUid target)
        {
            _popupSystem.PopupEntity(Loc.GetString("cocoon-start-third-person", ("target", Identity.Entity(target, EntityManager)), ("spider", Identity.Entity(uid, EntityManager))), uid,
                Shared.Popups.PopupType.MediumCaution);

            _popupSystem.PopupEntity(Loc.GetString("cocoon-start-second-person", ("target", Identity.Entity(target, EntityManager))), uid, uid, Shared.Popups.PopupType.Medium);

            var delay = component.CocoonDelay;

            if (HasComp<KnockedDownComponent>(target))
                delay *= component.CocoonKnockdownMultiplier;

            // Is it good practice to use empty data just to disambiguate doafters
            // Who knows, there's no docs!
            var ev = new CocoonerCocoonDoAfterEvent();

            var args = new DoAfterArgs(EntityManager, uid, delay, ev, uid, target: target)
            {
                BreakOnMove = true
            };

            _doAfter.TryStartDoAfter(args);
        }

        private void OnCocoonDoAfter(EntityUid uid, CocoonerComponent component, CocoonerCocoonDoAfterEvent args)
        {
            if (args.Handled || args.Cancelled || args.Args.Target == null)
                return;

            var spawnProto = HasComp<HumanoidAppearanceComponent>(args.Args.Target) ? "CocoonedHumanoid" : "CocoonSmall";
            Transform(args.Args.Target.Value).AttachToGridOrMap();
            var cocoon = Spawn(spawnProto, Transform(args.Args.Target.Value).Coordinates);

            if (!TryComp<ItemSlotsComponent>(cocoon, out var slots))
                return;

            // todo: our species should use scale visuals probably...
            // TODO: We need a client-accessible notion of scale influence here.
            /* if (spawnProto == "CocoonedHumanoid" && TryComp<SpriteComponent>(args.Args.Target.Value, out var sprite)) */
            /* { */
            /*     // why the fuck is this only available as a console command. */
            /*     _host.ExecuteCommand(null, "scale " + cocoon + " " + sprite.Scale.Y); */
            if (TryComp<PhysicsComponent>(args.Args.Target.Value, out var physics))
            {
                var scale = Math.Clamp(1 / (35 / physics.FixturesMass), 0.35, 2.5);
                _host.ExecuteCommand(null, "scale " + cocoon + " " + scale);
            }
            _itemSlots.SetLock(cocoon, BodySlot, false, slots);
            _itemSlots.TryInsert(cocoon, BodySlot, args.Args.Target.Value, args.Args.User);
            _itemSlots.SetLock(cocoon, BodySlot, true, slots);

            var impact = (spawnProto == "CocoonedHumanoid") ? LogImpact.High : LogImpact.Medium;

            _adminLogger.Add(LogType.Action, impact, $"{ToPrettyString(args.Args.User):player} cocooned {ToPrettyString(args.Args.Target.Value):target}");
            args.Handled = true;
        }
    }
}
