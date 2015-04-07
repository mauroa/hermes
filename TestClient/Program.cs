using System;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Hermes;

namespace TestClient
{
	class Program
	{
		static void Main (string[] args)
		{
			var subject = new Subject<int> ();

			subject.OnError (new Exception ());
			subject.OnCompleted ();

			Console.WriteLine ("Starting Test MQTT Client...");

			var configuration = new ProtocolConfiguration {
				BufferSize = 128 * 1024,
				Port = Protocol.DefaultNonSecurePort,
				KeepAliveSecs = 0,
				WaitingTimeoutSecs = 5
			};
			var initializer = new ClientInitializer (hostAddress: "127.0.0.1");
			var client = initializer.Initialize (configuration);
			var connected = ConnectAsync (client, new ClientCredentials ("testClient")).Result;

			if(connected)
					Console.WriteLine ("MQTT Client connected successfully...");
			else
					Console.WriteLine ("MQTT has not been connected...");

			Console.ReadKey ();
		}

		private static async Task<bool> ConnectAsync (IClient client, ClientCredentials credentials, bool cleanSession = false)
		{
			try {
				await client.ConnectAsync (credentials, cleanSession);
			} catch (ClientException clientEx) {
				var message = clientEx.Message;
				return false;
			}

			return true;
		}
	}
}
