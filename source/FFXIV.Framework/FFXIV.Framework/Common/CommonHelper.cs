using System;
using System.Collections.Generic;

namespace FFXIV.Framework.Common
{
    public static class CommonHelper
    {
        private const double DefaultMin = 0.05;
        private const double DefaultMax = 1.00;

        private static readonly Random random = new Random();

        public static Random Random => random;

        public static TimeSpan GetRandomTimeSpan() =>
            GetRandomTimeSpan(DefaultMin, DefaultMax);

        public static TimeSpan GetRandomTimeSpan(
            double maxSecounds = DefaultMax) =>
            GetRandomTimeSpan(DefaultMin, maxSecounds);

        public static TimeSpan GetRandomTimeSpan(
            double minSecounds = DefaultMin,
            double maxSecounds = DefaultMax) =>
            TimeSpan.FromMilliseconds(random.Next(
                (int)(minSecounds * 1000),
                (int)(maxSecounds * 1000)));

        public static T Clone<T>(this T source) where T : class
        {
            return typeof(T).InvokeMember(
              "MemberwiseClone",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod,
              null, source, null) as T;
        }

        public static dynamic Clone(this object source)
        {
            var t = source.GetType();
            return t.InvokeMember(
              "MemberwiseClone",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod,
              null, source, null);
        }

        public static void Walk<T>(this IEnumerable<T> e, Action<T> action)
        {
            foreach (var item in e)
            {
                action(item);
            }
        }
    }
}
