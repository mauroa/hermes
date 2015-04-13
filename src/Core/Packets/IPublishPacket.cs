using System;

namespace Hermes.Packets
{
	public interface IPublishPacket : IPacket
	{
		Guid Id { get; }
	}
}
