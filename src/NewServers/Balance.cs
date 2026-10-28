using System;

namespace NewServers
{
    internal static class Balance
    {
        public static readonly int[] Gens = { 2, 3, 4 };

        // Vanilla servers run at roughly 1,700 IOPS per rack unit.
        public static int IopsPerU(int gen) => gen switch
        {
            2 => 3000,
            3 => 5000,
            4 => 8000,
            _ => throw new ArgumentOutOfRangeException(nameof(gen)),
        };

        // Each Gen costs 1.1x the previous Gen per IOPS, in money and in XP, compounding from vanilla.
        public static double Premium(int gen) => Math.Pow(1.1, gen - 1);

        // The game stores IOPS divided by 100,000 (a 5,000 IOPS server has maxProcessingSpeed 0.05).
        public const float IopsUnit = 100000f;

        public static int RoundTo10(double value) => (int)(Math.Round(value / 10.0) * 10);
    }
}
