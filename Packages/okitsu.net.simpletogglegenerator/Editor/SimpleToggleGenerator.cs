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
            public string layerName = "";
            public Texture2D groupIcon;
            public bool exclusiveMode = true;
            public bool allowDisableAll = false;
            public AnimatorControllerParameterType parameterType = AnimatorControllerParameterType.Bool;
            public string intParameterName = "";
            public List<GameObject> objects = new();
            public bool isFoldout = true;
            public List<bool> isSettingsFoldout = new();
            public List<bool> save = new();
            public List<bool> sync = new();
            public List<Texture2D> propIcon = new();
            public List<string> customNames = new();
            public List<string> parameterNames = new();
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
        private string _blendTreeBaseName = "SimpleToggleGenerator";
        private bool _foldoutMenu = false;
        private bool _disablecfmdialog
        {
            get { return EditorPrefs.GetBool("DisableCfmDialog", false); }
            set { EditorPrefs.SetBool("DisableCfmDialog", value); }
        }
        private bool _enforceParameterType
        {
            get { return EditorPrefs.GetBool("EnforceParameterType", false); }
            set { EditorPrefs.SetBool("EnforceParameterType", value); }
        }
        private bool _experimentalMode
        {
            get { return EditorPrefs.GetBool("ExperimentalMode", false); }
            set { EditorPrefs.SetBool("ExperimentalMode", value); }
        }
        [MenuItem("Tools/Simple Toggle Generator")]
        public static void ShowWindow()
        {
            GetWindow<SimpleToggleGenerator>("Simple Toggle Generator");
        }

        private Vector2 _scrollPosition;
        private VRCAvatarDescriptor _previousAvatar;
        private BlendTree _rootNonExclusiveBlendTree;
        private AnimatorControllerLayer _nonExclusiveLayer;

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
            _animatorController = EditorGUILayout.ObjectField("FXLayer", _animatorController, typeof(AnimatorController), false) as AnimatorController;
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
            _vrcExpressionsMenu = EditorGUILayout.ObjectField("ExMenu", _vrcExpressionsMenu, typeof(VRCExpressionsMenu), false) as VRCExpressionsMenu;
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
            _rootMenu = EditorGUILayout.ObjectField("RootMenu", _rootMenu, typeof(VRCExpressionsMenu), false) as VRCExpressionsMenu;

            // RootMenu が null の場合のみ RootMenuName を表示
            if (_rootMenu == null && _vrcExpressionsMenu != null)
            {
                _rootMenuName = EditorGUILayout.TextField(
                    new GUIContent("RootMenuName", ""),
                    string.IsNullOrEmpty(_rootMenuName) ? "Simple Toggle Menu" : _rootMenuName
                );
            }

            // ExParam
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.expressionParameters != null);
            _vrcExpressionParameters = EditorGUILayout.ObjectField("ExpressionParameters", _vrcExpressionParameters, typeof(VRCExpressionParameters), false) as VRCExpressionParameters;
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

            // WD Base Name
            _blendTreeBaseName = EditorGUILayout.TextField(
                new GUIContent("BlendTree Base Name", "Base name for non-exclusive layer, blendtree, state and parameter"),
                string.IsNullOrEmpty(_blendTreeBaseName) ? "SimpleToggleGenerator" : _blendTreeBaseName
            );

            _foldoutMenu = EditorGUILayout.Foldout(_foldoutMenu, "Advanced Options");
            if (_foldoutMenu)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Disable Confirm dialog", GUILayout.Width(205));
                _disablecfmdialog = EditorGUILayout.Toggle(_disablecfmdialog);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Enforce Expression Parameter Type", GUILayout.Width(205));
                _enforceParameterType = EditorGUILayout.Toggle(_enforceParameterType);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Enable Experimental Option", GUILayout.Width(205));
                _experimentalMode = EditorGUILayout.Toggle(_experimentalMode);
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
                    if (!group.objects.Any(obj => obj != null && obj.activeSelf) && group.objects.Count > 0 && !group.allowDisableAll)
                    {
                        group.objects[0].SetActive(true);
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
                EditorGUI.LabelField(labelRect, group.layerName);

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

                    // Menu Name
                    group.layerName = EditorGUI.TextField(
                        new Rect(xL, yplus, wL, lh),
                        "Menu Name", string.IsNullOrEmpty(group.layerName) ? "New Layer" : group.layerName
                    );
                    yplus += lh + vs;

                    // Group Icon
                    group.groupIcon = (Texture2D)EditorGUI.ObjectField(
                        new Rect(xL, yplus, wL, EditorGUIUtility.singleLineHeight),
                        "Group Icon", group.groupIcon, typeof(Texture2D), false);
                    yplus += lh + vs;

                    // Exclusive Mode
                    group.exclusiveMode = EditorGUI.Toggle(
                        new Rect(xL, yplus, wL, lh),
                        "Exclusive Mode", group.exclusiveMode
                    );
                    yplus += lh + vs;

                    // AllowDisableAll（非排他モードのとき無効）
                    EditorGUI.BeginDisabledGroup(!group.exclusiveMode);
                    group.allowDisableAll = EditorGUI.Toggle(
                        new Rect(xL, yplus, wL, lh),
                        "AllowDisableAll", group.allowDisableAll
                    );
                    EditorGUI.EndDisabledGroup();
                    yplus += lh + vs;

                    if (group.exclusiveMode)
                    {
                        // Parameter Type (Bool / Float / Int)
                        List<string> typeOptions = new List<string> { "Bool", "Float" };
                        if (_experimentalMode && group.objects.Count >= 8)
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
                            bool isAlreadyAdded = _toggleGroups.Any(g => g.objects.Contains(obj));
                            if (!isAlreadyAdded && !group.objects.Contains(obj))
                            {
                                group.objects.Add(obj);
                                group.save.Add(true);
                                group.sync.Add(true);
                                group.propIcon.Add(null);
                                group.parameterNames.Add("");
                                group.customNames.Add("");
                            }
                        }
                    }

                    if (GUI.Button(new Rect(xL + btnW + gap, yplus, btnW, lh), "Clear All Objects"))
                    {
                        group.objects.Clear();
                        group.save.Clear();
                        group.sync.Clear();
                        group.propIcon.Clear();
                        group.parameterNames.Clear();
                        group.customNames.Clear();
                        group.isSettingsFoldout.Clear();
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
                                        bool isAlreadyAdded = _toggleGroups.Any(g => g.objects.Contains(draggedGO));
                                        if (!isAlreadyAdded && !group.objects.Contains(draggedGO))
                                        {
                                            group.objects.Add(draggedGO);
                                            group.save.Add(true);
                                            group.sync.Add(true);
                                            group.propIcon.Add(null);
                                            group.parameterNames.Add("");
                                            group.customNames.Add("");
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
                    // 基本設定 4行 (Menu, Icon, Exclusive, AllowDisableAll)
                    height += UIStyles.GetLines(4);

                    // ExclusiveMode のとき Parameter Type 行を追加
                    if (group.exclusiveMode)
                    {
                        height += UIStyles.GetLines(1); // ParameterType
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
            group.reorderableList = new UnityEditorInternal.ReorderableList(group.objects, typeof(GameObject), true, true, true, true);

            group.reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Objects (Drag to Reorder)");
            };

            group.reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= group.objects.Count) return;

                float lineHeight = EditorGUIUtility.singleLineHeight + 2;

                // デフォルトチェックボックス + ObjectField
                float toggleWidth = 20f;
                Rect toggleRect = new Rect(rect.x, rect.y, toggleWidth, EditorGUIUtility.singleLineHeight);
                Rect objRect = new Rect(rect.x + toggleWidth, rect.y, rect.width - toggleWidth, EditorGUIUtility.singleLineHeight);

                bool isChecked = GUI.Toggle(toggleRect, group.objects[index] != null && group.objects[index].activeSelf, GUIContent.none);
                if (isChecked)
                {
                    for (int k = 0; k < group.objects.Count; k++)
                    {
                        if (group.objects[k] != null)
                            group.objects[k].SetActive(k == index);
                    }
                }

                group.objects[index] = (GameObject)EditorGUI.ObjectField(objRect, group.objects[index], typeof(GameObject), true);

                // Settings Foldout
                while (group.isSettingsFoldout.Count <= index)
                    group.isSettingsFoldout.Add(false);

                string settingsLabel = !string.IsNullOrEmpty(group.customNames[index])
                    ? group.customNames[index]
                    : (group.objects[index] != null ? group.objects[index].name : "Settings");

                Rect foldoutRect = new Rect(rect.x, rect.y + lineHeight, rect.width, EditorGUIUtility.singleLineHeight);
                group.isSettingsFoldout[index] = EditorGUI.Foldout(foldoutRect, group.isSettingsFoldout[index], $"Settings ({settingsLabel})");

                if (group.isSettingsFoldout[index])
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
                    group.customNames[index] = EditorGUI.TextField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "InMenu Name", group.customNames[index]);
                    y += lineHeight;

                    // Parameter Name（Int 選択時は非表示）
                    if (group.parameterType != AnimatorControllerParameterType.Int)
                    {
                        group.parameterNames[index] = EditorGUI.TextField(
                            new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight),
                            "Parameter Name", group.parameterNames[index]);
                        y += lineHeight;
                    }

                    EditorGUI.indentLevel--;
                }
            };

            group.reorderableList.elementHeightCallback = (int index) =>
            {
                float lineHeight = EditorGUIUtility.singleLineHeight + 2;
                float height = lineHeight * 2; // ObjectField + Foldout

                if (index < group.isSettingsFoldout.Count && group.isSettingsFoldout[index])
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
                group.objects.Add(null);
                group.save.Add(true);
                group.sync.Add(true);
                group.propIcon.Add(null);
                group.parameterNames.Add("");
                group.customNames.Add("");
                group.isSettingsFoldout.Add(false);
            };

            // Remove ボタン
            group.reorderableList.onRemoveCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                int index = list.index;
                if (index >= 0 && index < group.objects.Count)
                {
                    group.objects.RemoveAt(index);
                    group.save.RemoveAt(index);
                    group.sync.RemoveAt(index);
                    group.propIcon.RemoveAt(index);
                    group.parameterNames.RemoveAt(index);
                    group.customNames.RemoveAt(index);
                    group.isSettingsFoldout.RemoveAt(index);
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
                    toggleGroupLayerNames.Add(group.layerName);
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
            if (!_disablecfmdialog && CheckDuplicateLayerNames())
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
            string brendTreeLayerName = _blendTreeBaseName + " (WD On)";
            bool HasDBTLayer()
            {
                if (_animatorController != null && _animatorController.layers.Any(l => l.name == brendTreeLayerName))
                {
                    return true;
                }
                return false;
            }
            if (HasDBTLayer())
            {
                string message =
                    $"Layer \"{brendTreeLayerName}\" and its BlendTree already exist in AnimatorController.\n" +
                    "The layer and BlendTree will be removed and regenerated if necessary.";
                bool overwrite = EditorUtility.DisplayDialog(
                    "DBT Layer Detected",
                    message,
                    "Yes (Remove and Continue)",
                    "Cancel"
                );

                if (overwrite)
                {
                    CleanupDBTLayers(brendTreeLayerName);
                }
                else
                {
                    Debug.Log("Processing has been canceled by user.");
                    return;
                }
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
                if (toggleGroup.objects.Count < 2)
                {
                    Debug.LogError("At least 2 game objects are required to generate animation clips in script group '" + toggleGroup.layerName + "'.");
                    return false;
                }
                if (string.IsNullOrEmpty(toggleGroup.layerName))
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
                foreach (var paramName in toggleGroup.parameterNames)
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
        private void GenerateLayerAndClip(ToggleGroup toggleGroup, string layerNameDBT)
        {
            string parameterNameDBT = _blendTreeBaseName + "_Blend";

            // 同名のレイヤーが既に存在するかチェック
            AnimatorControllerLayer existingLayer = _animatorController.layers.FirstOrDefault(layer => layer.name == toggleGroup.layerName);
            if (existingLayer != null)
            {
                // 同名のレイヤーを削除
                AnimatorControllerLayer[] updatedLayers = _animatorController.layers.Where(layer => layer.name != toggleGroup.layerName).ToArray();
                _animatorController.layers = updatedLayers;
                Debug.Log($"Overwritten the {_animatorController} layer. An unintended problem may have occurred.");
            }

            // 既存のパラメーター名を保存
            HashSet<string> existingParameterNames = new();
            foreach (var parameter in _animatorController.parameters)
            {
                existingParameterNames.Add(parameter.name);
            }

            if (toggleGroup.exclusiveMode)
            {
                string intParameterName = toggleGroup.intParameterName;
                // 新しいレイヤーを作成
                AnimatorControllerLayer newLayer = new()
                {
                    name = toggleGroup.layerName,
                    stateMachine = new AnimatorStateMachine
                    {
                        name = toggleGroup.layerName,
                        hideFlags = HideFlags.HideInHierarchy
                    }
                };
                AssetDatabase.AddObjectToAsset(newLayer.stateMachine, _animatorController);

                // AnimationClipを作成してStateの設定をする
                Dictionary<GameObject, AnimatorState> stateDictionary = new();
                foreach (var obj in toggleGroup.objects)
                {
                    string stateName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)];
                    AnimatorState state = newLayer.stateMachine.AddState(stateName);

                    state.motion = CreateAnimationClip(obj, toggleGroup.objects, toggleGroup);
                    state.writeDefaultValues = false;
                    stateDictionary.Add(obj, state);

                    // チェックボックスが有効なオブジェクトが存在しない場合の処理
                    if (obj.activeSelf && !toggleGroup.allowDisableAll)
                    {
                        newLayer.stateMachine.defaultState = state;
                    }
                }

                AnimatorState allDisabledState = null;
                if (toggleGroup.allowDisableAll)
                {
                    allDisabledState = newLayer.stateMachine.AddState("AllDisabled");
                    allDisabledState.motion = CreateDisableAllAnimationClip(toggleGroup.objects, toggleGroup);
                }

                // VRCAvatarParameterDriverをすべてのStateに追加
                if (toggleGroup.parameterType != AnimatorControllerParameterType.Int)
                {
                    foreach (var state in newLayer.stateMachine.states.Select(s => s.state))
                    {
                        VRCAvatarParameterDriver driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                        driver.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();

                        // VRCAvatarParameterDriverのパラメータを設定
                        foreach (var param in toggleGroup.parameterNames)
                        {
                            VRC.SDKBase.VRC_AvatarParameterDriver.Parameter driverParam = new()
                            {
                                name = param
                            };

                            // StateがDefault Stateか確認
                            if (state != newLayer.stateMachine.defaultState)
                            {
                                // Default Stateでない場合、自己遷移条件を除くすべてのパラメータを設定
                                if (param != state.name)
                                {
                                    driverParam.value = 0;
                                    driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                                    driver.parameters.Add(driverParam);
                                }
                            }
                            else
                            {
                                // Default Stateの場合、すべての遷移条件のパラメーターを設定
                                if (param != state.name)
                                {
                                    driverParam.value = 0;
                                    driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                                }
                                else
                                {
                                    driverParam.value = 1;
                                    driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                                }
                                driver.parameters.Add(driverParam);
                            }
                        }
                    }
                }

                // State間の遷移を設定
                foreach (var obj1 in toggleGroup.objects)
                {
                    foreach (var obj2 in toggleGroup.objects)
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
                                paramName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj2)];
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
                                int value = toggleGroup.objects.IndexOf(obj2) + 1;
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
                            foreach (var param in toggleGroup.parameterNames)
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
                                string paramName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)];
                                transition.AddCondition(AnimatorConditionMode.If, 1, paramName);
                            }
                            else if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                            {
                                string paramName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)];
                                transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, paramName);
                            }
                            else if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                            {
                                int value = toggleGroup.objects.IndexOf(obj) + 1; // ★ 1始まりで正しい値に
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

                            foreach (var param in toggleGroup.parameterNames)
                            {
                                if (toggleGroup.parameterType == AnimatorControllerParameterType.Bool)
                                {
                                    defaultTransition.AddCondition(AnimatorConditionMode.IfNot, 0, param);
                                }
                                else if (toggleGroup.parameterType == AnimatorControllerParameterType.Float)
                                {
                                    defaultTransition.AddCondition(AnimatorConditionMode.Less, 0.5f, param);
                                }
                                else if (toggleGroup.parameterType == AnimatorControllerParameterType.Int)
                                {
                                    defaultTransition.AddCondition(AnimatorConditionMode.Equals, 0f, intParameterName);
                                }
                            }
                        }
                    }
                }

                _animatorController.AddLayer(newLayer);
            }
            else
            {
                // 非排他モード

                // 初回なら親レイヤーと親BlendTreeを作成
                if (_rootNonExclusiveBlendTree == null)
                {
                    _nonExclusiveLayer = new AnimatorControllerLayer
                    {
                        name = layerNameDBT,
                        stateMachine = new AnimatorStateMachine
                        {
                            name = layerNameDBT,
                            hideFlags = HideFlags.HideInHierarchy
                        }
                    };
                    AssetDatabase.AddObjectToAsset(_nonExclusiveLayer.stateMachine, _animatorController);

                    _rootNonExclusiveBlendTree = new BlendTree
                    {
                        name = layerNameDBT,
                        blendType = BlendTreeType.Direct,
                        useAutomaticThresholds = false
                    };
                    AssetDatabase.AddObjectToAsset(_rootNonExclusiveBlendTree, _animatorController);

                    AnimatorState rootState = _nonExclusiveLayer.stateMachine.AddState(layerNameDBT);
                    rootState.motion = _rootNonExclusiveBlendTree;
                    rootState.writeDefaultValues = true;
                    _nonExclusiveLayer.stateMachine.defaultState = rootState;

                    _animatorController.AddLayer(_nonExclusiveLayer);

                    if (!_animatorController.parameters.Any(p => p.name == parameterNameDBT))
                    {
                        var parameter = new AnimatorControllerParameter
                        {
                            name = parameterNameDBT,
                            type = AnimatorControllerParameterType.Float,
                            defaultFloat = 1f
                        };
                        _animatorController.AddParameter(parameter);
                    }
                }

                // グループ用 Direct BlendTree
                BlendTree groupTree = _rootNonExclusiveBlendTree.CreateBlendTreeChild(0f);
                groupTree.name = $"{toggleGroup.layerName}_GroupTree";
                groupTree.blendType = BlendTreeType.Direct;
                groupTree.useAutomaticThresholds = false;

                // グループを親に追加
                var rootChildren = _rootNonExclusiveBlendTree.children.ToList();
                var lastChild = rootChildren[rootChildren.Count - 1];
                lastChild.directBlendParameter = parameterNameDBT;
                rootChildren[rootChildren.Count - 1] = lastChild;
                _rootNonExclusiveBlendTree.children = rootChildren.ToArray();

                // 各オブジェクト用 1D BlendTree
                foreach (var obj in toggleGroup.objects)
                {
                    string paramName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)];
                    CreateOrUpdateParameter(paramName, AnimatorControllerParameterType.Float);

                    // オブジェクト用 Direct BlendTree
                    BlendTree objTree = groupTree.CreateBlendTreeChild(0f);
                    objTree.name = $"{obj.name}_BlendTree";
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
                    lastGroupChild.directBlendParameter = parameterNameDBT;
                    groupChildren[groupChildren.Count - 1] = lastGroupChild;
                    groupTree.children = groupChildren.ToArray();
                }
            }
        }

        private void CleanupDBTLayers(string layerNameDBT)
        {
            if (_animatorController == null) return;

            // BlendTree のレイヤーを削除
            var layers = _animatorController.layers.ToList();
            int removed = layers.RemoveAll(l => l.name == layerNameDBT);
            if (removed > 0)
            {
                _animatorController.layers = layers.ToArray();
                Debug.Log($"Removed {removed} DBT layers from {_animatorController.name}");
            }

            // BlendTree や参照をクリア
            if (_rootNonExclusiveBlendTree != null)
            {
                UnityEngine.Object.DestroyImmediate(_rootNonExclusiveBlendTree, true);
                _rootNonExclusiveBlendTree = null;
            }
            if (_nonExclusiveLayer != null)
            {
                _nonExclusiveLayer = null;
            }
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

        // ====排他モードでのAnimation Clip作成====
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
            return SaveClip(clip, toggleGroup.layerName);
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
            return SaveClip(clip, toggleGroup.layerName);
        }

        // ====非排他モードでのAnimation Clip作成====
        private AnimationClip CreateSingleEnableClip(GameObject obj, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Enable";

            // オブジェクトを有効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 1f));

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.layerName);
        }
        private AnimationClip CreateSingleDisableClip(GameObject obj, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Disable";

            // オブジェクトを無効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.layerName);
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
            if (!AssetDatabase.IsValidFolder(rootMenuPath))
            {
                AssetDatabase.CreateFolder(_savePath, _avatar.name);
            }

            VRCExpressionsMenu rootExpressionsMenu;
            if (_rootMenu != null)
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
                    rootExpressionsMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    rootExpressionsMenu.name = _rootMenuName;
                    AssetDatabase.CreateAsset(rootExpressionsMenu, $"{rootMenuPath}/{rootExpressionsMenu.name}.asset");
                }
            }

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
                // オブジェクトの数を元にページ数を計算
                int numObjects = toggleGroup.objects.Count;
                int objectsPerPage = 7; // ページあたりの最大オブジェクト数
                int numObjectPages = Mathf.CeilToInt((float)numObjects / objectsPerPage);

                for (int objectPageNum = 0; objectPageNum < numObjectPages; objectPageNum++)
                {
                    VRCExpressionsMenu groupExpressionsMenu = null;
                    groupExpressionsMenu = CreateInstance<VRCExpressionsMenu>();
                    groupExpressionsMenu.name = $"{toggleGroup.layerName} ExpressionsMenu_Page{objectPageNum + 1}";
                    AssetDatabase.CreateAsset(groupExpressionsMenu, $"{subMenuPath}/{groupExpressionsMenu.name}.asset");

                    int startIndex_o = objectPageNum * objectsPerPage;
                    int endIndex_o = Mathf.Min((objectPageNum + 1) * objectsPerPage, numObjects);
                    for (int index_o = startIndex_o; index_o < endIndex_o; index_o++)
                    {
                        string paramName = toggleGroup.parameterNames[index_o];
                        string customName = toggleGroup.customNames[index_o];
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
                            // groupExpressionsMenuに同じ名前のコントロールが既に存在していないか確認
                            bool controlExists = groupExpressionsMenu.controls.Any(control => control.name == customName);
                            if (!controlExists)
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
                            else
                            {
                                Debug.LogWarning($"Control '{customName}' already exists in {groupExpressionsMenu.name}. Skipping duplicate.");
                            }
                        }
                    }
                    EditorUtility.SetDirty(groupExpressionsMenu);
                    AssetDatabase.SaveAssets();

                    if (previousGroupMenu != null && previousProcessLayerName == toggleGroup.layerName)
                    {
                        if (groupExpressionsMenu.controls.Count == 1)
                        {
                            foreach (var control in groupExpressionsMenu.controls)
                            {
                                previousGroupMenu.controls.Add(control);
                                AssetDatabase.DeleteAsset($"{subMenuPath}/{toggleGroup.layerName} ExpressionsMenu_Page{objectPageNum + 1}.asset");
                                Debug.Log($"Delete{subMenuPath}/{toggleGroup.layerName} ExpressionsMenu_Page{objectPageNum + 1}.asset");
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

                        // すでに同じ名前のサブメニューが存在するか確認
                        bool menuExists = rootExpressionsMenu.controls.Any(control => control.name == toggleGroup.layerName);

                        // 既存のサブメニューコントロールを検索する
                        VRCExpressionsMenu.Control existingSubMenuControl = rootExpressionsMenu.controls
                            .FirstOrDefault(control => control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                                                        control.name == toggleGroup.layerName);

                        if (!menuExists)
                        {
                            if (rootExpressionsMenu.controls.Count < 7)
                            {
                                VRCExpressionsMenu.Control subMenuControl = new()
                                {
                                    name = toggleGroup.layerName,
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
                                    VRCExpressionsMenu rootSubMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
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
                                                name = toggleGroup.layerName,
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
                                                name = toggleGroup.layerName,
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
                                                name = toggleGroup.layerName,
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
                                                name = toggleGroup.layerName,
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
                                        name = toggleGroup.layerName,
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
                            Debug.Log("既存のメニューを使用: " + toggleGroup.layerName);
                            Debug.Log($"{existingSubMenuControl.subMenu}");
                        }

                    }

                    // Update the previous group menu
                    previousGroupMenu = groupExpressionsMenu;
                    previousProcessLayerName = toggleGroup.layerName;
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
                    for (int i = 0; i < toggleGroup.objects.Count; i++)
                    {
                        if (toggleGroup.objects[i] != null && toggleGroup.objects[i].activeSelf)
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
                    for (int i = 0; i < toggleGroup.objects.Count; i++)
                    {
                        // 変数から設定値を取得
                        string parameterName = toggleGroup.parameterNames[i];
                        bool defaultValue = toggleGroup.objects.Any(obj => obj.activeSelf && toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)] == parameterName);
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
                baseNameDBT = _blendTreeBaseName,
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
                _blendTreeBaseName = saveData.baseNameDBT;

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
            public string layerName;
            public string groupIconPath;
            public bool exclusiveMode;
            public bool allowDisableAll;
            public string intParameterName;
            public AnimatorControllerParameterType parameterType;
            public List<string> objectPaths;
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
                isSettingsFoldout = group.isSettingsFoldout != null ? new List<bool>(group.isSettingsFoldout) : new List<bool>();
                layerName = group.layerName;
                groupIconPath = group.groupIcon != null ? AssetDatabase.GetAssetPath(group.groupIcon) : string.Empty;
                exclusiveMode = group.exclusiveMode;
                allowDisableAll = group.allowDisableAll;
                intParameterName = group.intParameterName;
                parameterType = group.parameterType;
                objectPaths = new List<string>();
                if (group.objects != null)
                {
                    foreach (var obj in group.objects)
                    {
                        objectPaths.Add(obj != null ? GetGameObjectPath(obj) : string.Empty);
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
                parameterNames = group.parameterNames != null ? new List<string>(group.parameterNames) : new List<string>();
                customNames = group.customNames != null ? new List<string>(group.customNames) : new List<string>();
            }

            public ToggleGroup ToToggleGroup()
            {
                ToggleGroup group = new ToggleGroup
                {
                    isFoldout = isFoldout,
                    isSettingsFoldout = new List<bool>(isSettingsFoldout),
                    layerName = layerName,
                    groupIcon = !string.IsNullOrEmpty(groupIconPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(groupIconPath) : null,
                    exclusiveMode = exclusiveMode,
                    allowDisableAll = allowDisableAll,
                    intParameterName = intParameterName,
                    parameterType = parameterType,
                    objects = new List<GameObject>(),
                    save = new List<bool>(save),
                    sync = new List<bool>(sync),
                    propIcon = new List<Texture2D>(),
                    parameterNames = new List<string>(parameterNames),
                    customNames = new List<string>(customNames)
                };

                foreach (var path in objectPaths)
                {
                    GameObject obj = FindGameObjectByPath(path);
                    if (obj != null)
                    {
                        group.objects.Add(obj);
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
            public string baseNameDBT;
            public List<SerializableToggleGroup> toggleGroups = new List<SerializableToggleGroup>();
        }
    }
}
