using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Net.Mqtt.Packets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Mqtt.Diagnostics;

namespace System.Net.Mqtt
{
	internal class PacketDispatcher : IPacketDispatcher, IDisposable
	{
		static readonly ITracer tracer = Tracer.Get<PacketDispatcher>();
		static readonly ConcurrentDictionary<string, IPacketDispatcher> dispatchers;
		static readonly string CommonIdentifier = "general";

		readonly ConcurrentQueue<DispatchOrder> dispatchQueue;
		readonly IDisposable monitorSubscription;
		bool disposed;

		static PacketDispatcher()
		{
			dispatchers = new ConcurrentDictionary<string, IPacketDispatcher> ();
		}

		private PacketDispatcher ()
		{
			dispatchQueue = new ConcurrentQueue<DispatchOrder> ();
			monitorSubscription = Observable
				.Interval (TimeSpan.FromMilliseconds(100), NewThreadScheduler.Default)
				.Subscribe(async _=> {
					await TryDispatchAsync ()
						.ConfigureAwait(continueOnCapturedContext: false);
				});
		}

		public static IPacketDispatcher Get()
		{
			return Get (CommonIdentifier);
		}

		public static IPacketDispatcher Get(string clientId)
		{
			var dispatcher = default (IPacketDispatcher);

			if (!dispatchers.TryGetValue (clientId, out dispatcher)) {
				dispatcher = new PacketDispatcher ();
				dispatchers.TryAdd (clientId, dispatcher);
			}

			return dispatcher;
		}

		public void Register(Guid id)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			if (dispatchQueue.Any (o => o.Id == id)) {
				return;
			}

			dispatchQueue.Enqueue (new DispatchOrder(id));
		}

		public void Set (IDispatchUnit unit, IChannel<IPacket> channel)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var order = dispatchQueue.FirstOrDefault (o => o.Id == unit.DispatchId);

			if (order == null) {
				this.Register (unit.DispatchId);
				this.Set (unit, channel);
			} else {
				order.Add (unit, channel);
			}
		}

		public void Dispatch(Guid id)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var order = dispatchQueue.FirstOrDefault (o => o.Id == id);

			if (order == null) {
				return;
			}

			order.State = DispatchState.Ready;
		}

		public void Cancel(Guid id)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var order = dispatchQueue.FirstOrDefault (o => o.Id == id);

			if (order == null) {
				return;
			}

			order.State = DispatchState.Cancelled;
		}

		public void Dispose ()
		{
			Dispose (disposing: true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			monitorSubscription.Dispose ();

			foreach (var order in dispatchQueue) {
				this.Cancel (order.Id);
			}

			disposed = true;
		}

		private async Task TryDispatchAsync()
		{
			var order = default(DispatchOrder);

			if (dispatchQueue.TryPeek (out order) &&
				order.State != DispatchState.Pending && 
				dispatchQueue.TryDequeue(out order)) {
					if (order.State == DispatchState.Ready) {
						var dispatchTasks = new List<Task>();

						foreach (var item in order.Items) {
							var channel = item.Item2;
							var packet = item.Item1;

							dispatchTasks.Add(channel.SendAsync (packet));
						}

						try {
							await Task.WhenAll(dispatchTasks)
								.ConfigureAwait(continueOnCapturedContext: false);
						} catch(Exception ex) {
							tracer.Error(ex, ex.Message);
						}
					}
			}
		}
	}
}
