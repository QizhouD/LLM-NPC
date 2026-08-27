using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// Adds an offset onto a pose produced by a lower layer, without letting the offset
    /// compound over frames.
    /// </summary>
    /// <remarks>
    /// A layer that reads a bone, adds to it and writes it back is only stable while
    /// something else rewrites that bone every frame. An Animator only writes the bones the
    /// current clip has curves for, and never its own transform, so for everything else the
    /// read-back is the previous frame's output. Folding that into the base adds one offset
    /// per frame and the bone walks away.
    ///
    /// The channel closes that hole by remembering what it wrote: a value that still matches
    /// means nothing external touched the bone, so the stored base is reused instead of the
    /// read-back.
    /// </remarks>
    public struct AdditiveRotationChannel
    {
        private Quaternion baseRotation;
        private Quaternion lastWritten;
        private bool hasWritten;

        /// <summary>
        /// Composes <paramref name="offset"/> over the pose a lower layer wrote, inferring
        /// that pose from <paramref name="currentValue"/> only when it actually changed.
        /// </summary>
        public Quaternion Apply(Quaternion currentValue, Quaternion offset)
        {
            if (!hasWritten || currentValue != lastWritten)
            {
                baseRotation = currentValue;
            }

            return Write(baseRotation * offset);
        }

        /// <summary>
        /// Composes <paramref name="offset"/> over a base the caller already knows, for when
        /// no lower layer is running and the rest pose is the pose to add onto.
        /// </summary>
        public Quaternion ApplyToBase(Quaternion explicitBase, Quaternion offset)
        {
            baseRotation = explicitBase;
            return Write(baseRotation * offset);
        }

        public void Reset()
        {
            baseRotation = Quaternion.identity;
            lastWritten = Quaternion.identity;
            hasWritten = false;
        }

        private Quaternion Write(Quaternion result)
        {
            lastWritten = result;
            hasWritten = true;
            return result;
        }
    }

    /// <summary>
    /// Position counterpart of <see cref="AdditiveRotationChannel"/>, with the same
    /// accumulation guarantee.
    /// </summary>
    public struct AdditivePositionChannel
    {
        private Vector3 basePosition;
        private Vector3 lastWritten;
        private bool hasWritten;

        public Vector3 Apply(Vector3 currentValue, Vector3 offset)
        {
            if (!hasWritten || currentValue != lastWritten)
            {
                basePosition = currentValue;
            }

            return Write(basePosition + offset);
        }

        public Vector3 ApplyToBase(Vector3 explicitBase, Vector3 offset)
        {
            basePosition = explicitBase;
            return Write(basePosition + offset);
        }

        public void Reset()
        {
            basePosition = Vector3.zero;
            lastWritten = Vector3.zero;
            hasWritten = false;
        }

        private Vector3 Write(Vector3 result)
        {
            lastWritten = result;
            hasWritten = true;
            return result;
        }
    }
}
