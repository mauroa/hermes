using System;
using System.Net.Mqtt;
using System.Net.Mqtt.Packets;
using System.Reactive.Subjects;
using Moq;
using Xunit;
using System.Reactive.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Net.Mqtt.Ordering;

namespace Tests
{
	public class PacketDispatcherSpec
	{
		[Fact]
		public void when_packets_are_registered_but_not_dispatched_then_are_not_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();
			var provider = new PacketDispatcherProvider ();
			var dispatcher = provider.Get (clientId);

			var channel = new Mock<IChannel<IPacket>> ();
			var sender = new Subject<IPacket> ();

			channel.Setup (c => c.Sender).Returns (sender);

			var units = 10;

			for (var i = 1; i <= units; i++) {
				var unitId = Guid.NewGuid ();

				dispatcher.Register (unitId);
			}

			Assert.Throws<TimeoutException>(() => sender.FirstOrDefaultAsync ().Timeout(TimeSpan.FromMilliseconds(500)).Wait());
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Never);
		}

		[Fact]
		public void when_packets_are_registered_and_completed_before_dispatched_then_are_not_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();
			var provider = new PacketDispatcherProvider ();
			var dispatcher = provider.Get (clientId);

			var channel = new Mock<IChannel<IPacket>> ();
			var sender = new Subject<IPacket> ();

			channel.Setup (c => c.Sender).Returns (sender);

			var units = 10;

			for (var i = 1; i <= units; i++) {
				var unitId = Guid.NewGuid ();
				var unit = Mock.Of<IDispatchUnit> (u => u.DispatchId == unitId);

				dispatcher.Register (unitId);
				dispatcher.Complete (unitId);
			}

			Assert.Throws<TimeoutException>(() => sender.FirstOrDefaultAsync ().Timeout(TimeSpan.FromMilliseconds(500)).Wait());
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Never);
		}

		[Fact]
		public void when_packets_are_registered_and_dispatched_then_are_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();
			var provider = new PacketDispatcherProvider ();
			var dispatcher = provider.Get (clientId);

			var channel = new Mock<IChannel<IPacket>> ();
			var sender = new Subject<IPacket> ();

			channel.Setup (c => c.Sender).Returns (sender);

			var units = 10;
			var dispatchSignal = new ManualResetEventSlim ();
			var dispatched = 0;

			channel
				.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback (() => {
					dispatched++;

					if (dispatched == units) {
						dispatchSignal.Set ();
					}
				})
				.Returns(Task.Delay(0));

			for (var i = 1; i <= units; i++) {
				var unitId = Guid.NewGuid ();
				var unit = Mock.Of<IDispatchUnit> (u => u.DispatchId == unitId);

				dispatcher.Register (unitId);
				dispatcher.DispatchAsync (unit, channel.Object);
				dispatcher.Complete (unitId);
			}

			dispatchSignal.Wait ();

			Assert.Equal (units, dispatched);
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Exactly (units));
		}

		[Fact]
		public async Task when_packets_are_registered_and_dispatched_then_are_sent_in_the_same_order_they_were_registered()
		{
			var clientId = Guid.NewGuid ().ToString ();
			var provider = new PacketDispatcherProvider ();
			var dispatcher = provider.Get (clientId);

			var channel = new Mock<IChannel<IPacket>> ();
			var sender = new Subject<IPacket> ();

			channel.Setup (c => c.Sender).Returns (sender);

			var units = 10;
			var unitsQueue = new ConcurrentQueue<IDispatchUnit> ();
			var dispatchSignal = new ManualResetEventSlim ();
			var dispatched = 0;
			var ordered = true;

			channel
				.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => {
					dispatched++;

					var dispatchedUnit = packet as IDispatchUnit;
					var originalUnit = default (IDispatchUnit);

					unitsQueue.TryDequeue (out originalUnit);

					ordered = ordered && originalUnit.DispatchId == dispatchedUnit.DispatchId;
 
					if (dispatched == units) {
						dispatchSignal.Set ();
					}
				})
				.Returns(Task.Delay(0));

			var dispatchTasks = new List<Task> ();

			for (var i = 1; i <= units; i++) {
				dispatchTasks.Add (Task.Run (() => {
					var unitId = Guid.NewGuid ();
					var unit = Mock.Of<IDispatchUnit> (u => u.DispatchId == unitId);

					unitsQueue.Enqueue (unit);
					dispatcher.Register (unitId);
					dispatcher.DispatchAsync (unit, channel.Object);
					dispatcher.Complete (unitId);
				}));
			}

			await Task.WhenAll (dispatchTasks);

			dispatchSignal.Wait ();

			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Exactly (units));
			Assert.True (ordered);
		}
	}
}
