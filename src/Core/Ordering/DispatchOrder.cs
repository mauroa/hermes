using System.Collections.Concurrent;
using System.Linq;
using System.Net.Mqtt.Diagnostics;
using System.Net.Mqtt.Packets;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Ordering
{
	internal class DispatchOrder : IDisposable
	{
		static readonly ITracer tracer = Tracer.Get<DispatchOrder> ();

		ConcurrentBag<DispatchOrderItem> items;
		bool disposed;
		readonly Subject<DispatchOrderItem> dispatched;

		internal DispatchOrder (Guid id)
		{
			this.items = new ConcurrentBag<DispatchOrderItem>();
			this.dispatched = new Subject<DispatchOrderItem> ();
			this.Id = id;
			this.State = DispatchState.Pending;
		}

		internal Guid Id { get; private set; }

		internal DispatchState State { get; private set; }

		internal IObservable<DispatchOrderItem> Dispatched { get { return this.dispatched; } }

		internal void Open()
		{
			if (this.disposed) {
				throw new ObjectDisposedException (this.GetType().FullName);
			}

			this.State = DispatchState.Active;
		}

		internal void Add(IDispatchUnit unit, IChannel<IPacket> channel)
		{
			if (this.disposed) {
				throw new ObjectDisposedException (this.GetType().FullName);
			}

			this.items.Add (new DispatchOrderItem (unit, channel));
		}

		internal void Close()
		{
			if (this.disposed) {
				throw new ObjectDisposedException (this.GetType().FullName);
			}

			this.State = DispatchState.Completed;
		}

		internal async Task DispatchItemsAsync ()
		{
			if (this.disposed) {
				throw new ObjectDisposedException (this.GetType().FullName);
			}

			foreach (var item in this.items.Where(i => !i.IsDispatched)) {
				try {
					await item.Channel
						.SendAsync (item.Unit)
						.ConfigureAwait (continueOnCapturedContext: false);

					item.IsDispatched = true;

					this.dispatched.OnNext (item);
				} catch (Exception ex) {
					tracer.Error(ex, ex.Message);
				}
			}
		}

		public void Dispose ()
		{
			this.Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (this.disposed) return;

			if (disposing) {
				this.dispatched.Dispose ();
				var emptyItems = new ConcurrentBag<DispatchOrderItem> ();

				Interlocked.Exchange (ref this.items, emptyItems);

				this.disposed = true;
			}
		}
	}
}