using System.Threading.Tasks;
using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt.Flows
{
	internal interface IProtocolFlow
	{
		/// <exception cref="MqttViolationException">MqttViolationException</exception>
		/// <exception cref="MqttException">MqttException</exception>
		Task ExecuteAsync (string clientId, IPacket input, IChannel<IPacket> channel);
	}
}
