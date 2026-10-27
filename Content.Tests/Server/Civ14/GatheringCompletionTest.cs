using System;
using System.Reflection;
using Content.Server.Rocks;
using Content.Server.TreeBranch;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Rocks;
using Content.Shared.TreeBranch;
using Moq;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Server.Civ14;

[TestFixture]
public sealed class GatheringCompletionTest
{
    // Invoke the completion callbacks without starting an engine or a server.
    // Leaving the entity manager uninjected also makes any attempted item spawn fail.
    private static (EntitySystem System, Component Component, DoAfterEvent Event, FieldInfo Count)
        CreateCompletion(bool flint, int remaining)
    {
        EntitySystem system = flint ? new FlintRockSystem() : new TreeBranchesSystem();
        Component component = flint ? new FlintRockComponent() : new TreeBranchesComponent();
        DoAfterEvent ev = flint ? new CollectFlintDoAfterEvent() : new CollectBranchDoAfterEvent();
        var count = component.GetType().GetField(flint ? "CurrentFlints" : "CurrentBranches")!;
        count.SetValue(component, remaining);
        var args = new DoAfterArgs(new Mock<IEntityManager>().Object, default, 0f, ev, default);
        ev.DoAfter = new DoAfter(0, args, TimeSpan.Zero);
        return (system, component, ev, count);
    }

    private static void Complete(EntitySystem system, Component component, DoAfterEvent ev)
    {
        var callback = system.GetType().GetMethod("OnDoAfter", BindingFlags.Instance | BindingFlags.NonPublic)!;
        callback.Invoke(system, new object[] { default(EntityUid), component, ev });
    }

    [TestCase(false, 0)]
    [TestCase(false, -1)]
    [TestCase(true, 0)]
    [TestCase(true, -1)]
    public void ExhaustedResourceDoesNotYieldAnotherItem(bool flint, int remaining)
    {
        var (system, component, ev, count) = CreateCompletion(flint, remaining);
        var popup = new Mock<SharedPopupSystem>();
        system.GetType().GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(system, popup.Object);

        // Another collector may have consumed the last item while this do-after ran.
        Complete(system, component, ev);
        // A handled completion must also be harmless if delivered again.
        Complete(system, component, ev);

        Assert.Multiple(() =>
        {
            Assert.That(count.GetValue(component), Is.EqualTo(remaining));
            Assert.That(ev.Handled, Is.True);
        });
        popup.Verify(p => p.PopupEntity(It.IsAny<string>(), default(EntityUid), default(EntityUid), PopupType.Small), Times.Once);
    }

    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public void CancelledOrHandledCompletionDoesNotConsumeStock(bool flint, bool cancelled, bool handled)
    {
        var (system, component, ev, count) = CreateCompletion(flint, 1);
#pragma warning disable RA0002 // Construct a cancelled event without running the do-after system.
        ev.DoAfter.CancelledTime = cancelled ? TimeSpan.Zero : null;
#pragma warning restore RA0002
        ev.Handled = handled;

        // No popup or entity manager is injected: either side effect would fail.
        Complete(system, component, ev);

        Assert.Multiple(() =>
        {
            Assert.That(count.GetValue(component), Is.EqualTo(1));
            Assert.That(ev.Handled, Is.EqualTo(handled));
        });
    }
}
