using System;
using System.Linq;
using System.Reflection;
using Content.Server.Composting;
using Content.Shared.Composting;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Moq;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.UnitTesting.Server;

namespace Content.Tests.Server.Civ14;

[TestFixture]
public sealed class CompostingInteractionTest
{
    private EntityManager _entities = default!;
    private CompostingSystem _system = default!;
    private Mock<SharedPopupSystem> _popup = default!;
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
            .RegisterPrototypes(prototypes => prototypes.LoadString(@"
- type: entity
  id: CompostTestOutput
"))
            .InitializeInstance();
        _entities = simulation.Resolve<EntityManager>();
    }

    [SetUp]
    public void SetupInteraction()
    {
        _user = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        _bin = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        _system = new CompostingSystem();
        _popup = new Mock<SharedPopupSystem>();
        // Use the actual empty-hand and pickup checks, without initializing unrelated systems.
        var hands = new Mock<SharedHandsSystem> { CallBase = true }.Object;
        Inject(hands, typeof(EntitySystem), "EntityManager", _entities);
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
            var slot = containerSystem.EnsureContainer<ContainerSlot>(_user, "test");
            var held = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(containerSystem.Insert(held, slot), Is.True);
            var hands = _entities.AddComponent<HandsComponent>(_user);
#pragma warning disable RA0002 // Construct an occupied hand without initializing the game hands system.
            hands.ActiveHand = new Hand("test", HandLocation.Right, slot);
#pragma warning restore RA0002
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
        var hands = _entities.AddComponent<HandsComponent>(_user);
#pragma warning disable RA0002 // Construct an empty hand without initializing the game hands system.
        hands.ActiveHand = new Hand("test", HandLocation.Right);
#pragma warning restore RA0002
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

    private EntityUid[] Outputs()
    {
        return _entities.GetEntities()
            .Where(uid => _entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CompostTestOutput")
            .ToArray();
    }
}
