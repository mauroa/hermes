using Moq;
using System;
using System.Net.Mqtt;
using System.Net.Mqtt.Packets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests
{
    public class DispatchOrderSpec
    {
        [Fact]
        public void when_creating_dispatch_order_then_is_active_by_default()
        {
            var orderId = Guid.NewGuid ();
            var order = new DispatchOrder (orderId);

            Assert.Equal (orderId, order.Id);
            Assert.Equal (DispatchOrderState.Active, order.State);
        }

        [Fact]
        public void when_adding_packet_to_inactive_order_then_fails()
        {
            var orderId = Guid.NewGuid ();
            var order = new DispatchOrder (orderId);

            order.Close ();

            var ex = Assert.Throws<InvalidOperationException> (() => order.Add (Mock.Of<IOrderedPacket> (), Mock.Of<IMqttChannel<IPacket>> ()));

            Assert.NotNull (ex);
        }

        [Fact]
        public async Task when_dispatching_packets_then_only_not_dispatched_are_sent_over_channel()
        {
            var orderId = Guid.NewGuid ();
            var order = new DispatchOrder (orderId);

            var count = 10;
            var packetsSent = 0;

            for (var i = 1; i <= count; i++) {
                var packet = Mock.Of<IOrderedPacket> ();
                var channel = new Mock<IMqttChannel<IPacket>> ();

                channel
                    .Setup(c => c.SendAsync (It.IsAny<IPacket> ()))
                    .Callback<IPacket>(_ => {
                        packetsSent++;
                    })
                    .Returns (Task.Delay (0));

                order.Add (packet, channel.Object);
            }

            await order.DispatchPacketsAsync ();

            var lastPacket = Mock.Of<IOrderedPacket> ();
            var lastChannel = new Mock<IMqttChannel<IPacket>> ();

            lastChannel
                .Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
                .Callback<IPacket> (_ => {
                    packetsSent++;
                })
                .Returns (Task.Delay (0));

            order.Add (lastPacket, lastChannel.Object);

            await order.DispatchPacketsAsync ();
            await order.DispatchPacketsAsync ();

            Assert.Equal (count + 1, packetsSent);
        }

        [Fact]
        public void when_closing_order_then_is_completed()
        {
            var orderId = Guid.NewGuid ();
            var order = new DispatchOrder (orderId);

            order.Close ();

            Assert.Equal (orderId, order.Id);
            Assert.Equal (DispatchOrderState.Completed, order.State);
        }

        [Fact]
        public void when_disposing_order_then_is_values_are_cleared()
        {
            var orderId = Guid.NewGuid ();
            var order = new DispatchOrder (orderId);

            var disposeSignal = new ManualResetEventSlim ();

            order.DispatchedPacketsStream.Subscribe (_ => { }, onCompleted: () => {
                disposeSignal.Set ();
            });

            order.Dispose ();

            var disposed = disposeSignal.Wait (millisecondsTimeout: 1000);

            Assert.True (disposed);
            Assert.Equal (Guid.Empty, order.Id);
            Assert.Equal (DispatchOrderState.Completed, order.State);
        }
    }
}
