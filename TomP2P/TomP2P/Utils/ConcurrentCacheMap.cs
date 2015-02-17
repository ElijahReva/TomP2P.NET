﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using TomP2P.Extensions;
using TomP2P.Extensions.Workaround;

namespace TomP2P.Utils
{
    public class ConcurrentCacheMap<TKey, TValue> where TValue : class 
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Number of segments that can be accessed concurrently.
        /// </summary>
        public const int SegmentNr = 16;
        /// <summary>
        /// Max. number of entries that the map can hold until the least recently used gets replaced.
        /// </summary>
        public const int MaxEntries = 1024;
        /// <summary>
        /// Time to live for a value. The value may stay longer in the map, but it is considered invalid.
        /// </summary>
        public const int DefaultTimeToLive = 60;

        private readonly CacheMap<TKey, ExpiringObject>[] _segments;

        private readonly int _timeToLiveSeconds;
        private readonly bool _refreshTimeout;
        private readonly VolatileInteger _removedCounter = new VolatileInteger();

        /// <summary>
        /// Creates a new instance of ConcurrentCacheMap using the default values
        /// and a <see cref="CacheMap{TKey,TValue}"/> for the internal data structure.
        /// </summary>
        public ConcurrentCacheMap()
            : this(DefaultTimeToLive, MaxEntries, true)
        { }

        /// <summary>
        /// Creates a new instance of ConcurrentCacheMap using the supplied values
        /// and a <see cref="CacheMap{TKey,TValue}"/> for the internal data structure.
        /// </summary>
        /// <param name="timeToLiveSeconds">The time-to-live value (seconds).</param>
        /// <param name="maxEntries">The maximum number of entries until items gets replaced with LRU.</param>
        public ConcurrentCacheMap(int timeToLiveSeconds, int maxEntries)
            : this(timeToLiveSeconds, maxEntries, true)
        { }

        /// <summary>
        /// Creates a new instance of ConcurrentCacheMap using the default values
        /// and a <see cref="CacheMap{TKey,TValue}"/> for the internal data structure.
        /// </summary>
        /// /// <param name="timeToLiveSeconds">The time-to-live value (seconds).</param>
        /// <param name="maxEntries">The maximum number of entries until items gets replaced with LRU.</param>
        /// <param name="refreshTimeout">If set to true, timeout will be reset in case of PutIfAbsent().</param>
        public ConcurrentCacheMap(int timeToLiveSeconds, int maxEntries, bool refreshTimeout)
        {
            _segments = new CacheMap<TKey, ExpiringObject>[SegmentNr];
            int maxEntriesPerSegment = maxEntries/SegmentNr;
            for (int i = 0; i < SegmentNr; i++)
            {
                // set updateOnInsert to true, since it should behave as a regular map
                _segments[i] = new CacheMap<TKey, ExpiringObject>(maxEntriesPerSegment, true);
            }
            _timeToLiveSeconds = timeToLiveSeconds;
            _refreshTimeout = refreshTimeout;
        }

        /// <summary>
        /// Returns the segment based on the key.
        /// </summary>
        /// <param name="key">The key where the hash code identifies the segment.</param>
        /// <returns>The cache map that corresponds to this segment.</returns>
        private CacheMap<TKey, ExpiringObject> Segment(object key)
        {
            // TODO works? interoperability concern if object.hashCode is impl by framework
            return _segments[(key.GetHashCode() & Int32.MaxValue) % SegmentNr];
        }

        public TValue Put(TKey key, TValue value)
        {
            var newValue = new ExpiringObject(value, Convenient.CurrentTimeMillis(), _timeToLiveSeconds);
            var segment = Segment(key);
            ExpiringObject oldValue;
            lock (segment)
            {
                oldValue = segment.Add(key, newValue);
            }
            if (oldValue == null || oldValue.IsExpired)
            {
                return null;
            }
            return oldValue.Value;
        }

        /// <summary>
        /// This does not reset the timer!
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public TValue PutIfAbsent(TKey key, TValue value)
        {
            var newValue = new ExpiringObject(value, Convenient.CurrentTimeMillis(), _timeToLiveSeconds);
            var segment = Segment(key);
            ExpiringObject oldValue;
            lock (segment)
            {
                if (!segment.ContainsKey(key))
                {
                    oldValue = segment.Add(key, newValue);
                }
                else
                {
                    oldValue = segment.Get(key);
                    if (oldValue.IsExpired)
                    {
                        segment.Add(key, newValue);
                    }
                    else if (_refreshTimeout)
                    {
                        oldValue = new ExpiringObject(oldValue.Value, Convenient.CurrentTimeMillis(), _timeToLiveSeconds);
                        segment.Add(key, oldValue);
                    }
                }
            }
            if (oldValue == null || oldValue.IsExpired)
            {
                return null;
            }
            return oldValue.Value;
        }

        // TODO in Java, this method allows key to be object
        public TValue Get(TKey key)
        {
            var segment = Segment(key);
            ExpiringObject oldValue;
            lock (segment)
            {
                oldValue = segment.Get(key);
            }
            if (oldValue != null)
            {
                if (Expire(segment, key, oldValue))
                {
                    return null;
                }
                else
                {
                    Logger.Debug("Get found. Key: {0}. Value: {1}.", key, oldValue.Value);
                    return oldValue.Value;
                }
            }
            Logger.Debug("Get not found. Key: {0}.", key);
            return null;
        }

        public TValue Remove(TKey key)
        {
            var segment = Segment(key);
            ExpiringObject oldValue;
            lock (segment)
            {
                oldValue = segment.Remove(key);
            }
            if (oldValue == null || oldValue.IsExpired)
            {
                return null;
            }
            return oldValue.Value;
        }

        public bool Remove(TKey key, TValue value)
        {
            var segment = Segment(key);
            ExpiringObject oldValue;
            bool removed = false;
            lock (segment)
            {
                oldValue = segment.Get(key);
                if (oldValue != null && oldValue.Equals(value) && !oldValue.IsExpired)
                {
                    removed = segment.Remove(key) != null;
                }
            }
            if (oldValue != null)
            {
                Expire(segment, key, oldValue);
            }
            return removed;
        }

        public bool ContainsKey(TKey key)
        {
            var segment = Segment(key);
            ExpiringObject oldValue;
            lock (segment)
            {
                oldValue = segment.Get(key);
            }
            if (oldValue != null)
            {
                if (!Expire(segment, key, oldValue))
                {
                    return true;
                }
            }
            return false;
        }

        // TODO ContainsValue(TValue value) needed?

        public int Size
        {
            get
            {
                var size = 0;
                foreach (var segment in _segments)
                {
                    lock (segment)
                    {
                        ExpireSegment(segment);
                        size += segment.Count();
                    }
                }
                return size;
            }
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var segment in _segments)
                {
                    lock (segment)
                    {
                        ExpireSegment(segment);
                        if (segment.Count() != 0)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        public void Clear()
        {
            foreach (var segment in _segments)
            {
                lock (segment)
                {
                    segment.Clear();
                }
            }
        }

        public override int GetHashCode()
        {
            int hashCode = 0;
            foreach (var segment in _segments)
            {
                lock (segment)
                {
                    ExpireSegment(segment);
                    hashCode += segment.GetHashCode();
                }
            }
            return hashCode;
        }

        public ISet<TKey> KeySet
        {
            get
            {
                var keySet = new HashSet<TKey>();
                foreach (var segment in _segments)
                {
                    lock (segment)
                    {
                        ExpireSegment(segment);
                        keySet.UnionWith(segment.KeySet());
                    }
                }
                return keySet;
            }
        }

        public void PutAll<TKey2, TValue2>(IDictionary<TKey2, TValue2> inMap)
            where TKey2 : TKey
            where TValue2 : TValue
        {
            foreach (var kvp in inMap)
            {
                Put(kvp.Key, kvp.Value);
            }
        }

        public ICollection<TValue> Values
        {
            get
            {
                foreach (var segment in _segments)
                {
                    lock (segment)
                    {
                        foreach (var expObj in segment) // iterate over copy
                        {
                            if (expObj.IsExpired)
                            {
                                // remove from original collection
                                segment.Remove()
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// An object that also holds expiration information.
        /// </summary>
        private class ExpiringObject : IEquatable<ExpiringObject>
        {
            /// <summary>
            /// The wrapped value.
            /// </summary>
            public TValue Value { get; private set; }
            private readonly long _lastAccessTimeMillis;

            //.NET-specific: use current _timeToLiveSeconds from ConcurrentCacheMap field
            private readonly int _timeToLiveSeconds;

            /// <summary>
            /// Creates a new expiring object with the given time of access.
            /// </summary>
            /// <param name="value">The value that is wrapped in this instance.</param>
            /// <param name="lastAccessTimeMillis">The time of access in milliseconds.</param>
            /// <param name="timeToLiveSeconds">.NET-specific: use current _timeToLiveSeconds from ConcurrentCacheMap field.</param>
            public ExpiringObject(TValue value, long lastAccessTimeMillis, int timeToLiveSeconds)
            {
                if (value == null)
                {
                    throw new ArgumentException("An expiring object cannot be null.");
                }
                Value = value;
                _lastAccessTimeMillis = lastAccessTimeMillis;
                _timeToLiveSeconds = timeToLiveSeconds;
            }

            /// <summary>
            /// Indicates whether the entry is expired.
            /// </summary>
            /// <returns></returns>
            public bool IsExpired
            {
                get
                {
                    // TODO correct?
                    return Convenient.CurrentTimeMillis() >=
                           _lastAccessTimeMillis + TimeSpan.FromSeconds(_timeToLiveSeconds).Milliseconds;
                }
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(obj, null))
                {
                    return false;
                }
                if (ReferenceEquals(this, obj))
                {
                    return true;
                }
                if (GetType() != obj.GetType())
                {
                    return false;
                }
                return Equals(obj as ExpiringObject);
            }

            public bool Equals(ExpiringObject other)
            {
                return Value.Equals(other.Value);
            }

            public override int GetHashCode()
            {
                return Value.GetHashCode();
            }
        }
    }
}
