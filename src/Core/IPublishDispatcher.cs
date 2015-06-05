using Hermes.Packets;

namespace Hermes
{
	public interface IPublishDispatcher
	{
		void Register (IPublishPacket packet);

		void Dispatch (IPublishPacket packet, IChannel<IPacket> channel = null);

		void Discard (IPublishPacket packet);
	}
}
