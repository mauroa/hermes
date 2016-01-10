using System.Collections.Concurrent;

namespace System.Net.Mqtt.Ordering
{
	internal class PacketDispatcherProvider : IPacketDispatcherProvider
	{
		static readonly string CommonIdentifier = "general";

		readonly ConcurrentDictionary<string, IPacketDispatcher> dispatchers;
		bool disposed;

		public PacketDispatcherProvider ()
		{
			this.dispatchers = new ConcurrentDictionary<string, IPacketDispatcher> ();
		}

		public IPacketDispatcher Get ()
		{
			return this.Get (CommonIdentifier);
		}

		public IPacketDispatcher Get (string clientId)
		{
			var dispatcher = default (IPacketDispatcher);

			if (!this.dispatchers.TryGetValue (clientId, out dispatcher)) {
				dispatcher = new PacketDispatcher ();
				this.dispatchers.TryAdd (clientId, dispatcher);
			}

			return dispatcher;
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
				disposed = true;

				foreach (var dispatcher in this.dispatchers) {
					dispatcher.Value.Dispose ();
				}

				this.dispatchers.Clear ();
			}
		}
	}
}
