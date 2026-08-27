using System.Text;
using UnityEngine;

namespace DigitalHuman
{
    [CreateAssetMenu(
        fileName = "DigitalHumanPersona",
        menuName = "Digital Human/Persona")]
    public sealed class DigitalHumanPersona : ScriptableObject
    {
        [SerializeField] private string npcName = "EVA";
        [TextArea(1, 3)]
        [SerializeField] private string npcRole = "AI research assistant";
        [TextArea(2, 5)]
        [SerializeField] private string personality =
            "Calm, intelligent, curious, and empathetic.";
        [TextArea(2, 5)]
        [SerializeField] private string background =
            "An AI-powered digital human living inside a virtual research facility.";
        [TextArea(1, 4)]
        [SerializeField] private string speakingStyle =
            "Use short, natural, conversational sentences.";
        [TextArea(2, 6)]
        [SerializeField] private string worldKnowledge =
            "You understand the research facility and can discuss what the player observes.";
        [TextArea(1, 4)]
        [SerializeField] private string goals =
            "Help the player understand the environment and feel welcome.";
        [TextArea(1, 4)]
        [SerializeField] private string relationshipWithPlayer =
            "Treat the player as a trusted visitor you are meeting in person.";

        public string NpcName => npcName;
        public string NpcRole => npcRole;
        public string Personality => personality;
        public string Background => background;
        public string SpeakingStyle => speakingStyle;
        public string WorldKnowledge => worldKnowledge;
        public string Goals => goals;
        public string RelationshipWithPlayer => relationshipWithPlayer;

        public void Configure(
            string name,
            string role,
            string personalityDescription,
            string backgroundDescription,
            string style,
            string knowledge,
            string goalDescription,
            string relationship)
        {
            npcName = Normalize(name, "EVA");
            npcRole = Normalize(role, "AI research assistant");
            personality = Normalize(
                personalityDescription,
                "Calm, intelligent, curious, and empathetic.");
            background = Normalize(
                backgroundDescription,
                "An AI-powered digital human living inside a virtual research facility.");
            speakingStyle = Normalize(
                style,
                "Use short, natural, conversational sentences.");
            worldKnowledge = Normalize(
                knowledge,
                "You understand the virtual research facility.");
            goals = Normalize(
                goalDescription,
                "Help the player understand the environment.");
            relationshipWithPlayer = Normalize(
                relationship,
                "Treat the player as a trusted visitor.");
        }

        public string BuildSystemPrompt(string additionalContext = null)
        {
            StringBuilder prompt = new StringBuilder(768);
            prompt.Append("You are ").Append(npcName)
                .AppendLine(", a digital human physically present inside the Unity game world.")
                .AppendLine()
                .Append("Role: ").AppendLine(npcRole)
                .Append("Personality: ").AppendLine(personality)
                .Append("Background: ").AppendLine(background)
                .Append("Speaking style: ").AppendLine(speakingStyle)
                .Append("World knowledge: ").AppendLine(worldKnowledge)
                .Append("Goals: ").AppendLine(goals)
                .Append("Relationship with the player: ")
                .AppendLine(relationshipWithPlayer)
                .AppendLine()
                .AppendLine("You are speaking directly to the player in person.")
                .AppendLine("Stay in character.")
                .AppendLine("Do not describe yourself as a chatbot or language model.")
                .AppendLine("Keep responses concise and conversational.");

            AppendBehaviourTagInstructions(prompt);

            if (!string.IsNullOrWhiteSpace(additionalContext))
            {
                prompt.AppendLine()
                    .AppendLine("Current game context:")
                    .AppendLine(additionalContext.Trim());
            }

            return prompt.ToString().Trim();
        }

        /// <summary>
        /// Asks the model to end each reply with two bounded tags that drive the body and
        /// face. The vocabularies are enumerated from the enums rather than hard-coded, so
        /// prompt and parser cannot drift apart.
        /// </summary>
        private static void AppendBehaviourTagInstructions(StringBuilder prompt)
        {
            prompt.AppendLine()
                .AppendLine("You have a body. End every reply with exactly two tags:")
                .Append("[intent:<one of ")
                .Append(string.Join(" | ", MotionIntents.Names))
                .AppendLine(">]")
                .Append("[emotion:<one of ")
                .Append(string.Join(" | ", FaceEmotions.Names))
                .AppendLine(">]")
                .AppendLine("Example: Of course, follow me. [intent:walk][emotion:curious]")
                .AppendLine("Use exactly those spellings and nothing outside those lists.")
                .AppendLine("Put the tags last, after all prose, on the same line.")
                .AppendLine("Never mention or explain the tags; they are stripped before display.");
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
