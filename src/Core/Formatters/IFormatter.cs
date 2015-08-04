using System.Threading.Tasks;
using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt.Formatters
{
	internal interface IFormatter
	{
		/// <summary>
		/// Gets the type of packet that this formatter support.
		/// </summary>
		PacketType PacketType { get; }

		/// <exception cref="MqttConnectionException">MqttConnectionException</exception>
		/// <exception cref="MqttViolationException">MqttViolationException</exception>
		/// <exception cref="MqttException">MqttException</exception>
		Task<IPacket> FormatAsync (byte[] bytes);

		/// <exception cref="MqttConnectionException">MqttConnectionException</exception>
		/// <exception cref="MqttViolationException">MqttViolationException</exception>
		/// <exception cref="MqttException">MqttException</exception>
		Task<byte[]> FormatAsync (IPacket packet);
	}
}
