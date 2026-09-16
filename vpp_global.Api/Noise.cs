public static class Noise
{
    public static double SampleGaussianNoise()
    {
        double u1 = 1.0 - Random.Shared.NextDouble();
        double u2 = Random.Shared.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }   
}

