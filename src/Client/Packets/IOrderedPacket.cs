namespace System.Net.Mqtt.Packets
{
    internal interface IOrderedPacket : IIdentifiablePacket
    {
        Guid OrderId { get; }

        void AssignOrder (Guid orderId);
    }
}
