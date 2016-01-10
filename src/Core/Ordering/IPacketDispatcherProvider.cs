namespace System.Net.Mqtt.Ordering
{
	internal interface IPacketDispatcherProvider : IDisposable
	{
		IPacketDispatcher Get ();

		IPacketDispatcher Get (string clientId);
	}
}
