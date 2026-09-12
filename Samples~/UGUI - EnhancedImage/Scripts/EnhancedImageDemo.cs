// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.UGUI.EnhancedImage
{
    using UnityEngine;
    using UnityEngine.UI;
    using WallstopStudios.UnityHelpers.Visuals.UGUI;

    /// <summary>
    /// Creates a Canvas + EnhancedImage at runtime and applies an HDR tint.
    /// </summary>
    public sealed class EnhancedImageDemo : MonoBehaviour
    {
        [SerializeField]
        private Material materialTemplate;

        private void Start()
        {
            GameObject canvasGo = new GameObject("DemoCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject imageGo = new GameObject("EnhancedImage");
            imageGo.transform.SetParent(canvasGo.transform, false);
            EnhancedImage image = imageGo.AddComponent<EnhancedImage>();
#pragma warning disable WPROTO028 // Transient UI geometry is never serialized by WallstopProto.
            image.rectTransform.sizeDelta = new Vector2(200f, 200f);
#pragma warning restore WPROTO028

            if (materialTemplate != null)
            {
                image.material = Object.Instantiate(materialTemplate);
            }

#pragma warning disable WPROTO028 // Transient UI tint is never serialized by WallstopProto.
            image.HdrColor = new Color(1.6f, 1.2f, 0.8f, 1f);
#pragma warning restore WPROTO028
        }
    }
}
