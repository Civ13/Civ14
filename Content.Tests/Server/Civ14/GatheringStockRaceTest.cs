using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Server.Rocks;
using Content.Server.TreeBranch;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Rocks;
using Content.Shared.TreeBranch;
using Moq;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Server;

namespace Content.Tests.Server.Civ14;

[TestFixture]
public sealed class GatheringStockRaceTest
{
    private EntityManager _entities = default!;

    [OneTimeSetUp]
    public void SetupSimulation()
    {
        // Core ECS only. No server process, client, game loop, or listening socket.
        var simulation = RobustServerSimulation.NewSimulation()
            .RegisterComponents(factory => factory.RegisterClass<HandsComponent>())
            .RegisterPrototypes(prototypes => prototypes.LoadString(@"
- type: entity
  id: LeafedStick
- type: entity
  id: Flint
"))
            .InitializeInstance();
        _entities = simulation.Resolve<EntityManager>();
    }

    [TearDown]
    public void CleanupEntities()
    {
        _entities.FlushEntities();
    }

    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void CompetingCompletionsYieldOnlyAvailableStock(bool flint, int stock)
    {
        EntitySystem system = flint ? new FlintRockSystem() : new TreeBranchesSystem();
        Component component = flint ? new FlintRockComponent() : new TreeBranchesComponent();
        var count = component.GetType().GetField(flint ? "CurrentFlints" : "CurrentBranches")!;
        count.SetValue(component, stock);
        var resource = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
        var hands = new Mock<SharedHandsSystem> { CallBase = true }.Object;
        Inject(hands, typeof(EntitySystem), "EntityManager", _entities);
        Inject(system, typeof(EntitySystem), "EntityManager", _entities);
        Inject(system, system.GetType(), "_hands", hands);
        Inject(system, system.GetType(), "_popup", new Mock<SharedPopupSystem>().Object);

        // Each player has a usable but occupied hand. Collected items should land on
        // the floor, exercising real entity spawning without presentation dependencies.
        var completions = new List<DoAfterEvent>();
        for (var i = 0; i <= stock; i++)
        {
            var user = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var slot = _entities.System<SharedContainerSystem>().EnsureContainer<ContainerSlot>(user, "test");
            var held = _entities.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(_entities.System<SharedContainerSystem>().Insert(held, slot), Is.True);
            var handComponent = _entities.AddComponent<HandsComponent>(user);
#pragma warning disable RA0002 // Construct the collector's hand without initializing unrelated game systems.
            handComponent.ActiveHand = new Hand("test", HandLocation.Right, slot);
#pragma warning restore RA0002
            DoAfterEvent ev = flint ? new CollectFlintDoAfterEvent() : new CollectBranchDoAfterEvent();
            var args = new DoAfterArgs(_entities, user, 0f, ev, resource) { NeedHand = true };
            ev.DoAfter = new DoAfter(0, args, default);
            completions.Add(ev);
        }

        // All collectors began with stock available; the server handles their queued
        // completions sequentially. The final collector must observe depleted stock.
        var callback = system.GetType().GetMethod("OnDoAfter", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var ev in completions)
        {
            callback.Invoke(system, new[] { (object) resource, component, ev });
            Assert.That(ev.Handled, Is.True);
        }

        // Redelivering completed events must not consume stock or yield another item.
        foreach (var ev in completions)
            callback.Invoke(system, new[] { (object) resource, component, ev });

        var output = flint ? "Flint" : "LeafedStick";
        var yielded = _entities.GetEntities()
            .Count(uid => _entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == output);
        Assert.Multiple(() =>
        {
            Assert.That(count.GetValue(component), Is.EqualTo(0));
            Assert.That(yielded, Is.EqualTo(stock));
        });
    }

    private static void Inject(object target, System.Type owner, string field, object value)
    {
        owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
}
