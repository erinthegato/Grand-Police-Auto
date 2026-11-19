using System;
using GPA.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GPA.Tests
{
    public class CoreTests
    {
        private struct TestEvt { public int Value; }
        private interface IFoo { }
        private class Foo : IFoo { }

        [SetUp] public void SetUp() => Services.ClearAll();
        [TearDown] public void TearDown() => Services.ClearAll();

        [Test]
        public void EventBus_DeliversToAllSubscribers()
        {
            var bus = new EventBus();
            int a = 0, b = 0;
            bus.Subscribe<TestEvt>(e => a += e.Value);
            bus.Subscribe<TestEvt>(e => b += e.Value);
            bus.Publish(new TestEvt { Value = 3 });
            Assert.AreEqual(3, a);
            Assert.AreEqual(3, b);
        }

        [Test]
        public void EventBus_DisposeUnsubscribes_EvenDuringPublish()
        {
            var bus = new EventBus();
            int calls = 0;
            IDisposable sub = null;
            sub = bus.Subscribe<TestEvt>(e => { calls++; sub.Dispose(); });
            bus.Publish(new TestEvt());
            bus.Publish(new TestEvt());
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void EventBus_HandlerExceptionDoesNotBlockOthers()
        {
            var bus = new EventBus();
            bool second = false;
            bus.Subscribe<TestEvt>(e => throw new InvalidOperationException("boom"));
            bus.Subscribe<TestEvt>(e => second = true);
            LogAssert.ignoreFailingMessages = true;
            bus.Publish(new TestEvt());
            LogAssert.ignoreFailingMessages = false;
            Assert.IsTrue(second);
        }

        [Test]
        public void Services_RegisterGetUnregister()
        {
            var foo = new Foo();
            Services.Register<IFoo>(foo);
            Assert.AreSame(foo, Services.Get<IFoo>());
            Services.Unregister<IFoo>(new Foo()); // different instance: ignored
            Assert.IsTrue(Services.Has<IFoo>());
            Services.Unregister<IFoo>(foo);
            Assert.IsFalse(Services.TryGet<IFoo>(out _));
        }

        [Test]
        public void GameState_PushPopReturnsToBase()
        {
            var gs = new GameStateService();
            gs.SetBase(GameState.InVehicle);
            gs.Push(GameState.PoliceComputer);
            Assert.AreEqual(GameState.PoliceComputer, gs.Current);
            gs.Push(GameState.Paused);
            gs.Pop(GameState.PoliceComputer); // remove from middle of stack
            Assert.AreEqual(GameState.Paused, gs.Current);
            gs.Pop(GameState.Paused);
            Assert.AreEqual(GameState.InVehicle, gs.Current);
            gs.Pop(GameState.InVehicle); // base can't be popped
            Assert.AreEqual(GameState.InVehicle, gs.Current);
        }

        [Test]
        public void TimeOfDay_AdvancesAndWraps()
        {
            var t = new TimeOfDayService(23.5f, 60f, new DateTime(2025, 1, 1)); // 60 game min per sec
            int changed = -1;
            t.HourChanged += h => changed = h;
            t.Tick(1f); // +1 hour
            Assert.AreEqual(0.5f, t.Hour, 0.001f);
            Assert.AreEqual(1, t.Day);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void LocationInfo_Formats()
        {
            var l = new LocationDescription {Street = "Commerce Ave", CrossStreet = "Market St", District = "Downtown", HouseNumber = 1420 };
            Assert.AreEqual("1420 Commerce Ave, Downtown", l.Address);
            Assert.AreEqual("Commerce Ave & Market St", l.Intersection);
        }
    }
}
