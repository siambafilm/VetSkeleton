using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class BoneViewerSystem : MonoBehaviour
{
    [Header("Список префабов (заполняется в инспекторе)")]
    public List<GameObject> prefabList = new List<GameObject>();

    [Header("Камера / фокус")]
    [Tooltip("Отступ фокуса при подборе дистанции")]
    public float focusPadding = 1.4f;
    [Tooltip("Размер маркера кости (доля от общего размера скелета)")]
    public float markerSizeFraction = 0.02f;

    // ---------------- Runtime ----------------
    private Camera cam;
    private OrbitCamera orbitCam;
    private GameObject currentInstance;
    private readonly List<BoneEntry> bones = new List<BoneEntry>();
    private float markerLocalScale = 0.05f;
    private Material markerMaterial;

    private Canvas     canvas;
    private RectTransform uiRoot;      // скрываемый UI
    private RectTransform topPanel;
    private RectTransform boneListContent;

    // Запоминаем, какая кость сейчас в режиме «только она»
    private BoneEntry soloedBone;

    private class BoneEntry
    {
        public string name;
        public Transform transform;
        public GameObject visualRoot;   // что включаем/выключаем
        public Renderer renderer;       // может быть null
        public bool visible = true;
        public Text toggleLabel;
        public Image itemBg;            // фон карточки в списке
    }

    // ===========================================================
    //  Спрайт со скруглёнными углами (генерируется в рантайме)
    // ===========================================================
    private static Sprite _roundedSprite;
    static Sprite RoundedSprite
    {
        get
        {
            if (_roundedSprite != null) return _roundedSprite;

            int size = 64;
            int radius = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = x + 0.5f;
                    float cy = y + 0.5f;
                    float alpha = 1f;

                    float dx = 0f, dy = 0f;
                    if      (cx < radius)           dx = radius - cx;
                    else if (cx > size - radius)    dx = cx - (size - radius);
                    if      (cy < radius)           dy = radius - cy;
                    else if (cy > size - radius)    dy = cy - (size - radius);

                    if (dx > 0f && dy > 0f)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        alpha = Mathf.Clamp01(radius - dist);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _roundedSprite = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            return _roundedSprite;
        }
    }

    // ===========================================================
    //  Инициализация
    // ===========================================================
    void Awake()
    {
        EnsureCamera();
        EnsureLight();
        EnsureEventSystem();
        BuildUI();
    }

    void EnsureCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);
        }
        orbitCam = cam.GetComponent<OrbitCamera>();
        if (orbitCam == null) orbitCam = cam.gameObject.AddComponent<OrbitCamera>();
        orbitCam.cam = cam;
        orbitCam.FocusOn(Vector3.zero, 10f, true);
    }

    void EnsureLight()
    {
        if (FindObjectOfType<Light>() != null) return;
        var go = new GameObject("Directional Light");
        var l  = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1f;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    // ===========================================================
    //  Построение UI полностью в коде
    // ===========================================================
    void BuildUI()
    {
        // Canvas
        var canvasGO = new GameObject("BoneViewerCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ---------------------------------------------------------
        // ВАЖНО: сначала создаём UIRoot (скрываемый слой),
        // а AlwaysVisible — ПОСЛЕ, чтобы кнопка X рисовалась поверх.
        // ---------------------------------------------------------

        // --- Toggleable UI ---
        uiRoot = NewRect("UIRoot", canvas.transform);
        Stretch(uiRoot);

        // TopPanel (сверху, кнопки префабов)
        topPanel = NewRect("TopPanel", uiRoot);
        topPanel.anchorMin = new Vector2(0, 1);
        topPanel.anchorMax = new Vector2(1, 1);
        topPanel.pivot     = new Vector2(0.5f, 1f);
        topPanel.offsetMin = new Vector2(0, -130);
        topPanel.offsetMax = new Vector2(0, 0);

        var topBg = topPanel.gameObject.AddComponent<Image>();
        topBg.color = new Color(0.08f, 0.09f, 0.13f, 0.88f);

        var topHLG = topPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        topHLG.childAlignment = TextAnchor.MiddleLeft;
        topHLG.spacing = 10;
        topHLG.padding = new RectOffset(20, 20, 15, 15);
        topHLG.childControlHeight = true;
        topHLG.childControlWidth  = false;
        topHLG.childForceExpandWidth  = false;
        topHLG.childForceExpandHeight = false;

        foreach (var prefab in prefabList)
        {
            if (prefab == null) continue;
            var p = prefab;
            var btn = CreateButton(topPanel, prefab.name, new Vector2(220, 100),
                                   new Color(0.24f, 0.5f, 0.88f, 1f),
                                   () => SpawnPrefab(p));
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = 220;
            le.preferredHeight = 100;
        }

        // LeftPanel (слева, список костей)
        var leftPanel = NewRect("LeftPanel", uiRoot);
        leftPanel.anchorMin = new Vector2(0, 0);
        leftPanel.anchorMax = new Vector2(0, 1);
        leftPanel.pivot     = new Vector2(0, 0.5f);
        leftPanel.offsetMin = new Vector2(0, 0);
        leftPanel.offsetMax = new Vector2(430, -130);

        var lpBg = leftPanel.gameObject.AddComponent<Image>();
        lpBg.color = new Color(0.08f, 0.09f, 0.13f, 0.88f);

        // ScrollView
        var scrollGO = NewRect("ScrollView", leftPanel);
        Stretch(scrollGO, 10, 10, 10, 10);
        var scrollRect = scrollGO.gameObject.AddComponent<ScrollRect>();

        var viewport = NewRect("Viewport", scrollGO);
        Stretch(viewport);
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 0.02f);
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        boneListContent = NewRect("Content", viewport);
        boneListContent.anchorMin = new Vector2(0, 1);
        boneListContent.anchorMax = new Vector2(1, 1);
        boneListContent.pivot     = new Vector2(0.5f, 1f);
        boneListContent.offsetMin = Vector2.zero;
        boneListContent.offsetMax = Vector2.zero;

        var vlg = boneListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childControlHeight = true;
        vlg.childControlWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;
        vlg.spacing = 6;
        vlg.padding = new RectOffset(6, 6, 6, 6);

        var fitter = boneListContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content  = boneListContent;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical   = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        // --- AlwaysVisible (создаётся ПОСЛЕ uiRoot => рендерится поверх) ---
        var alwaysRoot = NewRect("AlwaysVisible", canvas.transform);
        Stretch(alwaysRoot);

        // Кнопка X (правый верх)
        var xBtn = CreateButton(alwaysRoot, "X", new Vector2(100, 100),
                                new Color(0.9f, 0.28f, 0.35f, 1f),
                                ToggleUI);
        var xRT = xBtn.GetComponent<RectTransform>();
        xRT.anchorMin = new Vector2(1, 1);
        xRT.anchorMax = new Vector2(1, 1);
        xRT.pivot     = new Vector2(1, 1);
        xRT.anchoredPosition = new Vector2(-20, -20);
        xRT.sizeDelta = new Vector2(100, 100);

        // Материал для маркеров костей
        var shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        markerMaterial = new Material(shader);
        markerMaterial.color = new Color(0.1f, 1f, 0.9f, 1f);
    }

    void ToggleUI()
    {
        if (uiRoot == null) return;
        uiRoot.gameObject.SetActive(!uiRoot.gameObject.activeSelf);
    }

    // ===========================================================
    //  Спавн префаба
    // ===========================================================
    void SpawnPrefab(GameObject prefab)
    {
        if (currentInstance != null) Destroy(currentInstance);
        bones.Clear();
        soloedBone = null;   // сбрасываем режим соло при смене модели

        currentInstance = Instantiate(prefab);
        currentInstance.transform.position = Vector3.zero;

        FindBones(currentInstance);
        ComputeMarkerScale();
        foreach (var b in bones) SetupBoneVisual(b);
        RebuildBoneList();

        FocusCameraOnActive(true);
    }

    // ===========================================================
    //  Поиск костей
    // ===========================================================
    void FindBones(GameObject instance)
    {
        var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null && smr.bones != null && smr.bones.Length > 0)
        {
            var set = new HashSet<Transform>();
            if (smr.rootBone != null) AddBone(smr.rootBone, set);
            foreach (var t in smr.bones) AddBone(t, set);
            return;
        }

        foreach (var t in instance.GetComponentsInChildren<Transform>())
        {
            if (t == instance.transform) continue;
            if (t.gameObject.name.StartsWith("Marker_")) continue;
            bones.Add(new BoneEntry { name = t.name, transform = t });
        }
    }

    void AddBone(Transform t, HashSet<Transform> set)
    {
        if (t == null || !set.Add(t)) return;
        bones.Add(new BoneEntry { name = t.name, transform = t });
    }

    void ComputeMarkerScale()
    {
        if (bones.Count == 0) { markerLocalScale = 0.05f; return; }

        var b = new Bounds(bones[0].transform.position, Vector3.zero);
        foreach (var bone in bones) b.Encapsulate(bone.transform.position);
        float size = b.size.magnitude;
        if (size < 0.01f) size = 1f;
        markerLocalScale = size * markerSizeFraction;
    }

    // ===========================================================
    //  Визуализация кости
    // ===========================================================
    void SetupBoneVisual(BoneEntry b)
    {
        var r = b.transform.GetComponent<Renderer>();
        if (r != null)
        {
            b.renderer   = r;
            b.visualRoot = b.transform.gameObject;
            b.visible    = r.enabled;
            return;
        }

        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Marker_" + b.name;
        marker.transform.SetParent(b.transform, false);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale    = Vector3.one * markerLocalScale;

        var col = marker.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = marker.GetComponent<MeshRenderer>();
        mr.material = markerMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        b.visualRoot = marker;
        b.visible    = true;
    }

    // ===========================================================
    //  Перестроение списка костей в UI
    // ===========================================================
    void RebuildBoneList()
    {
        for (int i = boneListContent.childCount - 1; i >= 0; i--)
            Destroy(boneListContent.GetChild(i).gameObject);

        foreach (var bone in bones)
            CreateBoneItem(bone);
    }

    void CreateBoneItem(BoneEntry bone)
    {
        var item = NewRect("BoneItem", boneListContent);

        // Карточка с фоном (скруглённая)
        var bg = item.gameObject.AddComponent<Image>();
        bg.color  = new Color(0.18f, 0.20f, 0.26f, 0.9f);
        bg.sprite = RoundedSprite;
        bg.type   = Image.Type.Sliced;
        bone.itemBg = bg;

        var hlg = item.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = 6;
        hlg.padding = new RectOffset(8, 8, 6, 6);
        hlg.childControlHeight = true;
        hlg.childControlWidth  = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        var le = item.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 76;
        le.minHeight       = 76;

        // Кнопка +/-
        var toggleBtn = CreateButton(item, bone.visible ? "-" : "+", new Vector2(76, 0),
                                     new Color(0.35f, 0.35f, 0.42f, 1f),
                                     () => ToggleBone(bone));
        toggleBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 76;
        bone.toggleLabel = toggleBtn.GetComponentInChildren<Text>();

        // Кнопка "0" (соло)
        var zeroBtn = CreateButton(item, "0", new Vector2(76, 0),
                                   new Color(0.2f, 0.7f, 0.55f, 1f),
                                   () => SoloBone(bone));
        zeroBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 76;

        // Имя кости
        var nameGO = NewRect("Name", item);
        var nameTxt = nameGO.gameObject.AddComponent<Text>();
        nameTxt.text = bone.name;
        nameTxt.alignment = TextAnchor.MiddleLeft;
        nameTxt.color = new Color(0.92f, 0.94f, 1f, 1f);
        nameTxt.fontSize = 26;
        nameTxt.font = GetDefaultFont();
        nameTxt.horizontalOverflow = HorizontalWrapMode.Wrap;
        nameTxt.verticalOverflow   = VerticalWrapMode.Truncate;

        var nameLE = nameGO.gameObject.AddComponent<LayoutElement>();
        nameLE.preferredWidth = 250;
        nameLE.minWidth       = 100;
    }

    // ===========================================================
    //  Логика видимости
    // ===========================================================
    void ToggleBone(BoneEntry bone)
    {
        SetBoneVisible(bone, !bone.visible);
        FocusCameraOnActive(false);
    }

    void SoloBone(BoneEntry target)
    {
        // Повторный клик по «0» на той же кости → показать все
        if (soloedBone == target)
        {
            foreach (var b in bones) SetBoneVisible(b, true);
            soloedBone = null;
        }
        else
        {
            foreach (var b in bones) SetBoneVisible(b, b == target);
            soloedBone = target;
        }
        FocusCameraOnActive(false);
    }

    void SetBoneVisible(BoneEntry bone, bool visible)
    {
        bone.visible = visible;
        if (bone.visualRoot != null)
        {
            if (bone.renderer != null) bone.renderer.enabled = visible;
            else                       bone.visualRoot.SetActive(visible);
        }
        if (bone.toggleLabel != null)
            bone.toggleLabel.text = visible ? "-" : "+";

        // Карточка чуть притухает, когда кость скрыта — для наглядности
        if (bone.itemBg != null)
            bone.itemBg.color = visible
                ? new Color(0.18f, 0.20f, 0.26f, 0.90f)
                : new Color(0.10f, 0.11f, 0.15f, 0.65f);
    }

    // ===========================================================
    //  Фокус камеры на активных костях
    // ===========================================================
    void FocusCameraOnActive(bool instant)
    {
        var active = new List<BoneEntry>();
        foreach (var b in bones) if (b.visible) active.Add(b);

        if (active.Count == 0)
        {
            Vector3 c = currentInstance != null ? currentInstance.transform.position : Vector3.zero;
            orbitCam.FocusOn(c, 5f, instant);
            return;
        }

        Bounds bounds = new Bounds(active[0].transform.position, Vector3.zero);
        foreach (var b in active) bounds.Encapsulate(b.transform.position);

        Vector3 center = bounds.center;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float distance = radius / Mathf.Sin(fovRad * 0.5f) * focusPadding;

        orbitCam.FocusOn(center, distance, instant);
    }

    // ===========================================================
    //  Хелперы UI
    // ===========================================================
    static Font GetDefaultFont()
    {
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt, float l = 0, float r = 0, float b = 0, float t = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
    }

    static Button CreateButton(RectTransform parent, string label, Vector2 size,
                               Color bg, System.Action onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.color  = bg;
        img.sprite = RoundedSprite;      // скруглённые углы
        img.type   = Image.Type.Sliced;

        // Небольшая тень для объёма
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor    = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -3f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        var tGO = new GameObject("Text", typeof(RectTransform));
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero;
        tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;

        var txt = tGO.AddComponent<Text>();
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = 32;
        txt.fontStyle = FontStyle.Bold;
        txt.font = GetDefaultFont();
        txt.raycastTarget = false;

        // Тень у текста — читается лучше
        var txtShadow = tGO.AddComponent<Shadow>();
        txtShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
        txtShadow.effectDistance = new Vector2(1f, -1f);

        return btn;
    }
}