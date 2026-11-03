using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

/// <summary>Separate shield strip above life: one framed compartment per charge.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ShieldChargesGraphic : MaskableGraphic
{
    [SerializeField] private Sprite _frameArtwork;
    [SerializeField] private Color _rimColor = new Color(.72f, .73f, .71f, 1f);
    [SerializeField] private Color _chargedColor = new Color(.07f, .36f, 1f, 1f);
    [SerializeField] private Color _emptyColor = new Color(.035f, .05f, .07f, 1f);
    // Reuses the life HUD atlas directly: no generated textures or runtime materials.
    public override Texture mainTexture => _frameArtwork != null ? _frameArtwork.texture : Texture2D.whiteTexture;
    private Vector4 _artUv;
    private Vector2 _artSize;
    private Vector2 _solidUv;
    private int _charges;
    private int _capacity;
    public int DisplayedCharges => _charges;
    public int Capacity => _capacity;
    public void SetCharges(int charges, int capacity)
    {
        capacity = Mathf.Max(0, capacity);
        charges = Mathf.Clamp(charges, 0, capacity);
        if (_charges == charges && _capacity == capacity) return;
        _charges = charges;
        _capacity = capacity;
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_capacity <= 0) return;
        Rect r = GetPixelAdjustedRect();
        if (r.width < 50f || r.height < 12f) return;
        _artUv = _frameArtwork != null ? DataUtility.GetOuterUV(_frameArtwork) : new Vector4(0f,0f,1f,1f);
        _artSize = _frameArtwork != null ? _frameArtwork.rect.size : Vector2.one;
        _solidUv = _frameArtwork != null ? ArtworkUv(820.5f,133.5f) : Vector2.zero;
        Color rim = _rimColor;
        float slant = r.height * .38f;
        Wedge(vh, r, slant, new Color(.025f,.03f,.03f,1f));
        if (_frameArtwork != null)
        {
            // Atlas regions come from HudLeft: the gunmetal plate, silver rails and side bevel.
            TextureQuad(vh, new Vector2(r.xMin+1f,r.yMin+1f), new Vector2(r.xMin+slant,r.yMax-1f),
                new Vector2(r.xMax-1f,r.yMax-1f), new Vector2(r.xMax-1f,r.yMin+1f), new Rect(480,190,400,24));
            TextureRect(vh, new Rect(r.xMin+slant, r.yMax-5f, r.width-slant-1f, 4f), new Rect(440,79,540,23));
            TextureRect(vh, new Rect(r.xMin+1f, r.yMin+1f, r.width-2f, 4f), new Rect(440,302,540,25));
            TextureQuad(vh, new Vector2(r.xMin+1f,r.yMin+1f), new Vector2(r.xMin+slant,r.yMax-1f),
                new Vector2(r.xMin+slant+3f,r.yMax-5f), new Vector2(r.xMin+5f,r.yMin+5f), new Rect(1027,128,16,160));
            TextureRect(vh, new Rect(r.xMax-5f,r.yMin+5f,4f,r.height-10f), new Rect(1027,128,16,160));
            TextureRect(vh, new Rect(r.xMin+5f,r.yMin+5f,5.5f,5.5f), new Rect(1001,93,27,26));
            TextureRect(vh, new Rect(r.xMax-7f,r.yMax-8f,4.5f,4.5f), new Rect(1001,93,27,26));
        }
        else
        {
            Wedge(vh, Inset(r,1f), slant, rim);
            Wedge(vh, Inset(r,4f), slant*.85f, new Color(.06f,.065f,.06f,1f));
        }
        ShieldIcon(vh, new Vector2(r.xMin + 21f, r.center.y), 11f,
            _charges > 0 ? _chargedColor : rim * new Color(.5f,.5f,.5f,1f));

        float left = r.xMin + 32f, right = r.xMax - 7f;
        int cells = Mathf.Min(_capacity, 128);
        float gap = Mathf.Min(6f, (right - left) / cells * .25f);
        float cellWidth = (right - left - gap * (cells - 1)) / cells;
        for (int i = 0; i < cells; i++)
        {
            bool charged = i < _charges;
            Rect socket = new Rect(left + i * (cellWidth + gap), r.yMin + 6f, cellWidth, r.height - 12f);
            // Each charge has its own complete border, opaque socket, and wide dark separator.
            Quad(vh, socket, new Color(.005f, .015f, .025f, 1f));
            Outline(vh, socket, 1.3f, rim * new Color(.6f,.6f,.6f,1f));
            Rect cell = Inset(socket, 2.5f);
            if (cell.width <= 0f) continue;
            if (charged)
            {
                Quad(vh, cell, _chargedColor);
                Quad(vh, new Rect(cell.xMin, cell.yMin, cell.width, cell.height * .3f), _chargedColor * new Color(.35f,.6f,.85f,1f));
                Quad(vh, new Rect(cell.xMin, cell.yMax - 1f, cell.width, 1f), Color.Lerp(_chargedColor, Color.white, .35f));
            }
            else
            {
                Quad(vh, cell, _emptyColor);
            }
        }
    }

    private void Wedge(VertexHelper vh, Rect r, float slant, Color tint)
    {
        int start = vh.currentVertCount;
        tint *= color;
        vh.AddVert(new Vector3(r.xMin,r.yMin), tint, _solidUv);
        vh.AddVert(new Vector3(r.xMin+slant,r.yMax), tint, _solidUv);
        vh.AddVert(new Vector3(r.xMax,r.yMax), tint, _solidUv);
        vh.AddVert(new Vector3(r.xMax,r.yMin), tint, _solidUv);
        vh.AddTriangle(start,start+1,start+2);
        vh.AddTriangle(start,start+2,start+3);
    }

    private void ShieldIcon(VertexHelper vh, Vector2 center, float size, Color tint)
    {
        int start = vh.currentVertCount;
        float x = center.x, y = center.y, w = size * .44f, h = size * .5f;
        tint *= color;
        vh.AddVert(new Vector3(x-w,y+h), tint, _solidUv);
        vh.AddVert(new Vector3(x+w,y+h), tint, _solidUv);
        vh.AddVert(new Vector3(x+w*.85f,y-h*.35f), tint, _solidUv);
        vh.AddVert(new Vector3(x,y-h), tint, _solidUv);
        vh.AddVert(new Vector3(x-w*.85f,y-h*.35f), tint, _solidUv);
        vh.AddTriangle(start,start+1,start+2);
        vh.AddTriangle(start,start+2,start+3);
        vh.AddTriangle(start,start+3,start+4);
    }

    private Vector2 ArtworkUv(float x, float y)
    {
        return new Vector2(Mathf.Lerp(_artUv.x,_artUv.z,x/_artSize.x),
            Mathf.Lerp(_artUv.y,_artUv.w,1f-y/_artSize.y));
    }

    private void TextureRect(VertexHelper vh, Rect r, Rect source)
    {
        TextureQuad(vh, new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),
            new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin),source);
    }

    private void TextureQuad(VertexHelper vh, Vector2 bottomLeft, Vector2 topLeft,
        Vector2 topRight, Vector2 bottomRight, Rect source)
    {
        int start = vh.currentVertCount;
        vh.AddVert(bottomLeft, color, ArtworkUv(source.xMin,source.yMax));
        vh.AddVert(topLeft, color, ArtworkUv(source.xMin,source.yMin));
        vh.AddVert(topRight, color, ArtworkUv(source.xMax,source.yMin));
        vh.AddVert(bottomRight, color, ArtworkUv(source.xMax,source.yMax));
        vh.AddTriangle(start,start+1,start+2);
        vh.AddTriangle(start,start+2,start+3);
    }

    private static Rect Inset(Rect r, float a) => Rect.MinMaxRect(r.xMin+a, r.yMin+a, r.xMax-a, r.yMax-a);
    private static Color WithAlpha(Color c, float alpha) { c.a *= alpha; return c; }
    private void Outline(VertexHelper vh, Rect r, float size, Color tint)
    {
        Quad(vh, new Rect(r.xMin, r.yMin, r.width, size), tint);
        Quad(vh, new Rect(r.xMin, r.yMax-size, r.width, size), tint);
        Quad(vh, new Rect(r.xMin, r.yMin+size, size, r.height-size*2f), tint);
        Quad(vh, new Rect(r.xMax-size, r.yMin+size, size, r.height-size*2f), tint);
    }
    private void Quad(VertexHelper vh, Rect r, Color tint)
    {
        int start = vh.currentVertCount;
        tint *= color;
        vh.AddVert(new Vector3(r.xMin,r.yMin), tint, _solidUv);
        vh.AddVert(new Vector3(r.xMin,r.yMax), tint, _solidUv);
        vh.AddVert(new Vector3(r.xMax,r.yMax), tint, _solidUv);
        vh.AddVert(new Vector3(r.xMax,r.yMin), tint, _solidUv);
        vh.AddTriangle(start,start+1,start+2);
        vh.AddTriangle(start,start+2,start+3);
    }
}
