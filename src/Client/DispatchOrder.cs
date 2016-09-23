using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Mqtt.Packets;
using System.Reactive.Subjects;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Mqtt
{
    internal class DispatchOrder : IDisposable
    {
        static readonly ITracer tracer = Tracer.Get<DispatchOrder> ();

        bool disposed;
        ConcurrentBag<DispatchOrderItem> items;
        readonly ReplaySubject<Tuple<IOrderedPacket, ExceptionDispatchInfo>> dispatchedPackets;
        readonly object lockObject = new object ();
        readonly AsyncLock asyncLockObject = new AsyncLock ();

        internal DispatchOrder (Guid id)
        {
            items = new ConcurrentBag<DispatchOrderItem> ();
            dispatchedPackets = new ReplaySubject<Tuple<IOrderedPacket, ExceptionDispatchInfo>> (window: TimeSpan.FromSeconds (5));

            Id = id;
            State = DispatchOrderState.Active;
        }

        internal Guid Id { get; private set; }

        internal DispatchOrderState State { get; private set; }

        internal IObservable<Tuple<IOrderedPacket, ExceptionDispatchInfo>> DispatchedPacketsStream => dispatchedPackets;

        internal void Add (IOrderedPacket packet, IMqttChannel<IPacket> channel)
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (DispatchOrder));
            }

            if (State != DispatchOrderState.Active) {
                throw new InvalidOperationException (string.Format (Properties.Resources.DispatchOrder_AddInvalid, State));
            }

            items.Add (new DispatchOrderItem (packet, channel));
        }

        internal async Task DispatchPacketsAsync ()
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (DispatchOrder));
            }

            foreach (var item in items.Where (i => !i.IsDispatched)) {
                using (await asyncLockObject.LockAsync ()) {
                    try {
                        await item
                            .Channel
                            .SendAsync (item.Packet)
                            .ConfigureAwait (continueOnCapturedContext: false);

                        dispatchedPackets.OnNext (Tuple.Create (item.Packet, default (ExceptionDispatchInfo)));
                    } catch (Exception ex) {
                        dispatchedPackets.OnNext (Tuple.Create (item.Packet, ExceptionDispatchInfo.Capture (ex)));
                        tracer.Error (ex, ex.Message);
                    } finally {
                        item.IsDispatched = true;
                    }
                }
            }
        }

        internal void Close ()
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (DispatchOrder));
            }

            if (State != DispatchOrderState.Completed) {
                lock (lockObject) {
                    if (State != DispatchOrderState.Completed) {
                        State = DispatchOrderState.Completed;
                    }
                }
            }
        }

        public void Dispose ()
        {
            Dispose (disposing: true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                Id = Guid.Empty;
                State = DispatchOrderState.Completed;

                dispatchedPackets.OnCompleted ();
                dispatchedPackets.Dispose ();

                var emptyItems = new ConcurrentBag<DispatchOrderItem> ();

                Interlocked.Exchange (ref items, emptyItems);

                disposed = true;
            }
        }
    }
}
