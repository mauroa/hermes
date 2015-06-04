﻿using System;

namespace Hermes.Packets
{
	public class PublishRelease : IFlowPacket, IPublishPacket, IEquatable<PublishRelease>
    {
        public PublishRelease(ushort packetId)
        {
            this.PacketId = packetId;
			this.Id = Guid.NewGuid ();
        }

		public Guid Id { get; private set; }

		public PacketType Type { get { return PacketType.PublishRelease; }}

        public ushort PacketId { get; private set; }

		public bool Equals (PublishRelease other)
		{
			if (other == null)
				return false;

			return this.PacketId == other.PacketId;
		}

		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;

			var publishRelease = obj as PublishRelease;

			if (publishRelease == null)
				return false;

			return this.Equals (publishRelease);
		}

		public static bool operator == (PublishRelease publishRelease, PublishRelease other)
		{
			if ((object)publishRelease == null || (object)other == null)
				return Object.Equals(publishRelease, other);

			return publishRelease.Equals(other);
		}

		public static bool operator != (PublishRelease publishRelease, PublishRelease other)
		{
			if ((object)publishRelease == null || (object)other == null)
				return !Object.Equals(publishRelease, other);

			return !publishRelease.Equals(other);
		}

		public override int GetHashCode ()
		{
			return this.PacketId.GetHashCode ();
		}
    }
}
