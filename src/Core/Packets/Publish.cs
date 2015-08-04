using System.Linq;

namespace System.Net.Mqtt.Packets
{
	internal class Publish : IIdentifiablePacket, IDispatchUnit, IEquatable<Publish>
    {
        public Publish(string topic, QualityOfService qualityOfService, bool retain, bool duplicated, ushort packetId = default(ushort))
			: this(topic, qualityOfService, retain, duplicated, packetId, Guid.NewGuid())
        {
        }

		internal Publish(string topic, QualityOfService qualityOfService, bool retain, bool duplicated, ushort packetId, Guid dispatchId)
        {
            this.QualityOfService = qualityOfService;
			this.Duplicated = duplicated;
			this.Retain = retain;
			this.Topic = topic;
            this.PacketId = packetId;
			this.DispatchId = dispatchId;
        }

		public PacketType Type { get { return PacketType.Publish; }}

		public ushort PacketId { get; private set; }

		public QualityOfService QualityOfService { get; private set; }

		public bool Duplicated { get; private set; }

		public bool Retain { get; private set; }

        public string Topic { get; private set; }

		public byte[] Payload { get; set; }

		public Guid DispatchId { get; private set; }

		public bool Equals (Publish other)
		{
			if (other == null)
				return false;

			var equals = this.QualityOfService == other.QualityOfService &&
				this.Duplicated == other.Duplicated &&
				this.Retain == other.Retain &&
				this.Topic == other.Topic &&
				this.PacketId == other.PacketId;

			if(this.Payload != null) {
				equals &= this.Payload.ToList().SequenceEqual(other.Payload);
			}

			return equals;
		}

		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;

			var publish = obj as Publish;

			if (publish == null)
				return false;

			return this.Equals (publish);
		}

		public static bool operator == (Publish publish, Publish other)
		{
			if ((object)publish == null || (object)other == null)
				return Object.Equals(publish, other);

			return publish.Equals(other);
		}

		public static bool operator != (Publish publish, Publish other)
		{
			if ((object)publish == null || (object)other == null)
				return !Object.Equals(publish, other);

			return !publish.Equals(other);
		}

		public override int GetHashCode ()
		{
			var hashCode = this.QualityOfService.GetHashCode () +
				this.Duplicated.GetHashCode () +
				this.Retain.GetHashCode () +
				this.Topic.GetHashCode ();

			if (this.Payload != null) {
				hashCode += BitConverter.ToString (this.Payload).GetHashCode ();
			}

			if (this.PacketId != default(ushort)) {
				hashCode += this.PacketId.GetHashCode ();
			}

			return hashCode;
		}
	}
}
