using System.Collections.Concurrent;
using Hermes.Packets;
using System.Linq;
using System;
using System.Reactive.Linq;
using System.Timers;

namespace Hermes
{
	public class PublishDispatcher : IPublishDispatcher, IDisposable
	{
		readonly ConcurrentQueue<DispatchUnit> packetQueue;
		readonly IDisposable packetMonitorSubscription;
		bool disposed;

		public PublishDispatcher ()
		{
			packetQueue = new ConcurrentQueue<DispatchUnit> ();
			packetMonitorSubscription = Observable.Create<DispatchUnit> (observer => {
				var timer = new Timer {
					Interval = 100
				};

				timer.Elapsed += (sender, args) => {
					var unit = default(DispatchUnit);

					if (packetQueue.TryPeek (out unit)) {
						if ((unit.ReadyToDispatch || unit.Discarded) && 
							packetQueue.TryDequeue(out unit)) {
								observer.OnNext (unit);
						}
					}
				};

				return () => {
					timer.Dispose();
				};
			})
			.Subscribe(async unit => {
				if (unit.ReadyToDispatch) {
					var channel = unit.Channel;

					await channel.SendAsync (unit.Packet);
				}
			});
		}

		public void Store(IPublishPacket packet)
		{
			if (disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			if (packetQueue.Any (u => u.Packet.Id == packet.Id)) {
				return;
			}

			packetQueue.Enqueue (new DispatchUnit(packet));
		}

		public void Dispatch(IPublishPacket packet, IChannel<IPacket> channel)
		{
			if (disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			var unit = packetQueue.FirstOrDefault (u => u.Packet.Id == packet.Id);

			if (unit == null) {
				unit = new DispatchUnit(packet) { 
					ReadyToDispatch = true,
					Channel = channel
				};

				packetQueue.Enqueue (unit);
			} else {
				unit.ReadyToDispatch = true;
				unit.Channel = channel;
			}
		}

		public void Discard(IPublishPacket packet)
		{
			if (disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			var unit = packetQueue.FirstOrDefault (u => u.Packet.Id == packet.Id);

			if (unit == null) {
				return;
			}

			unit.Discarded = true;
			unit.Channel = null;
		}

		public void Dispose ()
		{
			Dispose (disposing: true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			packetMonitorSubscription.Dispose ();
			disposed = true;
		}
	}
}
