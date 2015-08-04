namespace System.Net.Mqtt
{
	public interface ITopicEvaluator
	{
		bool IsValidTopicFilter (string topicFilter);

		bool IsValidTopicName (string topicName);

		/// <exception cref="MqttException">MqttException</exception>
		bool Matches (string topicName, string topicFilter);
	}
}
