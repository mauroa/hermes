using System.Net.Mqtt.Packets;
using System.Threading.Tasks;

namespace System.Net.Mqtt
{
    internal interface IPacketDispatcher : IDisposable
    {
        Guid CreateOrder (DispatchPacketType type);

        Task DispatchAsync (IOrderedPacket packet, IMqttChannel<IPacket> channel);

        void CompleteOrder (DispatchPacketType type, Guid orderId);
    }
}
