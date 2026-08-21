#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanGeneratedArtBindingBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PortraitPath =
            "Assets/DigitalHuman/Textures/Generated/EVA_Character_KeyArt.png";
        private const string PanelPath =
            "Assets/DigitalHuman/Textures/Generated/EVA_Dialogue_Panel.png";
        private const string IconPath =
            "Assets/DigitalHuman/Textures/Generated/EVA_Interact_Icon.png";

        public static void BindAndValidate()
        {
            try
            {
                Sprite portraitSprite = LoadSprite(PortraitPath);
                Sprite panelSprite = LoadSprite(PanelPath);
                Sprite iconSprite = LoadSprite(IconPath);

                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameObject canvasObject = GameObject.Find("DialogueCanvas");
                Require(canvasObject != null, "DialogueCanvas is missing.");

                Transform panelTransform = canvasObject.transform.Find("DialoguePanel");
                Transform promptTransform = canvasObject.transform.Find("InteractionPrompt");
                Require(panelTransform != null, "DialoguePanel is missing.");
                Require(promptTransform != null, "InteractionPrompt is missing.");

                BindPanel(panelTransform, panelSprite, portraitSprite);
                BindInteractionIcon(promptTransform, iconSprite);
                ValidateBindings(canvasObject, panelTransform, promptTransform,
                    panelSprite, portraitSprite, iconSprite);

                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene, ScenePath),
                    "Failed to save SampleScene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] Generated EVA visual assets bound to Dialogue UI.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw;
            }
        }

        private static void BindPanel(
            Transform panelTransform,
            Sprite panelSprite,
            Sprite portraitSprite)
        {
            Image panelImage = panelTransform.GetComponent<Image>();
            Require(panelImage != null, "DialoguePanel Image is missing.");
            panelImage.sprite = panelSprite;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false;
            panelImage.color = Color.white;
            panelImage.raycastTarget = true;

            RectTransform panelRect = (RectTransform)panelTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(1360f, 765f);
            panelRect.anchoredPosition = new Vector2(0f, 24f);

            Image portrait = GetOrCreateImage("EVA_Portrait", panelTransform);
            portrait.sprite = portraitSprite;
            portrait.type = Image.Type.Simple;
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            portrait.color = Color.white;
            SetStretchRect(portrait.rectTransform,
                new Vector2(0.045f, 0.18f), new Vector2(0.235f, 0.80f));
            portrait.transform.SetAsFirstSibling();

            SetChildStretch(panelTransform, "NPCNameText",
                new Vector2(0.27f, 0.82f), new Vector2(0.93f, 0.96f));
            SetChildStretch(panelTransform, "DialogueText",
                new Vector2(0.27f, 0.34f), new Vector2(0.96f, 0.81f));
            SetChildStretch(panelTransform, "PlayerInputField",
                new Vector2(0.27f, 0.08f), new Vector2(0.75f, 0.28f));
            SetChildStretch(panelTransform, "SendButton",
                new Vector2(0.77f, 0.08f), new Vector2(0.95f, 0.28f));

            TMP_Text npcName = panelTransform.Find("NPCNameText")
                .GetComponent<TMP_Text>();
            npcName.color = new Color(0.30f, 0.92f, 1f, 1f);

            EditorUtility.SetDirty(panelImage);
            EditorUtility.SetDirty(panelRect);
            EditorUtility.SetDirty(portrait);
            EditorUtility.SetDirty(npcName);
        }

        private static void BindInteractionIcon(
            Transform promptTransform,
            Sprite iconSprite)
        {
            Image icon = GetOrCreateImage("InteractionIcon", promptTransform);
            icon.sprite = iconSprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = Color.white;

            RectTransform iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(64f, 64f);
            iconRect.anchoredPosition = new Vector2(-215f, 0f);
            EditorUtility.SetDirty(icon);
            EditorUtility.SetDirty(iconRect);
        }

        private static void ValidateBindings(
            GameObject canvasObject,
            Transform panelTransform,
            Transform promptTransform,
            Sprite panelSprite,
            Sprite portraitSprite,
            Sprite iconSprite)
        {
            Image panel = panelTransform.GetComponent<Image>();
            Image portrait = panelTransform.Find("EVA_Portrait")?.GetComponent<Image>();
            Image icon = promptTransform.Find("InteractionIcon")?.GetComponent<Image>();

            Require(panel != null && panel.sprite == panelSprite,
                "Dialogue panel sprite binding failed.");
            Require(portrait != null && portrait.sprite == portraitSprite,
                "EVA portrait sprite binding failed.");
            Require(icon != null && icon.sprite == iconSprite,
                "Interaction icon sprite binding failed.");
            Require(iconSprite.texture.alphaIsTransparency,
                "Interaction icon transparency import setting is disabled.");

            int missingScripts = 0;
            Transform[] transforms = canvasObject.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transforms[i].gameObject);
            }

            Require(missingScripts == 0,
                $"DialogueCanvas contains {missingScripts} missing script reference(s).");
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Require(sprite != null, $"Sprite asset is missing: {path}");
            return sprite;
        }

        private static Image GetOrCreateImage(string name, Transform parent)
        {
            Transform existing = parent.Find(name);
            GameObject imageObject;
            if (existing == null)
            {
                imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
                imageObject.transform.SetParent(parent, false);
            }
            else
            {
                imageObject = existing.gameObject;
                if (imageObject.GetComponent<Image>() == null)
                {
                    imageObject.AddComponent<Image>();
                }
            }

            return imageObject.GetComponent<Image>();
        }

        private static void SetChildStretch(
            Transform parent,
            string childName,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            Transform child = parent.Find(childName);
            Require(child != null, $"{childName} is missing.");
            SetStretchRect((RectTransform)child, anchorMin, anchorMax);
            EditorUtility.SetDirty(child);
        }

        private static void SetStretchRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
#endif
