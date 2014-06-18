using System;
using System.Collections.Generic;
using System.Linq;

public static class RandomHelper {
    public static readonly Random Random = new Random();

    public static bool Probability(double p) {
        return p >= Random.NextDouble();
    }

    public static float Between(double min, double max) {
        return (float)(Random.NextDouble() * (max - min) + min);
    }

    public static T InEnumerable<T>(IEnumerable<T> enumerable) {
        return enumerable.ElementAt(Random.Next(0, enumerable.Count()));
    }
}
