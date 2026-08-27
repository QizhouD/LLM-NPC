namespace DigitalHuman
{
    /// <summary>
    /// Which chat backend <see cref="AIConversationManager"/> should use.
    /// </summary>
    /// <remarks>
    /// Selecting a provider only swaps which <see cref="IConversationBackend"/> is called.
    /// Nothing downstream changes: the persona prompt, the bounded
    /// <c>[intent:x][emotion:y]</c> contract and the plan bridge are provider-agnostic.
    ///
    /// A provider whose key is missing, or whose component slot is empty, degrades to
    /// <see cref="Offline"/> behaviour at call time rather than failing the turn.
    /// </remarks>
    public enum LLMProvider
    {
        /// <summary>OpenAI Responses API. The project default.</summary>
        OpenAI = 0,

        /// <summary>DeepSeek Chat Completions API.</summary>
        DeepSeek = 1,

        /// <summary>
        /// No network call at all: <see cref="OfflineReplyHeuristic"/> answers. Replies are
        /// prefixed <c>[offline mode]</c> so they cannot be mistaken for model output.
        /// </summary>
        Offline = 2
    }
}
