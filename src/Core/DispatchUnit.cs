using Hermes.Packets;

namespace Hermes
{
	internal class DispatchUnit
	{
		internal DispatchUnit (IPublishPacket packet)
		{
			this.Packet = packet;
		}

		internal IPublishPacket Packet { get; private set; }

		internal bool Ready { get; set; }

		internal bool Discarded { get; set; }

		internal IChannel<IPacket> Channel { get; set; }
	}
}
