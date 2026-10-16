using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.UI.UGUI
{
    /// <summary>
    /// 使用原生网格绘制圆角面板、键帽轮廓与手柄焦点。
    /// </summary>
    public sealed class SkyPanelGraphic : Image
    {
        private Color _border;
        private float _line = 1, _radius = 8;
        private bool _focused;
        /// <summary>
        /// 设置填色、轮廓与焦点状态，仅在外观变化时重建网格。
        /// </summary>
        public void Configure(Color fill, Color border, float line = 1, float radius = 8, bool focused = false)
        {
            if (color == fill && _border == border && _line == line && _radius == radius && _focused == focused) return;
            color = fill; _border = border; _line = line; _radius = radius; _focused = focused; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var area = GetPixelAdjustedRect();
            if (area.width <= 0 || area.height <= 0) return;
            Rounded(mesh, area, _radius, _border);
            float inset = Mathf.Min(_line, area.width / 2, area.height / 2);
            if (_focused)
            {
                var gap = Inset(area, inset); Rounded(mesh, gap, Mathf.Max(0, _radius - inset), new Color32(9, 31, 29, 255));
                inset += Mathf.Min(2, gap.width / 2, gap.height / 2);
            }
            Rounded(mesh, Inset(area, inset), Mathf.Max(0, _radius - inset), color);
        }
        private static Rect Inset(Rect area, float amount) => new Rect(area.x + amount, area.y + amount, Mathf.Max(0, area.width - amount * 2), Mathf.Max(0, area.height - amount * 2));
        private static void Rounded(VertexHelper mesh, Rect area, float radius, Color tint)
        {
            radius = Mathf.Min(radius, area.width / 2, area.height / 2);
            int first = mesh.currentVertCount; mesh.AddVert(area.center, tint, Vector2.zero);
            const int segments = 6, count = 4 * (segments + 1);
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = corner == 0 || corner == 3 ? area.xMin + radius : area.xMax - radius;
                float cy = corner < 2 ? area.yMin + radius : area.yMax - radius;
                float start = corner == 0 ? 180 : corner == 1 ? 270 : corner == 2 ? 0 : 90;
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (start + step * 90f / segments) * Mathf.Deg2Rad;
                    mesh.AddVert(new Vector3(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius), tint, Vector2.zero);
                }
            }
            for (int index = 0; index < count; index++) mesh.AddTriangle(first, first + index + 1, first + (index + 1) % count + 1);
        }
    }
}
