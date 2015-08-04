namespace System.Net.Mqtt.Packets
{
	internal interface IIdentifiablePacket : IPacket
    {
		ushort PacketId { get; }
	}
}
