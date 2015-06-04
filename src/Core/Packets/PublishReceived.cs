﻿using System;

namespace Hermes.Packets
{
	public class PublishReceived : IFlowPacket, IPublishPacket, IEquatable<PublishReceived>
	{
		public PublishReceived(ushort packetId)
		{
			this.PacketId = packetId;
			this.Id = Guid.NewGuid ();
		}

		public Guid Id { get; private set; }

		public PacketType Type { get { return PacketType.PublishReceived; } }

		public ushort PacketId { get; private set; }

		public bool Equals(PublishReceived other)
		{
			if (other == null)
				return false;

			return this.PacketId == other.PacketId;
		}

		public override bool Equals(object obj)
		{
			if (obj == null)
				return false;

			var publishReceived = obj as PublishReceived;

			if (publishReceived == null)
				return false;

			return this.Equals(publishReceived);
		}

		public static bool operator ==(PublishReceived publishReceived, PublishReceived other)
		{
			if ((object)publishReceived == null || (object)other == null)
				return Object.Equals(publishReceived, other);

			return publishReceived.Equals(other);
		}

		public static bool operator !=(PublishReceived publishReceived, PublishReceived other)
		{
			if ((object)publishReceived == null || (object)other == null)
				return !Object.Equals(publishReceived, other);

			return !publishReceived.Equals(other);
		}

		public override int GetHashCode()
		{
			return this.PacketId.GetHashCode();
		}
	}
}
