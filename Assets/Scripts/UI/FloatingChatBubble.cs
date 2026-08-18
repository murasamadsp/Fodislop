#nullable enable

using Fodinae.Core;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;

namespace Fodinae.UI
{
    public class FloatingChatBubble : MonoBehaviour
    {
        private TextMesh? _textMesh;
        private MeshRenderer? _meshRenderer;
        private MeshRenderer? _bgRenderer;
        private MeshFilter? _bgFilter;
        private Mesh? _bgMesh;
        private MaterialPropertyBlock? _bgPropertyBlock;
        private float _elapsed;
        private const float DURATION = 5f;
        private const float FLOAT_SPEED = 0.3f;
        private const float FADE_START = 4f;
        private Camera? _cam;

        public void Init(string text)
        {
            _cam = Camera.main;
            _elapsed = 0f;
            if (_textMesh == null)
            {
                _textMesh = gameObject.AddComponent<TextMesh>();
                _meshRenderer = GetComponent<MeshRenderer>();
                UnityRenderLayerContracts.ApplyWorldUI(_meshRenderer, 300);

                var bgGo = new GameObject("ChatBubbleBG");
                bgGo.transform.SetParent(transform, false);
                bgGo.transform.localPosition = new Vector3(0, 0, 0.01f);
                _bgFilter = bgGo.AddComponent<MeshFilter>();
                _bgRenderer = bgGo.AddComponent<MeshRenderer>();
                UnityRenderLayerContracts.ApplyWorldUI(_bgRenderer, 299);
                _bgRenderer.sharedMaterial = SharedMaterialCache.GetForTexture(Texture2D.whiteTexture);
                _bgPropertyBlock = new MaterialPropertyBlock();
                SetBackgroundAlpha(0.5f);
            }

            _textMesh.text = text;
            UpdateBackgroundMesh();
            _textMesh.fontSize = 48;
            _textMesh.color = Color.white;
            _textMesh.anchor = TextAnchor.LowerCenter;
            _textMesh.alignment = TextAlignment.Center;

            if (_cam != null)
            {
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);
            }

            gameObject.SetActive(true);
        }

        private void UpdateBackgroundMesh()
        {
            if (_textMesh == null || _bgRenderer == null)
            {
                return;
            }

            float textWidth = _textMesh.text.Length * 0.12f;
            float w = Mathf.Max(textWidth, 1.5f) + 0.4f;
            const float h = 0.3f;

            _bgMesh ??= new Mesh { name = "ChatBubbleBackground" };
            Vector3[] vertices =
            {
                new Vector3(-w / 2, -h / 2, 0),
                new Vector3(w / 2, -h / 2, 0),
                new Vector3(-w / 2, h / 2, 0),
                new Vector3(w / 2, h / 2, 0),
            };
            _bgMesh.vertices = vertices;
            _bgMesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            _bgMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            _bgMesh.RecalculateBounds();

            if (_bgFilter != null)
            {
                _bgFilter.sharedMesh = _bgMesh;
            }
        }

        private void SetBackgroundAlpha(float alpha)
        {
            if (_bgRenderer == null)
            {
                return;
            }

            _bgPropertyBlock ??= new MaterialPropertyBlock();
            _bgPropertyBlock.SetColor("_Color", new Color(0f, 0f, 0f, alpha));
            _bgRenderer.SetPropertyBlock(_bgPropertyBlock);
        }

        protected void Update()
        {
            _elapsed += Time.deltaTime;
            transform.Translate(0, FLOAT_SPEED * Time.deltaTime, 0);

            if (_cam != null && _textMesh != null)
            {
                _textMesh.characterSize = 0.08f * (_cam.orthographicSize / 10f);
            }

            if (_elapsed >= FADE_START && _textMesh != null)
            {
                float t = (_elapsed - FADE_START) / (DURATION - FADE_START);
                Color c = _textMesh.color;
                c.a = Mathf.Lerp(1f, 0f, t);
                _textMesh.color = c;
                SetBackgroundAlpha(Mathf.Lerp(0.5f, 0f, t));
            }

            if (_elapsed >= DURATION)
            {
                gameObject.SetActive(false);
            }
        }

        protected void OnDisable()
        {
            _elapsed = 0f;
            if (_textMesh != null)
            {
                var c = _textMesh.color;
                c.a = 1f;
                _textMesh.color = c;
            }

            SetBackgroundAlpha(0.5f);
        }

        protected void OnDestroy()
        {
            if (_bgRenderer != null)
            {
                Destroy(_bgRenderer.gameObject);
            }

            if (_bgMesh != null)
            {
                Destroy(_bgMesh);
            }
        }
    }
}
