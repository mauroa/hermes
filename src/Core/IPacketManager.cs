using System.Threading.Tasks;
using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt
{
	internal interface IPacketManager
	{
		/// <exception cref="MqttConnectionException">MqttConnectionException</exception>
		/// <exception cref="MqttViolationException">MqttViolationException</exception>
		/// <exception cref="MqttException">MqttException</exception>
		Task<IPacket> GetPacketAsync (byte[] bytes);

		/// <exception cref="MqttConnectionException">MqttConnectionException</exception>
		/// <exception cref="MqttViolationException">MqttViolationException</exception>
		/// <exception cref="MqttException">MqttException</exception>
		Task<byte[]> GetBytesAsync (IPacket packet);
	}
}
