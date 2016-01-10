using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt.Ordering
{
	internal class DispatchOrderItem
	{
		public DispatchOrderItem (IDispatchUnit unit, IChannel<IPacket> channel)
		{
			this.Unit = unit;
			this.Channel = channel;
		}

		internal IDispatchUnit Unit { get; private set; }

		internal IChannel<IPacket> Channel { get; private set; }

		internal bool IsDispatched { get; set; }
	}
}
