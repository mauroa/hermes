namespace System.Net.Mqtt.Packets
{
	internal interface IDispatchUnit : IIdentifiablePacket
	{
		Guid DispatchId { get; }
	}
}
