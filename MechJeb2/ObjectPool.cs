using System;
using System.Collections.Generic;
using MuMech;

namespace MechJeb2
{
	public class ObjectPool<T> : IDisposable
	{
		private readonly Stack<PoolItem<T>> itemStore;
		private readonly Func<PoolItem<T>> factory;

		public bool IsDisposed { get; private set; }

		public int Count
		{
			get
			{
				return itemStore.Count;
			}
		}

		public ObjectPool(Func<T> factory, int preallocateAmount)
		{
			itemStore = new Stack<PoolItem<T>>(preallocateAmount);

			this.factory = () =>
			{
				var resource = factory();

				return new PoolItem<T>(this, resource);
			};

			for(var i = 0; i < preallocateAmount; i++)
			{
				itemStore.Push(this.factory());
			}
		}

		public PoolItem<T> Acquire()
		{
			lock(itemStore)
			{
				return itemStore.Count > 0 ? itemStore.Pop() : factory();
			}
		}

		public void Release(PoolItem<T> item)
		{
			lock(itemStore)
			{
				itemStore.Push(item);
			}
		}

		public void Dispose()
		{
			if(IsDisposed)
				return;

			IsDisposed = true;

			if(typeof(IDisposable).IsAssignableFrom(typeof(T)))
			{
				lock(itemStore)
				{
					foreach(var item in itemStore)
					{
						((IDisposable) item).Dispose();
					}
				}
			}
		}
	}

	public class PoolItem<T> : IDisposable
	{
		private readonly ObjectPool<T> pool;
		public T Resource { get; private set; }

		public PoolItem(ObjectPool<T> pool, T resource)
		{
			this.pool = pool;
			Resource = resource;
		}

		public void Dispose()
		{
			if(pool.IsDisposed)
			{
				IDisposable disposable = Resource as IDisposable;

				if(disposable != null)
					disposable.Dispose();
			}
			else
				pool.Release(this);
		}
	}
}
