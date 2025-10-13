using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace okitsu.net.SimpleToggleGenerator
{
    public class SimpleToggleGenerator : EditorWindow
    {
        [Serializable]
        public class ToggleGroup
        {
            public string groupName = "";
            public Texture2D groupIcon;
            public VRCExpressionsMenu groupRootMenu;
            public VRCExpressionsMenu mergeMenu;
            public bool exclusiveMode = true;
            public bool blendTreeMode = true;
            public bool allowDisableAll = false;
            public AnimatorControllerParameterType parameterType = AnimatorControllerParameterType.Bool;
            public string intParameterName = "";
            public List<GameObject> gameObject = new();
            public bool isFoldout = true;
            public List<bool> isSettingFoldout = new();
            public List<bool> save = new();
            public List<bool> sync = new();
            public List<Texture2D> propIcon = new();
            public List<string> customName = new();
            public List<string> parameterName = new();
            [NonSerialized] public List<GameObject> lastGameObjectOrder;
            [NonSerialized] public UnityEditorInternal.ReorderableList reorderableList;
        }

        private List<ToggleGroup> _toggleGroups = new();
        private UnityEditorInternal.ReorderableList _groupReorderableList;
        private string _savePath;
        private VRCAvatarDescriptor _avatar;

        private void OnEnable()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            SearchForAvatar();
            SetupGroupReorderableList();
        }
        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            SearchForAvatar();
        }
        private void SearchForAvatar()
        {
            // _avatar が未設定の場合、VRCAvatarDescriptor を探す
            if (_avatar == null)
            {
                _avatar = FindObjectOfType<VRCAvatarDescriptor>();

                if (_avatar != null)
                {
                    Debug.Log($"Found VRCAvatarDescriptor: {_avatar.name}");
                    Repaint();
                }
                else
                {
                    Debug.LogWarning("VRCAvatarDescriptor not found in the scene.");
                }
            }
        }

        private AnimatorController _animatorController;
        private VRCExpressionsMenu _vrcExpressionsMenu;
        private VRCExpressionsMenu _rootMenu;
        private string _rootMenuName = "";
        private VRCExpressionParameters _vrcExpressionParameters;
        private string _blendTreeBaseName = "";
        private string _prefix = "STG: ";
        private bool _foldoutMenu = false;
        private bool _disableCfmDialog
        {
            get { return EditorPrefs.GetBool("DisableCfmDialog", false); }
            set { EditorPrefs.SetBool("DisableCfmDialog", value); }
        }
        private bool _enforceParameterType
        {
            get { return EditorPrefs.GetBool("EnforceParameterType", false); }
            set { EditorPrefs.SetBool("EnforceParameterType", value); }
        }
        private bool _experimentalOption
        {
            get { return EditorPrefs.GetBool("ExperimentalOption", false); }
            set { EditorPrefs.SetBool("ExperimentalOption", value); }
        }

        [MenuItem("Tools/Simple Toggle Generator")]
        public static void ShowWindow()
        {
            GetWindow<SimpleToggleGenerator>("Simple Toggle Generator");
        }

        private Vector2 _scrollPosition;
        private VRCAvatarDescriptor _previousAvatar;
        private BlendTree _rootBlendTree;
        private AnimatorControllerLayer _blendTreeLayer;
        private AnimationClip _doNotEditClip;
        private string _parameterDriver = "_ParameterDriver";

        // ====GUI====
        private void OnGUI()
        {
            GUILayout.Label("Save Path", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            // Save Path
            _savePath = EditorGUILayout.TextField(new GUIContent("Save Path", "If Avatar is specified, AnimationClip will be saved in AvatarName/LayerName/. If not specified, it will be saved in AnimatorControllerName/Layer Name/."), _savePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string newSavePath = EditorUtility.OpenFolderPanel("Select Save Path", Application.dataPath, "");
                if (!string.IsNullOrEmpty(newSavePath))
                {
                    _savePath = "Assets" + newSavePath.Substring(Application.dataPath.Length);
                }
            }
            GUILayout.EndHorizontal();

            _avatar = EditorGUILayout.ObjectField("Avatar (Required)", _avatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;

            // VRCAvatarDescriptorの割り当てまたは変更時にAnimatorController/VRCExpressionsMenu/VRCExpressionParametersを自動的に割り当てる
            if (_avatar != _previousAvatar)
            {
                _previousAvatar = _avatar;

                if (_avatar != null)
                {
                    // FXLayer
                    if (_avatar.baseAnimationLayers[4].animatorController != null)
                    {
                        _animatorController = (AnimatorController)_avatar.baseAnimationLayers[4].animatorController;
                    }
                    else
                    {
                        Debug.LogWarning("FXLayer is not assigned in the AvatarDescriptor's baseAnimationLayers.");
                        _animatorController = null;
                    }

                    // VRCExpressionsMenu
                    if (_avatar.expressionsMenu != null)
                    {
                        _vrcExpressionsMenu = _avatar.expressionsMenu;
                    }
                    else
                    {
                        Debug.LogWarning("ExMenu is not assigned to AvatarDescriptor.");
                        _vrcExpressionsMenu = null;
                    }

                    // VRCExpressionParameters
                    if (_avatar.expressionParameters != null)
                    {
                        _vrcExpressionParameters = _avatar.expressionParameters;
                    }
                    else
                    {
                        Debug.LogWarning("EXParam is not assigned to AvatarDescriptor.");
                        _vrcExpressionParameters = null;
                    }
                }
                else
                {
                    _animatorController = null;
                    _vrcExpressionsMenu = null;
                    _vrcExpressionParameters = null;
                }
            }

            if (_avatar == null)
            {
                _vrcExpressionsMenu = null;
                _vrcExpressionParameters = null;
            }

            // FXLayer
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.baseAnimationLayers[4].animatorController != null);
            _animatorController = EditorGUILayout.ObjectField("Animator Controller (FX)", _animatorController, typeof(AnimatorController), false) as AnimatorController;
            if (_avatar != null && _animatorController == null && _avatar.baseAnimationLayers[4].animatorController == null)
            {
                if (GUILayout.Button("Create FXlayer", GUILayout.Width(100)))
                {
                    if (!_avatar.customizeAnimationLayers)
                    {
                        _avatar.customizeAnimationLayers = true;
                    }
                    if (_avatar.baseAnimationLayers[4].isDefault)
                    {
                        _avatar.baseAnimationLayers[4].isDefault = false;
                    }

                    var controller = AnimatorController.CreateAnimatorControllerAtPath(_savePath + "/" + "FX_" + _avatar.name + ".controller");

                    _avatar.baseAnimationLayers[4].animatorController = (RuntimeAnimatorController)AssetDatabase.LoadAssetAtPath(_savePath + "/" + "FX_" + _avatar.name + ".controller", typeof(RuntimeAnimatorController));
                    _animatorController = (AnimatorController)_avatar.baseAnimationLayers[4].animatorController;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            // ExMenu
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.expressionsMenu != null);
            _vrcExpressionsMenu = EditorGUILayout.ObjectField("Expressions Menu", _vrcExpressionsMenu, typeof(VRCExpressionsMenu), false) as VRCExpressionsMenu;
            if (_avatar != null && _vrcExpressionsMenu == null && _avatar.expressionsMenu == null)
            {
                if (GUILayout.Button("Create ExMenu", GUILayout.Width(100)))
                {
                    if (!_avatar.customExpressions)
                    {
                        _avatar.customExpressions = true;
                    }
                    VRCExpressionsMenu expressionsMenu = CreateInstance<VRCExpressionsMenu>();
                    AssetDatabase.CreateAsset(expressionsMenu, _savePath + "/" + "ExMenu_" + _avatar.name + ".asset");
                    _avatar.expressionsMenu = AssetDatabase.LoadAssetAtPath(_savePath + "/" + "ExMenu_" + _avatar.name + ".asset", typeof(VRCExpressionsMenu)) as VRCExpressionsMenu;
                    _vrcExpressionsMenu = _avatar.expressionsMenu;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            // RootMenu フィールドを追加
            _rootMenu = EditorGUILayout.ObjectField("Root Menu", _rootMenu, typeof(VRCExpressionsMenu), false) as VRCExpressionsMenu;

            // RootMenu が null の場合のみ RootMenuName を表示
            if (_rootMenu == null && _vrcExpressionsMenu != null)
            {
                _rootMenuName = EditorGUILayout.TextField(
                    new GUIContent("Root Menu Name", ""),
                    string.IsNullOrEmpty(_rootMenuName) ? "Simple Toggle Menu" : _rootMenuName
                );
            }

            // ExParam
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.expressionParameters != null);
            _vrcExpressionParameters = EditorGUILayout.ObjectField("Expression Parameters", _vrcExpressionParameters, typeof(VRCExpressionParameters), false) as VRCExpressionParameters;
            if (_avatar != null && _vrcExpressionParameters == null && _avatar.expressionParameters == null)
            {
                if (GUILayout.Button("Create ExParam", GUILayout.Width(100)))
                {
                    if (!_avatar.customExpressions)
                    {
                        _avatar.customExpressions = true;
                    }
                    VRCExpressionParameters expressionParameters = CreateInstance<VRCExpressionParameters>();
                    AssetDatabase.CreateAsset(expressionParameters, _savePath + "/" + "ExParan_" + _avatar.name + ".asset");
                    _avatar.expressionParameters = AssetDatabase.LoadAssetAtPath(_savePath + "/" + "ExParan_" + _avatar.name + ".asset", typeof(VRCExpressionParameters)) as VRCExpressionParameters;
                    _vrcExpressionParameters = _avatar.expressionParameters;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            // BlendTree Base Name
            _blendTreeBaseName = EditorGUILayout.TextField(
                new GUIContent("BlendTree Base Name", "The base name to use when creating the BlendTree"),
                string.IsNullOrEmpty(_blendTreeBaseName) ? "SimpleToggleGenerator" : _blendTreeBaseName
            );

            _foldoutMenu = EditorGUILayout.Foldout(_foldoutMenu, "Advanced Options");
            if (_foldoutMenu)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Disable Confirm dialog", GUILayout.Width(205));
                _disableCfmDialog = EditorGUILayout.Toggle(_disableCfmDialog);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Enforce Expression Parameter Type", GUILayout.Width(205));
                _enforceParameterType = EditorGUILayout.Toggle(_enforceParameterType);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Enable Experimental Option", GUILayout.Width(205));
                _experimentalOption = EditorGUILayout.Toggle(_experimentalOption);
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Toggle Groups", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Group"))
            {
                var newGroup = new ToggleGroup();
                SetupReorderableList(newGroup);
                _toggleGroups.Add(newGroup);
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Clear All Groups"))
            {
                if (_toggleGroups.Count > 0)
                {
                    string message =
                        "Clear all groups. Are you sure?";
                    bool checkClear = EditorUtility.DisplayDialog(
                        "Clear All Groups",
                        message,
                        "Yes",
                        "Cancel"
                    );
                    if (checkClear)
                    {
                        _toggleGroups.Clear();
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (GUILayout.Button("Generate Toggle"))
            {
                foreach (var group in _toggleGroups)
                {
                    if (!group.gameObject.Any(obj => obj != null && obj.activeSelf) && group.gameObject.Count > 0 && !group.allowDisableAll && group.exclusiveMode)
                    {
                        group.gameObject[0].SetActive(true);
                    }
                }
                GenerateToggle();
            }

            GUILayout.Space(10);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            _groupReorderableList.DoLayoutList();
            EditorGUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                SaveToggleGroups();
            }
            if (GUILayout.Button("Load"))
            {
                LoadToggleGroups();
            }
            GUILayout.EndHorizontal();
        }

        private void SetupGroupReorderableList()
        {
            _groupReorderableList = new UnityEditorInternal.ReorderableList(_toggleGroups, typeof(ToggleGroup), true, true, true, true);

            _groupReorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Toggle Groups (Drag to reorder)");
            };

            _groupReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= _toggleGroups.Count) return;
                var group = _toggleGroups[index];

                float lh = UIStyles.LineHeight;            // 1行の高さ
                float vs = UIStyles.VerticalSpacing;       // 行間
                float padding = 6f;                        // 枠内の左右余白
                float xL = rect.x + padding;               // 左端（余白込み）
                float wL = rect.width - (padding * 2);     // 有効幅（余白込み）
                float yplus = rect.y + 1;

                // 背景
                GUI.Box(new Rect(rect.x, rect.y, rect.width, rect.height), GUIContent.none, EditorStyles.helpBox);

                // ヘッダー部分（Foldout）（三角マークのみ）
                Rect foldoutRect = new Rect(xL, yplus, 16, lh);
                group.isFoldout = EditorGUI.Foldout(foldoutRect, group.isFoldout, GUIContent.none, false);

                // ラベル部分（ドラッグ用）
                // Foldout の右にラベルだけ表示する。
                Rect labelRect = new Rect(xL + 18, yplus, wL - 142, lh);
                EditorGUI.LabelField(labelRect, group.groupName);

                // Remove Group ボタン
                if (GUI.Button(new Rect(rect.x + rect.width - padding - 120, yplus + 2, 120, lh), "Remove Group"))
                {
                    _toggleGroups.RemoveAt(index);
                    return;
                }
                yplus += lh + vs;

                if (group.isFoldout)
                {
                    EditorGUI.indentLevel++;

                    // Group Name
                    group.groupName = EditorGUI.TextField(
                        new Rect(xL, yplus, wL, lh),
                        "Group Name", string.IsNullOrEmpty(group.groupName) ? "New Layer" : group.groupName
                    );
                    yplus += lh + vs;

                    // Group Icon
                    group.groupIcon = (Texture2D)EditorGUI.ObjectField(
                        new Rect(xL, yplus, wL, EditorGUIUtility.singleLineHeight),
                        "Group Icon", group.groupIcon, typeof(Texture2D), false);
                    yplus += lh + vs;

                    // Group Menu
                    group.groupRootMenu = (VRCExpressionsMenu)EditorGUI.ObjectField(
                        new Rect(xL, yplus, wL, EditorGUIUtility.singleLineHeight),
                        new GUIContent("Group Root Menu", "If a menu is specified here, it will be added to the specified menu instead of the RootMenu."),
                        group.groupRootMenu,
                        typeof(VRCExpressionsMenu),
                        false
                    );
                    yplus += lh + vs;

                    group.mergeMenu = (VRCExpressionsMenu)EditorGUI.ObjectField(
                        new Rect(xL, yplus, wL, EditorGUIUtility.singleLineHeight),
                        "Merge Menu", group.mergeMenu, typeof(VRCExpressionsMenu), false);
                    yplus += lh + vs;

                    // Exclusive Mode
                    bool prevExclusiveMode = group.exclusiveMode;
                    group.exclusiveMode = EditorGUI.Toggle(
                        new Rect(xL, yplus, wL, lh),
                        "Exclusive Mode", group.exclusiveMode
                    );
                    yplus += lh + vs;
                    if (!prevExclusiveMode && group.exclusiveMode)
                    {
                        bool firstFound = false;
                        for (int k = 0; k < group.gameObject.Count; k++)
                        {
                            if (group.gameObject[k] != null)
                            {
                                if (!firstFound)
                                {
                                    // 最初の有効オブジェクトを残す（無効が1つもなければ最初の要素を有効にする）
                                    group.gameObject[k].SetActive(true);
                                    firstFound = true;
                                }
                                else
                                {
                                    group.gameObject[k].SetActive(false);
                                }
                            }
                        }
                    }

                    if (group.exclusiveMode)
                    {
                        group.blendTreeMode = EditorGUI.Toggle(
                            new Rect(xL, yplus, wL, lh),
                            "BlendTree Mode", group.blendTreeMode
                        );
                        yplus += lh + vs;

                        group.allowDisableAll = EditorGUI.Toggle(
                            new Rect(xL, yplus, wL, lh),
                            "Allow Disable All", group.allowDisableAll
                        );
                        yplus += lh + vs;

                        // Parameter Type (Bool / Float / Int)
                        List<string> typeOptions = new List<string> { "Bool", "Float" };
                        if (_experimentalOption && group.gameObject.Count >= 8)
                            typeOptions.Add("Int");

                        int selectedIndex = 0;
                        if (group.parameterType == AnimatorControllerParameterType.Float) selectedIndex = 1;
                        else if (group.parameterType == AnimatorControllerParameterType.Int && typeOptions.Contains("Int")) selectedIndex = 2;

                        selectedIndex = EditorGUI.Popup(
                            new Rect(xL, yplus, wL, lh),
                            "Parameter Type",
                            selectedIndex,
                            typeOptions.ToArray()
                        );

                        if (selectedIndex == 0) group.parameterType = AnimatorControllerParameterType.Bool;
                        else if (selectedIndex == 1) group.parameterType = AnimatorControllerParameterType.Float;
                        else if (selectedIndex == 2) group.parameterType = AnimatorControllerParameterType.Int;
                        yplus += lh + vs;

                        // Int 選択時は専用パラメータ名入力
                        if (group.parameterType == AnimatorControllerParameterType.Int)
                        {
                            group.intParameterName = EditorGUI.TextField(
                                new Rect(xL, yplus, wL, lh),
                                "Parameter Name", group.intParameterName
                            );
                            yplus += lh + vs;
                        }
                    }

                    // ReorderableList（オブジェクト一覧）
                    if (group.reorderableList == null)
                        SetupReorderableList(group);

                    Rect listRect = new Rect(xL, yplus, wL, group.reorderableList.GetHeight());
                    group.reorderableList.DoList(listRect);
                    yplus += group.reorderableList.GetHeight() + vs;

                    // Add/Clear ボタン
                    const float btnW = 120f;
                    const float gap = 10f;

                    if (GUI.Button(new Rect(xL, yplus, btnW, lh), "Add Object"))
                    {
                        GameObject[] selectedObjects = Selection.gameObjects;
                        foreach (var obj in selectedObjects)
                        {
                            bool isAlreadyAdded = _toggleGroups.Any(g => g.gameObject.Contains(obj));
                            if (!isAlreadyAdded && !group.gameObject.Contains(obj))
                            {
                                group.gameObject.Add(obj);
                                group.save.Add(true);
                                group.sync.Add(true);
                                group.propIcon.Add(null);
                                group.parameterName.Add("");
                                group.customName.Add("");
                            }
                        }
                    }

                    if (GUI.Button(new Rect(xL + btnW + gap, yplus, btnW, lh), "Clear All Objects"))
                    {
                        group.gameObject.Clear();
                        group.save.Clear();
                        group.sync.Clear();
                        group.propIcon.Clear();
                        group.parameterName.Clear();
                        group.customName.Clear();
                        group.isSettingFoldout.Clear();
                    }
                    yplus += lh + vs;

                    // Drag & Drop Area
                    Rect dropArea = new Rect(xL, yplus, wL, UIStyles.DropAreaHeight);
                    GUI.Box(dropArea, "Drag & Drop GameObjects Here");

                    Event evt = Event.current;
                    switch (evt.type)
                    {
                        case EventType.DragUpdated:
                        case EventType.DragPerform:
                            if (!dropArea.Contains(evt.mousePosition))
                                break;

                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                            if (evt.type == EventType.DragPerform)
                            {
                                DragAndDrop.AcceptDrag();

                                foreach (var draggedObject in DragAndDrop.objectReferences)
                                {
                                    if (draggedObject is GameObject draggedGO)
                                    {
                                        bool isAlreadyAdded = _toggleGroups.Any(g => g.gameObject.Contains(draggedGO));
                                        if (!isAlreadyAdded && !group.gameObject.Contains(draggedGO))
                                        {
                                            group.gameObject.Add(draggedGO);
                                            group.save.Add(true);
                                            group.sync.Add(true);
                                            group.propIcon.Add(null);
                                            group.parameterName.Add("");
                                            group.customName.Add("");
                                        }
                                        else
                                        {
                                            Debug.LogWarning($"Object {draggedGO.name} is already added to another group.");
                                        }
                                    }
                                }
                            }
                            Event.current.Use();
                            break;
                    }

                    EditorGUI.indentLevel--;
                }
            };

            _groupReorderableList.elementHeightCallback = (int index) =>
            {
                if (index >= _toggleGroups.Count) return UIStyles.LineHeight;
                var group = _toggleGroups[index];
                float height = UIStyles.LineHeight + UIStyles.VerticalSpacing;

                if (group.isFoldout)
                {
                    // Group Name, Group Icon, Group Menu, Merge Menu, Exclusive Mode
                    height += UIStyles.GetLines(5);

                    if (group.exclusiveMode)
                    {
                        // BlendTree Mode, Allow Disable All, Parameter Type
                        height += UIStyles.GetLines(3);
                        if (group.parameterType == AnimatorControllerParameterType.Int)
                        {
                            height += UIStyles.GetLines(1); // Int 用 Parameter Name
                        }
                    }

                    // ReorderableList の高さ
                    if (group.reorderableList != null)
                        height += group.reorderableList.GetHeight() + UIStyles.VerticalSpacing;

                    // Add/Clear ボタン
                    height += UIStyles.ButtonRowHeight;

                    // Drag & Drop エリア
                    height += UIStyles.DropAreaHeight + UIStyles.VerticalSpacing;
                }

                return height;
            };

            _groupReorderableList.onAddCallback = (list) =>
            {
                var newGroup = new ToggleGroup();
                SetupReorderableList(newGroup);
                _toggleGroups.Add(newGroup);
            };

            _groupReorderableList.onRemoveCallback = (list) =>
            {
                if (list.index >= 0 && list.index < _toggleGroups.Count)
                    _toggleGroups.RemoveAt(list.index);
            };
        }

        private void SetupReorderableList(ToggleGroup group)
        {
            group.reorderableList = new UnityEditorInternal.ReorderableList(group.gameObject, typeof(GameObject), true, true, true, true);

            group.reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Objects (Drag to Reorder)");
            };

            group.reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= group.gameObject.Count) return;

                float lineHeight = EditorGUIUtility.singleLineHeight + 2;

                // デフォルトチェックボックス + ObjectField
                float toggleWidth = 20f;
                Rect toggleRect = new Rect(rect.x, rect.y, toggleWidth, EditorGUIUtility.singleLineHeight);
                Rect objRect = new Rect(rect.x + toggleWidth, rect.y, rect.width - toggleWidth, EditorGUIUtility.singleLineHeight);

                bool currentState = group.gameObject[index] != null && group.gameObject[index].activeSelf;
                bool newState = GUI.Toggle(toggleRect, currentState, GUIContent.none);

                if (newState != currentState)
                {
                    if (group.exclusiveMode)
                    {
                        if (newState)
                        {
                            // 排他モード → 1つだけ有効にする
                            for (int k = 0; k < group.gameObject.Count; k++)
                            {
                                if (group.gameObject[k] != null)
                                    group.gameObject[k].SetActive(k == index);
                            }
                        }
                        else if (group.allowDisableAll)
                        {
                            // 排他モード + allowDisableAll → 全無効化を許可
                            if (group.gameObject[index] != null)
                                group.gameObject[index].SetActive(false);
                        }
                        else
                        {
                            // 排他モード + allowDisableAll無効 → 少なくとも1つ有効に保つ
                            if (group.gameObject[index] != null)
                                group.gameObject[index].SetActive(true);
                        }
                    }
                    else
                    {
                        // 非排他モード → トグル状態をそのまま反映（複数有効化OK）
                        if (group.gameObject[index] != null)
                            group.gameObject[index].SetActive(newState);
                    }
                }

                group.gameObject[index] = (GameObject)EditorGUI.ObjectField(objRect, group.gameObject[index], typeof(GameObject), true);

                // Settings Foldout
                while (group.isSettingFoldout.Count <= index)
                    group.isSettingFoldout.Add(false);

                string settingsLabel = !string.IsNullOrEmpty(group.customName[index])
                    ? group.customName[index]
                    : (group.gameObject[index] != null ? group.gameObject[index].name : "Settings");

                Rect foldoutRect = new Rect(rect.x, rect.y + lineHeight, rect.width, EditorGUIUtility.singleLineHeight);
                group.isSettingFoldout[index] = EditorGUI.Foldout(foldoutRect, group.isSettingFoldout[index], $"Settings ({settingsLabel})");

                if (group.isSettingFoldout[index])
                {
                    EditorGUI.indentLevel++;
                    float y = rect.y + lineHeight * 2;

                    // Save
                    group.save[index] = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Save", index < group.save.Count ? group.save[index] : true);
                    y += lineHeight;

                    // Sync
                    group.sync[index] = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Sync", index < group.sync.Count ? group.sync[index] : true);
                    y += lineHeight;

                    // Prop Icon
                    group.propIcon[index] = (Texture2D)EditorGUI.ObjectField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Prop Icon", group.propIcon[index], typeof(Texture2D), false);
                    y += lineHeight;

                    // Custom Name
                    string currentCustomName = group.customName[index];
                    string defaultName = group.gameObject[index] != null ? group.gameObject[index].name : "";
                    group.customName[index] = EditorGUI.TextField(
                        new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight),
                        "InMenu Name",
                        string.IsNullOrEmpty(currentCustomName) ? defaultName : currentCustomName
                    );
                    y += lineHeight;

                    // Parameter Name（Int 選択時は非表示）
                    if (group.parameterType != AnimatorControllerParameterType.Int)
                    {
                        group.parameterName[index] = EditorGUI.TextField(
                            new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight),
                            "Parameter Name", group.parameterName[index]);
                        y += lineHeight;
                    }

                    EditorGUI.indentLevel--;
                }
            };

            // オブジェクト並び替え時に元の設定を保持する
            group.reorderableList.onSelectCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                group.lastGameObjectOrder = new List<GameObject>(group.gameObject);
            };

            group.reorderableList.onReorderCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                if (group.lastGameObjectOrder == null || group.lastGameObjectOrder.Count == 0)
                {
                    return;
                }

                try
                {
                    var newGameObjects = group.gameObject.ToList();

                    List<T> ReorderList<T>(List<T> source, Func<GameObject, int> indexOf)
                    {
                        var result = new List<T>(newGameObjects.Count);
                        for (int i = 0; i < newGameObjects.Count; i++)
                        {
                            var g = newGameObjects[i];
                            int oldIndex = group.lastGameObjectOrder.IndexOf(g);
                            if (oldIndex >= 0 && oldIndex < source.Count)
                                result.Add(source[oldIndex]);
                            else
                                result.Add(default);
                        }
                        return result;
                    }

                    group.save = ReorderList(group.save, go => group.lastGameObjectOrder.IndexOf(go));
                    group.sync = ReorderList(group.sync, go => group.lastGameObjectOrder.IndexOf(go));
                    group.propIcon = ReorderList(group.propIcon, go => group.lastGameObjectOrder.IndexOf(go));
                    group.customName = ReorderList(group.customName, go => group.lastGameObjectOrder.IndexOf(go));
                    group.parameterName = ReorderList(group.parameterName, go => group.lastGameObjectOrder.IndexOf(go));
                    group.isSettingFoldout = ReorderList(group.isSettingFoldout, go => group.lastGameObjectOrder.IndexOf(go));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Failed to reorder parallel lists: " + ex.Message);
                }
                finally
                {
                    group.lastGameObjectOrder = null;
                }
            };

            group.reorderableList.elementHeightCallback = (int index) =>
            {
                float lineHeight = EditorGUIUtility.singleLineHeight + 2;
                float height = lineHeight * 2; // ObjectField + Foldout

                if (index < group.isSettingFoldout.Count && group.isSettingFoldout[index])
                {
                    // Save, Sync, Prop Icon, Custom Name は常に表示
                    height += (lineHeight * 4);

                    // Parameter Name は Int 以外のときだけ表示
                    if (group.parameterType != AnimatorControllerParameterType.Int)
                    {
                        height += lineHeight;
                    }
                }
                return height;
            };

            // Add ボタン
            group.reorderableList.onAddCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                group.gameObject.Add(null);
                group.save.Add(true);
                group.sync.Add(true);
                group.propIcon.Add(null);
                group.parameterName.Add("");
                group.customName.Add("");
                group.isSettingFoldout.Add(false);
            };

            // Remove ボタン
            group.reorderableList.onRemoveCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                int index = list.index;
                if (index >= 0 && index < group.gameObject.Count)
                {
                    group.gameObject.RemoveAt(index);
                    group.save.RemoveAt(index);
                    group.sync.RemoveAt(index);
                    group.propIcon.RemoveAt(index);
                    group.parameterName.RemoveAt(index);
                    group.customName.RemoveAt(index);
                    group.isSettingFoldout.RemoveAt(index);
                }
            };
        }

        private static class UIStyles
        {
            public const float LineHeight = 20f;
            public const float VerticalSpacing = 4f;
            public const float DropAreaHeight = 60f;
            public const float ButtonRowHeight = LineHeight + VerticalSpacing;

            public static float GetLines(int count)
            {
                return (LineHeight + VerticalSpacing) * count;
            }
        }

        // ====実行ボタン====
        private void GenerateToggle()
        {
            // 続行する前にグループを検証
            if (!VerifyToggleGroups())
            {
                Debug.LogError("Verification failed");
                return;
            }

            // レイヤー名の重複チェック
            bool CheckDuplicateLayerNames()
            {
                List<string> toggleGroupLayerNames = new List<string>();
                foreach (var group in _toggleGroups)
                {
                    toggleGroupLayerNames.Add(_prefix + group.groupName);
                    toggleGroupLayerNames.Add(_prefix + group.groupName + _parameterDriver);
                }

                foreach (var layer in _animatorController.layers)
                {
                    if (toggleGroupLayerNames.Contains(layer.name))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (!_disableCfmDialog && CheckDuplicateLayerNames())
            {
                string message =
                    "Duplicate layer names detected in AnimatorController.\n" +
                    "Please overwrite or cancel and change the layer name.";
                bool overwrite = EditorUtility.DisplayDialog(
                    "Confirm",
                    message,
                    "Continue",
                    "Cancel"
                );

                if (!overwrite)
                {
                    Debug.Log("Processing has been canceled.");
                    return;
                }
            }

            // このスクリプトで生成したBlendTreeが含まれるレイヤーがあるかチェック
            string brendTreeLayerName = _prefix + _blendTreeBaseName + " (WD On)";
            bool HasBlendTreeLayer()
            {
                if (_animatorController != null && _animatorController.layers.Any(l => l.name == brendTreeLayerName))
                {
                    return true;
                }
                return false;
            }
            if (HasBlendTreeLayer())
            {
                string message =
                    $"Layer \"{brendTreeLayerName}\" and its BlendTree already exist in AnimatorController.\n" +
                    "The layer and BlendTree will be removed and regenerated if necessary.";
                bool overwrite = EditorUtility.DisplayDialog(
                    "BlendTree Layer Detected",
                    message,
                    "Yes (Remove and Continue)",
                    "Cancel"
                );

                if (overwrite)
                {
                    CleanupBlendTreeLayers(brendTreeLayerName);
                }
                else
                {
                    Debug.Log("Processing has been canceled by user.");
                    return;
                }
            }

            // 既存レイヤーを削除（_prefix から始まるもののみ）
            var removedLayerNames = _animatorController.layers
                .Where(layer =>
                    layer.name.StartsWith(_prefix) && !layer.name.EndsWith(" (WD On)"))
                .Select(layer => layer.name)
                .ToArray();

            if (removedLayerNames.Length > 0)
            {
                AnimatorControllerLayer[] updatedLayers = _animatorController.layers
                    .Where(layer => !removedLayerNames.Contains(layer.name))
                    .ToArray();

                _animatorController.layers = updatedLayers;

                Debug.Log($"[AnimatorController] The following layers were removed/overwritten:\n" +
                        $"{string.Join("\n", removedLayerNames)}\n" +
                        $"Remaining layers: {_animatorController.layers.Length}");
            }

            foreach (var toggleGroup in _toggleGroups)
            {
                GenerateLayerAndClip(toggleGroup, brendTreeLayerName);
            }

            if (_vrcExpressionsMenu == null)
            {
                Debug.LogError("ExpressionsMenu are not set on the AvatarDescriptor. Cannot add expressions menu.");
                return;
            }
            else
            {
                CreateExpressionMenus();
            }

            if (_vrcExpressionParameters == null)
            {
                Debug.LogError("ExpressionParameters are not set on the AvatarDescriptor. Cannot add expression parameters.");
                return;
            }
            else
            {
                AddExpressionParameters();
            }

            // Save Assets
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Animation clips generated and added to Animator Controller: " + _animatorController.name);
        }

        // ====検証====
        private bool VerifyToggleGroups()
        {
            if (_toggleGroups.Count == 0)
            {
                Debug.LogError("No groups defined. Please add at least one group.");
                return false;
            }
            if (_avatar == null)
            {
                Debug.LogError("No Avatar selected.");
                return false;
            }

            foreach (var toggleGroup in _toggleGroups)
            {
                if (toggleGroup.gameObject.Count < 2)
                {
                    Debug.LogError("At least 2 game objects are required to generate animation clips in script group '" + toggleGroup.groupName + "'.");
                    return false;
                }
                if (string.IsNullOrEmpty(toggleGroup.groupName))
                {
                    Debug.LogError("Layer name is required for generating animation clips.");
                    return false;
                }

                if (_enforceParameterType && toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                {
                    Debug.LogWarning("Note: Enforcing Float parameter type may increase memory usage.");
                    string message =
                        "Enforcing Float parameter types may increase memory usage.\n\n" +
                        "See documentation for details:\nhttps://vrc.school/docs/Other/Parameter-Mismatching/";
                    bool enforceParamType = EditorUtility.DisplayDialog(
                        "Memory Usage Warning",
                        message,
                        "OK",
                        "Cancel"
                    );
                    if (!enforceParamType)
                    {
                        return false;
                    }
                }

                // パラメーター型不一致チェック（排他/非排他共通）
                List<(string paramName, AnimatorControllerParameterType selected, AnimatorControllerParameterType actual)> invalidParams = new();
                foreach (var paramName in toggleGroup.parameterName)
                {
                    if (string.IsNullOrEmpty(paramName)) continue;

                    // 非排他モードでは強制的に Float を使用
                    var expectedType = toggleGroup.exclusiveMode
                        ? toggleGroup.parameterType
                        : AnimatorControllerParameterType.Float;

                    var existingParam = _animatorController.parameters.FirstOrDefault(p => p.name == paramName);
                    if (existingParam != null && existingParam.type != expectedType)
                    {
                        invalidParams.Add((paramName, expectedType, existingParam.type));
                    }
                }

                if (invalidParams.Count > 0 && toggleGroup.parameterType != AnimatorControllerParameterType.Int)
                {
                    string paramList = string.Join("\n", invalidParams.Select(p => $"- {p.paramName}: selected {p.selected}, found {p.actual}"));
                    string message =
                        "The following parameters do not match the selected type:\n\n" +
                        $"Parameter List:\n{paramList}\n\n" +
                        "Do you want to update the parameters and transition conditions and continue?";
                    bool cont = EditorUtility.DisplayDialog(
                        "Parameter Type Warning",
                        message,
                        "Continue",
                        "Cancel"
                    );

                    if (!cont)
                    {
                        Debug.Log("Process canceled by user due to parameter type mismatch.");
                        return false;
                    }
                }
            }
            return true;
        }

        // ====コア====
        private void GenerateLayerAndClip(ToggleGroup toggleGroup, string layerNameBlendTree)
        {
            string parameterNameBlendTree = _blendTreeBaseName + "_Blend";

            // 既存のパラメーター名を保存
            HashSet<string> existingParameterNames = new();
            foreach (var parameter in _animatorController.parameters)
            {
                existingParameterNames.Add(parameter.name);
            }

            if (toggleGroup.exclusiveMode)
            {
                // 排他モード
                string intParameterName = toggleGroup.intParameterName;
                string layerName = _prefix + toggleGroup.groupName;
                if (toggleGroup.blendTreeMode)
                {
                    layerName = _prefix + toggleGroup.groupName + _parameterDriver;
                    CreateGroupBlendTree(toggleGroup, parameterNameBlendTree, layerNameBlendTree);
                }
                GenerateExclusiveLayer(toggleGroup, layerName, intParameterName);
            }
            else
            {
                // 非排他モード
                CreateGroupBlendTree(toggleGroup, parameterNameBlendTree, layerNameBlendTree);
            }
        }

        private void CleanupBlendTreeLayers(string layerNameBlendTree)
        {
            if (_animatorController == null) return;

            // BlendTree のレイヤーを削除
            var layers = _animatorController.layers.ToList();
            int removed = layers.RemoveAll(l => l.name.StartsWith(_prefix) && l.name.EndsWith(" (WD On)"));
            if (removed > 0)
            {
                _animatorController.layers = layers.ToArray();
                Debug.Log($"Removed {removed} BlendTree layers from {_animatorController.name}");
            }

            // AnimatorController に含まれる BlendTree を全て探索して削除
            string controllerPath = AssetDatabase.GetAssetPath(_animatorController);
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(controllerPath);

            foreach (var sub in subAssets)
            {
                if (sub is BlendTree bt && bt.name.Contains(layerNameBlendTree))
                {
                    Debug.Log($"Deleting BlendTree asset: {bt.name}");
                    DestroyImmediate(bt, true);
                }
                else if (sub is AnimatorStateMachine sm && sm.name.Contains(layerNameBlendTree))
                {
                    Debug.Log($"Deleting StateMachine asset: {sm.name}");
                    DestroyImmediate(sm, true);
                }
            }
        }

        private BlendTree CreateGroupBlendTree(ToggleGroup toggleGroup, string parameterNameBlendTree, string layerNameBlendTree)
        {
            // 初回なら親レイヤーと親BlendTreeを作成
            if (_rootBlendTree == null)
            {
                _blendTreeLayer = new AnimatorControllerLayer
                {
                    name = layerNameBlendTree,
                    stateMachine = new AnimatorStateMachine
                    {
                        name = layerNameBlendTree,
                        hideFlags = HideFlags.HideInHierarchy
                    }
                };
                AssetDatabase.AddObjectToAsset(_blendTreeLayer.stateMachine, _animatorController);

                _rootBlendTree = new BlendTree
                {
                    name = layerNameBlendTree,
                    blendType = BlendTreeType.Direct,
                    useAutomaticThresholds = false
                };
                AssetDatabase.AddObjectToAsset(_rootBlendTree, _animatorController);

                AnimatorState rootState = _blendTreeLayer.stateMachine.AddState(layerNameBlendTree);
                rootState.motion = _rootBlendTree;
                rootState.writeDefaultValues = true;
                _blendTreeLayer.stateMachine.defaultState = rootState;

                _animatorController.AddLayer(_blendTreeLayer);

                if (!_animatorController.parameters.Any(p => p.name == parameterNameBlendTree))
                {
                    var parameter = new AnimatorControllerParameter
                    {
                        name = parameterNameBlendTree,
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = 1f
                    };
                    _animatorController.AddParameter(parameter);
                }
            }

            // グループ用 Direct BlendTree
            BlendTree groupTree = _rootBlendTree.CreateBlendTreeChild(0f);
            groupTree.name = $"{toggleGroup.groupName}_GroupTree";
            groupTree.blendType = BlendTreeType.Direct;
            groupTree.useAutomaticThresholds = false;

            // グループを親に追加
            var rootChildren = _rootBlendTree.children.ToList();
            var lastChild = rootChildren[rootChildren.Count - 1];
            lastChild.directBlendParameter = parameterNameBlendTree;
            rootChildren[rootChildren.Count - 1] = lastChild;
            _rootBlendTree.children = rootChildren.ToArray();

            // 各オブジェクト用 1D BlendTree
            foreach (var obj in toggleGroup.gameObject)
            {
                string paramName = toggleGroup.parameterName[toggleGroup.gameObject.IndexOf(obj)];
                CreateOrUpdateParameter(paramName, AnimatorControllerParameterType.Float);

                // オブジェクト用 Direct BlendTree
                BlendTree objTree = groupTree.CreateBlendTreeChild(0f);
                objTree.name = toggleGroup.customName[toggleGroup.gameObject.IndexOf(obj)];
                objTree.blendType = BlendTreeType.Simple1D;
                objTree.useAutomaticThresholds = false;
                objTree.blendParameter = paramName;

                // 0=Off, 1=On のクリップ
                AnimationClip offClip = CreateSingleDisableClip(obj, toggleGroup);
                AnimationClip onClip = CreateSingleEnableClip(obj, toggleGroup);
                objTree.AddChild(offClip, 0f);
                objTree.AddChild(onClip, 1f);

                // オブジェクト用の 1D BlendTree をグループBlendTreeに追加
                var groupChildren = groupTree.children.ToList();
                var lastGroupChild = groupChildren[groupChildren.Count - 1];
                lastGroupChild.directBlendParameter = parameterNameBlendTree;
                groupChildren[groupChildren.Count - 1] = lastGroupChild;
                groupTree.children = groupChildren.ToArray();
            }
            return groupTree;
        }

        private void GenerateExclusiveLayer(ToggleGroup toggleGroup, string layerName, string intParameterName)
        {
            // レイヤー作成
            AnimatorControllerLayer newLayer = new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                }
            };
            AssetDatabase.AddObjectToAsset(newLayer.stateMachine, _animatorController);

            // AnimationClipを作成してStateの設定をする
            Dictionary<GameObject, AnimatorState> stateDictionary = new Dictionary<GameObject, AnimatorState>();
            foreach (var obj in toggleGroup.gameObject)
            {
                string stateName = toggleGroup.customName[toggleGroup.gameObject.IndexOf(obj)];
                AnimatorState state = newLayer.stateMachine.AddState(stateName);

                if (toggleGroup.blendTreeMode)
                {
                    // Float → _doNotEditClip を使い回す
                    state.motion = GetDoNotEditClip();
                }
                else
                {
                    // Bool / Int → オブジェクトを有効にする AnimationClip を作成
                    state.motion = CreateAnimationClip(obj, toggleGroup.gameObject, toggleGroup);
                }
                state.writeDefaultValues = false;
                stateDictionary.Add(obj, state);

                // 有効になっているオブジェクトを DefaultState に設定
                if (obj.activeSelf && !toggleGroup.allowDisableAll)
                {
                    newLayer.stateMachine.defaultState = state;
                }
            }

            // AllDisabled ステート
            AnimatorState allDisabledState = null;
            if (toggleGroup.allowDisableAll)
            {
                allDisabledState = newLayer.stateMachine.AddState("AllDisabled");
                if (toggleGroup.blendTreeMode)
                {
                    allDisabledState.motion = GetDoNotEditClip();
                }
                else
                {
                    allDisabledState.motion = CreateDisableAllAnimationClip(toggleGroup.gameObject, toggleGroup);
                }
                allDisabledState.writeDefaultValues = false;
            }

            if (toggleGroup.allowDisableAll && allDisabledState != null)
            {
                bool allInactive = true;
                foreach (var obj in toggleGroup.gameObject)
                {
                    if (obj != null && obj.activeInHierarchy)
                    {
                        allInactive = false;
                        break;
                    }
                }

                if (allInactive)
                {
                    // 全て非アクティブなら AllDisabled をデフォルトステートにする
                    newLayer.stateMachine.defaultState = allDisabledState;
                }
            }

            // VRCAvatarParameterDriverをすべてのStateに追加
            if (toggleGroup.parameterType != AnimatorControllerParameterType.Int)
            {
                foreach (var state in newLayer.stateMachine.states.Select(s => s.state))
                {
                    VRCAvatarParameterDriver driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    driver.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();

                    // VRCAvatarParameterDriverのパラメータを設定
                    for (int i = 0; i < toggleGroup.parameterName.Count; i++)
                    {
                        string param = toggleGroup.parameterName[i];
                        string customName = toggleGroup.customName[i];

                        VRC.SDKBase.VRC_AvatarParameterDriver.Parameter driverParam = new()
                        {
                            name = param
                        };

                        bool isDefaultState = state == newLayer.stateMachine.defaultState;
                        bool isStateForThisParam = state.name == customName;

                        if (!isDefaultState)
                        {
                            // DefaultStateでない場合はこのStateのパラメータだけ除外
                            if (!isStateForThisParam)
                            {
                                driverParam.value = 0;
                                driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                                driver.parameters.Add(driverParam);
                            }
                        }
                        else
                        {
                            // DefaultStateでは全てのパラメータを設定
                            driverParam.value = isStateForThisParam ? 1 : 0;
                            driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                            driver.parameters.Add(driverParam);
                        }
                    }
                }
            }

            // State間の遷移を設定
            foreach (var obj1 in toggleGroup.gameObject)
            {
                foreach (var obj2 in toggleGroup.gameObject)
                {
                    if (obj1 != obj2)
                    {
                        // transitionを作成
                        AnimatorStateTransition transition = stateDictionary[obj1].AddTransition(stateDictionary[obj2]);
                        transition.hasExitTime = false;
                        transition.exitTime = 0f;
                        transition.duration = 0f;
                        transition.interruptionSource = TransitionInterruptionSource.None;

                        // parameterNamesが既存のパラメーター名に無い場合パラメーターを追加
                        string paramName;

                        if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                        {
                            // Int モードでは intParameterName を使う
                            paramName = toggleGroup.intParameterName;
                            CreateOrUpdateParameter(paramName, AnimatorControllerParameterType.Int);
                        }
                        else
                        {
                            paramName = toggleGroup.parameterName[toggleGroup.gameObject.IndexOf(obj2)];
                            CreateOrUpdateParameter(paramName, toggleGroup.parameterType);
                        }

                        // 遷移条件を設定
                        if (toggleGroup.parameterType == AnimatorControllerParameterType.Bool)
                        {
                            transition.AddCondition(AnimatorConditionMode.If, 0, paramName);
                        }
                        else if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                        {
                            transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, paramName);
                        }
                        else if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                        {
                            int value = toggleGroup.gameObject.IndexOf(obj2) + 1;
                            transition.AddCondition(AnimatorConditionMode.Equals, value, intParameterName);
                        }
                    }
                }
            }

            // 戻り遷移の設定
            if (toggleGroup.allowDisableAll && allDisabledState != null)
            {
                // AllowDisableAll が有効なら全ステートから AllDisabledState へ
                foreach (var state in newLayer.stateMachine.states)
                {
                    if (state.state == null || state.state == allDisabledState)
                        continue;

                    AnimatorStateTransition toAllDisabled = state.state.AddTransition(allDisabledState);
                    toAllDisabled.hasExitTime = false;
                    toAllDisabled.duration = 0f;
                    toAllDisabled.exitTime = 0f;
                    toAllDisabled.interruptionSource = TransitionInterruptionSource.None;

                    if (toggleGroup.parameterType != AnimatorControllerParameterType.Int)
                    {
                        foreach (var param in toggleGroup.parameterName)
                        {
                            if (toggleGroup.parameterType == AnimatorControllerParameterType.Bool)
                            {
                                toAllDisabled.AddCondition(AnimatorConditionMode.IfNot, 0, param);
                            }
                            else if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                            {
                                toAllDisabled.AddCondition(AnimatorConditionMode.Less, 0.5f, param);
                            }
                        }
                    }
                    else if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                    {
                        toAllDisabled.AddCondition(AnimatorConditionMode.Equals, 0f, intParameterName);
                    }
                }

                // AllDisabledから各Stateへの遷移を追加
                if (allDisabledState != null)
                {
                    foreach (var kvp in stateDictionary)
                    {
                        var obj = kvp.Key;
                        var state = kvp.Value;

                        AnimatorStateTransition transition = allDisabledState.AddTransition(state);
                        transition.hasExitTime = false;
                        transition.exitTime = 0f;
                        transition.duration = 0f;
                        transition.interruptionSource = TransitionInterruptionSource.None;

                        if (toggleGroup.parameterType == AnimatorControllerParameterType.Bool)
                        {
                            string paramName = toggleGroup.parameterName[toggleGroup.gameObject.IndexOf(obj)];
                            transition.AddCondition(AnimatorConditionMode.If, 1, paramName);
                        }
                        else if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                        {
                            string paramName = toggleGroup.parameterName[toggleGroup.gameObject.IndexOf(obj)];
                            transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, paramName);
                        }
                        else if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                        {
                            int value = toggleGroup.gameObject.IndexOf(obj) + 1; // ★ 1始まりで正しい値に
                            transition.AddCondition(AnimatorConditionMode.Equals, value, toggleGroup.intParameterName);
                        }
                    }
                }
            }
            else
            {
                // AllowDisableAll が無効な場合は DefaultState に戻す
                foreach (var nonDefaultState in newLayer.stateMachine.states)
                {
                    if (nonDefaultState.state != null && nonDefaultState.state != newLayer.stateMachine.defaultState)
                    {
                        AnimatorStateTransition defaultTransition = nonDefaultState.state.AddTransition(newLayer.stateMachine.defaultState);
                        defaultTransition.hasExitTime = false;
                        defaultTransition.duration = 0f;
                        defaultTransition.exitTime = 0f;
                        defaultTransition.interruptionSource = TransitionInterruptionSource.None;

                        foreach (var param in toggleGroup.parameterName)
                        {
                            if (toggleGroup.parameterType == AnimatorControllerParameterType.Bool)
                            {
                                defaultTransition.AddCondition(AnimatorConditionMode.IfNot, 0, param);
                            }
                            else if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                            {
                                defaultTransition.AddCondition(AnimatorConditionMode.Less, 0.5f, param);
                            }
                        }

                        if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                        {
                            defaultTransition.AddCondition(AnimatorConditionMode.Equals, 0f, intParameterName);
                        }
                    }
                }
            }

            // AnimatorController にレイヤー追加
            _animatorController.AddLayer(newLayer);
        }

        // ====パラメーター作成====
        private void CreateOrUpdateParameter(string paramName, AnimatorControllerParameterType type)
        {
            var existingParam = _animatorController.parameters.FirstOrDefault(p => p.name == paramName);
            if (existingParam == null)
            {
                var parameter = new AnimatorControllerParameter { name = paramName, type = type };
                _animatorController.AddParameter(parameter);
            }
            else if (existingParam.type != type)
            {
                // 既存の遷移条件を更新
                foreach (var layer in _animatorController.layers)
                {
                    UpdateTransitionConditions(layer.stateMachine, paramName, type);
                }
                _animatorController.RemoveParameter(existingParam);
                var parameter = new AnimatorControllerParameter { name = paramName, type = type };
                _animatorController.AddParameter(parameter);
            }
        }

        // ====遷移条件更新====
        private void UpdateTransitionConditions(AnimatorStateMachine stateMachine, string paramName, AnimatorControllerParameterType newType)
        {
            // 各Stateの遷移をチェック
            foreach (var state in stateMachine.states)
            {
                var transitions = state.state.transitions;
                for (int i = 0; i < transitions.Length; i++)
                {
                    var conds = transitions[i].conditions;
                    for (int j = 0; j < conds.Length; j++)
                    {
                        if (conds[j].parameter == paramName)
                        {
                            conds[j] = ConvertCondition(conds[j], newType);
                        }
                    }
                    transitions[i].conditions = conds;
                }
            }

            // AnyState遷移
            foreach (var transition in stateMachine.anyStateTransitions)
            {
                var conds = transition.conditions;
                for (int j = 0; j < conds.Length; j++)
                {
                    if (conds[j].parameter == paramName)
                    {
                        conds[j] = ConvertCondition(conds[j], newType);
                    }
                }
                transition.conditions = conds;
            }

            // Entry遷移
            foreach (var transition in stateMachine.entryTransitions)
            {
                var conds = transition.conditions;
                for (int j = 0; j < conds.Length; j++)
                {
                    if (conds[j].parameter == paramName)
                    {
                        conds[j] = ConvertCondition(conds[j], newType);
                    }
                }
                transition.conditions = conds;
            }

            // サブステートマシンも再帰的に処理
            foreach (var sub in stateMachine.stateMachines)
            {
                UpdateTransitionConditions(sub.stateMachine, paramName, newType);
            }
        }

        private AnimatorCondition ConvertCondition(AnimatorCondition condition, AnimatorControllerParameterType newType)
        {
            if (newType == AnimatorControllerParameterType.Float)
            {
                // Bool → Float
                if (condition.mode == AnimatorConditionMode.If)
                {
                    condition.mode = AnimatorConditionMode.Greater;
                    condition.threshold = 0.5f;
                }
                else if (condition.mode == AnimatorConditionMode.IfNot)
                {
                    condition.mode = AnimatorConditionMode.Less;
                    condition.threshold = 0.5f;
                }
            }
            else if (newType == AnimatorControllerParameterType.Bool)
            {
                // Float → Bool
                if (condition.mode == AnimatorConditionMode.Greater && condition.threshold >= 0.5f)
                {
                    condition.mode = AnimatorConditionMode.If;
                    condition.threshold = 0f;
                }
                else if (condition.mode == AnimatorConditionMode.Less && condition.threshold <= 0.5f)
                {
                    condition.mode = AnimatorConditionMode.IfNot;
                    condition.threshold = 0f;
                }
            }
            return condition;
        }

        // ====排他モードかつ Bool/Int でのAnimation Clip作成====
        private AnimationClip CreateAnimationClip(GameObject obj, List<GameObject> groupObjects, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Enabled";

            // オブジェクトを有効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 1f));

            // 同じグループの他オブジェクトを無効化
            foreach (var otherObj in groupObjects)
            {
                if (otherObj != obj)
                {
                    clip.SetCurve(GetGameObjectPath(otherObj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));
                }
            }

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.groupName);
        }

        private AnimationClip CreateDisableAllAnimationClip(List<GameObject> groupObjects, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = "AllDisabled";

            // 全オブジェクト無効化
            foreach (var obj in groupObjects)
            {
                clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));
            }

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.groupName);
        }

        // ===排他モードかつ Float でのダミークリップ作成===
        private AnimationClip GetDoNotEditClip()
        {
            if (_doNotEditClip != null)
                return _doNotEditClip;

            AnimationClip clip = new AnimationClip();
            clip.name = "Empty";

            string path;
            path = "_doNotEdit"; // 存在しない場合もカーブを作成

            clip.SetCurve(path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 1f));
            clip.wrapMode = WrapMode.Once;

            _doNotEditClip = SaveClip(clip, "Empty");
            return _doNotEditClip;
        }

        // ====非排他モードでのAnimation Clip作成====
        private AnimationClip CreateSingleEnableClip(GameObject obj, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Enable";

            // オブジェクトを有効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 1f));

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.groupName);
        }

        private AnimationClip CreateSingleDisableClip(GameObject obj, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Disable";

            // オブジェクトを無効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.groupName);
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = "";
            Transform transform = obj.transform;
            while (transform != null)
            {
                if (transform.parent != null)
                {
                    path = transform.name + (path == "" ? "" : "/") + path;
                }
                transform = transform.parent;
            }
            return path;
        }

        // ====Animation Clipを保存====
        private AnimationClip SaveClip(AnimationClip clip, string folderName)
        {
            string rootPath = _avatar != null ? _avatar.name : _animatorController.name;
            string directoryPath = $"{_savePath}/{rootPath}/{folderName}";
            if (!AssetDatabase.IsValidFolder($"{_savePath}/{rootPath}"))
            {
                AssetDatabase.CreateFolder(_savePath, rootPath);
            }
            if (!AssetDatabase.IsValidFolder(directoryPath))
            {
                AssetDatabase.CreateFolder($"{_savePath}/{rootPath}", folderName);
            }
            string clipPath = $"{directoryPath}/{clip.name}.anim";
            AssetDatabase.CreateAsset(clip, clipPath);
            return clip;
        }

        // ====Expression関連====
        private void CreateExpressionMenus()
        {
            string rootMenuPath = $"{_savePath}/{_avatar.name}";
            VRCExpressionsMenu rootExpressionsMenu = null;

            bool allGroupsHaveMenu = _toggleGroups.All(g => g.groupRootMenu != null);

            // 各グループのメニューを作成
            string subMenuPath = $"{_savePath}/{_avatar.name}/SubMenu";
            if (!AssetDatabase.IsValidFolder(subMenuPath))
            {
                AssetDatabase.CreateFolder($"{_savePath}/{_avatar.name}", "SubMenu");
            }

            VRCExpressionsMenu previousGroupMenu = null;
            VRCExpressionsMenu previousRootSubMenu = null;
            string previousProcessLayerName = null;
            int groupPage = 0;
            foreach (var toggleGroup in _toggleGroups)
            {
                rootExpressionsMenu = null;
                if (allGroupsHaveMenu)
                {
                    rootExpressionsMenu = toggleGroup.groupRootMenu;
                }
                else if (!allGroupsHaveMenu)
                {
                    if (!AssetDatabase.IsValidFolder(rootMenuPath))
                    {
                        AssetDatabase.CreateFolder(_savePath, _avatar.name);
                    }

                    if (toggleGroup.groupRootMenu)
                    {
                        rootExpressionsMenu = toggleGroup.groupRootMenu;
                    }
                    else if (_rootMenu != null)
                    {
                        // ユーザーが RootMenu を直接指定している場合はこちらを使う
                        rootExpressionsMenu = _rootMenu;
                    }
                    else
                    {
                        // RootMenuName を使用
                        VRCExpressionsMenu existingMenu =
                            AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>($"{rootMenuPath}/{_rootMenuName}.asset");
                        if (existingMenu != null)
                        {
                            rootExpressionsMenu = existingMenu;
                            Debug.LogWarning($"Menu with name '{_rootMenuName}' already exists. Using existing asset.");
                        }
                        else
                        {
                            rootExpressionsMenu = CreateInstance<VRCExpressionsMenu>();
                            rootExpressionsMenu.name = _rootMenuName;
                            AssetDatabase.CreateAsset(rootExpressionsMenu, $"{rootMenuPath}/{rootExpressionsMenu.name}.asset");
                        }
                    }
                }

                // オブジェクトの数を元にページ数を計算（Merge Menu の項目数を考慮）
                int numObjects = toggleGroup.gameObject.Count;
                int objectsPerPage = 7; // ページあたりの最大オブジェクト数
                int mergeCount = (toggleGroup.mergeMenu != null) ? toggleGroup.mergeMenu.controls.Count : 0;
                int totalControls = numObjects + mergeCount;
                int numObjectPages = Mathf.CeilToInt((float)totalControls / objectsPerPage);

                for (int objectPageNum = 0; objectPageNum < numObjectPages; objectPageNum++)
                {
                    // サブメニューを作成
                    VRCExpressionsMenu groupExpressionsMenu = CreateInstance<VRCExpressionsMenu>();
                    groupExpressionsMenu.name = $"{toggleGroup.groupName} ExpressionsMenu_Page{objectPageNum + 1}";
                    AssetDatabase.CreateAsset(groupExpressionsMenu, $"{subMenuPath}/{groupExpressionsMenu.name}.asset");

                    // 1ページ目に Merge Menu の内容を先頭にコピー
                    if (objectPageNum == 0 && toggleGroup.mergeMenu != null)
                    {
                        foreach (var mergeControl in toggleGroup.mergeMenu.controls)
                        {
                            var copiedParam = (mergeControl.parameter != null)
                                ? new VRCExpressionsMenu.Control.Parameter() { name = mergeControl.parameter.name }
                                : null;

                            VRCExpressionsMenu.Control newControl = new VRCExpressionsMenu.Control()
                            {
                                name = mergeControl.name,
                                type = mergeControl.type,
                                value = mergeControl.value,
                                parameter = copiedParam,
                                icon = mergeControl.icon,
                                subMenu = mergeControl.subMenu
                            };
                            groupExpressionsMenu.controls.Add(newControl);
                        }
                    }

                    // オブジェクト項目をページに割り当てる
                    // オブジェクトの開始/終了インデックスは Merge Menu の分を差し引いて計算する
                    int startIndex_o = Mathf.Max(0, objectPageNum * objectsPerPage - mergeCount);
                    int endIndex_o = Mathf.Min((objectPageNum + 1) * objectsPerPage - mergeCount, numObjects);

                    for (int index_o = startIndex_o; index_o < endIndex_o; index_o++)
                    {
                        string paramName = toggleGroup.parameterName[index_o];
                        string customName = toggleGroup.customName[index_o];
                        Texture2D propicon = toggleGroup.propIcon[index_o];

                        if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                        {
                            VRCExpressionsMenu.Control control = new()
                            {
                                name = customName,
                                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                                value = index_o + 1,
                                parameter = new VRCExpressionsMenu.Control.Parameter()
                                {
                                    name = toggleGroup.intParameterName
                                },
                                icon = toggleGroup.propIcon != null ? propicon : null
                            };
                            groupExpressionsMenu.controls.Add(control);
                        }
                        else
                        {
                            VRCExpressionsMenu.Control control = new()
                            {
                                name = customName,
                                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                                value = 1f,
                                parameter = new VRCExpressionsMenu.Control.Parameter()
                                {
                                    name = paramName
                                },
                                icon = toggleGroup.propIcon != null ? propicon : null
                            };
                            groupExpressionsMenu.controls.Add(control);
                        }
                    }

                    EditorUtility.SetDirty(groupExpressionsMenu);
                    AssetDatabase.SaveAssets();

                    if (previousGroupMenu != null && previousProcessLayerName == toggleGroup.groupName)
                    {
                        if (groupExpressionsMenu.controls.Count == 1)
                        {
                            foreach (var control in groupExpressionsMenu.controls)
                            {
                                previousGroupMenu.controls.Add(control);
                                AssetDatabase.DeleteAsset($"{subMenuPath}/{toggleGroup.groupName} ExpressionsMenu_Page{objectPageNum + 1}.asset");
                                Debug.Log($"Delete{subMenuPath}/{toggleGroup.groupName} ExpressionsMenu_Page{objectPageNum + 1}.asset");
                            }
                        }
                        else
                        if (!previousGroupMenu.controls.Any(control => control.name == "Next Page"))
                        {
                            VRCExpressionsMenu.Control subMenuControl = new()
                            {
                                name = "Next Page",
                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                subMenu = groupExpressionsMenu
                            };
                            previousGroupMenu.controls.Add(subMenuControl);
                        }
                        else
                        {
                            VRCExpressionsMenu.Control existingSubMenuControl = previousGroupMenu.controls
                                .FirstOrDefault(control => control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                                                            control.name == "Next Page");
                            existingSubMenuControl.subMenu = groupExpressionsMenu;
                            existingSubMenuControl.icon = toggleGroup.groupIcon != null ? toggleGroup.groupIcon : null;
                            Debug.LogWarning($"Next Page control already exists in {previousGroupMenu.name}. Skipping duplicate.");
                        }
                        EditorUtility.SetDirty(previousGroupMenu);
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        Texture2D groupIcon = toggleGroup.groupIcon;

                        // 既存のサブメニューコントロールを検索
                        VRCExpressionsMenu.Control existingSubMenuControl = null;
                        existingSubMenuControl = rootExpressionsMenu.controls
                            .FirstOrDefault(control =>
                                control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                                control.name == toggleGroup.groupName);

                        if (existingSubMenuControl == null)
                        {
                            if (rootExpressionsMenu.controls.Count < 7)
                            {
                                VRCExpressionsMenu.Control subMenuControl = new()
                                {
                                    name = toggleGroup.groupName,
                                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                    subMenu = groupExpressionsMenu,
                                    icon = toggleGroup.groupIcon != null ? groupIcon : null
                                };
                                rootExpressionsMenu.controls.Add(subMenuControl);
                            }
                            else
                            {
                                if (rootExpressionsMenu.controls.Count == 7 || previousRootSubMenu != null && previousRootSubMenu.controls.Count == 7)
                                {
                                    VRCExpressionsMenu rootSubMenu = CreateInstance<VRCExpressionsMenu>();
                                    rootSubMenu.name = $"{rootExpressionsMenu.name}_Page{groupPage + 1}";
                                    AssetDatabase.CreateAsset(rootSubMenu, $"{rootMenuPath}/{rootSubMenu.name}.asset");

                                    int currentIndex = _toggleGroups.IndexOf(toggleGroup);
                                    bool isLastGroup = currentIndex == (_toggleGroups.Count - 1);

                                    if (!isLastGroup)
                                    {
                                        if (rootExpressionsMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl_root = new()
                                            {
                                                name = "Next Page",
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = rootSubMenu
                                            };
                                            rootExpressionsMenu.controls.Add(subMenuControl_root);

                                            VRCExpressionsMenu.Control subMenuControl_rootSub = new()
                                            {
                                                name = toggleGroup.groupName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            rootSubMenu.controls.Add(subMenuControl_rootSub);

                                            groupPage++;
                                            Debug.Log("add page to rootexmenu");
                                        }
                                        else
                                        if (previousRootSubMenu != null && previousRootSubMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl_previous = new()
                                            {
                                                name = "Next Page",
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = rootSubMenu
                                            };
                                            previousRootSubMenu.controls.Add(subMenuControl_previous);

                                            VRCExpressionsMenu.Control subMenuControl_rootSub = new()
                                            {
                                                name = toggleGroup.groupName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            rootSubMenu.controls.Add(subMenuControl_rootSub);

                                            groupPage++;
                                            Debug.Log("add page to previousrootexmenu");
                                        }
                                    }
                                    else
                                    {
                                        if (_toggleGroups.Count == 8 && rootExpressionsMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl = new()
                                            {
                                                name = toggleGroup.groupName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            rootExpressionsMenu.controls.Add(subMenuControl);
                                            Debug.Log("add to root");
                                            AssetDatabase.DeleteAsset($"{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                            Debug.Log($"Delete{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                        }
                                        if (_toggleGroups.Count % 7 == 1 && _toggleGroups.Count > 14 && previousRootSubMenu != null && previousRootSubMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl = new()
                                            {
                                                name = toggleGroup.groupName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            previousRootSubMenu.controls.Add(subMenuControl);
                                            Debug.Log("add to sub root");
                                            AssetDatabase.DeleteAsset($"{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                            Debug.Log($"Delete{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                        }
                                    }
                                    previousRootSubMenu = rootSubMenu;
                                }
                                else
                                {
                                    VRCExpressionsMenu.Control subMenuControl = new()
                                    {
                                        name = toggleGroup.groupName,
                                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                        subMenu = groupExpressionsMenu,
                                        icon = toggleGroup.groupIcon != null ? groupIcon : null
                                    };
                                    previousRootSubMenu.controls.Add(subMenuControl);
                                }
                            }
                            EditorUtility.SetDirty(rootExpressionsMenu);
                            AssetDatabase.SaveAssets();
                        }
                        else
                        {
                            // すでに存在する場合、そのメニューを使用
                            existingSubMenuControl.subMenu = groupExpressionsMenu;
                            existingSubMenuControl.icon = toggleGroup.groupIcon != null ? toggleGroup.groupIcon : null;
                            Debug.Log("既存のメニューを使用: " + toggleGroup.groupName);
                            Debug.Log($"{existingSubMenuControl.subMenu}");
                        }
                    }
                    // 前のグループメニューを更新する
                    previousGroupMenu = groupExpressionsMenu;
                    previousProcessLayerName = toggleGroup.groupName;
                }
            }
            EditorUtility.SetDirty(rootExpressionsMenu);
            AssetDatabase.SaveAssets();

            // RootMenu が指定されている場合は _vrcExpressionsMenu への追加をスキップする
            if (_rootMenu != null)
            {
                return;
            }

            bool rootMenuAdded = false;
            VRCExpressionsMenu.Control existingControl = null;
            foreach (var control in _vrcExpressionsMenu.controls)
            {
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.name == _rootMenuName)
                {
                    rootMenuAdded = true;
                    existingControl = control;
                    break;
                }
            }

            if (!rootMenuAdded)
            {
                VRCExpressionsMenu.Control rootSubMenuControl = new()
                {
                    name = _rootMenuName,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = rootExpressionsMenu
                };
                _vrcExpressionsMenu.controls.Insert(0, rootSubMenuControl);
            }
            // RootExpressionsMenuがすでに追加されている場合は、既存のコントロールを更新
            else
            {
                existingControl.subMenu = rootExpressionsMenu;
            }

            EditorUtility.SetDirty(_vrcExpressionsMenu);
            AssetDatabase.SaveAssets();
        }

        private void AddExpressionParameters()
        {
            // 既存のパラメーター名を保存
            List<string> existingParameters = _vrcExpressionParameters.parameters.Select(parameter => parameter.name).ToList();

            foreach (var toggleGroup in _toggleGroups)
            {
                // Int モードの場合は専用処理
                if (toggleGroup.exclusiveMode && toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                {
                    string parameterName = toggleGroup.intParameterName;

                    // 有効なオブジェクトに合わせて初期値を決定
                    int defaultValue = 0;
                    for (int i = 0; i < toggleGroup.gameObject.Count; i++)
                    {
                        if (toggleGroup.gameObject[i] != null && toggleGroup.gameObject[i].activeSelf)
                        {
                            defaultValue = i + 1; // 1始まり
                            break;
                        }
                    }

                    bool saved = true;
                    bool synced = true;
                    var paramType = VRCExpressionParameters.ValueType.Int;

                    if (!existingParameters.Contains(parameterName))
                    {
                        var length = _vrcExpressionParameters.parameters.Length;
                        Array.Resize(ref _vrcExpressionParameters.parameters, length + 1);

                        _vrcExpressionParameters.parameters[length] = new VRCExpressionParameters.Parameter()
                        {
                            name = parameterName,
                            valueType = paramType,
                            saved = saved,
                            networkSynced = synced,
                            defaultValue = defaultValue
                        };
                    }
                    else
                    {
                        // 既存のパラメータを更新
                        var parameterIndex = Array.FindIndex(_vrcExpressionParameters.parameters, p => p.name == parameterName);
                        _vrcExpressionParameters.parameters[parameterIndex].defaultValue = defaultValue;
                        _vrcExpressionParameters.parameters[parameterIndex].saved = saved;
                        _vrcExpressionParameters.parameters[parameterIndex].networkSynced = synced;
                        _vrcExpressionParameters.parameters[parameterIndex].valueType = paramType;
                    }
                }
                else
                {
                    for (int i = 0; i < toggleGroup.gameObject.Count; i++)
                    {
                        // 変数から設定値を取得
                        string parameterName = toggleGroup.parameterName[i];
                        bool defaultValue = toggleGroup.gameObject.Any(obj => obj.activeSelf && toggleGroup.parameterName[toggleGroup.gameObject.IndexOf(obj)] == parameterName);
                        bool saved = i < toggleGroup.save.Count ? toggleGroup.save[i] : true;
                        bool synced = i < toggleGroup.sync.Count ? toggleGroup.sync[i] : true;
                        var paramType = VRCExpressionParameters.ValueType.Bool;

                        // enforceParameterType が true のときのみ ToggleGroup の設定を尊重
                        if (_enforceParameterType)
                        {
                            if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                                paramType = VRCExpressionParameters.ValueType.Float;
                            else
                                paramType = VRCExpressionParameters.ValueType.Bool;
                        }

                        // parameterNamesが既存のexpression parametersに無い場合パラメーターを追加
                        if (!existingParameters.Contains(parameterName))
                        {
                            var length = _vrcExpressionParameters.parameters.Length;
                            Array.Resize(ref _vrcExpressionParameters.parameters, length + 1);

                            _vrcExpressionParameters.parameters[length] = new VRCExpressionParameters.Parameter()
                            {
                                name = parameterName,
                                valueType = paramType,
                                saved = saved,
                                networkSynced = synced,
                                defaultValue = defaultValue ? 1 : 0
                            };
                        }
                        else
                        {
                            // 既存のパラメータを更新
                            var parameterIndex = Array.FindIndex(_vrcExpressionParameters.parameters, p => p.name == parameterName);
                            _vrcExpressionParameters.parameters[parameterIndex].defaultValue = defaultValue ? 1 : 0;
                            _vrcExpressionParameters.parameters[parameterIndex].saved = saved;
                            _vrcExpressionParameters.parameters[parameterIndex].networkSynced = synced;
                            _vrcExpressionParameters.parameters[parameterIndex].valueType = paramType;
                        }
                    }
                }
            }

            // Avatar descriptorを更新
            EditorUtility.SetDirty(_avatar.expressionParameters);
        }

        // ====Save and Load====
        private void SaveToggleGroups()
        {
            string defaultPath = string.IsNullOrEmpty(_savePath) ? "Assets" : _savePath;
            string avatarName = _avatar != null ? _avatar.gameObject.name : "";
            string defaultFileName = string.IsNullOrEmpty(avatarName) ? "toggleGroups.json" : $"{avatarName}_toggleGroups.json";
            string path = EditorUtility.SaveFilePanel("Save Script Groups", defaultPath, defaultFileName, "json");
            if (string.IsNullOrEmpty(path))
                return;

            SaveData saveData = new SaveData
            {
                savePath = _savePath,
                rootMenuPath = _rootMenu != null ? AssetDatabase.GetAssetPath(_rootMenu) : string.Empty,
                rootMenuName = _rootMenuName,
                baseBlendName = _blendTreeBaseName,
                toggleGroups = _toggleGroups.Select(group => new SerializableToggleGroup(group)).ToList()
            };

            string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
            File.WriteAllText(path, json);

            AssetDatabase.Refresh();
        }

        private void LoadToggleGroups()
        {
            string defaultPath = string.IsNullOrEmpty(_savePath) ? "Assets" : _savePath;
            string path = EditorUtility.OpenFilePanel("Load Script Groups", defaultPath, "json");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                Debug.Log("Loaded JSON: " + json);

                SaveData saveData = JsonConvert.DeserializeObject<SaveData>(json);

                if (saveData == null)
                {
                    Debug.LogError("Failed to deserialize save data.");
                    return;
                }

                _savePath = saveData.savePath;
                _rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(saveData.rootMenuPath);
                _rootMenuName = saveData.rootMenuName;
                _blendTreeBaseName = saveData.baseBlendName;

                _toggleGroups.Clear();
                foreach (var serializableGroup in saveData.toggleGroups)
                {
                    if (serializableGroup == null)
                    {
                        Debug.LogError("One of the deserialized script groups is null.");
                        continue;
                    }
                    _toggleGroups.Add(serializableGroup.ToToggleGroup());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load script groups: {ex.Message}");
            }

            Repaint();
        }

        [Serializable]
        public class SerializableToggleGroup
        {
            public string groupName;
            public string groupIconPath;
            public string groupRootMenuPath;
            public string mergeMenuPath;
            public bool exclusiveMode;
            public bool blendTreeMode;
            public bool allowDisableAll;
            public string intParameterName;
            public AnimatorControllerParameterType parameterType;
            public List<string> gameObjectPaths;
            public bool isFoldout;
            public List<bool> isSettingsFoldout;
            public List<bool> save;
            public List<bool> sync;
            public List<string> propIconPaths;
            public List<string> customNames;
            public List<string> parameterNames;

            public SerializableToggleGroup(ToggleGroup group)
            {
                if (group == null)
                {
                    // 必ずnullなるが、問題ないのでreturn
                    return;
                }

                isFoldout = group.isFoldout;
                isSettingsFoldout = group.isSettingFoldout != null ? new List<bool>(group.isSettingFoldout) : new List<bool>();
                groupName = group.groupName;
                groupIconPath = group.groupIcon != null ? AssetDatabase.GetAssetPath(group.groupIcon) : string.Empty;
                groupRootMenuPath = group.groupRootMenu != null ? AssetDatabase.GetAssetPath(group.groupRootMenu) : string.Empty;
                mergeMenuPath = group.mergeMenu != null ? AssetDatabase.GetAssetPath(group.mergeMenu) : string.Empty;
                exclusiveMode = group.exclusiveMode;
                blendTreeMode = group.blendTreeMode;
                allowDisableAll = group.allowDisableAll;
                intParameterName = group.intParameterName;
                parameterType = group.parameterType;
                gameObjectPaths = new List<string>();
                if (group.gameObject != null)
                {
                    foreach (var obj in group.gameObject)
                    {
                        gameObjectPaths.Add(obj != null ? GetGameObjectPath(obj) : string.Empty);
                    }
                }
                save = group.save != null ? new List<bool>(group.save) : new List<bool>();
                sync = group.sync != null ? new List<bool>(group.sync) : new List<bool>();
                propIconPaths = new List<string>();
                if (group.propIcon != null)
                {
                    foreach (var propIcon in group.propIcon)
                    {
                        propIconPaths.Add(propIcon != null ? AssetDatabase.GetAssetPath(propIcon) : string.Empty);
                    }
                }
                parameterNames = group.parameterName != null ? new List<string>(group.parameterName) : new List<string>();
                customNames = group.customName != null ? new List<string>(group.customName) : new List<string>();
            }

            public ToggleGroup ToToggleGroup()
            {
                ToggleGroup group = new ToggleGroup
                {
                    isFoldout = isFoldout,
                    isSettingFoldout = new List<bool>(isSettingsFoldout),
                    groupName = groupName,
                    groupIcon = !string.IsNullOrEmpty(groupIconPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(groupIconPath) : null,
                    groupRootMenu = !string.IsNullOrEmpty(groupRootMenuPath) ? AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(groupRootMenuPath) : null,
                    mergeMenu = !string.IsNullOrEmpty(mergeMenuPath) ? AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(mergeMenuPath) : null,
                    exclusiveMode = exclusiveMode,
                    blendTreeMode = blendTreeMode,
                    allowDisableAll = allowDisableAll,
                    intParameterName = intParameterName,
                    parameterType = parameterType,
                    gameObject = new List<GameObject>(),
                    save = new List<bool>(save),
                    sync = new List<bool>(sync),
                    propIcon = new List<Texture2D>(),
                    parameterName = new List<string>(parameterNames),
                    customName = new List<string>(customNames)
                };

                foreach (var path in gameObjectPaths)
                {
                    GameObject obj = FindGameObjectByPath(path);
                    if (obj != null)
                    {
                        group.gameObject.Add(obj);
                    }
                }

                foreach (var path in propIconPaths)
                {
                    group.propIcon.Add(!string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null);
                }

                return group;
            }

            private string GetGameObjectPath(GameObject obj)
            {
                if (obj == null) return string.Empty;
                string path = obj.name;
                Transform current = obj.transform;
                while (current.parent != null)
                {
                    current = current.parent;
                    path = current.name + "/" + path;
                }
                return path;
            }

            private GameObject FindGameObjectByPath(string path)
            {
                return string.IsNullOrEmpty(path) ? null : GameObject.Find(path);
            }
        }

        [Serializable]
        public class SaveData
        {
            public string savePath;
            public string rootMenuPath;
            public string rootMenuName;
            public string baseBlendName;
            public List<SerializableToggleGroup> toggleGroups = new List<SerializableToggleGroup>();
        }
    }
}
