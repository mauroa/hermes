using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt
{
    internal class DispatchOrderItem
    {
        public DispatchOrderItem (IOrderedPacket packet, IMqttChannel<IPacket> channel)
        {
            Packet = packet;
            Channel = channel;
        }

        internal IOrderedPacket Packet { get; }

        internal IMqttChannel<IPacket> Channel { get; }

        internal bool IsDispatched { get; set; }
    }

}
