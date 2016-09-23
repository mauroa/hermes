namespace System.Net.Mqtt.Packets
{
	internal class PublishAck : IOrderedPacket, IEquatable<PublishAck>
	{
		public PublishAck (ushort packetId)
		{
            PacketId = packetId;
            OrderId = Guid.Empty;
        }

        public MqttPacketType Type { get { return MqttPacketType.PublishAck; } }

		public ushort PacketId { get; }

        public Guid OrderId { get; private set; }

        public void AssignOrder (Guid orderId)
        {
            if (OrderId != Guid.Empty) {
                throw new InvalidOperationException (string.Format (Properties.Resources.OrderedPacket_AlreadyAssignedOrder, nameof (PublishAck)));
            }

            OrderId = orderId;
        }

        public bool Equals (PublishAck other)
		{
			if (other == null)
				return false;

			return PacketId == other.PacketId;
		}

		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;

			var publishAck = obj as PublishAck;

			if (publishAck == null)
				return false;

			return Equals (publishAck);
		}

		public static bool operator == (PublishAck publishAck, PublishAck other)
		{
			if ((object)publishAck == null || (object)other == null)
				return Object.Equals (publishAck, other);

			return publishAck.Equals (other);
		}

		public static bool operator != (PublishAck publishAck, PublishAck other)
		{
			if ((object)publishAck == null || (object)other == null)
				return !Object.Equals (publishAck, other);

			return !publishAck.Equals (other);
		}

		public override int GetHashCode ()
		{
			return PacketId.GetHashCode ();
		}
	}
}
