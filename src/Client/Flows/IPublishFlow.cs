using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Flows
{
    internal interface IPublishFlow : IProtocolFlow
	{
		Task SendAckAsync (string clientId, IOrderedPacket ack, IMqttChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend);
	}
}
