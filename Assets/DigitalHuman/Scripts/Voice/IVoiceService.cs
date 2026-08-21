using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman
{
    public interface IVoiceService
    {
        Task<AudioClip> GenerateSpeechAsync(
            string text,
            CancellationToken cancellationToken = default);
    }
}
