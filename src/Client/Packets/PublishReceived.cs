namespace System.Net.Mqtt.Packets
{
	internal class PublishReceived : IOrderedPacket, IEquatable<PublishReceived>
	{
		public PublishReceived (ushort packetId)
		{
            PacketId = packetId;
            OrderId = Guid.Empty;
        }

        public MqttPacketType Type { get { return MqttPacketType.PublishReceived; } }

		public ushort PacketId { get; }

        public Guid OrderId { get; private set; }

        public void AssignOrder (Guid orderId)
        {
            if (OrderId != Guid.Empty) {
                throw new InvalidOperationException (string.Format (Properties.Resources.OrderedPacket_AlreadyAssignedOrder, nameof (PublishReceived)));
            }

            OrderId = orderId;
        }

        public bool Equals (PublishReceived other)
		{
			if (other == null)
				return false;

			return PacketId == other.PacketId;
		}

		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;

			var publishReceived = obj as PublishReceived;

			if (publishReceived == null)
				return false;

			return Equals (publishReceived);
		}

		public static bool operator == (PublishReceived publishReceived, PublishReceived other)
		{
			if ((object)publishReceived == null || (object)other == null)
				return Object.Equals (publishReceived, other);

			return publishReceived.Equals (other);
		}

		public static bool operator != (PublishReceived publishReceived, PublishReceived other)
		{
			if ((object)publishReceived == null || (object)other == null)
				return !Object.Equals (publishReceived, other);

			return !publishReceived.Equals (other);
		}

		public override int GetHashCode ()
		{
			return PacketId.GetHashCode ();
		}
	}
}
