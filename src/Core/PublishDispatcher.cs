using System.Collections.Concurrent;
using Hermes.Packets;
using System.Linq;
using System;
using System.Reactive.Linq;
using System.Reactive.Concurrency;

namespace Hermes
{
	public class PublishDispatcher : IPublishDispatcher, IDisposable
	{
		readonly IChannel<IPacket> channel;
		readonly ConcurrentQueue<DispatchUnit> packetQueue;
		readonly IDisposable packetMonitorSubscription;
		bool disposed;

		public PublishDispatcher (IChannel<IPacket> channel = null)
		{
			this.channel = channel;

			packetQueue = new ConcurrentQueue<DispatchUnit> ();
			packetMonitorSubscription = Observable
				.Timer (TimeSpan.FromMilliseconds(100), NewThreadScheduler.Default)
				.Subscribe(async _=> {
					var unit = default(DispatchUnit);

					if (packetQueue.TryPeek (out unit)) {
						if ((unit.Ready || unit.Discarded) && 
							packetQueue.TryDequeue(out unit)) {
								if (unit.Ready) {
									await unit.Channel.SendAsync (unit.Packet);
								}
						}
					}
				});
		}

		public void Register(IPublishPacket packet)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			if (packetQueue.Any (u => u.Packet.Id == packet.Id)) {
				return;
			}

			packetQueue.Enqueue (new DispatchUnit(packet));
		}

		public void Dispatch(IPublishPacket packet, IChannel<IPacket> channel = null)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			if (channel == null && this.channel == null) {
				throw new ArgumentNullException ("channel");
			}

			var unit = packetQueue.FirstOrDefault (u => u.Packet.Id == packet.Id);

			if (unit == null) {
				unit = new DispatchUnit(packet) { 
					Ready = true,
					Channel = channel ?? this.channel
				};

				packetQueue.Enqueue (unit);
			} else {
				unit.Ready = true;
				unit.Channel = channel ?? this.channel;
			}
		}

		public void Discard(IPublishPacket packet)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

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
