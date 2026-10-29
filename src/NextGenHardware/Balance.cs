using System;

namespace NextGenHardware
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

        // A family's bigger server (7U) is 1.2x as dense as its smaller one (3U) in the same Gen. That is
        // enough for six 7U (42U) to out-perform sixteen 3U (48U), while staying below the next Gen's 3U.
        public const double BigServerDensity = 1.2;

        // The bigger server costs more per IOPS than the smaller one: vanilla already charges ~1.6x for
        // three families, and this floor gives the fourth (Mainframe, ~1.0x in vanilla) a premium too.
        public const double BigServerMinPricePremium = 1.2;

        public static int Iops(int gen, int sizeInU, bool bigger) =>
            (int)(Math.Round(IopsPerU(gen) * sizeInU * (bigger ? BigServerDensity : 1.0) / 1000.0) * 1000);

        // Each Gen costs 1.1x the previous Gen per IOPS in money, compounding from vanilla.
        public static double Premium(int gen) => Math.Pow(1.1, gen - 1);

        // XP to unlock climbs a ladder through each family: vanilla 3U, vanilla 7U, Gen 2 3U, Gen 2 7U, Gen 3 3U ...
        // Each rung costs XpStep times the rung below, starting from the family's vanilla 7U (rung 0). At 1.4,
        // Gen 4 7U costs about what it did under the older IOPS-based XP.
        public const double XpStep = 1.4;

        public static int XpRung(int gen, bool bigger) => 2 * (gen - 1) - (bigger ? 0 : 1);

        // The game stores IOPS divided by 100,000 (a 5,000 IOPS server has maxProcessingSpeed 0.05).
        public const float IopsUnit = 100000f;

        public static int RoundTo10(double value) => (int)(Math.Round(value / 10.0) * 10);
    }
}
