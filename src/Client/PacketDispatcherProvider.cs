using System.Collections.Concurrent;

namespace System.Net.Mqtt
{
    internal class PacketDispatcherProvider : IPacketDispatcherProvider
    {
        bool disposed;
        readonly ConcurrentDictionary<string, IPacketDispatcher> dispatchers;

        public PacketDispatcherProvider ()
        {
            dispatchers = new ConcurrentDictionary<string, IPacketDispatcher> ();
        }

        public IPacketDispatcher GetDispatcher (string clientId)
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (PacketDispatcherProvider));
            }

            var dispatcher = default (IPacketDispatcher);

            if (!dispatchers.TryGetValue (clientId, out dispatcher)) {
                dispatcher = new PacketDispatcher ();
                dispatchers.TryAdd (clientId, dispatcher);
            }

            return dispatcher;
        }

        public void Dispose ()
        {
            Dispose (disposing: true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (disposed) return;

            if (disposing) {
                disposed = true;

                foreach (var dispatcher in dispatchers) {
                    dispatcher.Value.Dispose ();
                }

                dispatchers.Clear ();
            }
        }
    }
}
