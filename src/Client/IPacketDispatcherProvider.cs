namespace System.Net.Mqtt
{
    internal interface IPacketDispatcherProvider : IDisposable
    {
        IPacketDispatcher GetDispatcher (string clientId);
    }
}
