using System.Collections.Concurrent;
using System.Linq;
using System.Net.Mqtt.Packets;
using System.Threading.Tasks;
using System.Net.Mqtt.Diagnostics;
using System.Threading;

namespace System.Net.Mqtt.Ordering
{
	internal class PacketDispatcher : IPacketDispatcher
	{
		static readonly ITracer tracer = Tracer.Get<PacketDispatcher>();

		readonly ConcurrentQueue<DispatchOrder> dispatchQueue;
		readonly Thread dispatchThread;
		bool disposed;

		public PacketDispatcher ()
		{
			this.dispatchQueue = new ConcurrentQueue<DispatchOrder> ();
			this.dispatchThread = new Thread (async () => {
				while (!this.disposed) {
					if (dispatchQueue.Any ()) {
						await TryDispatchAsync ().ConfigureAwait(continueOnCapturedContext: false);
					}
				}
			});
			this.dispatchThread.IsBackground = true; 
			this.dispatchThread.SetApartmentState (ApartmentState.STA);
			this.dispatchThread.Name = this.GetType().Name;
			this.dispatchThread.Start ();
		}

		public void Register(Guid id)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var order = this.dispatchQueue.FirstOrDefault (o => o.Id == id);

			if (order == null) {
				this.dispatchQueue.Enqueue (new DispatchOrder (id));
			}
		}

		public IObservable<DispatchOrderItem> DispatchAsync (IDispatchUnit unit, IChannel<IPacket> channel)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var order = this.dispatchQueue.FirstOrDefault (o => o.Id == unit.DispatchId);

			if (order == null) {
				order = new DispatchOrder (unit.DispatchId);

				this.dispatchQueue.Enqueue (order);
			}

			if (order.State == DispatchState.Pending) {
				order.Open ();
			}

			order.Add (unit, channel);

			return order.Dispatched;
		}

		public void Complete(Guid id)
		{
			if (disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var order = this.dispatchQueue.FirstOrDefault (o => o.Id == id);

			if (order == null) {
				return;
			}

			order.Close ();
		}

		public void Dispose ()
		{
			Dispose (disposing: true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;

			if (disposing) {
				foreach (var order in this.dispatchQueue) {
					this.Complete (order.Id);
				}

				disposed = true;
				this.dispatchThread.Join ();
			}
		}

		private async Task TryDispatchAsync()
		{
			var order = default(DispatchOrder);

			if (this.dispatchQueue.TryPeek (out order) && order.State != DispatchState.Pending) {
				await order.DispatchItemsAsync ().ConfigureAwait (continueOnCapturedContext: false);

				if (order.State == DispatchState.Completed) {
					this.dispatchQueue.TryDequeue (out order);
					order.Dispose ();
				}
			}
		}
	}
}
