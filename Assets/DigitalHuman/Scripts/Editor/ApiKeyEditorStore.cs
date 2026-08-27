#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace DigitalHuman.Editor
{
    /// <summary>
    /// Editor-only key entry, for when restarting Unity Hub to pick up a new environment
    /// variable is not worth it.
    /// </summary>
    /// <remarks>
    /// Keys go into <see cref="EditorPrefs"/>, which lives in the per-user registry outside the
    /// project, so nothing enters a scene, a prefab or a commit. On load the stored values are
    /// pushed into this Editor process's own environment block, so the services keep reading
    /// them through <c>Environment.GetEnvironmentVariable</c> exactly as they read a real
    /// environment variable. That is why no runtime script needed changing for this.
    ///
    /// This file is in the Editor assembly, so it does not exist in a build. A built player
    /// still has the environment variable as its only source of a key, which is the intended
    /// behaviour: the demo someone else runs falls back to the offline heuristic rather than
    /// spending your quota.
    ///
    /// EditorPrefs is plaintext, same as a user environment variable. This is a convenience for
    /// a development key, not a secret store.
    /// </remarks>
    public sealed class ApiKeyEditorStore : EditorWindow
    {
        private const string PrefPrefix = "DigitalHuman.ApiKey.";
        private const string InjectedPrefix = "DigitalHuman.ApiKeyInjected.";

        private static readonly string[] VariableNames =
        {
            OpenAILLMService.DefaultApiKeyEnvironmentVariable,
            DeepSeekLlmService.DefaultApiKeyEnvironmentVariable
        };

        private readonly string[] pendingEntry = new string[VariableNames.Length];

        [MenuItem("Digital Human/API Keys")]
        public static void Open()
        {
            ApiKeyEditorStore window = GetWindow<ApiKeyEditorStore>(true, "API Keys");
            window.minSize = new Vector2(460f, 300f);
        }

        /// <summary>
        /// Describes a secret without revealing it: enough to tell two keys apart and to catch
        /// a truncated paste, safe to write to <c>Editor.log</c>.
        /// </summary>
        public static string DescribeSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "not set";
            }

            string trimmed = value.Trim();
            string tail = trimmed.Length >= 4
                ? trimmed.Substring(trimmed.Length - 4)
                : "????";
            return $"{trimmed.Length} chars, ends ...{tail}";
        }

        /// <summary>
        /// Explains where the key the services will actually use is coming from. Distinguishing
        /// the two sources matters: a stored value silently shadowing a real environment
        /// variable would be a confusing way to lose an afternoon.
        /// </summary>
        public static string DescribeSource(string variableName)
        {
            string live = Environment.GetEnvironmentVariable(variableName);
            string stored = EditorPrefs.GetString(PrefPrefix + variableName, string.Empty);
            bool injected = SessionState.GetBool(InjectedPrefix + variableName, false);

            if (!string.IsNullOrWhiteSpace(live))
            {
                string origin = injected ? "stored in this Editor" : "real environment variable";
                string shadowed = !injected && !string.IsNullOrWhiteSpace(stored)
                    ? " (a stored value exists but is ignored)"
                    : string.Empty;
                return $"in use, from the {origin}: {DescribeSecret(live)}{shadowed}";
            }

            return string.IsNullOrWhiteSpace(stored)
                ? "not set"
                : $"stored but not applied yet, reopen the project: {DescribeSecret(stored)}";
        }

        [InitializeOnLoadMethod]
        private static void ApplyStoredKeys()
        {
            foreach (string variableName in VariableNames)
            {
                string stored = EditorPrefs.GetString(PrefPrefix + variableName, string.Empty);
                if (string.IsNullOrWhiteSpace(stored))
                {
                    continue;
                }

                // A real environment variable always wins, so a properly configured machine is
                // never shadowed by something pasted here once and forgotten.
                bool alreadyOurs = SessionState.GetBool(InjectedPrefix + variableName, false);
                bool occupied =
                    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName));
                if (occupied && !alreadyOurs)
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(variableName, stored.Trim());
                SessionState.SetBool(InjectedPrefix + variableName, true);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Stored in EditorPrefs, outside the project. Nothing here is committed and " +
                "nothing here reaches a build. A real environment variable takes precedence.",
                MessageType.Info);

            for (int i = 0; i < VariableNames.Length; i++)
            {
                DrawVariable(i);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Log status (safe, does not print keys)"))
            {
                DigitalHumanSceneTool.ReportApiKeyStatus();
            }
        }

        private void DrawVariable(int index)
        {
            string variableName = VariableNames[index];
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(variableName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                DescribeSource(variableName),
                EditorStyles.wordWrappedMiniLabel);

            pendingEntry[index] =
                EditorGUILayout.PasswordField("Paste key", pendingEntry[index]);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool hasEntry = !string.IsNullOrWhiteSpace(pendingEntry[index]);
                using (new EditorGUI.DisabledScope(!hasEntry))
                {
                    if (GUILayout.Button("Apply and store"))
                    {
                        Store(variableName, pendingEntry[index]);
                        pendingEntry[index] = string.Empty;
                        GUI.FocusControl(null);
                    }
                }

                bool hasStored = !string.IsNullOrWhiteSpace(
                    EditorPrefs.GetString(PrefPrefix + variableName, string.Empty));
                using (new EditorGUI.DisabledScope(!hasStored))
                {
                    if (GUILayout.Button("Forget"))
                    {
                        Forget(variableName);
                    }
                }
            }
        }

        private static void Store(string variableName, string key)
        {
            string trimmed = key.Trim();
            EditorPrefs.SetString(PrefPrefix + variableName, trimmed);
            Environment.SetEnvironmentVariable(variableName, trimmed);
            SessionState.SetBool(InjectedPrefix + variableName, true);
            Debug.Log($"[DigitalHuman] {variableName} set for this Editor: " +
                      $"{DescribeSecret(trimmed)}");
        }

        private static void Forget(string variableName)
        {
            EditorPrefs.DeleteKey(PrefPrefix + variableName);

            // Only clear the live value if it was ours, so forgetting a stored key cannot
            // knock out a real environment variable for the rest of the session.
            if (SessionState.GetBool(InjectedPrefix + variableName, false))
            {
                Environment.SetEnvironmentVariable(variableName, null);
                SessionState.SetBool(InjectedPrefix + variableName, false);
            }

            Debug.Log($"[DigitalHuman] {variableName} forgotten.");
        }
    }
}
#endif
