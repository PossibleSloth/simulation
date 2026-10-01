using UnityEngine;

/// <summary>
/// Attach this component to any GameObject to turn it into an EM wave source.
/// The WaveFieldRenderer will automatically discover all active WaveSource
/// instances in the scene each frame.
/// </summary>
public class WaveSource : MonoBehaviour
{
    [Tooltip("Wave frequency in Hz (cycles per world-unit of distance per second).")]
    public float frequency = 1f;

    [Tooltip("Peak amplitude of this source's contribution.")]
    public float amplitude = 1f;

    [Tooltip("Initial phase offset in radians.")]
    public float phase = 0f;

    [Tooltip("Propagation speed (world-units per second).")]
    public float speed = 2f;

    [Tooltip("If > 0, intensity falls off as 1/distance^falloffPower. " +
             "Set to 0 for no spatial falloff (useful for debugging).")]
    public float falloffPower = 0.5f;

    /// <summary>
    /// Evaluate this source's scalar field value at world-space point <paramref name="worldPos"/>
    /// at time <paramref name="t"/>.
    ///
    /// Physics:  E(r,t) = A * cos(k*r - ω*t + φ) / r^falloff
    ///   where   k  = 2π / λ  (wavenumber)
    ///           ω  = 2π * f  (angular frequency)
    ///           λ  = speed / frequency
    ///           r  = distance from source to sample point
    /// </summary>
    public float Evaluate(Vector2 worldPos, float t)
    {
        Vector2 srcPos = transform.position; // z ignored – 2-D simulation
        float r = Vector2.Distance(worldPos, srcPos);

        if (r < 1e-5f) return amplitude; // avoid divide-by-zero at the source

        float wavelength = speed / Mathf.Max(frequency, 1e-5f);
        float k = 2f * Mathf.PI / wavelength;
        float omega = 2f * Mathf.PI * frequency;

        float envelope = (falloffPower > 0f)
            ? amplitude / Mathf.Pow(r, falloffPower)
            : amplitude;

        return envelope * Mathf.Cos(k * r - omega * t + phase);
    }
}
