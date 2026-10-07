namespace WavefrontErrorCalculator.Core;

/// <summary>Small dense linear algebra.</summary>
internal static class Numerics
{
    /// <summary>Solves min |A c - b| by Householder QR, which keeps the conditioning of A rather than squaring it.</summary>
    public static double[] LeastSquares(double[,] a, double[] b)
    {
        int m = a.GetLength(0), n = a.GetLength(1);
        a = (double[,])a.Clone();
        b = (double[])b.Clone();
        for (int k = 0; k < n; k++)
        {
            double norm = 0.0;
            for (int i = k; i < m; i++) norm += a[i, k] * a[i, k];
            norm = Math.Sqrt(norm);
            if (norm == 0.0) continue;
            double alpha = a[k, k] > 0 ? -norm : norm;
            var v = new double[m];
            v[k] = a[k, k] - alpha;
            for (int i = k + 1; i < m; i++) v[i] = a[i, k];
            double vv = 0.0;
            for (int i = k; i < m; i++) vv += v[i] * v[i];
            if (vv == 0.0) continue;
            for (int j = k; j < n; j++)
            {
                double dot = 0.0;
                for (int i = k; i < m; i++) dot += v[i] * a[i, j];
                double f = 2.0 * dot / vv;
                for (int i = k; i < m; i++) a[i, j] -= f * v[i];
            }
            double db = 0.0;
            for (int i = k; i < m; i++) db += v[i] * b[i];
            double fb = 2.0 * db / vv;
            for (int i = k; i < m; i++) b[i] -= fb * v[i];
        }
        var x = new double[n];
        for (int k = n - 1; k >= 0; k--)
        {
            double s = b[k];
            for (int j = k + 1; j < n; j++) s -= a[k, j] * x[j];
            x[k] = Math.Abs(a[k, k]) > 1e-300 ? s / a[k, k] : 0.0;
        }
        return x;
    }
}
