using System.Threading;
using System.Threading.Tasks;

namespace DigitalHuman
{
    public interface IConversationService
    {
        Task<string> GetResponseAsync(
            string playerMessage,
            CancellationToken cancellationToken = default);
    }
}
