using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalHuman
{
    public interface IConversationBackend
    {
        Task<string> SendMessageAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken cancellationToken = default);
    }
}
