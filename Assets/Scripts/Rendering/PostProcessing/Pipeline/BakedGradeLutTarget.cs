#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Rendering.PostProcessing
{
    // Печёная таблица цветокоррекции: сама трёхмерная текстура и кэш, который
    // решает, надо ли её перепекать.
    //
    // Отдельный тип, а не поля прохода: время жизни текстуры к записи графа
    // отношения не имеет, а проход обязан заниматься кадром. Держать их вместе
    // значило смешивать «что рисуем в этом кадре» с «что живёт между кадрами».
    //
    // Эта таблица не вывозится наружу и вывезена быть не может как есть: печка
    // (BakeGradeLut в PostProcess.compute) кладёт в неё лог-кодированные вход и
    // выход, а потребитель файла таблицы (например `.cube`) читает сетку как
    // обычные значения — вывоз дал бы тихо неверный вид, а не ошибку. Если
    // вывоз когда-нибудь понадобится, печь надо заново на GPU в домене
    // потребителя; считать KernLogEncode/Decode на CPU нельзя — это второй
    // источник истины о виде.
    internal sealed class BakedGradeLutTarget
    {
        // Размер обязан совпадать с BakedGradeLutSize в PostProcess.compute.
        public const int Size = 33;

        private RenderTexture? _texture;

        public BakedGradeLutCache Cache { get; } = new();

        public RenderTexture Ensure()
        {
            if (_texture != null && _texture.IsCreated())
            {
                return _texture;
            }

            Release();
            _texture = new RenderTexture(
                Size,
                Size,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "_PPBakedGradeLut",
                dimension = TextureDimension.Tex3D,
                volumeDepth = Size,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _texture.Create();
            return _texture;
        }

        public void Release()
        {
            Cache.Invalidate();
            if (_texture == null)
            {
                return;
            }

            _texture.Release();
            CoreUtils.Destroy(_texture);
            _texture = null;
        }
    }
}
