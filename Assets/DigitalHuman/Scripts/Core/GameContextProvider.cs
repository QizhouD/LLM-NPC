using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class GameContextProvider : MonoBehaviour, IGameContextProvider
    {
        [SerializeField] private Transform playerTransform;
        [SerializeField] private string currentLocation = "Research Laboratory";
        [SerializeField, Range(0f, 100f)] private float playerHealth = 100f;
        [TextArea(1, 3)]
        [SerializeField] private string currentObjective =
            "Meet EVA and learn about the facility.";
        [SerializeField] private bool includeSceneName = true;
        [SerializeField] private bool includeSessionTime = true;

        public Transform PlayerTransform => playerTransform;
        public string CurrentLocation => currentLocation;
        public float PlayerHealth => playerHealth;
        public string CurrentObjective => currentObjective;

        public void Configure(
            Transform player,
            string location,
            float health,
            string objective,
            bool includeScene = true,
            bool includeTime = true)
        {
            playerTransform = player;
            currentLocation = string.IsNullOrWhiteSpace(location)
                ? "Unknown"
                : location.Trim();
            playerHealth = Mathf.Clamp(health, 0f, 100f);
            currentObjective = string.IsNullOrWhiteSpace(objective)
                ? "No active objective."
                : objective.Trim();
            includeSceneName = includeScene;
            includeSessionTime = includeTime;
        }

        public void SetWorldState(
            string location,
            float health,
            string objective)
        {
            currentLocation = string.IsNullOrWhiteSpace(location)
                ? currentLocation
                : location.Trim();
            playerHealth = Mathf.Clamp(health, 0f, 100f);
            currentObjective = string.IsNullOrWhiteSpace(objective)
                ? currentObjective
                : objective.Trim();
        }

        public string GetGameContext()
        {
            StringBuilder context = new StringBuilder(256);
            if (includeSceneName)
            {
                context.Append("Scene: ")
                    .AppendLine(SceneManager.GetActiveScene().name);
            }

            context.Append("Location: ").AppendLine(currentLocation);
            if (playerTransform != null)
            {
                Vector3 position = playerTransform.position;
                context.Append("Player position: ")
                    .AppendFormat(
                        CultureInfo.InvariantCulture,
                        "({0:0.00}, {1:0.00}, {2:0.00})",
                        position.x,
                        position.y,
                        position.z)
                    .AppendLine();
            }
            else
            {
                context.AppendLine("Player position: unavailable");
            }

            context.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Player health: {0:0}/100",
                    playerHealth)
                .AppendLine();
            context.Append("Current objective: ").AppendLine(currentObjective);
            if (includeSessionTime)
            {
                context.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "Session time: {0:0.0} seconds",
                        Time.time)
                    .AppendLine();
            }

            return context.ToString().Trim();
        }

        private void OnValidate()
        {
            playerHealth = Mathf.Clamp(playerHealth, 0f, 100f);
        }
    }
}
