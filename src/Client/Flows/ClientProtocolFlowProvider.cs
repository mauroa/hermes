using System.Collections.Generic;
using System.Net.Mqtt.Ordering;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;

namespace System.Net.Mqtt.Flows
{
	internal class ClientProtocolFlowProvider : ProtocolFlowProvider
	{
		readonly IPacketDispatcherProvider dispatcherProvider;

		public ClientProtocolFlowProvider (ITopicEvaluator topicEvaluator, 
			IPacketDispatcherProvider dispatcherProvider, 
			IRepositoryProvider repositoryProvider, 
			ProtocolConfiguration configuration)
			: base(topicEvaluator, repositoryProvider, configuration)
		{
			this.dispatcherProvider = dispatcherProvider;
		}

		protected override IDictionary<ProtocolFlowType, IProtocolFlow> InitializeFlows ()
		{
			var flows = new Dictionary<ProtocolFlowType, IProtocolFlow>();

			var sessionRepository = repositoryProvider.GetRepository<ClientSession>();
			var retainedRepository = repositoryProvider.GetRepository<RetainedMessage> ();
			var senderFlow = new PublishSenderFlow (dispatcherProvider, sessionRepository, configuration);

			flows.Add (ProtocolFlowType.Connect, new ClientConnectFlow (sessionRepository, senderFlow));
			flows.Add (ProtocolFlowType.PublishSender, senderFlow);
			flows.Add (ProtocolFlowType.PublishReceiver, new PublishReceiverFlow (topicEvaluator, 
				dispatcherProvider, retainedRepository, sessionRepository, configuration));
			flows.Add (ProtocolFlowType.Subscribe, new ClientSubscribeFlow ());
			flows.Add (ProtocolFlowType.Unsubscribe, new ClientUnsubscribeFlow ());
			flows.Add (ProtocolFlowType.Ping, new PingFlow ());

			return flows;
		}

		protected override bool IsValidPacketType (PacketType packetType)
		{
			return packetType == PacketType.ConnectAck ||
				packetType == PacketType.SubscribeAck ||
				packetType == PacketType.UnsubscribeAck ||
				packetType == PacketType.Publish ||
				packetType == PacketType.PublishAck ||
				packetType == PacketType.PublishComplete ||
				packetType == PacketType.PublishReceived ||
				packetType == PacketType.PublishRelease ||
				packetType == PacketType.PingResponse;
		}
	}
}
