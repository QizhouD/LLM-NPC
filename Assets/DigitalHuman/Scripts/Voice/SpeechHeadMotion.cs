using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Couples head motion to the loudness of the voice clip currently playing.
    /// </summary>
    /// <remarks>
    /// This is the substitute for lip sync, not an approximation of it. The mesh has no blend
    /// shapes and no jaw bone, so the mouth cannot move at all; instead the audio envelope
    /// drives the head, which is where a lot of the perceived "this body is producing this
    /// sound" signal actually comes from. Loudness contributes a nod, and a slow noise field
    /// gated by that same loudness adds yaw and roll so the motion is not a metronome.
    ///
    /// Runs after <see cref="ProceduralFaceController"/> and composes the same way it does:
    /// multiplying into whatever local rotation is already on the bone, so gaze, body motion
    /// and speech motion all add up instead of overwriting each other.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class SpeechHeadMotion : MonoBehaviour
    {
        /// <summary>Power of two, as required by <c>GetOutputData</c>.</summary>
        private const int SampleWindow = 256;

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Transform head;
        [SerializeField] private Transform neck;
        [Tooltip("Root to search for Head and Neck bones. Defaults to this transform.")]
        [SerializeField] private Transform skeletonRoot;

        [Header("Envelope")]
        [Tooltip("RMS is multiplied by this before clamping. Speech RMS is typically 0.05-0.2.")]
        [SerializeField, Min(0.1f)] private float loudnessGain = 6f;
        [Tooltip("Seconds for the envelope to rise. Short, so onsets are not smeared away.")]
        [SerializeField, Min(0.01f)] private float attack = 0.06f;
        [Tooltip("Seconds for the envelope to fall. Longer than attack, so it does not chatter.")]
        [SerializeField, Min(0.01f)] private float release = 0.22f;

        [Header("Motion")]
        [Tooltip("Degrees of downward nod at full loudness.")]
        [SerializeField, Range(0f, 12f)] private float nodDegrees = 3.5f;
        [Tooltip("Degrees of noise-driven yaw at full loudness.")]
        [SerializeField, Range(0f, 12f)] private float swayYawDegrees = 2.5f;
        [Tooltip("Degrees of noise-driven roll at full loudness.")]
        [SerializeField, Range(0f, 12f)] private float swayRollDegrees = 1.8f;
        [SerializeField, Min(0.05f)] private float swayFrequency = 0.55f;
        [Tooltip("Share of the head motion also applied to the neck.")]
        [SerializeField, Range(0f, 1f)] private float neckShare = 0.35f;

        private readonly float[] samples = new float[SampleWindow];
        private float envelope;
        private float noiseSeed;

        /// <summary>Smoothed loudness in 0-1. Useful for a gesture layer weight.</summary>
        public float Envelope => envelope;

        private void Awake()
        {
            noiseSeed = Random.value * 100f;
            ResolveBones();
        }

        public void Configure(AudioSource source, Transform root)
        {
            audioSource = source;
            skeletonRoot = root;
            head = null;
            neck = null;
            ResolveBones();
        }

        private void LateUpdate()
        {
            float target = SampleLoudness();

            // Asymmetric smoothing: a syllable onset should show up, its tail should not flicker.
            float timeConstant = target > envelope ? attack : release;
            envelope = Mathf.Lerp(
                envelope, target, Mathf.Clamp01(Time.deltaTime / timeConstant));

            if (envelope <= 0.001f || head == null)
            {
                return;
            }

            float phase = Time.time * swayFrequency;
            float yaw = (Mathf.PerlinNoise(noiseSeed, phase) - 0.5f) * 2f * swayYawDegrees;
            float roll = (Mathf.PerlinNoise(noiseSeed + 13.7f, phase) - 0.5f) * 2f * swayRollDegrees;
            Vector3 offset = new Vector3(nodDegrees, yaw, roll) * envelope;

            head.localRotation *= Quaternion.Euler(offset);
            if (neck != null && neckShare > 0f)
            {
                neck.localRotation *= Quaternion.Euler(offset * neckShare);
            }
        }

        private float SampleLoudness()
        {
            if (audioSource == null || !audioSource.isPlaying)
            {
                return 0f;
            }

            audioSource.GetOutputData(samples, 0);
            float sumOfSquares = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sumOfSquares += samples[i] * samples[i];
            }

            float rms = Mathf.Sqrt(sumOfSquares / samples.Length);
            return Mathf.Clamp01(rms * loudnessGain);
        }

        private void ResolveBones()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            Transform root = skeletonRoot != null ? skeletonRoot : transform;
            head = head ?? FindBone(root, "Base HumanHead");
            neck = neck ?? FindBone(root, "Base HumanNeck");
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == boneName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindBone(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnValidate()
        {
            release = Mathf.Max(attack, release);
        }
    }
}
