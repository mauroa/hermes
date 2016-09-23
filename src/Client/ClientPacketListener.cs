using System.Diagnostics;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Flows;
using System.Net.Mqtt.Packets;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace System.Net.Mqtt
{
    internal class ClientPacketListener : IPacketListener
	{
		static readonly ITracer tracer = Tracer.Get<ClientPacketListener> ();

		readonly IMqttChannel<IPacket> channel;
		readonly IProtocolFlowProvider flowProvider;
        readonly IPacketDispatcherProvider dispatcherProvider;
		readonly MqttConfiguration configuration;
		readonly ReplaySubject<IPacket> packets;
		readonly TaskRunner flowRunner;
		IDisposable listenerDisposable;
		bool disposed;
		string clientId = string.Empty;
		Timer keepAliveTimer;

		public ClientPacketListener (IMqttChannel<IPacket> channel, 
			IProtocolFlowProvider flowProvider,
            IPacketDispatcherProvider dispatcherProvider,
            MqttConfiguration configuration)
		{
			this.channel = channel;
			this.flowProvider = flowProvider;
            this.dispatcherProvider = dispatcherProvider;
			this.configuration = configuration;
			packets = new ReplaySubject<IPacket> (window: TimeSpan.FromSeconds (configuration.WaitTimeoutSecs));
			flowRunner = TaskRunner.Get ();
		}

		public IObservable<IPacket> PacketStream { get { return packets; } }

		public void Listen ()
		{
			if (disposed) {
				throw new ObjectDisposedException (GetType ().FullName);
			}

			listenerDisposable = new CompositeDisposable (
				ListenFirstPacket (),
				ListenNextPackets (),
				ListenCompletionAndErrors (),
				ListenSentConnectPacket (),
				ListenSentDisconnectPacket ());
		}

		public void Dispose ()
		{
			Dispose (disposing: true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (disposed) {
				return;
			}

			if (disposing) {
				tracer.Info (Properties.Resources.Mqtt_Disposing, GetType ().FullName);

				listenerDisposable.Dispose ();
				StopKeepAliveMonitor ();
				packets.OnCompleted ();
                dispatcherProvider.Dispose ();
				(flowRunner as IDisposable)?.Dispose ();
				disposed = true;
			}
		}

		IDisposable ListenFirstPacket ()
		{
			return channel
                .ReceiverStream
				.FirstOrDefaultAsync ()
				.Subscribe (async packet => {
					if (packet == default (IPacket)) {
						return;
					}

					tracer.Info (Properties.Resources.ClientPacketListener_FirstPacketReceived, clientId, packet.Type);

					var connectAck = packet as ConnectAck;

					if (connectAck == null) {
						NotifyError (Properties.Resources.ClientPacketListener_FirstReceivedPacketMustBeConnectAck);
						return;
					}

					await ExecuteFlowAsync (packet)
						.ConfigureAwait (continueOnCapturedContext: false);
				}, ex => {
					NotifyError (ex);
				});
		}

		IDisposable ListenNextPackets ()
		{
			return channel
                .ReceiverStream
				.Skip (1)
				.Subscribe (async packet => {
                    RegisterForDispatch (packet as IOrderedPacket);
                     
					await ExecuteFlowAsync (packet)
						.ConfigureAwait (continueOnCapturedContext: false);
				}, ex => {
					NotifyError (ex);
				});
		}

		IDisposable ListenCompletionAndErrors ()
		{
			return channel
                .ReceiverStream
                .Subscribe (_ => { },
				    ex => {
					    NotifyError (ex);
				    }, () => {
					    tracer.Warn (Properties.Resources.ClientPacketListener_PacketChannelCompleted, clientId);

					    packets.OnCompleted ();
				    }
                );
		}

		IDisposable ListenSentConnectPacket ()
		{
			return channel.SenderStream
				.OfType<Connect> ()
				.FirstAsync ()
				.ObserveOn (NewThreadScheduler.Default)
				.Subscribe (connect => {
					clientId = connect.ClientId;

					if (configuration.KeepAliveSecs > 0) {
						StartKeepAliveMonitor ();
					}
				});
		}

		IDisposable ListenSentDisconnectPacket ()
		{
			return channel.SenderStream
				.OfType<Disconnect> ()
				.FirstAsync ()
				.ObserveOn (NewThreadScheduler.Default)
				.Subscribe (disconnect => {
					if (configuration.KeepAliveSecs > 0) {
						StopKeepAliveMonitor ();
					}
				});
		}

		void StartKeepAliveMonitor ()
		{
			var interval = configuration.KeepAliveSecs * 1000;

			keepAliveTimer = new Timer ();

			keepAliveTimer.AutoReset = true;
			keepAliveTimer.IntervalMillisecs = interval;
			keepAliveTimer.Elapsed += async (sender, e) => {
				try {
					tracer.Warn (Properties.Resources.ClientPacketListener_SendingKeepAlive, clientId, configuration.KeepAliveSecs);

					var ping = new PingRequest ();

					await channel.SendAsync (ping)
						.ConfigureAwait (continueOnCapturedContext: false);
				} catch (Exception ex) {
					NotifyError (ex);
				}
			};
			keepAliveTimer.Start ();

			channel.SenderStream.Subscribe (p => {
				keepAliveTimer.IntervalMillisecs = interval;
			});
		}

		void StopKeepAliveMonitor ()
		{
			if (keepAliveTimer != null) {
				keepAliveTimer.Stop ();
			}
		}

        void RegisterForDispatch (IOrderedPacket packet)
        {
            if (packet == null)  {
                return;
            }

            //No dispatch registration since QoS finishes with these types
            if (packet.Type == MqttPacketType.PublishAck || packet.Type == MqttPacketType.PublishComplete) {
                return;
            }

            var clientDispatcher = dispatcherProvider.GetDispatcher (clientId);
            var dispatchType = default (DispatchPacketType);

            switch (packet.Type) {
                case MqttPacketType.Publish:
                    dispatchType = DispatchPacketType.PublishAck1;
                    break;
                case MqttPacketType.PublishReceived:
                    dispatchType = DispatchPacketType.PublishAck2;
                    break;
                case MqttPacketType.PublishRelease:
                    dispatchType = DispatchPacketType.PublishAck3;
                    break;
            }

            var orderId = clientDispatcher.CreateOrder (dispatchType);

            packet.AssignOrder (orderId);
        }

        async Task ExecuteFlowAsync (IPacket packet)
		{
			var flow = flowProvider.GetFlow (packet.Type);

			if (flow == null) {
                return;
			}

            try {
                TraceExecution (packet, flow);
                packets.OnNext (packet);

                await flowRunner.Run (async () => {
                    await flow
                        .ExecuteAsync (clientId, packet, channel)
                        .ConfigureAwait (continueOnCapturedContext: false);
                }).ConfigureAwait(continueOnCapturedContext: false);
            } catch (Exception ex) {
                NotifyError(ex);
            }
        }

        void TraceExecution (IPacket packet, IProtocolFlow flow)
 		{
 			if (packet.Type == MqttPacketType.Publish) {
 				var publish = packet as Publish;
 
 				tracer.Info (Properties.Resources.ClientPacketListener_DispatchingPublish, clientId, flow.GetType ().Name, publish.Topic);
 			} else {
 				tracer.Info (Properties.Resources.ClientPacketListener_DispatchingMessage, clientId, packet.Type, flow.GetType ().Name);
 			}
 		}

        void NotifyError (Exception exception)
		{
			tracer.Error (exception, Properties.Resources.ClientPacketListener_Error);

			packets.OnError (exception);
		}

		void NotifyError (string message)
		{
			NotifyError (new MqttException (message));
		}

		void NotifyError (string message, Exception exception)
		{
			NotifyError (new MqttException (message, exception));
		}
	}
}
