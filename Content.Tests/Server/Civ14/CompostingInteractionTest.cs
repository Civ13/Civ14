using System;
using System.Linq;
using System.Reflection;
using Content.Server.Composting;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Composting;
using Content.Shared.Database;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Moq;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;
using Robust.UnitTesting.Server;

namespace Content.Tests.Server.Civ14;

[TestFixture]
public sealed class CompostingInteractionTest
{
    private EntityManager _entities = default!;
    private CompostingSystem _system = default!;
    private Mock<SharedPopupSystem> _popup = default!;
    private Mock<SharedHandsSystem> _hands = default!;
    private RejectCompostInsertionSystem _insertion = default!;
    private EntityUid _user;
    private EntityUid _bin;

    [OneTimeSetUp]
    public void SetupSimulation()
    {
        // Core ECS simulation only: no game loop, client, listening socket, or server process.
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterComponents(factory =>
            {
                factory.RegisterClass<TagComponent>();
                factory.RegisterClass<HandsComponent>();
                factory.RegisterClass<Content.Shared.Item.ItemComponent>();
            })
            .RegisterEntitySystems(systems => systems.LoadExtraSystemType<RejectCompostInsertionSystem>())
            .RegisterPrototypes(prototypes => prototypes.LoadString(@"
- type: entity
  id: CompostTestOutput
- type: entity
  id: CompostTestPickupOutput
"))
            .InitializeInstance();
        _entities = simulation.Resolve<EntityManager>();
        _insertion = _entities.System<RejectCompostInsertionSystem>();
        // The core simulator only registers serialization for engine components.
        // Attach the fixture's real ItemComponent before the production Spawn call returns.
        _entities.EntityInitialized += entity =>
        {
            if (entity.Comp.EntityPrototype?.ID == "CompostTestPickupOutput")
                _entities.AddComponent<Content.Shared.Item.ItemComponent>(entity.Owner);
        };
    }

    [SetUp]
    public void SetupInteraction()
    {
        _user = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        _bin = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        _system = new CompostingSystem();
        _popup = new Mock<SharedPopupSystem>();
        _insertion.RejectForUser = null;
        _insertion.Attempts = 0;
        // Use the actual empty-hand and pickup checks, without initializing unrelated systems.
        _hands = new Mock<SharedHandsSystem> { CallBase = true };
        var hands = _hands.Object;
        // Insertion rejection deliberately exercises DoPickup's error path.
        typeof(EntitySystem).GetProperty(nameof(EntitySystem.Log))!.SetValue(hands, new Mock<ISawmill>().Object);
        Inject(hands, typeof(EntitySystem), "EntityManager", _entities);
        Inject(hands, typeof(SharedHandsSystem), "ContainerSystem", _entities.System<SharedContainerSystem>());
        Inject(hands, typeof(SharedHandsSystem), "TransformSystem", _entities.System<SharedTransformSystem>());
        var actionBlocker = new ActionBlockerSystem();
        Inject(actionBlocker, typeof(EntitySystem), "EntityManager", _entities);
        Inject(hands, typeof(SharedHandsSystem), "_actionBlocker", actionBlocker);
        Inject(hands, typeof(SharedHandsSystem), "_storage", new Mock<SharedStorageSystem>().Object);
        Inject(hands, typeof(SharedHandsSystem), "_adminLogger", new TestAdminLogManager());
        Inject(_system, typeof(EntitySystem), "EntityManager", _entities);
        Inject(_system, typeof(CompostingSystem), "_hands", hands);
        Inject(_system, typeof(CompostingSystem), "_popup", _popup.Object);
        Inject(_system, typeof(CompostingSystem), "_gameTiming", new Mock<IGameTiming>().Object);
    }

    [TearDown]
    public void CleanupEntities()
    {
        _entities.FlushEntities();
    }

    private static void Inject(object target, Type owner, string field, object value)
    {
        owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private void Interact(string callback, CompostingComponent component, object ev)
    {
        typeof(CompostingSystem).GetMethod(callback, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_system, new[] { (object) _bin, component, ev });
    }

    private ContainerSlot AddHand(string name = "test", bool active = true, EntityUid? user = null)
    {
        var owner = user ?? _user;
        var slot = _entities.System<SharedContainerSystem>().EnsureContainer<ContainerSlot>(owner, name);
        var hands = _entities.EnsureComponent<HandsComponent>(owner);
        var hand = new Hand(name, HandLocation.Right, slot);
#pragma warning disable RA0002 // Construct canonical hand state without initializing unrelated game systems.
        hands.Hands.Add(name, hand);
        hands.SortedHands.Add(name);
        if (active)
            hands.ActiveHand = hand;
#pragma warning restore RA0002
        return slot;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RejectedInputIsNotConsumed(bool hasTags)
    {
        var input = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        if (hasTags)
            _entities.AddComponent<TagComponent>(input);
        var component = new CompostingComponent { Whitelist = new() { "Fruit" } };
        var ev = new InteractUsingEvent(_user, input, _bin, default);

        Interact("OnInteractUsing", component, ev);

        Assert.Multiple(() =>
        {
            Assert.That(component.CompostingItems, Is.Empty);
            Assert.That(_entities.IsQueuedForDeletion(input), Is.False);
            Assert.That(ev.Handled, Is.False);
        });
        _popup.Verify(p => p.PopupEntity(It.IsAny<string>(), _bin, _user, PopupType.Small), Times.Once);
    }

    [Test]
    public void AllowedInputStillStartsComposting()
    {
        var input = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
#pragma warning disable RA0002 // Construct a tagged input without initializing the game tag system.
        _entities.AddComponent<TagComponent>(input).Tags.Add("Fruit");
#pragma warning restore RA0002
        var component = new CompostingComponent { Whitelist = new() { "Fruit" } };
        var ev = new InteractUsingEvent(_user, input, _bin, default);

        Interact("OnInteractUsing", component, ev);

        Assert.Multiple(() =>
        {
            Assert.That(component.CompostingItems.ContainsKey(input), Is.True);
            Assert.That(_entities.IsQueuedForDeletion(input), Is.True);
            Assert.That(ev.Handled, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoAvailableHandDoesNotSpawnOrConsumeOutput(bool occupiedHand)
    {
        if (occupiedHand)
        {
            var containerSystem = _entities.System<SharedContainerSystem>();
            var slot = AddHand();
            var held = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(containerSystem.Insert(held, slot), Is.True);
        }
        var component = new CompostingComponent { ReadyCompost = 1, OutputPrototype = "CompostTestOutput" };
        var ev = new InteractHandEvent(_user, _bin);

        Interact("OnInteractHand", component, ev);

        Assert.Multiple(() =>
        {
            Assert.That(component.ReadyCompost, Is.EqualTo(1));
            Assert.That(Outputs(), Is.Empty);
            Assert.That(ev.Handled, Is.True);
        });
    }

    [Test]
    public void FailedPickupDeletesOnlyNewOutputAndPreservesStock()
    {
        AddHand();
        // The configured output lacks ItemComponent, so the real pickup checks reject it
        // even though the user has an empty hand. It must not become free floor output.
        var existingOutput = _entities.SpawnEntity("CompostTestOutput", MapCoordinates.Nullspace);
        var component = new CompostingComponent { ReadyCompost = 1, OutputPrototype = "CompostTestOutput" };
        var ev = new InteractHandEvent(_user, _bin);

        Interact("OnInteractHand", component, ev);

        var output = Outputs().Single(uid => uid != existingOutput);
        Assert.Multiple(() =>
        {
            Assert.That(_entities.IsQueuedForDeletion(output), Is.True);
            Assert.That(_entities.IsQueuedForDeletion(existingOutput), Is.False);
            Assert.That(_entities.IsQueuedForDeletion(_user), Is.False);
            Assert.That(_entities.IsQueuedForDeletion(_bin), Is.False);
            Assert.That(component.ReadyCompost, Is.EqualTo(1));
            Assert.That(ev.Handled, Is.True);
        });
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public void EmptyOrHandledInteractionDoesNothing(int stock, bool handled)
    {
        var component = new CompostingComponent { ReadyCompost = stock, OutputPrototype = "CompostTestOutput" };
        var ev = new InteractHandEvent(_user, _bin) { Handled = handled };

        Interact("OnInteractHand", component, ev);

        Assert.Multiple(() =>
        {
            Assert.That(component.ReadyCompost, Is.EqualTo(stock));
            Assert.That(Outputs(), Is.Empty);
            Assert.That(ev.Handled, Is.EqualTo(handled));
        });
    }

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void SuccessfulPickupTransfersOneOutputAndConsumesOneUnit(int stock, bool activeHandOccupied)
    {
        var slot = AddHand();
        if (activeHandOccupied)
        {
            var held = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(_entities.System<SharedContainerSystem>().Insert(held, slot), Is.True);
            slot = AddHand("other", active: false);
        }
        var component = new CompostingComponent { ReadyCompost = stock, OutputPrototype = "CompostTestPickupOutput" };
        var ev = new InteractHandEvent(_user, _bin);

        // Exercise the real pickup checks and container transfer, including the successful branch.
        Interact("OnInteractHand", component, ev);
        // Redelivery must neither consume another unit nor create another output.
        Interact("OnInteractHand", component, ev);

        var output = Outputs().Single();
        Assert.Multiple(() =>
        {
            Assert.That(slot.ContainedEntity, Is.EqualTo(output));
            Assert.That(_entities.IsQueuedForDeletion(output), Is.False);
            Assert.That(component.ReadyCompost, Is.EqualTo(stock - 1));
            Assert.That(ev.Handled, Is.True);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void RejectedInsertionAfterEligibilityDoesNotDebitOrReportSuccess(int stock)
    {
        var slot = AddHand();
        var existingOutput = _entities.SpawnEntity("CompostTestOutput", MapCoordinates.Nullspace);
        var component = new CompostingComponent { ReadyCompost = stock, OutputPrototype = "CompostTestPickupOutput" };
        var ev = new InteractHandEvent(_user, _bin);
        _insertion.RejectForUser = _user;

        Interact("OnInteractHand", component, ev);
        Interact("OnInteractHand", component, ev);

        var output = Outputs().Single(uid => uid != existingOutput);
        Assert.Multiple(() =>
        {
            Assert.That(_insertion.Attempts, Is.EqualTo(2), "Eligibility succeeds; the actual insertion is rejected.");
            Assert.That(slot.ContainedEntity, Is.Null);
            Assert.That(component.ReadyCompost, Is.EqualTo(stock));
            Assert.That(_entities.IsQueuedForDeletion(output), Is.True);
            Assert.That(_entities.IsQueuedForDeletion(existingOutput), Is.False);
            Assert.That(_entities.IsQueuedForDeletion(_user), Is.False);
            Assert.That(_entities.IsQueuedForDeletion(_bin), Is.False);
            Assert.That(ev.Handled, Is.True);
        });
        _hands.Verify(h => h.DoPickup(_user, It.IsAny<Hand>(), output, It.IsAny<HandsComponent>(), true), Times.Once);
        _popup.Verify(p => p.PopupEntity($"You collect a unit of {component.OutputName}.", _bin, _user, PopupType.Small), Times.Never);
        _popup.Verify(p => p.PopupEntity("Your hands are full.", _bin, _user, PopupType.Small), Times.Once);
    }

    [Test]
    public void CompetingCollectorsReceiveOnlyTheLastReadyUnit()
    {
        var firstHand = AddHand();
        var otherUser = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        var otherHand = AddHand(user: otherUser);
        var component = new CompostingComponent { ReadyCompost = 1, OutputPrototype = "CompostTestPickupOutput" };
        var first = new InteractHandEvent(_user, _bin);
        var second = new InteractHandEvent(otherUser, _bin);

        Interact("OnInteractHand", component, first);
        Interact("OnInteractHand", component, second);

        var output = Outputs().Single();
        Assert.Multiple(() =>
        {
            Assert.That(firstHand.ContainedEntity, Is.EqualTo(output));
            Assert.That(otherHand.ContainedEntity, Is.Null);
            Assert.That(component.ReadyCompost, Is.EqualTo(0));
            Assert.That(first.Handled, Is.True);
            Assert.That(second.Handled, Is.False);
        });
    }

    private EntityUid[] Outputs()
    {
        return _entities.GetEntities()
            .Where(uid => _entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID is "CompostTestOutput" or "CompostTestPickupOutput")
            .ToArray();
    }

    private sealed class TestAdminLogManager : ISharedAdminLogManager
    {
        public void Add(LogType type, LogImpact impact, ref LogStringHandler handler) { }
        public void Add(LogType type, ref LogStringHandler handler) { }
    }

    [Reflect(false)]
    public sealed class RejectCompostInsertionSystem : EntitySystem
    {
        public EntityUid? RejectForUser;
        public int Attempts;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<HandsComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        }

        private void OnInsert(EntityUid uid, HandsComponent component, ContainerIsInsertingAttemptEvent args)
        {
            if (uid != RejectForUser)
                return;
            if (++Attempts == 2)
                args.Cancel();
        }
    }
}
