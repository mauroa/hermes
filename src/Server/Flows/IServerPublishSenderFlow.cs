using System.Collections.Generic;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Flows
{
	internal interface IServerPublishSenderFlow : IPublishSenderFlow
	{
		Task ForwardPublishAsync (IEnumerable<ClientSubscription> subscriptions, Publish message, bool isWill = false);
	}
}
