using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mqtt.Packets;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace System.Net.Mqtt
{
    internal class PacketDispatcher : IPacketDispatcher
    {
        static readonly ITracer tracer = Tracer.Get<PacketDispatcher> ();

        bool disposed;
        ConcurrentDictionary<DispatchPacketType, ConcurrentQueue<DispatchOrder>> dispatchQueues;
        readonly TaskRunner dispatchRunner;

        public PacketDispatcher ()
        {
            InitializeDispatchQueues ();

            dispatchRunner = TaskRunner.Get ();
            dispatchRunner.Run (async () => {
                while (!disposed) {
                    await DispatchOrdersAsync ().ConfigureAwait (continueOnCapturedContext: false); 
                }
            });
        }

        public Guid CreateOrder (DispatchPacketType type)
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (PacketDispatcher));
            }

            var orderId = GetOrderId (type);
            var dispatchQueue = GetDispatchQueue (type);

            dispatchQueue.Enqueue (new DispatchOrder (orderId));

            return orderId;
        }

        public async Task DispatchAsync (IOrderedPacket packet, IMqttChannel<IPacket> channel)
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (PacketDispatcher));
            }

            var dispatchQueue = GetDispatchQueue (packet.Type.ToDispatchPacketType ());
            var order = dispatchQueue.FirstOrDefault (o => o.Id == packet.OrderId);

            if (order == null) {
                throw new InvalidOperationException (string.Format (Properties.Resources.PacketDispatcher_Dispatch_OrderDoesNotExists, packet.OrderId));
            }

            order.Add (packet, channel);

            var dispatched = await order
                .DispatchedPacketsStream
                .FirstOrDefaultAsync (t => t.Item1.PacketId == packet.PacketId && t.Item1.Type == packet.Type);

            if (dispatched.Item2 != null) {
                dispatched.Item2.Throw ();
            }
        }

        public void CompleteOrder (DispatchPacketType type, Guid orderId)
        {
            if (disposed) {
                throw new ObjectDisposedException (nameof (PacketDispatcher));
            }

            var dispatchQueue = GetDispatchQueue (type);
            var order = dispatchQueue.FirstOrDefault (o => o.Id == orderId);

            order?.Close ();
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
                foreach (var pair in dispatchQueues) {
                    var dispatchQueue = pair.Value;

                    foreach (var order in dispatchQueue) {
                        CompleteOrder (pair.Key, order.Id);
                    }
                }

                disposed = true;
                dispatchRunner.Dispose ();
            }
        }

        void InitializeDispatchQueues ()
        {
            dispatchQueues = new ConcurrentDictionary<DispatchPacketType, ConcurrentQueue<DispatchOrder>> ();

            dispatchQueues.TryAdd (DispatchPacketType.Publish, new ConcurrentQueue<DispatchOrder> ());
            dispatchQueues.TryAdd (DispatchPacketType.PublishAck1, new ConcurrentQueue<DispatchOrder> ());
            dispatchQueues.TryAdd (DispatchPacketType.PublishAck2, new ConcurrentQueue<DispatchOrder> ());
            dispatchQueues.TryAdd (DispatchPacketType.PublishAck3, new ConcurrentQueue<DispatchOrder> ());

        }

        async Task DispatchOrdersAsync ()
        {
            var dispatchTasks = new List<Task> ();

            foreach (var pair in dispatchQueues) {
                dispatchTasks.Add (DispatchOrdersAsync (pair.Key, pair.Value));
            }

            await Task.WhenAll (dispatchTasks).ConfigureAwait (continueOnCapturedContext: false);
        }

        async Task DispatchOrdersAsync (DispatchPacketType packetType, ConcurrentQueue<DispatchOrder> dispatchQueue)
        {
            var order = default (DispatchOrder);

            if (dispatchQueue.TryPeek (out order)) {
                try {
                    await order
                        .DispatchPacketsAsync ()
                        .ConfigureAwait (continueOnCapturedContext: false);

                    if (order.State == DispatchOrderState.Completed) {
                        order.Dispose ();
                        dispatchQueue.TryDequeue (out order);
                    }
                } catch (Exception ex) {
                    tracer.Warn(ex, Properties.Resources.PacketDispatcher_DispatchOrderError, packetType);
                }
            }
        }

        Guid GetOrderId (DispatchPacketType type)
        {
            var orderId = Guid.NewGuid ();
            var dispatchQueue = GetDispatchQueue (type);

            if (dispatchQueue.Any (o => o.Id == orderId))  {
                return GetOrderId (type);
            }

            return orderId;
        }

        ConcurrentQueue<DispatchOrder> GetDispatchQueue (DispatchPacketType type)
        {
            var dispatchQueue = default (ConcurrentQueue<DispatchOrder>);

            dispatchQueues.TryGetValue (type, out dispatchQueue);

            return dispatchQueue;
        }
    }
}
