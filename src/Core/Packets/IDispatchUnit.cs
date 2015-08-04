namespace System.Net.Mqtt.Packets
{
	internal interface IDispatchUnit : IPacket
	{
		Guid DispatchId { get; }
	}
}
