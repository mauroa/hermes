using Moq;
using System;
using System.Collections.Generic;
using System.Net.Mqtt;
using System.Net.Mqtt.Packets;
using System.Threading.Tasks;
using Xunit;

namespace Tests
{
    public class PacketDispatcherSpec
    {
        [Fact]
        public void when_creating_order_then_succeeds ()
        {
            var dispatcher = new PacketDispatcher ();
            var orderId = dispatcher.CreateOrder (DispatchPacketType.Publish);

            Assert.NotEqual (Guid.Empty, orderId);

            dispatcher.Dispose ();
        }

        [Fact]
        public async Task when_dispatching_single_packet_then_it_is_sent()
        {
            var dispatcher = new PacketDispatcher ();
            var orderId = dispatcher.CreateOrder (DispatchPacketType.Publish);
            var packetId = GetPacketId ();
            var packet = GetPacket (orderId, packetId);
            var channel = new Mock<IMqttChannel<IPacket>> ();
            var packetSent = false;

            channel
                .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                .Callback<IPacket> (_ => {
                    packetSent = true;
                })
                .Returns(Task.Delay (0));

            await dispatcher.DispatchAsync (packet, channel.Object);

            Assert.True (packetSent);

            dispatcher.Dispose ();
        }

        [Fact]
        public async Task when_dispatching_multiple_packets_and_orders_are_completed_then_packets_are_sent()
        {
            var dispatcher = new PacketDispatcher ();

            var totalOrders = 10;
            var ordersToComplete = 5;
            var packetsSent = 0;

            var dispatchTasks = new List<Task> ();
            var orderIds = new Queue<Guid> ();

            for (var i = 1; i <= totalOrders; i++)  {
                var orderId = dispatcher.CreateOrder (DispatchPacketType.Publish);

                var fooPacketId = GetPacketId ();
                var fooPacket = GetPacket (orderId, fooPacketId);
                var fooChannel = new Mock<IMqttChannel<IPacket>> ();

                fooChannel
                   .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                   .Callback<IPacket> (_ => {
                       packetsSent++;
                   })
                   .Returns(Task.Delay (0));

                var barPacketId = GetPacketId ();
                var barPacket = GetPacket (orderId, barPacketId);
                var barChannel = new Mock<IMqttChannel<IPacket>> ();

                barChannel
                   .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                   .Callback<IPacket> (_ => {
                       packetsSent++;
                   })
                   .Returns (Task.Delay (0));

                dispatchTasks.Add (dispatcher.DispatchAsync (fooPacket, fooChannel.Object));
                dispatchTasks.Add (dispatcher.DispatchAsync (barPacket, barChannel.Object));

                if (i <= ordersToComplete) {
                    orderIds.Enqueue (orderId);
                }
            }

            for (var j = 1; j <= ordersToComplete; j++) {
                var orderId = orderIds.Dequeue ();

                dispatcher.CompleteOrder (DispatchPacketType.Publish, orderId);
            }

            await Task.Delay (TimeSpan.FromMilliseconds (1000));

            Assert.Equal ((ordersToComplete + 1) * 2, packetsSent);

            dispatcher.Dispose ();
        }

        [Fact]
        public async Task when_dispatching_multiple_packets_and_top_order_is_not_completed_then_subsequent_packets_are_not_sent()
        {
            var dispatcher = new PacketDispatcher ();

            var totalOrders = 10;
            var packetsSent = 0;

            var dispatchTasks = new List<Task> ();

            for (var i = 1; i <= totalOrders; i++) {
                var orderId = dispatcher.CreateOrder (DispatchPacketType.Publish);

                var fooPacketId = GetPacketId ();
                var fooPacket = GetPacket (orderId, fooPacketId);
                var fooChannel = new Mock<IMqttChannel<IPacket>> ();

                fooChannel
                   .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                   .Callback<IPacket> (_ => {
                       packetsSent++;
                   })
                   .Returns (Task.Delay (0));

                var barPacketId = GetPacketId ();
                var barPacket = GetPacket (orderId, barPacketId);
                var barChannel = new Mock<IMqttChannel<IPacket>> ();

                barChannel
                   .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                   .Callback<IPacket> (_ => {
                       packetsSent++;
                   })
                   .Returns (Task.Delay (0));

                dispatchTasks.Add (dispatcher.DispatchAsync (fooPacket, fooChannel.Object));
                dispatchTasks.Add (dispatcher.DispatchAsync (barPacket, barChannel.Object));
            }

            await Task.Delay (TimeSpan.FromMilliseconds (1000));

            Assert.Equal (2, packetsSent);

            dispatcher.Dispose();
        }

        [Fact]
        public void when_dispatching_packet_with_no_order_then_fails ()
        {
            var dispatcher = new PacketDispatcher ();

            dispatcher.CreateOrder (DispatchPacketType.Publish);

            var packetId = GetPacketId ();
            var packet = GetPacket (orderId: Guid.Empty, packetId: packetId);
            var channel = new Mock<IMqttChannel<IPacket>> ();

            channel
                .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                .Returns (Task.Delay (0));

            var ex = Assert.Throws<AggregateException> (() => dispatcher.DispatchAsync (packet, channel.Object).Wait ());

            Assert.NotNull (ex);
            Assert.NotNull (ex.InnerException);
            Assert.True (ex.InnerException is InvalidOperationException);

            dispatcher.Dispose ();
        }

        ushort GetPacketId ()
        {
            return (ushort)new Random ().Next (ushort.MaxValue);
        }

        IOrderedPacket GetPacket (Guid orderId, ushort packetId)
        {
            return Mock.Of<IOrderedPacket> (p => p.OrderId == orderId &&
                p.PacketId == packetId && p.Type == MqttPacketType.Publish);
        }
    }
}
