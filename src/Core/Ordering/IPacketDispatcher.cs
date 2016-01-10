using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt.Ordering
{
	internal interface IPacketDispatcher : IDisposable
	{
		void Register (Guid id);

		IObservable<DispatchOrderItem> DispatchAsync (IDispatchUnit unit, IChannel<IPacket> channel);

		void Complete (Guid id);
	}
}