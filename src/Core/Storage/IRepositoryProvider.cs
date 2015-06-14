﻿namespace System.Net.Mqtt.Storage
{
	public interface IRepositoryProvider
	{
		IRepository<T> GetRepository<T> () where T : StorageObject;
	}
}
