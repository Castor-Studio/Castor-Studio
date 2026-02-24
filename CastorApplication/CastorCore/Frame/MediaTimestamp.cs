using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorCore.Frame
{
    public readonly struct MediaTimestamp : IComparable<MediaTimestamp>, IEquatable<MediaTimestamp>
    {
        const double MICROSECOND_PER_MILLISECOND = 1000.0;
        const double SECOND_PER_MILLISECOND = 1000000.0;

        static public MediaTimestamp Zero
        {
            get => new MediaTimestamp(0);
        }
        
        public long Microseconds { get; }

        public MediaTimestamp(long microseconds)
        {
            Microseconds = microseconds;
        }

        public static MediaTimestamp FromTimeSpan(TimeSpan ts)
            => new MediaTimestamp((long)(ts.TotalMilliseconds * MICROSECOND_PER_MILLISECOND));

        public TimeSpan ToTimeSpan()
            => TimeSpan.FromMilliseconds(Microseconds / MICROSECOND_PER_MILLISECOND);

        public double TotalMilliseconds => Microseconds / MICROSECOND_PER_MILLISECOND;
        public double TotalSeconds => Microseconds / SECOND_PER_MILLISECOND;

        public static MediaTimestamp Now()
        {
            double tickUs = SECOND_PER_MILLISECOND / Stopwatch.Frequency;
            long us = (long)(Stopwatch.GetTimestamp() * tickUs);
            return new MediaTimestamp(us);
        }

        public static MediaTimestamp operator +(MediaTimestamp a, MediaTimestamp b)
            => new(a.Microseconds + b.Microseconds);

        public static MediaTimestamp operator -(MediaTimestamp a, MediaTimestamp b)
            => new(a.Microseconds - b.Microseconds);

        public int CompareTo(MediaTimestamp other) => Microseconds.CompareTo(other.Microseconds);

        public bool Equals(MediaTimestamp other) => Microseconds == other.Microseconds;
        public override bool Equals(object obj) => obj is MediaTimestamp mt && Equals(mt);
        public override int GetHashCode() => Microseconds.GetHashCode();

        public override string ToString() => Microseconds.ToString();
    }

}
