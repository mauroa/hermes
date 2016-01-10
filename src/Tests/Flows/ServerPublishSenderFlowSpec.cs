using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Net.Mqtt;
using System.Net.Mqtt.Flows;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using Moq;
using Xunit;
using System.Net.Mqtt.Server;
using System.Threading;
using System.Net.Mqtt.Ordering;

namespace Tests.Flows
{
	public class ServerPublishSenderFlowSpec
	{
		[Fact]
		public async Task when_forwarding_publish_then_it_is_sent_to_subscribers()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { MaximumQualityOfService = QualityOfService.ExactlyOnce };
			var connectionProvider = new Mock<IConnectionProvider> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();

			var dispatcher = new Mock<IPacketDispatcher> ();
			var dispatcherProvider = new Mock<IPacketDispatcherProvider> ();

			dispatcher
				.Setup (d => d.DispatchAsync (It.IsAny<IDispatchUnit> (), It.IsAny<IChannel<IPacket>> ()))
				.Callback<IDispatchUnit, IChannel<IPacket>> (async (u, c) => {
					await c.SendAsync (u);
				});
			dispatcherProvider
				.Setup (p => p.Get ())
				.Returns (dispatcher.Object);

			var flow = new ServerPublishSenderFlow (connectionProvider.Object,
				packetIdProvider, dispatcherProvider.Object, sessionRepository.Object, configuration);

			var topic = "foo/bar";

			var subscribedClientId1 = Guid.NewGuid().ToString();
			var subscribedClientId2 = Guid.NewGuid().ToString();
			var requestedQoS1 = QualityOfService.AtMostOnce;
			var requestedQoS2 = QualityOfService.AtMostOnce;
			var subscriptions = new List<ClientSubscription> { 
				new ClientSubscription { ClientId = subscribedClientId1, 
					MaximumQualityOfService = requestedQoS1, TopicFilter = topic },
				new ClientSubscription { ClientId = subscribedClientId2, 
					MaximumQualityOfService = requestedQoS2, TopicFilter = topic }
			};

			var client1Receiver = new Subject<IPacket> ();
			var client1Channel = new Mock<IChannel<IPacket>> ();

			client1Channel.Setup (c => c.IsConnected).Returns (true);
			client1Channel.Setup (c => c.Receiver).Returns (client1Receiver);

			var client2Receiver = new Subject<IPacket> ();
			var client2Channel = new Mock<IChannel<IPacket>> ();

			client2Channel.Setup (c => c.IsConnected).Returns (true);
			client2Channel.Setup (c => c.Receiver).Returns (client2Receiver);

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId1)))
				.Returns (client1Channel.Object);
			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId2)))
				.Returns (client2Channel.Object);

			var publish = new Publish (topic, QualityOfService.AtMostOnce, retain: false, duplicated: false);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var publishChannel1Signal = new ManualResetEventSlim ();
			var publishChannel2Signal = new ManualResetEventSlim ();

			client1Channel
				.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => {
					if (packet is Publish && ((Publish)packet).Topic == publish.Topic &&
						((Publish)packet).Payload.ToList ().SequenceEqual (publish.Payload)) {
						publishChannel1Signal.Set ();
					}
				})
				.Returns (Task.Delay (0));

			client2Channel
				.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => {
					if(packet is Publish && ((Publish)packet).Topic == publish.Topic &&
						((Publish)packet).Payload.ToList ().SequenceEqual (publish.Payload)) {
							publishChannel2Signal.Set ();
					}
				})
				.Returns (Task.Delay (0));

			await flow.ForwardPublishAsync (subscriptions, publish)
				.ConfigureAwait(continueOnCapturedContext: false);

			var publishSent = publishChannel1Signal.Wait (1000) && publishChannel2Signal.Wait(1000);

			Assert.True (publishSent);
			client1Channel.Verify (c => c.SendAsync (It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload))));
			client2Channel.Verify (s => s.SendAsync (It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload))));
		}
	}
}
