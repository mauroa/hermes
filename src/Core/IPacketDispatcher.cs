using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt
{
	internal interface IPacketDispatcher
	{
		void Register (Guid id);

		void Set (IDispatchUnit unit, IChannel<IPacket> channel);

		void Dispatch (Guid id);

		void Cancel (Guid id);
	}
}