using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Net.Mqtt.Diagnostics;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Net.Mqtt.Server;
using Props = System.Net.Mqtt.Server.Properties;
using System.Net.Mqtt.Ordering;

namespace System.Net.Mqtt.Flows
{
	internal class ServerPublishReceiverFlow : PublishReceiverFlow
	{
		static readonly ITracer tracer = Tracer.Get<ServerPublishReceiverFlow> ();

		readonly IConnectionProvider connectionProvider;
		readonly IServerPublishSenderFlow senderFlow;
		readonly IRepository<ConnectionWill> willRepository;
		readonly IPacketIdProvider packetIdProvider;
		readonly IEventStream eventStream;

		public ServerPublishReceiverFlow (ITopicEvaluator topicEvaluator,
			IPacketDispatcherProvider dispatcherProvider,
			IConnectionProvider connectionProvider,
			IServerPublishSenderFlow senderFlow,
			IRepository<RetainedMessage> retainedRepository, 
			IRepository<ClientSession> sessionRepository,
			IRepository<ConnectionWill> willRepository,
			IPacketIdProvider packetIdProvider,
			IEventStream eventStream,
			ProtocolConfiguration configuration)
			: base(topicEvaluator, dispatcherProvider, retainedRepository, sessionRepository, configuration)
		{
			this.connectionProvider = connectionProvider;
			this.senderFlow = senderFlow;
			this.willRepository = willRepository;
			this.packetIdProvider = packetIdProvider;
			this.eventStream = eventStream;
		}

		protected override async Task ProcessPublishAsync (Publish publish, string clientId)
		{
			if (publish.Retain) {
				var existingRetainedMessage = this.retainedRepository.Get(r => r.Topic == publish.Topic);

				if(existingRetainedMessage != null) {
					this.retainedRepository.Delete(existingRetainedMessage);
				}

				if (publish.Payload.Length > 0) {
					var retainedMessage = new RetainedMessage {
						Topic = publish.Topic,
						QualityOfService = publish.QualityOfService,
						Payload = publish.Payload
					};

					this.retainedRepository.Create(retainedMessage);
				}
			}

			await this.ForwardPublishAsync (publish, clientId)
				.ConfigureAwait(continueOnCapturedContext: false);
		}

		internal async Task SendWillAsync(string clientId)
		{
			var will = this.willRepository.Get (w => w.ClientId == clientId);

			if (will != null && will.Will != null) {
				var willPublish = new Publish (will.Will.Topic, will.Will.QualityOfService, will.Will.Retain, duplicated: false) {
					Payload = Encoding.UTF8.GetBytes (will.Will.Message)
				};

				tracer.Info (Props.Resources.Tracer_ServerPublishReceiverFlow_SendingWill, clientId, willPublish.Topic);

				await this.ForwardPublishAsync(willPublish, clientId, isWill: true)
					.ConfigureAwait(continueOnCapturedContext: false);
			}
		}

		private async Task ForwardPublishAsync (Publish publish, string clientId, bool isWill = false)
		{
			var subscriptions = this.sessionRepository
				.GetAll ().ToList ()
				.SelectMany (s => s.GetSubscriptions ())
				.Where (x => this.topicEvaluator.Matches (publish.Topic, x.TopicFilter));

			if (subscriptions.Any ()) {
				await this.senderFlow.ForwardPublishAsync (subscriptions, publish, isWill)
					.ConfigureAwait(continueOnCapturedContext: false);
			} else {
				tracer.Verbose (Props.Resources.Tracer_ServerPublishReceiverFlow_TopicNotSubscribed, publish.Topic, clientId);

				this.eventStream.Push (new TopicNotSubscribed { Topic = publish.Topic, SenderId = clientId, Payload = publish.Payload });
				this.dispatcherProvider.Get ().Complete (publish.DispatchId);
			}
		}
	}
}
