#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class DiceVolumeSystemCreator : EditorWindow
{
    private float dieSize = 0.5f;
    private Color dieColor = new Color(0.9f, 0.9f, 0.85f);
    private Color dotColor = new Color(0.1f, 0.1f, 0.1f);
    
    [MenuItem("Tools/Game/Create Dice Volume System")]
    public static void ShowWindow()
    {
        GetWindow<DiceVolumeSystemCreator>("Dice Volume Creator");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Dice Volume System Creator", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox(
            "Creates a dice-based volume control system:\n" +
            "• Two 3D dice with physics\n" +
            "• Volume meter UI (10 bars)\n" +
            "• Roll button to throw dice\n" +
            "• Volume = dice total (2-12)\n" +
            "• 11-12 shows red 'LOUD' indicator",
            MessageType.Info);
        
        EditorGUILayout.Space(10);
        
        dieSize = EditorGUILayout.Slider("Die Size", dieSize, 0.3f, 1f);
        dieColor = EditorGUILayout.ColorField("Die Color", dieColor);
        dotColor = EditorGUILayout.ColorField("Dot Color", dotColor);
        
        EditorGUILayout.Space(20);
        
        if (GUILayout.Button("Create Complete System", GUILayout.Height(40)))
        {
            CreateCompleteSystem();
        }
        
        EditorGUILayout.Space(10);
        
        GUILayout.Label("Individual Components", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Create Dice Only"))
        {
            CreateDiceOnly();
        }
        if (GUILayout.Button("Create UI Only"))
        {
            CreateUIOnly();
        }
        EditorGUILayout.EndHorizontal();
        
        if (GUILayout.Button("Create Table/Ground"))
        {
            CreateTable();
        }
    }
    
    void CreateCompleteSystem()
    {
        // Create parent object
        GameObject system = new GameObject("DiceVolumeSystem");
        Undo.RegisterCreatedObjectUndo(system, "Create Dice Volume System");
        
        // Add controller
        VolumeSettingsDice controller = system.AddComponent<VolumeSettingsDice>();
        
        // Create throw origin
        GameObject throwOrigin = new GameObject("ThrowOrigin");
        throwOrigin.transform.SetParent(system.transform);
        throwOrigin.transform.localPosition = new Vector3(0, 2f, 0);
        controller.throwOrigin = throwOrigin.transform;
        
        // Create dice
        VolumeDie die1 = CreateDie("Die1", new Vector3(-0.5f, 2f, 0));
        VolumeDie die2 = CreateDie("Die2", new Vector3(0.5f, 2f, 0));
        die1.transform.SetParent(system.transform);
        die2.transform.SetParent(system.transform);
        controller.die1 = die1;
        controller.die2 = die2;
        
        // Create table
        GameObject table = CreateTable();
        table.transform.SetParent(system.transform);
        
        // Create UI
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas");
            canvasObj.transform.SetParent(system.transform);
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        CreateVolumeUI(canvas.transform, controller);
        
        // Create camera if needed
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camObj = new GameObject("DiceCamera");
            camObj.transform.SetParent(system.transform);
            cam = camObj.AddComponent<Camera>();
            camObj.transform.position = new Vector3(0, 3f, -3f);
            camObj.transform.rotation = Quaternion.Euler(35, 0, 0);
        }
        
        Selection.activeGameObject = system;
        
        EditorUtility.DisplayDialog("Created!",
            "Dice Volume System created!\n\n" +
            "• Press Play to test\n" +
            "• Click 'Roll Dice' button to set volume\n" +
            "• Dice total determines volume (2-12)\n" +
            "• 11-12 shows red 'overload' effect",
            "OK");
    }
    
    VolumeDie CreateDie(string name, Vector3 position)
    {
        GameObject die = GameObject.CreatePrimitive(PrimitiveType.Cube);
        die.name = name;
        die.transform.position = position;
        die.transform.localScale = Vector3.one * dieSize;
        die.transform.rotation = Random.rotation;
        
        // Material - use the default cube's material as base
        MeshRenderer renderer = die.GetComponent<MeshRenderer>();
        Material mat = new Material(renderer.sharedMaterial);
        mat.name = "DieMaterial";
        mat.color = dieColor;
        // Try to set color for different render pipelines
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", dieColor); // URP/HDRP
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", dieColor); // Standard
        renderer.material = mat;
        
        // Physics
        Rigidbody rb = die.AddComponent<Rigidbody>();
        rb.mass = 0.1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        
        // VolumeDie script
        VolumeDie volumeDie = die.AddComponent<VolumeDie>();
        volumeDie.method = VolumeDie.DetectionMethod.Raycast;
        
        // Add numbers to faces
        AddDieNumbers(die.transform);
        
        Undo.RegisterCreatedObjectUndo(die, "Create Die");
        return volumeDie;
    }
    
    void AddDieNumbers(Transform die)
    {
        // Standard die face positions and values
        // 1 opposite 6, 2 opposite 5, 3 opposite 4
        (Vector3 pos, Vector3 rot, int value)[] faces = new[]
        {
            (Vector3.up * 0.51f, new Vector3(90, 0, 0), 6),      // Top
            (Vector3.down * 0.51f, new Vector3(-90, 0, 0), 1),   // Bottom
            (Vector3.right * 0.51f, new Vector3(0, 0, -90), 5),  // Right
            (Vector3.left * 0.51f, new Vector3(0, 0, 90), 2),    // Left
            (Vector3.forward * 0.51f, new Vector3(0, 0, 0), 4),  // Front
            (Vector3.back * 0.51f, new Vector3(0, 180, 0), 3),   // Back
        };
        
        foreach (var (pos, rot, value) in faces)
        {
            GameObject numObj = new GameObject($"Face{value}");
            numObj.transform.SetParent(die);
            numObj.transform.localPosition = pos * (dieSize / 0.5f);
            numObj.transform.localRotation = Quaternion.Euler(rot);
            numObj.transform.localScale = Vector3.one * 0.3f;
            
            TextMesh tm = numObj.AddComponent<TextMesh>();
            tm.text = GetDiceDots(value);
            tm.fontSize = 40;
            tm.color = dotColor;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.1f;
        }
    }
    
    string GetDiceDots(int value)
    {
        // Unicode dice dots or simple numbers
        // Using numbers for simplicity (could use • symbols)
        return value.ToString();
    }
    
    void CreateDiceOnly()
    {
        VolumeDie die1 = CreateDie("VolumeDie1", new Vector3(-0.5f, 1f, 0));
        VolumeDie die2 = CreateDie("VolumeDie2", new Vector3(0.5f, 1f, 0));
        
        EditorUtility.DisplayDialog("Created", "Created 2 dice. Add a VolumeSettingsDice controller and assign them.", "OK");
    }
    
    void CreateUIOnly()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("No Canvas", "Please create a Canvas first.", "OK");
            return;
        }
        
        VolumeSettingsDice controller = FindObjectOfType<VolumeSettingsDice>();
        CreateVolumeUI(canvas.transform, controller);
        
        EditorUtility.DisplayDialog("Created", "Volume UI created. Wire up the VolumeSettingsDice references.", "OK");
    }
    
    void CreateVolumeUI(Transform canvasTransform, VolumeSettingsDice controller)
    {
        // Main panel
        GameObject panel = new GameObject("DiceVolumePanel");
        panel.transform.SetParent(canvasTransform, false);
        
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(0, 0);
        panelRect.pivot = new Vector2(0, 0);
        panelRect.anchoredPosition = new Vector2(20, 20);
        panelRect.sizeDelta = new Vector2(300, 180);
        
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.9f, 0.75f, 0.3f, 0.5f);
        outline.effectDistance = new Vector2(2, 2);
        
        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(15, 15, 10, 10);
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        
        if (controller != null)
            controller.volumeMeterPanel = panel;
        
        // Title
        CreateUIText(panel.transform, "Title", "VOLUME", 20, new Color(0.9f, 0.75f, 0.3f), 30);
        
        // Volume number display
        GameObject numDisplay = CreateUIText(panel.transform, "VolumeNumber", "7", 48, Color.white, 55);
        if (controller != null)
            controller.volumeNumberText = numDisplay.GetComponent<TextMeshProUGUI>();
        
        // Volume bars container
        GameObject barsContainer = new GameObject("VolumeBars");
        barsContainer.transform.SetParent(panel.transform, false);
        
        RectTransform barsRect = barsContainer.AddComponent<RectTransform>();
        LayoutElement barsLE = barsContainer.AddComponent<LayoutElement>();
        barsLE.preferredHeight = 30;
        
        HorizontalLayoutGroup barsHLG = barsContainer.AddComponent<HorizontalLayoutGroup>();
        barsHLG.spacing = 4;
        barsHLG.childAlignment = TextAnchor.MiddleCenter;
        barsHLG.childControlWidth = true;
        barsHLG.childControlHeight = true;
        barsHLG.childForceExpandWidth = true;
        barsHLG.childForceExpandHeight = true;
        
        // Create 10 volume bars
        Image[] bars = new Image[10];
        for (int i = 0; i < 10; i++)
        {
            GameObject bar = new GameObject($"Bar{i}");
            bar.transform.SetParent(barsContainer.transform, false);
            
            Image barImg = bar.AddComponent<Image>();
            barImg.color = i < 7 ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.2f, 0.2f, 0.2f);
            bars[i] = barImg;
        }
        
        if (controller != null)
            controller.volumeBars = bars;
        
        // Status text
        GameObject statusObj = CreateUIText(panel.transform, "Status", "Click 'Roll' to set volume", 12, new Color(0.6f, 0.6f, 0.6f), 20);
        if (controller != null)
            controller.statusText = statusObj.GetComponent<TextMeshProUGUI>();
        
        // Buttons row
        GameObject btnRow = new GameObject("Buttons");
        btnRow.transform.SetParent(panel.transform, false);
        
        RectTransform btnRowRect = btnRow.AddComponent<RectTransform>();
        LayoutElement btnRowLE = btnRow.AddComponent<LayoutElement>();
        btnRowLE.preferredHeight = 35;
        
        HorizontalLayoutGroup btnHLG = btnRow.AddComponent<HorizontalLayoutGroup>();
        btnHLG.spacing = 10;
        btnHLG.childAlignment = TextAnchor.MiddleCenter;
        btnHLG.childControlWidth = true;
        btnHLG.childControlHeight = true;
        btnHLG.childForceExpandWidth = true;
        btnHLG.childForceExpandHeight = true;
        
        // Roll button
        Button rollBtn = CreateUIButton(btnRow.transform, "RollButton", "ROLL DICE", new Color(0.2f, 0.5f, 0.3f));
        if (controller != null)
            controller.throwButton = rollBtn;
        
        // Reset button
        Button resetBtn = CreateUIButton(btnRow.transform, "ResetButton", "RESET", new Color(0.4f, 0.35f, 0.3f));
        if (controller != null)
            controller.resetButton = resetBtn;
        
        Undo.RegisterCreatedObjectUndo(panel, "Create Volume UI");
        Selection.activeGameObject = panel;
    }
    
    GameObject CreateUIText(Transform parent, string name, string text, int fontSize, Color color, float height)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        
        return obj;
    }
    
    Button CreateUIButton(Transform parent, string name, string text, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        
        Image bg = btnObj.AddComponent<Image>();
        bg.color = bgColor;
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        
        return btn;
    }
    
    GameObject CreateTable()
    {
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "DiceTable";
        table.transform.position = new Vector3(0, -0.1f, 0);
        table.transform.localScale = new Vector3(5f, 0.2f, 5f);
        table.tag = "Ground";
        
        // Material - use default material as base
        MeshRenderer renderer = table.GetComponent<MeshRenderer>();
        Material mat = new Material(renderer.sharedMaterial);
        mat.name = "TableMaterial";
        Color tableColor = new Color(0.3f, 0.2f, 0.15f); // Dark wood color
        mat.color = tableColor;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", tableColor); // URP/HDRP
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", tableColor); // Standard
        renderer.material = mat;
        
        // Make sure it's not a trigger
        Collider col = table.GetComponent<Collider>();
        col.isTrigger = false;
        
        Undo.RegisterCreatedObjectUndo(table, "Create Dice Table");
        return table;
    }
}
#endif
