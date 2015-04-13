using Hermes.Packets;

namespace Hermes
{
	public interface IPublishDispatcher
	{
		void Store (IPublishPacket packet);

		void Dispatch (IPublishPacket packet, IChannel<IPacket> channel);

		void Discard (IPublishPacket packet);
	}
}
