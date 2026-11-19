using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.Core
{
    public interface IEventBus
    {
        void Publish<T>(in T evt) where T : struct;
        IDisposable Subscribe<T>(Action<T> handler) where T : struct;
    }

    /// <summary>
    /// Allocation-free (on publish) typed event bus. Handlers are isolated: an exception in one
    /// handler is logged and does not prevent other handlers from receiving the event.
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        private interface IChannel { }

        private sealed class Channel<T> : IChannel where T : struct
        {
            public readonly List<Action<T>> Handlers = new List<Action<T>>();
            public int PublishDepth;
            public bool Dirty;
        }

        private sealed class Subscription<T> : IDisposable where T : struct
        {
            private Channel<T> _channel;
            private Action<T> _handler;

            public Subscription(Channel<T> channel, Action<T> handler) { _channel = channel; _handler = handler; }

            public void Dispose()
            {
                if (_channel == null) return;
                int idx = _channel.Handlers.IndexOf(_handler);
                if (idx >= 0)
                {
                    if (_channel.PublishDepth > 0) { _channel.Handlers[idx] = null; _channel.Dirty = true; }
                    else _channel.Handlers.RemoveAt(idx);
                }
                _channel = null;
                _handler = null;
            }
        }

        private readonly Dictionary<Type, IChannel> _channels = new Dictionary<Type, IChannel>();

        private Channel<T> GetChannel<T>() where T : struct
        {
            if (!_channels.TryGetValue(typeof(T), out var c))
            {
                c = new Channel<T>();
                _channels[typeof(T)] = c;
            }
            return (Channel<T>)c;
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var ch = GetChannel<T>();
            ch.Handlers.Add(handler);
            return new Subscription<T>(ch, handler);
        }

        public void Publish<T>(in T evt) where T : struct
        {
            if (!_channels.TryGetValue(typeof(T), out var c)) return;
            var ch = (Channel<T>)c;
            ch.PublishDepth++;
            try
            {
                // Iterate by index so handlers added during publish are not invoked this round.
                int count = ch.Handlers.Count;
                for (int i = 0; i < count; i++)
                {
                    var h = ch.Handlers[i];
                    if (h == null) continue;
                    try { h(evt); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
            finally
            {
                ch.PublishDepth--;
                if (ch.PublishDepth == 0 && ch.Dirty)
                {
                    ch.Handlers.RemoveAll(x => x == null);
                    ch.Dirty = false;
                }
            }
        }
    }

    /// <summary>Convenience static access to the registered bus.</summary>
    public static class GameEvents
    {
        /// <summary>The registered bus; created and registered on first use if missing.</summary>
        public static IEventBus Bus
        {
            get
            {
                if (!Services.TryGet<IEventBus>(out var bus))
                {
                    bus = new EventBus();
                    Services.Register(bus);
                }
                return bus;
            }
        }

        public static void Publish<T>(in T evt) where T : struct => Bus.Publish(evt);

        public static IDisposable Subscribe<T>(Action<T> handler) where T : struct => Bus.Subscribe(handler);
    }
}
