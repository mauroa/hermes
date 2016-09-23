namespace System.Net.Mqtt.Packets
{
	internal class PublishRelease : IOrderedPacket, IEquatable<PublishRelease>
	{
		public PublishRelease (ushort packetId)
		{
            PacketId = packetId;
            OrderId = Guid.Empty;
        }

        public MqttPacketType Type { get { return MqttPacketType.PublishRelease; } }

		public ushort PacketId { get; }

        public Guid OrderId { get; private set; }

        public void AssignOrder (Guid orderId)
        {
            if (OrderId != Guid.Empty) {
                throw new InvalidOperationException (string.Format (Properties.Resources.OrderedPacket_AlreadyAssignedOrder, nameof (PublishRelease)));
            }

            OrderId = orderId;
        }

        public bool Equals (PublishRelease other)
		{
			if (other == null)
				return false;

			return PacketId == other.PacketId;
		}

		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;

			var publishRelease = obj as PublishRelease;

			if (publishRelease == null)
				return false;

			return Equals (publishRelease);
		}

		public static bool operator == (PublishRelease publishRelease, PublishRelease other)
		{
			if ((object)publishRelease == null || (object)other == null)
				return Object.Equals (publishRelease, other);

			return publishRelease.Equals (other);
		}

		public static bool operator != (PublishRelease publishRelease, PublishRelease other)
		{
			if ((object)publishRelease == null || (object)other == null)
				return !Object.Equals (publishRelease, other);

			return !publishRelease.Equals (other);
		}

		public override int GetHashCode ()
		{
			return PacketId.GetHashCode ();
		}
	}
}
