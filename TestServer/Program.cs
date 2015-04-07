using System;
using System.Threading;
using Hermes;

namespace TestServer
{
	class Program
	{
		static void Main (string[] args)
		{
			Console.WriteLine ("Starting Test MQTT Server...");

			var configuration = new ProtocolConfiguration {
				BufferSize = 128 * 1024,
				Port = Protocol.DefaultNonSecurePort,
				KeepAliveSecs = 0,
				WaitingTimeoutSecs = 5,
			};
			var initializer = new ServerInitializer ();
			var server = initializer.Initialize (configuration);

			server.Start ();

			Console.WriteLine ("MQTT Server Started Successfully...");

			while (true) {
				Thread.Sleep (1000);

				var clients = server.ActiveClients;
			}
		}
	}
}
