using System.Collections.Generic;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Reactive.Subjects;

namespace System.Net.Mqtt.Flows
{
    internal class ServerProtocolFlowProvider : ProtocolFlowProvider
	{
		readonly IMqttAuthenticationProvider authenticationProvider;
		readonly IConnectionProvider connectionProvider;
        readonly IPacketDispatcherProvider dispatcherProvider;
        readonly IPacketIdProvider packetIdProvider;
		readonly ISubject<MqttUndeliveredMessage> undeliveredMessagesListener;

		public ServerProtocolFlowProvider (IMqttAuthenticationProvider authenticationProvider,
			IConnectionProvider connectionProvider,
            IPacketDispatcherProvider dispatcherProvider,
            IMqttTopicEvaluator topicEvaluator,
			IRepositoryProvider repositoryProvider,
			IPacketIdProvider packetIdProvider,
            ISubject<MqttUndeliveredMessage> undeliveredMessagesListener,
			MqttConfiguration configuration)
			: base (topicEvaluator, repositoryProvider, configuration)
		{
			this.authenticationProvider = authenticationProvider;
			this.connectionProvider = connectionProvider;
            this.dispatcherProvider = dispatcherProvider;
			this.packetIdProvider = packetIdProvider;
			this.undeliveredMessagesListener = undeliveredMessagesListener;
		}

		protected override IDictionary<ProtocolFlowType, IProtocolFlow> InitializeFlows ()
		{
			var flows = new Dictionary<ProtocolFlowType, IProtocolFlow>();

			var sessionRepository = repositoryProvider.GetRepository<ClientSession>();
			var willRepository = repositoryProvider.GetRepository<ConnectionWill> ();
			var retainedRepository = repositoryProvider.GetRepository<RetainedMessage> ();
			var senderFlow = new ServerPublishSenderFlow (connectionProvider, packetIdProvider, dispatcherProvider, sessionRepository, configuration);

			flows.Add (ProtocolFlowType.Connect, new ServerConnectFlow (authenticationProvider, dispatcherProvider, 
                sessionRepository, willRepository, senderFlow));
			flows.Add (ProtocolFlowType.PublishSender, senderFlow);
			flows.Add (ProtocolFlowType.PublishReceiver, new ServerPublishReceiverFlow (topicEvaluator, connectionProvider, dispatcherProvider,
				senderFlow, retainedRepository, sessionRepository, willRepository, packetIdProvider, undeliveredMessagesListener, configuration));
			flows.Add (ProtocolFlowType.Subscribe, new ServerSubscribeFlow (senderFlow, dispatcherProvider,
                packetIdProvider, topicEvaluator, sessionRepository, retainedRepository, configuration));
			flows.Add (ProtocolFlowType.Unsubscribe, new ServerUnsubscribeFlow (sessionRepository));
			flows.Add (ProtocolFlowType.Ping, new PingFlow ());
			flows.Add (ProtocolFlowType.Disconnect, new DisconnectFlow (connectionProvider, sessionRepository, willRepository));

			return flows;
		}

		protected override bool IsValidPacketType (MqttPacketType packetType)
		{
			return packetType == MqttPacketType.Connect ||
				packetType == MqttPacketType.Subscribe ||
				packetType == MqttPacketType.Unsubscribe ||
				packetType == MqttPacketType.Publish ||
				packetType == MqttPacketType.PublishAck ||
				packetType == MqttPacketType.PublishComplete ||
				packetType == MqttPacketType.PublishReceived ||
				packetType == MqttPacketType.PublishRelease ||
				packetType == MqttPacketType.PingRequest ||
				packetType == MqttPacketType.Disconnect;
		}
	}
}
